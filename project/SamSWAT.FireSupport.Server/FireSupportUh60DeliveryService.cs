using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Cloners;
using IOPath = System.IO.Path;

namespace SamSWAT.FireSupport.ArysReloaded;

[Injectable(InjectionType.Singleton)]
public sealed class FireSupportUh60DeliveryService(
	ISptLogger<FireSupportUh60DeliveryService> logger,
	DatabaseService databaseService,
	ProfileHelper profileHelper,
	MailSendService mailSendService,
	SaveServer saveServer,
	TimeUtil timeUtil,
	ConfigServer configServer,
	ICloner cloner)
{
	public const string MessengerTraderId = "66f51f3a0000000000000a60";
	public const string MessengerAvatarRoute = "/tsc/assets/uh60-pilot.png";

	private const string BtrTraderId = "656f0f98d80a697f855d34b1";
	private const string FallbackAvatar =
		"/files/trader/avatar/5935c25fb3acc3127c3d8cd9.png";
	private const string DeliveryMessagePrefix =
		"Cargo delivered. Your transferred items are ready for collection.";
	private const string PriorityExfilArtworkPath =
		"assets/content/ui/phone/icons/amber_512/priority_exfil.png";

	private readonly TraderConfig _traderConfig =
		configServer.GetConfig<TraderConfig>();
	private readonly FireSupportUh60TransferMarkerStore _markerStore = new();

	private byte[]? _messengerAvatar;
	private bool _messengerReady;

	public void Initialize(string pathToMod)
	{
		string storageDirectory = IOPath.Combine(pathToMod, "storage");
		_markerStore.Initialize(storageDirectory);
		if (!string.IsNullOrWhiteSpace(_markerStore.LastLoadWarning))
		{
			logger.Warning($"TSC {_markerStore.LastLoadWarning}");
		}

		LoadMessengerAvatar(pathToMod);
		InitializeMessengerIdentity();
	}

	public FireSupportUh60TransferMarkerResponse TryMarkTransfer(
		MongoId sessionId,
		FireSupportUh60TransferMarkerRequest? request)
	{
		if (sessionId.IsEmpty)
		{
			return Rejected("AuthenticatedSessionRequired");
		}

		if (request == null ||
		    string.IsNullOrWhiteSpace(request.ProfileId) ||
		    request.ItemIds == null ||
		    request.ItemIds.Count == 0)
		{
			return Rejected("InvalidRequest");
		}

		if (request.ItemIds.Count >
		    FireSupportUh60TransferMarkerStore.MaxItemIdsPerProfile)
		{
			return Rejected("TooManyItems");
		}

		string authenticatedProfileId;
		try
		{
			authenticatedProfileId =
				profileHelper.GetPmcProfile(sessionId)?.Id?.ToString() ??
				string.Empty;
		}
		catch (Exception exception)
		{
			logger.Warning(
				$"TSC UH-60 marker rejected because the authenticated profile could not be resolved: {exception.Message}");
			return Rejected("ProfileNotFound");
		}

		if (string.IsNullOrWhiteSpace(authenticatedProfileId) ||
		    !string.Equals(
			    authenticatedProfileId,
			    request.ProfileId.Trim(),
			    StringComparison.OrdinalIgnoreCase))
		{
			return Rejected("ProfileMismatch");
		}

		if (!_messengerReady)
		{
			return Rejected("MessengerUnavailable");
		}

		if (!_markerStore.TryMark(
			    sessionId.ToString(),
			    authenticatedProfileId,
			    request.ItemIds,
			    out int acceptedItemCount,
			    out string reason))
		{
			logger.Warning(
				$"TSC UH-60 transfer marker was not persisted; stock BTR delivery remains active. reason={reason}");
			return Rejected(reason);
		}

		return new FireSupportUh60TransferMarkerResponse
		{
			Ok = true,
			AcceptedItemCount = acceptedItemCount,
			Reason = string.Empty
		};
	}

	public bool TryPartitionMarkedItems(
		MongoId sessionId,
		List<Item> packageItems,
		out string profileId,
		out List<Item> messengerItems,
		out List<Item> stockItems)
	{
		profileId = string.Empty;
		messengerItems = [];
		stockItems = packageItems?.ToList() ?? [];
		if (!_messengerReady || packageItems == null || packageItems.Count == 0)
		{
			return false;
		}

		try
		{
			profileId =
				profileHelper.GetPmcProfile(sessionId)?.Id?.ToString() ??
				string.Empty;
		}
		catch (Exception exception)
		{
			logger.Warning(
				$"TSC UH-60 routing could not resolve profile {sessionId}; using stock BTR delivery. {exception.Message}");
			return false;
		}

		HashSet<string> markedItemIds = _markerStore.GetMarkedItemIds(
			sessionId.ToString(),
			profileId);
		if (markedItemIds.Count == 0)
		{
			return false;
		}

		var itemsById = new Dictionary<string, Item>(
			StringComparer.OrdinalIgnoreCase);
		var childrenByParent = new Dictionary<string, List<Item>>(
			StringComparer.OrdinalIgnoreCase);
		foreach (Item item in packageItems)
		{
			string itemId = item.Id.ToString();
			if (!string.IsNullOrWhiteSpace(itemId))
			{
				itemsById.TryAdd(itemId, item);
			}

			if (!string.IsNullOrWhiteSpace(item.ParentId))
			{
				if (!childrenByParent.TryGetValue(
					    item.ParentId,
					    out List<Item>? children))
				{
					children = [];
					childrenByParent[item.ParentId] = children;
				}

				children.Add(item);
			}
		}

		var selectedIds = new HashSet<string>(
			markedItemIds.Where(itemsById.ContainsKey),
			StringComparer.OrdinalIgnoreCase);
		if (selectedIds.Count == 0)
		{
			return false;
		}

		// A marker for any attachment or nested item selects its entire connected
		// top-level item tree. This prevents a weapon/container and its children
		// from being divided between two delivery messages.
		var pending = new Queue<string>(selectedIds);
		while (pending.Count > 0)
		{
			string selectedId = pending.Dequeue();
			if (!itemsById.TryGetValue(selectedId, out Item? selectedItem))
			{
				continue;
			}

			if (!string.IsNullOrWhiteSpace(selectedItem.ParentId) &&
			    itemsById.ContainsKey(selectedItem.ParentId) &&
			    selectedIds.Add(selectedItem.ParentId))
			{
				pending.Enqueue(selectedItem.ParentId);
			}

			if (!childrenByParent.TryGetValue(
				    selectedId,
				    out List<Item>? childItems))
			{
				continue;
			}

			foreach (Item childItem in childItems)
			{
				string childId = childItem.Id.ToString();
				if (selectedIds.Add(childId))
				{
					pending.Enqueue(childId);
				}
			}
		}

		messengerItems = packageItems
			.Where(item => selectedIds.Contains(item.Id.ToString()))
			.ToList();
		stockItems = packageItems
			.Where(item => !selectedIds.Contains(item.Id.ToString()))
			.ToList();
		return messengerItems.Count > 0;
	}

	public void SendMessengerDelivery(
		MongoId sessionId,
		List<Item> items,
		string receiptToken)
	{
		if (!_messengerReady ||
		    databaseService.GetTrader(MessengerTraderId) == null)
		{
			throw new InvalidOperationException(
				"The TSC UH-60 Pilot messenger identity is unavailable.");
		}

		if (!TryValidateItemTemplates(items, out string missingTemplate))
		{
			throw new InvalidOperationException(
				$"The TSC UH-60 Pilot delivery contains an unavailable item template: {missingTemplate}");
		}

		mailSendService.SendDirectNpcMessageToPlayer(
			sessionId,
			MessengerTraderId,
			MessageType.BtrItemsDelivery,
			BuildDeliveryMessage(receiptToken),
			items,
			timeUtil.GetHoursAsSeconds(
				_traderConfig.Fence.BtrDeliveryExpireHours));
	}

	public bool TryValidateItemTemplates(
		IEnumerable<Item> items,
		out string missingTemplate)
	{
		missingTemplate = string.Empty;
		Dictionary<MongoId, TemplateItem> itemTemplates;
		try
		{
			itemTemplates = databaseService.GetItems();
		}
		catch (Exception exception)
		{
			missingTemplate = $"database unavailable ({exception.Message})";
			return false;
		}

		foreach (Item item in items ?? [])
		{
			if (!itemTemplates.ContainsKey(item.Template))
			{
				missingTemplate = item.Template.ToString();
				return false;
			}
		}

		return true;
	}

	public bool TryPrepareDelivery(
		MongoId sessionId,
		string profileId,
		string packageId,
		IEnumerable<Item> deliveryItems,
		out FireSupportUh60DeliveryReceipt receipt,
		out string reason)
	{
		return _markerStore.TryPrepareDelivery(
			sessionId.ToString(),
			profileId,
			packageId,
			deliveryItems.Select(item => item.Id.ToString()),
			out receipt,
			out reason);
	}

	public bool TryRecordMailObserved(
		MongoId sessionId,
		string profileId,
		string packageId)
	{
		if (_markerStore.TryRecordMailObserved(
			    sessionId.ToString(),
			    profileId,
			    packageId,
			    out string reason))
		{
			return true;
		}

		logger.Warning(
			$"TSC UH-60 Pilot mail was observed, but its durable receipt phase could not be updated. " +
			$"The profile receipt remains authoritative. reason={reason}");
		return false;
	}

	public bool TryCompleteDelivery(
		MongoId sessionId,
		string profileId,
		string packageId,
		IEnumerable<Item> deliveredItems)
	{
		if (_markerStore.TryCompleteDelivery(
			    sessionId.ToString(),
			    profileId,
			    packageId,
			    deliveredItems.Select(item => item.Id.ToString()),
			    out string reason))
		{
			return true;
		}

		logger.Warning(
			$"TSC UH-60 delivery was saved to the profile, but its sidecar receipt could not be completed. " +
			$"The saved profile remains authoritative. reason={reason}");
		return false;
	}

	public FireSupportDeliveryMailStatus InspectMessengerDeliveryReceipt(
		MongoId sessionId,
		string receiptToken,
		IEnumerable<Item> expectedItems)
	{
		string expectedText = BuildDeliveryMessage(receiptToken);
		if (!TryGetDialogueMessages(
			    sessionId,
			    MessengerTraderId,
			    out List<Message> messages))
		{
			return FireSupportDeliveryMailStatus.Incomplete;
		}

		List<Message> receiptMessages = messages
			.Where(message => string.Equals(
				message.Text,
				expectedText,
				StringComparison.Ordinal))
			.ToList();
		if (receiptMessages.Count == 0)
		{
			return FireSupportDeliveryMailStatus.Missing;
		}

		return receiptMessages.Any(message =>
			MessageContainsExpectedItems(message, expectedItems))
			? FireSupportDeliveryMailStatus.Complete
			: FireSupportDeliveryMailStatus.Incomplete;
	}

	public bool TryCaptureDeliveryMessageIds(
		MongoId sessionId,
		string traderId,
		out HashSet<string> messageIds)
	{
		messageIds = [];
		if (!TryGetDialogueMessages(
			    sessionId,
			    traderId,
			    out List<Message> messages))
		{
			return false;
		}

		messageIds = new HashSet<string>(
			messages
				.Select(message => message.Id.ToString()),
			StringComparer.OrdinalIgnoreCase);
		return true;
	}

	public FireSupportDeliveryMailStatus InspectNewDeliveryMessage(
		MongoId sessionId,
		string traderId,
		IReadOnlySet<string> existingMessageIds,
		IEnumerable<Item> expectedItems)
	{
		if (!TryGetDialogueMessages(
			    sessionId,
			    traderId,
			    out List<Message> messages))
		{
			return FireSupportDeliveryMailStatus.Incomplete;
		}

		List<Message> newMessages = messages
			.Where(message =>
				!existingMessageIds.Contains(message.Id.ToString()))
			.ToList();
		if (newMessages.Count == 0)
		{
			return FireSupportDeliveryMailStatus.Missing;
		}

		return newMessages.Any(message =>
			MessageContainsExpectedItems(message, expectedItems))
			? FireSupportDeliveryMailStatus.Complete
			: FireSupportDeliveryMailStatus.Incomplete;
	}

	public bool TryGetMessengerAvatar(out byte[] avatar)
	{
		avatar = _messengerAvatar ?? [];
		return avatar.Length > 0;
	}

	private void InitializeMessengerIdentity()
	{
		try
		{
			Dictionary<MongoId, Trader> traders = databaseService.GetTraders();
			Trader? pilot;
			if (traders.TryGetValue(MessengerTraderId, out Trader? existing))
			{
				if (!IsOwnedMessengerIdentity(existing))
				{
					_messengerReady = false;
					logger.Error(
						$"TSC UH-60 Pilot messenger ID {MessengerTraderId} is already owned by another trader. " +
						"The existing trader was left unchanged and stock BTR delivery will remain active.");
					return;
				}

				pilot = existing;
			}
			else
			{
				Trader? btrDriver = databaseService.GetTrader(BtrTraderId);
				if (btrDriver == null)
				{
					logger.Warning(
						"TSC UH-60 Pilot identity could not be created; stock BTR delivery will remain active.");
					return;
				}

				pilot = cloner.Clone(btrDriver);
				if (pilot == null)
				{
					logger.Warning(
						"TSC UH-60 Pilot identity clone failed; stock BTR delivery will remain active.");
					return;
				}

				traders[MessengerTraderId] = pilot;
			}

			pilot.Base.Id = MessengerTraderId;
			pilot.Base.Name = "UH-60 Pilot";
			pilot.Base.Nickname = "UH-60 Pilot";
			pilot.Base.Surname = string.Empty;
			pilot.Base.Location = "Tactical Services Control";
			pilot.Base.AvailableInRaid = false;
			pilot.Base.UnlockedByDefault = false;
			pilot.Base.IsCanTransferItems = false;
			pilot.Base.IsCanTransferItemsFromPve = false;
			pilot.Base.Avatar = _messengerAvatar is { Length: > 0 }
				? MessengerAvatarRoute
				: FallbackAvatar;
			pilot.Assort.Items.Clear();
			pilot.Assort.BarterScheme.Clear();
			pilot.Assort.LoyalLevelItems.Clear();
			pilot.QuestAssort.Clear();
			pilot.Suits = [];
			pilot.Services = [];

			AddMessengerLocales();
			_messengerReady = true;
			logger.Success(
				"TSC UH-60 Pilot messenger identity registered independently of the native BTR Driver.");
		}
		catch (Exception exception)
		{
			_messengerReady = false;
			logger.Error(
				"TSC UH-60 Pilot identity initialization failed; stock BTR delivery will remain active.",
				exception);
		}
	}

	private static bool IsOwnedMessengerIdentity(Trader trader)
	{
		return trader?.Base != null &&
		       string.Equals(
			       trader.Base.Id.ToString(),
			       MessengerTraderId,
			       StringComparison.OrdinalIgnoreCase) &&
		       string.Equals(
			       trader.Base.Name,
			       "UH-60 Pilot",
			       StringComparison.Ordinal) &&
		       string.Equals(
			       trader.Base.Nickname,
			       "UH-60 Pilot",
			       StringComparison.Ordinal) &&
		       string.Equals(
			       trader.Base.Location,
			       "Tactical Services Control",
			       StringComparison.Ordinal);
	}

	private bool TryGetDialogueMessages(
		MongoId sessionId,
		string traderId,
		out List<Message> messages)
	{
		messages = [];
		try
		{
			SptProfile profile = saveServer.GetProfile(sessionId);
			Dialogue? dialogue = profile.DialogueRecords?
				.FirstOrDefault(pair => string.Equals(
					pair.Key.ToString(),
					traderId,
					StringComparison.OrdinalIgnoreCase))
				.Value;
			messages = dialogue?.Messages ?? [];
			return true;
		}
		catch (Exception exception)
		{
			logger.Warning(
				$"TSC could not inspect delivery mail for trader {traderId}; the package will remain queued. {exception.Message}");
			return false;
		}
	}

	private static bool MessageContainsExpectedItems(
		Message message,
		IEnumerable<Item> expectedItems)
	{
		List<Item>? actualItems = message.Items?.Data;
		if (actualItems == null || actualItems.Count == 0)
		{
			return false;
		}

		Dictionary<string, int> expectedTemplateCounts =
			GetTemplateCounts(expectedItems);
		Dictionary<string, int> actualTemplateCounts =
			GetTemplateCounts(actualItems);
		return expectedTemplateCounts.Count > 0 &&
		       expectedTemplateCounts.All(pair =>
			       actualTemplateCounts.TryGetValue(
				       pair.Key,
				       out int actualCount) &&
			       actualCount >= pair.Value);
	}

	private static Dictionary<string, int> GetTemplateCounts(
		IEnumerable<Item> items)
	{
		return (items ?? [])
			.GroupBy(
				item => item.Template.ToString(),
				StringComparer.OrdinalIgnoreCase)
			.ToDictionary(
				group => group.Key,
				group => group.Count(),
				StringComparer.OrdinalIgnoreCase);
	}

	private static string BuildDeliveryMessage(string receiptToken)
	{
		string normalizedToken = receiptToken?.Trim().ToUpperInvariant() ??
		                         string.Empty;
		if (normalizedToken.Length != 12 ||
		    !normalizedToken.All(Uri.IsHexDigit))
		{
			throw new ArgumentException(
				"UH-60 delivery receipt tokens must be 12 hexadecimal characters.",
				nameof(receiptToken));
		}

		return $"{DeliveryMessagePrefix} Manifest {normalizedToken}.";
	}

	private void AddMessengerLocales()
	{
		foreach (var (_, lazyLocales) in databaseService.GetLocales().Global)
		{
			lazyLocales.AddTransformer(localeData =>
			{
				if (localeData == null)
				{
					return null;
				}

				localeData[$"{MessengerTraderId} FullName"] = "UH-60 Pilot";
				localeData[$"{MessengerTraderId} FirstName"] = "UH-60";
				localeData[$"{MessengerTraderId} Nickname"] = "Pilot";
				localeData[$"{MessengerTraderId} Location"] =
					"Tactical Services Control";
				localeData[$"{MessengerTraderId} Description"] =
					"TerraGroup tactical airlift pilot and cargo liaison.";
				return localeData;
			});
		}
	}

	private void LoadMessengerAvatar(string pathToMod)
	{
		try
		{
			string? avatarPath = FindPriorityExfilArtwork(pathToMod);
			if (avatarPath == null)
			{
				logger.Warning(
					"TSC UH-60 Pilot artwork was not found; a neutral built-in trader portrait will be used temporarily.");
				return;
			}

			_messengerAvatar = File.ReadAllBytes(avatarPath);
			logger.Success(
				"TSC UH-60 Pilot is using the shipped Priority Exfil artwork as its temporary messenger portrait.");
		}
		catch (Exception exception)
		{
			_messengerAvatar = null;
			logger.Warning(
				$"TSC UH-60 Pilot artwork could not be loaded; a neutral built-in portrait will be used. {exception.Message}");
		}
	}

	private static string? FindPriorityExfilArtwork(string pathToMod)
	{
		DirectoryInfo? directory = new(pathToMod);
		for (int depth = 0; directory != null && depth < 7; depth++)
		{
			foreach (string pluginFolder in new[]
			         {
				         "Tylevo.TacticalServicesControl",
				         "TylevoTacticalServicesControl"
			         })
			{
				string candidate = IOPath.Combine(
					directory.FullName,
					"BepInEx",
					"plugins",
					pluginFolder,
					PriorityExfilArtworkPath);
				if (File.Exists(candidate))
				{
					return candidate;
				}
			}

			directory = directory.Parent;
		}

		return null;
	}

	private static FireSupportUh60TransferMarkerResponse Rejected(
		string reason)
	{
		return new FireSupportUh60TransferMarkerResponse
		{
			Ok = false,
			AcceptedItemCount = 0,
			Reason = reason
		};
	}
}

public enum FireSupportDeliveryMailStatus
{
	Missing = 0,
	Complete = 1,
	Incomplete = 2
}

public sealed class FireSupportUh60TransferMarkerRequest
{
	public string ProfileId { get; set; } = string.Empty;
	public List<string> ItemIds { get; set; } = [];
}

public sealed class FireSupportUh60TransferMarkerResponse
{
	public bool Ok { get; set; }
	public int AcceptedItemCount { get; set; }
	public string Reason { get; set; } = string.Empty;
}
