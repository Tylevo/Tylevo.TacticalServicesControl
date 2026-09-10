using Comfort.Common;
using Cysharp.Threading.Tasks;
using EFT;
using EFT.Communications;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.Insurance;
using EFT.UI.Screens;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

/// <summary>
/// Opens EFT's native in-raid item-delivery screen for the requesting player
/// while a TSC helicopter is landed.
///
/// The screen must use one of the GameWorld-owned transfer controllers. Those
/// are the only controllers serialized by EFT and synchronized by Fika. A
/// standalone TransferItemsController looks functional but drops
/// its staged cargo at raid end.
/// </summary>
internal static class FireSupportItemTransfer
{
	private const string BtrTraderId = "656f0f98d80a697f855d34b1";
	private const int TraderDataRefreshTimeoutSeconds = 5;
	private const int TransferMoveVerificationFrames = 60;
	private static readonly FieldInfo s_localServiceAvailabilityField =
		typeof(LocalPlayer)
			.GetFields(
				BindingFlags.Instance |
				BindingFlags.NonPublic |
				BindingFlags.Public)
			.FirstOrDefault(
				field =>
					field.FieldType ==
					typeof(HashSet<ETraderServiceType>));

	private static HeliCargoTransferPoint s_activePoint;
	private static Player s_activePlayer;
	private static HeliCargoTransferPoint s_sessionPoint;
	private static Player s_sessionPlayer;
	private static TransferItemsController s_transferController;
	private static TransferItemsInRaidScreen.TransferItemsInRaidScreenController s_screenController;
	private static LocalPlayer s_servicePlayer;
	private static Profile.TraderInfo s_serviceTraderInfo;
	private static ETraderServiceType s_serviceType = ETraderServiceType.None;
	private static bool s_restoreServiceAvailability;
	private static bool s_previousServiceAvailability;
	private static bool s_previousPurchasedInRaid;
	private static bool s_previousLocalServiceAvailability;
	private static bool s_servicePurchaseObserved;
	private static int s_sessionGeneration;
	private static int s_serverCargoGridWidth;
	private static int s_serverCargoGridHeight;
	private static int s_sessionCargoGridWidth;
	private static int s_sessionCargoGridHeight;

	internal static void SetServerCargoGridSize(int width, int height)
	{
		s_serverCargoGridWidth = CargoGridPolicy.IsValidWidth(width) ? width : 0;
		s_serverCargoGridHeight = CargoGridPolicy.IsValidHeight(height) ? height : 0;
	}

	internal static void OverrideCargoGridSize(
		TransferItemsController controller,
		string profileId,
		ref IntVec2 size)
	{
		if (FireSupportServerConfigClient.IsFikaClientHostAuthorityActive ||
		    controller == null || controller != s_transferController ||
		    s_screenController == null || s_sessionPoint == null ||
		    s_sessionPlayer == null || !s_sessionPlayer.IsYourPlayer ||
		    !string.Equals(profileId, s_sessionPlayer.ProfileId, StringComparison.Ordinal))
		{
			return;
		}

		// Only the active UH-60 screen gets these dimensions. EFT constructs or
		// safely clamps its own temporary grid and keeps the canonical delivery
		// stash (10 columns, vertically expandable) intact. A later Transit/BTR
		// screen asks for its native size again after this session is cleared.
		size = new IntVec2(
			s_sessionCargoGridWidth > 0 ? s_sessionCargoGridWidth : size.X,
			s_sessionCargoGridHeight > 0 ? s_sessionCargoGridHeight : size.Y);
	}

	internal static bool IsInteractionAvailable(
		HeliCargoTransferPoint point,
		Player player)
	{
		return PluginSettings.EnableHelicopterItemTransfer?.Value == true &&
		       !FireSupportServerConfigClient.IsFikaClientHostAuthorityActive &&
		       point != null &&
		       point == s_activePoint &&
		       player != null &&
		       player == s_activePlayer &&
		       player.IsYourPlayer &&
		       s_screenController == null &&
		       point.CanOpenItemTransfer(player);
	}

	internal static void RefreshInteractionAvailability()
	{
		s_activePlayer?.SearchForInteractions();
	}

	internal static HeliCargoTransferPoint GetActivePoint(Player player)
	{
		return player != null && player == s_activePlayer
			? s_activePoint
			: null;
	}

	internal static void EnterZone(
		HeliCargoTransferPoint point,
		Player player)
	{
		if (point == null || player == null || !player.IsYourPlayer)
		{
			return;
		}

		s_activePoint = point;
		s_activePlayer = player;
		player.SearchForInteractions();
	}

	internal static void LeaveZone(
		HeliCargoTransferPoint point,
		Player player)
	{
		if (point == null || point != s_activePoint)
		{
			return;
		}

		s_activePoint = null;
		s_activePlayer = null;
		player?.SearchForInteractions();
	}

	internal static void PointDestroyed(HeliCargoTransferPoint point)
	{
		if (point == null)
		{
			return;
		}

		bool wasActive = point == s_activePoint;
		bool ownsOpenSession = point == s_sessionPoint;
		Player activePlayer = wasActive ? s_activePlayer : null;
		if (wasActive || ownsOpenSession)
		{
			ForceClose("helicopter departed");
		}

		if (wasActive)
		{
			s_activePoint = null;
			s_activePlayer = null;
			activePlayer?.SearchForInteractions();
		}
	}

	internal static async void TryOpen(
		HeliCargoTransferPoint point,
		Player player)
	{
		if (!IsInteractionAvailable(point, player))
		{
			return;
		}

		if (player is not LocalPlayer localPlayer)
		{
			FailOpen(
				"UH-60 cargo transfer is unavailable because the local player controller was not ready.");
			return;
		}

		if (FireSupportServerConfigClient.IsFikaClientHostAuthorityActive)
		{
			FailOpen(
				"UH-60 cargo transfer is temporarily unavailable to non-host Fika players because native cargo transactions and delivery are not synchronized with the raid host.");
			return;
		}

		GameWorld gameWorld = Singleton<GameWorld>.Instance;
		if (!TryResolveCanonicalController(
			    gameWorld,
			    out TransferItemsController transferController,
			    out string controllerName))
		{
			FailOpen(
				"UH-60 cargo transfer is unavailable on this raid. No native delivery controller was initialized.");
			return;
		}

		if (!TryEnsurePlayerTransferGrid(
			    transferController,
			    player,
			    out string gridError))
		{
			FireSupportPlugin.LogSource?.LogWarning(
				$"UH-60 cargo transfer refused: the canonical {controllerName} controller could not initialize a grid for profile {player.ProfileId}. {gridError}");
			FailOpen(
				"UH-60 cargo transfer is not ready for this player. Nothing was removed from your inventory.");
			return;
		}

		if (!point.TryBeginItemTransfer(player))
		{
			return;
		}

		s_sessionPoint = point;
		s_sessionPlayer = player;
		int generation = ++s_sessionGeneration;
		try
		{
			await RefreshTraderServiceData(localPlayer);
			if (generation != s_sessionGeneration)
			{
				return;
			}

			if (point != s_activePoint ||
			    player != s_activePlayer ||
			    point != s_sessionPoint ||
			    player != s_sessionPlayer)
			{
				CleanupSession(generation, endPointSession: true);
				return;
			}

			if (!TryEnableService(localPlayer, transferController.ServiceType))
			{
				throw new InvalidOperationException(
					$"Trader service {transferController.ServiceType} was unavailable.");
			}

			var insurance = new InsuranceCompany(null, player.Profile);
			var screenController = new TransferItemsInRaidScreen.TransferItemsInRaidScreenController(
				player.Profile,
				player.InventoryController,
				player.QuestController,
				insurance,
				transferController);

			// Freeze the latest server dimensions for this screen. A config
			// refresh during loading/payment must not resize staged cargo.
			s_sessionCargoGridWidth = s_serverCargoGridWidth;
			s_sessionCargoGridHeight = s_serverCargoGridHeight;
			s_transferController = transferController;
			s_screenController = screenController;
			screenController.OnClose += () => OnScreenClosed(generation);
			screenController.ShowScreen(EScreenState.Queued);

			FireSupportPlugin.LogSource?.LogInfo(
				$"Opened UH-60 cargo transfer through EFT's canonical {controllerName} controller for profile {player.ProfileId}.");
		}
		catch (Exception ex)
		{
			if (generation != s_sessionGeneration)
			{
				return;
			}

			FireSupportPlugin.LogSource?.LogError(
				$"Failed to open UH-60 cargo transfer. {ex}");
			if (s_screenController != null)
			{
				ForceClose("screen initialization failed");
				point.EndItemTransfer(player);
			}
			else
			{
				CleanupSession(generation, endPointSession: true);
			}

			FailOpen(
				"UH-60 cargo transfer could not be opened. Nothing was removed from your inventory.");
		}
	}

	private static async Task RefreshTraderServiceData(LocalPlayer player)
	{
		Task refreshTask =
			player.UpdateTradersServiceData(BtrTraderId);
		Task completedTask = await Task.WhenAny(
			refreshTask,
			Task.Delay(
				TimeSpan.FromSeconds(
					TraderDataRefreshTimeoutSeconds)));
		if (completedTask != refreshTask)
		{
			ObserveLateTraderRefreshAsync(refreshTask);
			throw new TimeoutException(
				"Timed out while refreshing the native transfer service.");
		}

		await refreshTask;
	}

	internal static bool TryOverrideCargoTransferPrice(
		TransferItemsController controller,
		Stash temporaryStash,
		ETraderServiceType serviceType,
		out int price)
	{
		price = 0;
		return controller != null &&
		       controller == s_transferController &&
		       IsExactActiveCargoPurchase(
			       s_sessionPlayer?.InventoryController,
			       serviceType) &&
		       OwnsTemporaryCargoStash(controller, temporaryStash);
	}

	internal static void IncludeCargoHandlingInServicePurchase(
		InventoryController inventoryController,
		EFT.Quests.QuestController questController,
		string subServiceId,
		ref GlobalConfiguration.ServiceData serviceData)
	{
		if (serviceData == null ||
		    !string.IsNullOrEmpty(subServiceId) ||
		    questController != (s_sessionPlayer as LocalPlayer)?.QuestController ||
		    !IsExactActiveCargoPurchase(inventoryController, serviceData.ServiceType))
		{
			return;
		}

		Stash temporaryStash = s_transferController._transferContainers?
			.FirstOrDefault(stash => OwnsTemporaryCargoStash(s_transferController, stash));
		if (temporaryStash?.Grids?.Any(grid => grid?.Items?.Any() == true) != true)
		{
			return;
		}

		// EFT retains this argument in its operation result and reads it again
		// when the transaction executes. A private copy keeps the fee absent
		// even after this screen closes, without changing global BTR/Transit data.
		serviceData = CargoTransferServiceData.CopyWithIncludedHandling(serviceData);
	}

	private static bool OwnsTemporaryCargoStash(
		TransferItemsController controller,
		Stash temporaryStash)
	{
		return controller != null &&
		       temporaryStash != null &&
		       s_sessionPlayer != null &&
		       string.Equals(temporaryStash.Id, s_sessionPlayer.ProfileId, StringComparison.Ordinal) &&
		       controller._transferContainers?.Contains(temporaryStash) == true;
	}

	private static bool IsExactActiveCargoPurchase(
		InventoryController inventoryController,
		ETraderServiceType serviceType)
	{
		LocalPlayer player = s_sessionPlayer as LocalPlayer;
		return !FireSupportServerConfigClient.IsFikaClientHostAuthorityActive &&
		       player != null &&
		       player.IsYourPlayer &&
		       player == s_servicePlayer &&
		       player.InventoryController == inventoryController &&
		       s_sessionPoint != null &&
		       s_screenController != null &&
		       s_transferController != null &&
		       (serviceType == ETraderServiceType.TransitItemsDelivery ||
		        serviceType == ETraderServiceType.BtrItemsDelivery) &&
		       s_transferController.ServiceType == serviceType &&
		       s_serviceType == serviceType;
	}

	private static async void ObserveLateTraderRefreshAsync(
		Task refreshTask)
	{
		try
		{
			await refreshTask;
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource?.LogWarning(
				$"Native transfer-service refresh failed after the UH-60 cargo timeout. {ex}");
		}
	}

	internal static void NotifyServicePurchased(
		LocalPlayer player,
		ETraderServiceType serviceType)
	{
		if (player == null ||
		    player != s_servicePlayer ||
		    serviceType != s_serviceType ||
		    s_screenController == null ||
		    s_transferController == null ||
		    s_servicePurchaseObserved)
		{
			return;
		}

		s_servicePurchaseObserved = true;

		try
		{
			// This postfix runs immediately before EFT's native
			// MoveItemsFromTempStashToTransferStash call. Capture only the items
			// staged in this TSC screen, then wait until EFT proves those exact
			// IDs reached its persistent raid-delivery grid before tagging them.
			TransferItemsController controller =
				s_transferController;
			string profileId = player.ProfileId;
			HashSet<string> stagedItemIds =
				CollectTemporaryTransferItemIds(controller, profileId);
			int generation = s_sessionGeneration;
			if (stagedItemIds.Count == 0)
			{
				FireSupportPlugin.LogSource?.LogWarning(
					"UH-60 cargo purchase completed without any staged item IDs to mark; native delivery remains active.");
			}

			HeliCargoTransferPoint point = s_sessionPoint;
			MarkVerifiedUh60TransferAsync(
					controller,
					profileId,
					stagedItemIds,
					point,
					player,
					generation)
				.Forget();
		}
		catch (Exception ex)
		{
			// Never let messenger tagging interfere with EFT's native purchase
			// or the immediately following inventory move.
			FireSupportPlugin.LogSource?.LogWarning(
				$"Could not capture UH-60 cargo IDs for Pilot tagging; native delivery remains active. {ex}");
		}
	}

	private static HashSet<string> CollectTemporaryTransferItemIds(
		TransferItemsController controller,
		string profileId)
	{
		Stash temporaryStash =
			controller?._transferContainers?.FirstOrDefault(
				stash =>
					stash != null &&
					string.Equals(
						stash.Id,
						profileId,
						StringComparison.Ordinal));
		IEnumerable<Item> stagedRoots =
			temporaryStash?.Grids?.FirstOrDefault()?.Items;
		return CollectItemTreeIds(stagedRoots);
	}

	private static HashSet<string> CollectPersistentTransferItemIds(
		TransferItemsController controller,
		string profileId)
	{
		Grid playerGrid =
			controller?.Stash?.Grids?.FirstOrDefault(
				grid =>
					grid != null &&
					string.Equals(
						grid.ID,
						profileId,
						StringComparison.Ordinal));
		return CollectItemTreeIds(playerGrid?.Items);
	}

	private static HashSet<string> CollectItemTreeIds(
		IEnumerable<Item> roots)
	{
		var ids = new HashSet<string>(StringComparer.Ordinal);
		if (roots == null)
		{
			return ids;
		}

		var pending = new Queue<Item>(
			roots.Where(item => item != null));
		while (pending.Count > 0 && ids.Count < 4096)
		{
			Item item = pending.Dequeue();
			if (string.IsNullOrWhiteSpace(item.Id) ||
			    !ids.Add(item.Id))
			{
				continue;
			}

			if (item is not CompoundItem compoundItem)
			{
				continue;
			}

			IEnumerable<EFT.InventoryLogic.IContainer> containers =
				compoundItem.Containers;
			if (containers == null)
			{
				continue;
			}

			foreach (EFT.InventoryLogic.IContainer container in
			         containers)
			{
				if (container?.Items == null)
				{
					continue;
				}

				foreach (Item child in container.Items)
				{
					if (child != null)
					{
						pending.Enqueue(child);
					}
				}
			}
		}

		return ids;
	}

	private static async UniTaskVoid MarkVerifiedUh60TransferAsync(
		TransferItemsController controller,
		string profileId,
		HashSet<string> stagedItemIds,
		HeliCargoTransferPoint point,
		Player player,
		int generation)
	{
		try
		{
			int verificationFrame = 0;
			while (verificationFrame < TransferMoveVerificationFrames)
			{
				await UniTask.Yield();
				if (generation != s_sessionGeneration ||
				    string.IsNullOrWhiteSpace(profileId))
				{
					return;
				}

				if (point == null)
				{
					return;
				}

				// The purchase callback can precede the native screen-close
				// boundary by an arbitrary amount of player time. Start the
				// finite verification budget only after EFT reports a
				// successful close so a deliberate review of the transfer
				// screen cannot strand the helicopter in its paused state.
				if (!point.IsSuccessfulTransferPending)
				{
					continue;
				}

				verificationFrame++;
				HashSet<string> persistentItemIds =
					CollectPersistentTransferItemIds(
						controller,
						profileId);
				string[] verifiedItemIds =
					stagedItemIds
						.Where(persistentItemIds.Contains)
						.Take(4096)
						.ToArray();
				if (verifiedItemIds.Length == 0)
				{
					continue;
				}

				// EFT has now proven the paid cargo reached its persistent
				// delivery grid. Departure can begin without waiting on the
				// optional Pilot-messenger marker HTTP request.
				point.CompleteSuccessfulTransfer(player);
				bool marked =
					await FireSupportServerConfigClient
						.TryMarkUh60TransferAsync(
							profileId,
							verifiedItemIds);
				if (marked)
				{
					FireSupportPlugin.LogSource?.LogInfo(
						$"Marked {verifiedItemIds.Length} verified UH-60 cargo item IDs for Pilot delivery.");
				}
				else
				{
					FireSupportPlugin.LogSource?.LogWarning(
						"UH-60 cargo was transferred natively but could not be marked for the Pilot messenger; it will safely use the stock BTR delivery sender.");
				}

				return;
			}

			FireSupportPlugin.LogSource?.LogWarning(
				"UH-60 cargo purchase completed, but the staged item IDs were not observed in EFT's persistent transfer grid; no marker was written and native delivery remains active.");
			point?.EndSuccessfulTransferVerification();
		}
		catch (Exception ex)
		{
			point?.EndSuccessfulTransferVerification();
			FireSupportPlugin.LogSource?.LogWarning(
				$"UH-60 cargo marker verification failed; native delivery remains active. {ex}");
		}
	}

	internal static void ResetForRaidBoundary(string reason)
	{
		s_activePoint = null;
		s_activePlayer = null;
		ForceClose(reason);
		RestoreServiceAvailability();
		s_sessionGeneration++;
	}

	private static bool TryResolveCanonicalController(
		GameWorld gameWorld,
		out TransferItemsController controller,
		out string controllerName)
	{
		controller = gameWorld?.TransitController?.TransferItemsController;
		if (IsUsableCanonicalController(controller))
		{
			controllerName = "Transit delivery";
			return true;
		}

		controller = gameWorld?.BtrController?.TransferItemsController;
		if (IsUsableCanonicalController(controller))
		{
			controllerName = "BTR delivery";
			return true;
		}

		controller = null;
		controllerName = string.Empty;
		return false;
	}

	private static bool IsUsableCanonicalController(
		TransferItemsController controller)
	{
		return controller?.Stash != null &&
		       (controller.ServiceType == ETraderServiceType.TransitItemsDelivery ||
		        controller.ServiceType == ETraderServiceType.BtrItemsDelivery);
	}

	private static bool HasPlayerTransferGrid(
		TransferItemsController controller,
		string profileId)
	{
		return controller?.Stash?.Grids != null &&
		       !string.IsNullOrWhiteSpace(profileId) &&
		       controller.Stash.Grids.Any(
			       grid => grid != null &&
			               string.Equals(
				               grid.ID,
				               profileId,
				               StringComparison.Ordinal));
	}

	private static bool TryEnsurePlayerTransferGrid(
		TransferItemsController controller,
		Player player,
		out string error)
	{
		error = string.Empty;
		if (controller == null ||
		    player == null ||
		    string.IsNullOrWhiteSpace(player.ProfileId))
		{
			error = "The native controller or requester profile was unavailable.";
			return false;
		}

		if (HasPlayerTransferGrid(controller, player.ProfileId))
		{
			return true;
		}

		try
		{
			// EFT normally initializes this grid only when the player first
			// enters a Transit/BTR interaction. The TSC helicopter may be the
			// first delivery service used in a fresh raid, so initialize once.
			// Never repeat this for an existing grid: InitPlayerStash clears
			// and recreates that player's native staging grid.
			controller.InitPlayerStash(player);
		}
		catch (Exception ex)
		{
			error = ex.Message;
			return false;
		}

		if (HasPlayerTransferGrid(controller, player.ProfileId))
		{
			return true;
		}

		error = "The native controller did not create the requester grid.";
		return false;
	}

	private static bool TryEnableService(
		LocalPlayer player,
		ETraderServiceType serviceType)
	{
		if (player?.Profile?.TradersInfo == null ||
		    !Singleton<GlobalConfiguration>.Instantiated)
		{
			return false;
		}

		GlobalConfiguration backend = Singleton<GlobalConfiguration>.Instance;
		if (backend?.ServicesData == null ||
		    !backend.ServicesData.TryGetValue(
			    serviceType,
			    out GlobalConfiguration.ServiceData serviceData) ||
		    !player.Profile.TradersInfo.TryGetValue(
			    serviceData.TraderId,
			    out Profile.TraderInfo traderInfo))
		{
			return false;
		}

		if (s_localServiceAvailabilityField?.GetValue(player) is not
		    HashSet<ETraderServiceType> localServiceAvailability)
		{
			FireSupportPlugin.LogSource?.LogWarning(
				"UH-60 cargo transfer refused: EFT's local trader-service availability set could not be inspected safely.");
			return false;
		}

		s_servicePlayer = player;
		s_serviceTraderInfo = traderInfo;
		s_serviceType = serviceType;
		s_previousServiceAvailability =
			traderInfo.IsServiceAvailableForPurchase(serviceType);
		s_previousPurchasedInRaid =
			traderInfo.IsServiceAlreadyPurchased(serviceType);
		s_previousLocalServiceAvailability =
			localServiceAvailability.Contains(serviceType);
		s_servicePurchaseObserved = false;
		s_restoreServiceAvailability = !s_previousServiceAvailability;
		if (s_restoreServiceAvailability)
		{
			traderInfo.SetServiceAvailability(
				serviceType,
				availabilityState: true,
				wasPurchasedInRaid: s_previousPurchasedInRaid);
		}

		player.SetTraderServiceAvailability(serviceType, available: true);
		return traderInfo.IsServiceAvailableForPurchase(serviceType);
	}

	private static void OnScreenClosed(int generation)
	{
		if (generation != s_sessionGeneration)
		{
			return;
		}

		HeliCargoTransferPoint point = s_sessionPoint;
		Player player = s_sessionPlayer;
		bool purchaseObserved = s_servicePurchaseObserved;
		if (purchaseObserved)
		{
			// This is the first safe lifecycle boundary after EFT's native
			// purchase callback and item move. A cancelled or rejected screen
			// never enters this one-way completion state.
			point?.BeginSuccessfulTransfer(player);
		}

		CleanupSession(generation, endPointSession: true);
	}

	private static void CleanupSession(
		int generation,
		bool endPointSession)
	{
		if (generation != s_sessionGeneration)
		{
			return;
		}

		HeliCargoTransferPoint point = s_sessionPoint;
		Player player = s_sessionPlayer;
		s_sessionPoint = null;
		s_sessionPlayer = null;
		s_transferController = null;
		s_screenController = null;
		s_sessionCargoGridWidth = 0;
		s_sessionCargoGridHeight = 0;
		RestoreServiceAvailability();

		if (endPointSession && point != null)
		{
			point.EndItemTransfer(player);
		}
	}

	private static void ForceClose(string reason)
	{
		TransferItemsInRaidScreen.TransferItemsInRaidScreenController screenController =
			s_screenController;
		bool hadSession =
			screenController != null ||
			s_sessionPoint != null ||
			s_sessionPlayer != null;
		s_sessionPoint = null;
		s_sessionPlayer = null;
		s_transferController = null;
		s_sessionCargoGridWidth = 0;
		s_sessionCargoGridHeight = 0;
		if (screenController == null)
		{
			if (hadSession)
			{
				s_sessionGeneration++;
			}

			RestoreServiceAvailability();
			return;
		}

		int invalidatedGeneration = s_sessionGeneration;
		s_sessionGeneration++;
		s_screenController = null;
		RestoreServiceAvailability();

		try
		{
			Task closeTask =
				screenController.CloseForcedAndReturnToRoot();
			ObserveForcedCloseAsync(
				closeTask,
				reason,
				invalidatedGeneration);
			FireSupportPlugin.LogSource?.LogInfo(
				$"Requested forced close of UH-60 cargo transfer: {reason}.");
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource?.LogWarning(
				$"Could not force-close UH-60 cargo transfer generation {invalidatedGeneration}. {ex}");
		}
	}

	private static async void ObserveForcedCloseAsync(
		Task closeTask,
		string reason,
		int generation)
	{
		try
		{
			await closeTask;
			FireSupportPlugin.LogSource?.LogInfo(
				$"Completed forced close of UH-60 cargo transfer generation {generation}: {reason}.");
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource?.LogWarning(
				$"Asynchronous forced close failed for UH-60 cargo transfer generation {generation}. {ex}");
		}
	}

	private static void RestoreServiceAvailability()
	{
		LocalPlayer player = s_servicePlayer;
		Profile.TraderInfo traderInfo = s_serviceTraderInfo;
		ETraderServiceType serviceType = s_serviceType;
		bool restore = s_restoreServiceAvailability;
		bool previousAvailability = s_previousServiceAvailability;
		bool previousPurchased = s_previousPurchasedInRaid;
		bool previousLocalAvailability =
			s_previousLocalServiceAvailability;
		bool purchaseObserved =
			s_servicePurchaseObserved ||
			(traderInfo != null &&
			 traderInfo.IsServiceAlreadyPurchased(serviceType) !=
			 previousPurchased);

		s_servicePlayer = null;
		s_serviceTraderInfo = null;
		s_serviceType = ETraderServiceType.None;
		s_restoreServiceAvailability = false;
		s_previousServiceAvailability = false;
		s_previousPurchasedInRaid = false;
		s_previousLocalServiceAvailability = false;
		s_servicePurchaseObserved = false;

		if (purchaseObserved)
		{
			return;
		}

		try
		{
			if (restore)
			{
				traderInfo?.SetServiceAvailability(
					serviceType,
					previousAvailability,
					previousPurchased);
			}

			player?.SetTraderServiceAvailability(
				serviceType,
				available: previousLocalAvailability);
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource?.LogWarning(
				$"Could not restore trader service availability after UH-60 cargo transfer. {ex}");
		}
	}

	private static void FailOpen(string message)
	{
		NotificationManager.DisplayWarningNotification(
			message,
			EFT.Communications.ENotificationDurationType.Long);
	}
}
