using Comfort.Common;
using Cysharp.Threading.Tasks;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
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
/// standalone TransferItemsControllerAbstractClass looks functional but drops
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
	private static TransferItemsControllerAbstractClass s_transferController;
	private static TransferItemsInRaidScreen.GClass3893 s_screenController;
	private static LocalPlayer s_servicePlayer;
	private static Profile.TraderInfo s_serviceTraderInfo;
	private static ETraderServiceType s_serviceType = ETraderServiceType.None;
	private static bool s_restoreServiceAvailability;
	private static bool s_previousServiceAvailability;
	private static bool s_previousPurchasedInRaid;
	private static bool s_previousLocalServiceAvailability;
	private static bool s_servicePurchaseObserved;
	private static bool s_stashFeePurchaseInFlight;
	private static int s_sessionGeneration;
	[ThreadStatic]
	private static bool s_nativePurchaseBypass;

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
				"UH-60 cargo transfer is temporarily unavailable to non-host Fika players because EFT's native transfer price is not synchronized with the raid host.");
			return;
		}

		GameWorld gameWorld = Singleton<GameWorld>.Instance;
		if (!TryResolveCanonicalController(
			    gameWorld,
			    out TransferItemsControllerAbstractClass transferController,
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

			var insurance = new InsuranceCompanyClass(null, player.Profile);
			var screenController = new TransferItemsInRaidScreen.GClass3893(
				player.Profile,
				player.InventoryController,
				player.AbstractQuestControllerClass,
				insurance,
				transferController);

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

	internal static bool TryInterceptTraderServicePurchase(
		InventoryController inventoryController,
		ETraderServiceType serviceType,
		AbstractQuestControllerClass questController,
		string subServiceId,
		out Task<bool> purchaseTask)
	{
		purchaseTask = null;
		if (s_nativePurchaseBypass ||
		    PluginSettings.HelicopterTransferFeeSource?.Value !=
		    HelicopterTransferFeeSource.Stash ||
		    questController !=
		    (s_sessionPlayer as LocalPlayer)?.AbstractQuestControllerClass ||
		    !IsExactActiveCargoPurchase(
			    inventoryController,
			    serviceType))
		{
			return false;
		}

		purchaseTask = PurchaseCargoTransferWithStashFeeAsync(
			inventoryController,
			serviceType,
			questController,
			subServiceId);
		return true;
	}

	internal static void ApplyStashFeeTransferButtonState(
		InventoryController inventoryController,
		StashItemClass temporaryStash,
		TransferItemsControllerAbstractClass transferController,
		DefaultUIButton transferButton)
	{
		if (PluginSettings.HelicopterTransferFeeSource?.Value !=
		    HelicopterTransferFeeSource.Stash ||
		    transferButton == null ||
		    transferController == null ||
		    transferController != s_transferController ||
		    temporaryStash == null ||
		    !IsExactActiveCargoPurchase(
			    inventoryController,
			    transferController.ServiceType))
		{
			return;
		}

		LocalPlayer player = s_sessionPlayer as LocalPlayer;
		bool ownsTemporaryStash =
			player != null &&
			string.Equals(
				temporaryStash.Id,
				player.ProfileId,
				StringComparison.Ordinal) &&
			transferController.List_0?.Contains(temporaryStash) == true;
		if (!ownsTemporaryStash)
		{
			return;
		}

		bool hasItems =
			temporaryStash.Grids?.Any(
				grid => grid?.Items?.Any() == true) == true;
		transferButton.Interactable = hasItems;
		if (hasItems)
		{
			// method_1 may have just applied EFT's carried-cash warning. The
			// stash path performs an authoritative balance check at Prepare, so
			// carried money must not leave a stale disabled tooltip behind.
			transferButton.SetDisabledTooltip(
				string.Empty,
				false);
		}
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
		       s_transferController.ServiceType == serviceType &&
		       s_serviceType == serviceType;
	}

	private static async Task<bool> PurchaseCargoTransferWithStashFeeAsync(
		InventoryController inventoryController,
		ETraderServiceType serviceType,
		AbstractQuestControllerClass questController,
		string subServiceId)
	{
		if (s_stashFeePurchaseInFlight)
		{
			FailOpen(
				"A UH-60 cargo transfer payment is already being processed.");
			return false;
		}

		s_stashFeePurchaseInFlight = true;
		bool prepared = false;
		string transactionId = Guid.NewGuid().ToString("N");
		string profileId = s_sessionPlayer?.ProfileId?.Trim() ?? string.Empty;
		string sessionKey =
			FireSupportServerConfigClient.GetAuthenticatedSessionKey();
		int generation = s_sessionGeneration;
		int nativeFeeRoubles = 0;
		try
		{
			try
			{
			if (!TryGetExactNativeFee(
				    inventoryController,
				    serviceType,
				    generation,
				    out nativeFeeRoubles,
				    out string feeError))
			{
				FailOpen(
					$"UH-60 cargo transfer payment was not started. {feeError}");
				return false;
			}

			if (nativeFeeRoubles == 0)
			{
				// There is no stash mutation to authorize or journal. Preserve
				// the native transfer transaction, but avoid creating a
				// zero-value server record that could consume journal capacity.
				return await StartNativePurchaseWithZeroRubCost(
					inventoryController,
					serviceType,
					questController,
					subServiceId);
			}

			if (string.IsNullOrWhiteSpace(profileId) ||
			    string.IsNullOrWhiteSpace(sessionKey) ||
			    !FireSupportServerConfigClient.IsAuthenticatedProfile(
				    profileId))
			{
				FailOpen(
					"UH-60 cargo transfer could not verify the authenticated PMC stash.");
				return false;
			}

			await Uh60TransferFeeRecoveryStore.RetryMatchingProfileAsync(
				profileId,
				"before a new stash-funded cargo purchase");
			if (!Uh60TransferFeeRecoveryStore.CanStartNewTransaction(
				    profileId,
				    out string recoveryBlockReason))
			{
				FailOpen(recoveryBlockReason);
				return false;
			}

			if (!string.Equals(
				    sessionKey,
				    FireSupportServerConfigClient
					    .GetAuthenticatedSessionKey(),
				    StringComparison.Ordinal) ||
			    !FireSupportServerConfigClient.IsAuthenticatedProfile(
				    profileId) ||
			    !TryGetExactNativeFee(
				    inventoryController,
				    serviceType,
				    generation,
				    out int feeAfterRecovery,
				    out _) ||
			    feeAfterRecovery != nativeFeeRoubles)
			{
				FailOpen(
					"UH-60 cargo transfer changed while an earlier stash payment was being reconciled.");
				return false;
			}

			FireSupportUh60TransferFeeResponse prepareResponse =
					await FireSupportServerConfigClient
						.PrepareUh60TransferFeeAsync(
							profileId,
							transactionId,
							nativeFeeRoubles);
			if (!IsPreparedFeeResponse(prepareResponse))
			{
				bool reconciled =
					await ReconcileAmbiguousPrepareFailureAsync(
					profileId,
					transactionId,
					nativeFeeRoubles,
					prepareResponse);
				if (reconciled)
				{
					DisplayStashFeeFailure(
						prepareResponse,
						"UH-60 cargo transfer fee was declined");
				}
				else
				{
					FailOpen(
						"UH-60 cargo transfer did not start, but its stash payment state could not be reconciled. Do not retry this transfer until the TSC server log is checked.");
				}
				return false;
			}

			prepared = true;

			// The server round trip intentionally happens before EFT is allowed
			// to touch the carried inventory. Revalidate the exact screen,
			// profile, service, and dynamically calculated native fee so a
			// closed screen or changed cargo cannot spend a stale quote.
			if (!string.Equals(
				    sessionKey,
				    FireSupportServerConfigClient
					    .GetAuthenticatedSessionKey(),
				    StringComparison.Ordinal) ||
			    !FireSupportServerConfigClient.IsAuthenticatedProfile(
				    profileId) ||
			    !TryGetExactNativeFee(
				    inventoryController,
				    serviceType,
				    generation,
				    out int revalidatedFee,
				    out _) ||
			    revalidatedFee != nativeFeeRoubles)
			{
				bool refunded = await RefundPreparedStashFeeAsync(
					profileId,
					transactionId,
					nativeFeeRoubles,
					"cargo session changed before native purchase");
				prepared = false;
				FailOpen(
					refunded
						? "UH-60 cargo transfer changed while payment was processing. The stash fee was refunded."
						: "UH-60 cargo transfer changed before submission, but its stash refund could not be confirmed. Do not retry until the TSC server log is checked.");
				return false;
			}

			Task<bool> nativePurchaseTask =
				StartNativePurchaseWithZeroRubCost(
					inventoryController,
					serviceType,
					questController,
					subServiceId);
			bool nativePurchaseSucceeded = await nativePurchaseTask;
			if (!nativePurchaseSucceeded)
			{
				bool refunded = await RefundPreparedStashFeeAsync(
					profileId,
					transactionId,
					nativeFeeRoubles,
					"native purchase returned false");
				prepared = false;
				if (!refunded)
				{
					FailOpen(
						"UH-60 cargo was not submitted, but its stash refund could not be confirmed. Do not retry until the TSC server log is checked.");
				}
				return false;
			}
			}
			catch (Exception ex)
			{
				if (prepared)
				{
					bool refunded = await RefundPreparedStashFeeAsync(
						profileId,
						transactionId,
						nativeFeeRoubles,
						$"native purchase exception: {ex.GetType().Name}");
					if (!refunded)
					{
						FailOpen(
							"UH-60 cargo was not submitted, but its stash refund could not be confirmed. Do not retry until the TSC server log is checked.");
					}
				}

				FireSupportPlugin.LogSource?.LogWarning(
					$"UH-60 stash-funded cargo purchase failed before native success. transaction={transactionId} {ex}");
				throw;
			}

			// EFT has accepted and serialized the native service transaction.
			// From this point forward it owns delivery persistence. Commit is
			// idempotent, and an acknowledgement loss must never trigger a
			// refund that would make a completed transfer free.
			prepared = false;
			bool commitIntentPersisted =
				Uh60TransferFeeRecoveryStore.PersistCommitIntent(
					profileId,
					transactionId,
					nativeFeeRoubles,
					"native cargo purchase succeeded");
			bool committed =
				await Uh60TransferFeeRecoveryStore.TryResolveIntentAsync(
					profileId,
					transactionId,
					"native cargo purchase succeeded");
			if (!commitIntentPersisted || !committed)
			{
				FireSupportPlugin.LogSource?.LogWarning(
					$"UH-60 cargo transfer completed natively, but its durable fee commit remains pending. transaction={transactionId}. No refund was attempted.");
			}

			return true;
		}
		finally
		{
			s_stashFeePurchaseInFlight = false;
		}
	}

	private static bool TryGetExactNativeFee(
		InventoryController inventoryController,
		ETraderServiceType serviceType,
		int generation,
		out int feeRoubles,
		out string error)
	{
		feeRoubles = 0;
		error = string.Empty;
		if (generation != s_sessionGeneration ||
		    !IsExactActiveCargoPurchase(
			    inventoryController,
			    serviceType))
		{
			error = "The cargo session is no longer active.";
			return false;
		}

		if (!Singleton<BackendConfigSettingsClass>.Instantiated ||
		    Singleton<BackendConfigSettingsClass>.Instance?.ServicesData ==
		    null ||
		    !Singleton<BackendConfigSettingsClass>.Instance.ServicesData
			    .TryGetValue(
				    serviceType,
				    out BackendConfigSettingsClass.ServiceData
					    serviceData) ||
		    serviceData?.ServiceItemCost == null ||
		    serviceData.ServiceItemCost.Count != 1 ||
		    !serviceData.ServiceItemCost.TryGetValue(
			    PaymentCurrencyInfo.RoubleTemplateId,
			    out int quotedFee) ||
		    quotedFee < 0)
		{
			error = "EFT's native RUB fee quote was unavailable.";
			return false;
		}

		StashItemClass temporaryStash =
			s_transferController?.List_0?.FirstOrDefault(
				stash =>
					stash != null &&
					string.Equals(
						stash.Id,
						s_sessionPlayer?.ProfileId,
						StringComparison.Ordinal));
		if (temporaryStash == null)
		{
			error = "The native cargo staging grid was unavailable.";
			return false;
		}

		int calculatedFee =
			s_transferController.GetGridItemsPrice(temporaryStash);
		if (calculatedFee != quotedFee)
		{
			error = "EFT's native cargo fee changed before payment.";
			return false;
		}

		feeRoubles = quotedFee;
		return true;
	}

	private static Task<bool> StartNativePurchaseWithZeroRubCost(
		InventoryController inventoryController,
		ETraderServiceType serviceType,
		AbstractQuestControllerClass questController,
		string subServiceId)
	{
		if (!Singleton<BackendConfigSettingsClass>.Instantiated ||
		    Singleton<BackendConfigSettingsClass>.Instance?.ServicesData ==
		    null ||
		    !Singleton<BackendConfigSettingsClass>.Instance.ServicesData
			    .TryGetValue(
				    serviceType,
				    out BackendConfigSettingsClass.ServiceData
					    serviceData) ||
		    serviceData?.ServiceItemCost == null)
		{
			throw new InvalidOperationException(
				"EFT's native trader-service cost dictionary was unavailable.");
		}

		var serviceItemCost = serviceData.ServiceItemCost;
		KeyValuePair<string, int>[] originalCosts =
			serviceItemCost.ToArray();
		Task<bool> nativePurchaseTask;
		s_nativePurchaseBypass = true;
		try
		{
			serviceItemCost.Clear();
			serviceItemCost.Add(
				PaymentCurrencyInfo.RoubleTemplateId,
				0);
			nativePurchaseTask =
				inventoryController.TryPurchaseTraderService(
					serviceType,
					questController,
					subServiceId);
		}
		finally
		{
			try
			{
				// The native async state machine builds its transaction before
				// returning this Task. Restore the complete dynamic dictionary
				// now—before awaiting—so no other EFT service observes zero.
				serviceItemCost.Clear();
				foreach (KeyValuePair<string, int> cost in originalCosts)
				{
					serviceItemCost.Add(cost.Key, cost.Value);
				}
			}
			finally
			{
				s_nativePurchaseBypass = false;
			}
		}

		return nativePurchaseTask ??
		       throw new InvalidOperationException(
			       "EFT returned no native trader-service purchase task.");
	}

	private static bool IsPreparedFeeResponse(
		FireSupportUh60TransferFeeResponse response)
	{
		return response?.Ok == true &&
		       (string.Equals(
			        response.State,
			        "Prepared",
			        StringComparison.OrdinalIgnoreCase) ||
		        string.Equals(
			        response.State,
			        "Committed",
			        StringComparison.OrdinalIgnoreCase));
	}

	private static async Task<bool> RefundPreparedStashFeeAsync(
		string profileId,
		string transactionId,
		int amountRoubles,
		string reason,
		bool notFoundIsSuccess = false)
	{
		bool persisted =
			Uh60TransferFeeRecoveryStore.PersistRefundIntent(
				profileId,
				transactionId,
				amountRoubles,
				notFoundIsSuccess,
				reason);
		bool resolved =
			await Uh60TransferFeeRecoveryStore.TryResolveIntentAsync(
				profileId,
				transactionId,
				reason);
		if (!persisted)
		{
			FireSupportPlugin.LogSource?.LogError(
				$"UH-60 cargo transfer fee refund intent could not be durably persisted. transaction={transactionId} trigger={reason}");
		}

		return resolved;
	}

	private static async Task<bool> ReconcileAmbiguousPrepareFailureAsync(
		string profileId,
		string transactionId,
		int amountRoubles,
		FireSupportUh60TransferFeeResponse prepareResponse)
	{
		string reason = prepareResponse?.Reason ?? string.Empty;
		// Native EFT purchase has not started here. Reconcile every rejected
		// Prepare, not only transport ambiguity: a profile save can succeed
		// before the server fails to persist Prepared, and an internal failure
		// can therefore carry a durable debit despite an explicit rejection.
		// Refund is idempotent for DebitPending/Prepared and harmless when the
		// transaction never reached the journal.
		return await RefundPreparedStashFeeAsync(
			profileId,
			transactionId,
			amountRoubles,
			$"rejected Prepare response: {reason}",
			notFoundIsSuccess: true);
	}

	private static void DisplayStashFeeFailure(
		FireSupportUh60TransferFeeResponse response,
		string prefix)
	{
		string detail = response?.Reason switch
		{
			"InsufficientFunds" or
				"InsufficientRoubles" =>
				"Not enough RUB is available in the PMC stash.",
			"ProfileSessionChanged" => "The authenticated PMC profile changed.",
			"ProfileNotFound" => "The authenticated PMC profile was not found.",
			"InvalidRequest" => "The native fee quote was invalid.",
			"RequestFailed" => "The TSC server did not accept the stash payment request.",
			"ServerConfigUnavailable" => "The TSC server is unavailable or does not support stash-funded cargo fees.",
			_ => response?.Reason ?? "The TSC server returned an invalid response."
		};
		FailOpen($"{prefix}. {detail}");
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
			TransferItemsControllerAbstractClass controller =
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
		TransferItemsControllerAbstractClass controller,
		string profileId)
	{
		StashItemClass temporaryStash =
			controller?.List_0?.FirstOrDefault(
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
		TransferItemsControllerAbstractClass controller,
		string profileId)
	{
		StashGridClass playerGrid =
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
		TransferItemsControllerAbstractClass controller,
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
		out TransferItemsControllerAbstractClass controller,
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
		TransferItemsControllerAbstractClass controller)
	{
		return controller?.Stash != null &&
		       (controller.ServiceType == ETraderServiceType.TransitItemsDelivery ||
		        controller.ServiceType == ETraderServiceType.BtrItemsDelivery);
	}

	private static bool HasPlayerTransferGrid(
		TransferItemsControllerAbstractClass controller,
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
		TransferItemsControllerAbstractClass controller,
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
		    !Singleton<BackendConfigSettingsClass>.Instantiated)
		{
			return false;
		}

		BackendConfigSettingsClass backend = Singleton<BackendConfigSettingsClass>.Instance;
		if (backend?.ServicesData == null ||
		    !backend.ServicesData.TryGetValue(
			    serviceType,
			    out BackendConfigSettingsClass.ServiceData serviceData) ||
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
			traderInfo.AlreadyPurchasedServices.Contains(serviceType);
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
		RestoreServiceAvailability();

		if (endPointSession && point != null)
		{
			point.EndItemTransfer(player);
		}
	}

	private static void ForceClose(string reason)
	{
		TransferItemsInRaidScreen.GClass3893 screenController =
			s_screenController;
		bool hadSession =
			screenController != null ||
			s_sessionPoint != null ||
			s_sessionPlayer != null;
		s_sessionPoint = null;
		s_sessionPlayer = null;
		s_transferController = null;
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
			 traderInfo.AlreadyPurchasedServices.Contains(serviceType) !=
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
		NotificationManagerClass.DisplayWarningNotification(
			message,
			EFT.Communications.ENotificationDurationType.Long);
	}
}
