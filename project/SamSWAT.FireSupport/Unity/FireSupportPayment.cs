using Comfort.Common;
using Cysharp.Threading.Tasks;
using EFT;
using EFT.Communications;
using EFT.InventoryLogic;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public static class FireSupportPayment
{
	private const int MaxServerFinalizationAttempts = 65;
	private const int MaxServerFinalizationRetryDelaySeconds = 30;

	private readonly struct CostLogState(int cost, string source)
	{
		public readonly int Cost = cost;
		public readonly string Source = source;
	}

	private readonly struct AuthorizationMutationAttempt(
		bool success,
		bool retryable,
		string reason)
	{
		public readonly bool Success = success;
		public readonly bool Retryable = retryable;
		public readonly string Reason = reason;
	}

	private static int? _syncedStrafeCost;
	private static int? _syncedDoubleStrafeCost;
	private static int? _syncedExtractionCost;
	private static int? _syncedPriorityExfilCost;
	private static int? _syncedUavCost;
	private static int? _syncedFocusedSweepCost;
	private static PaymentMode? _syncedPaymentMode;
	private static PaymentSource? _syncedPaymentSource;
	private static int? _serverStrafeCost;
	private static int? _serverDoubleStrafeCost;
	private static int? _serverExtractionCost;
	private static int? _serverPriorityExfilCost;
	private static int? _serverUavCost;
	private static int? _serverFocusedSweepCost;
	private static PaymentMode? _serverPaymentMode;
	private static PaymentSource? _serverPaymentSource;
	private static int? _serverStashRoubleBalance;
	private static int _serverConfigRevision;
	private static bool _serverConfigUnavailable;
	private static string _serverConfigUnavailableReason;
	private static bool _serverPurchasePersistenceEnabled;
	private static bool _serverRefundFailedDispatch = true;
	private static bool _serverSpendCreditsBeforeCash = true;
	private static bool _serverAllowAutoPurchaseOnUse = true;
	private static FireSupportPurchaseResponse _lastPurchaseDenial;
	private static readonly SemaphoreSlim s_serverLedgerMutationGate = new(1, 1);
	private static readonly Dictionary<ESupportType, CostLogState> s_lastLoggedCost = new(new SupportTypeComparer());

	public static event EventHandler SettingsChanged;

	public static bool HasSyncedCosts =>
		_syncedStrafeCost.HasValue ||
		_syncedDoubleStrafeCost.HasValue ||
		_syncedExtractionCost.HasValue ||
		_syncedPriorityExfilCost.HasValue ||
		_syncedUavCost.HasValue ||
		_syncedFocusedSweepCost.HasValue;

	public static bool HasServerConfigCosts =>
		_serverStrafeCost.HasValue ||
		_serverDoubleStrafeCost.HasValue ||
		_serverExtractionCost.HasValue ||
		_serverPriorityExfilCost.HasValue ||
		_serverUavCost.HasValue ||
		_serverFocusedSweepCost.HasValue;

	public static int ServerConfigRevision => _serverConfigRevision;

	public static string GetLastPurchaseDenialTitle(ESupportType supportType)
	{
		FireSupportPurchaseResponse denial = GetLastPurchaseDenial(supportType);
		return denial?.Reason switch
		{
			"AuthorizationLimitReached" => "AUTHORIZATION LIMIT REACHED",
			"InsufficientRoubles" => "INSUFFICIENT FUNDS",
			"RateLimited" => "PURCHASE ALREADY PROCESSING",
			"ServerConfigUnavailable" or "RequestFailed" or "InvalidServerResponse" => "SERVER PAYMENT UNAVAILABLE",
			"ProfileNotFound" or "ProfileSessionMismatch" => "PROFILE VERIFY FAILED",
			"ServiceUnavailable" => "SERVICE UNAVAILABLE",
			"PaymentSourceNotServerBacked" => "SERVER PAYMENT DISABLED",
			"ProfileSaveFailed" => "PROFILE SAVE FAILED",
			_ => "AUTHORIZATION DENIED"
		};
	}

	public static string GetLastPurchaseDenialDetail(ESupportType supportType)
	{
		FireSupportPurchaseResponse denial = GetLastPurchaseDenial(supportType);
		if (denial == null)
		{
			return "No authorization was granted.";
		}

		switch (denial.Reason)
		{
			case "AuthorizationLimitReached":
				int held = GetAuthorizationCount(denial, supportType);
				return held > 0
					? $"{held} held. Deploy one from the Uplink before buying more."
					: "Deploy an existing authorization from the Uplink before buying more.";
			case "InsufficientRoubles":
				return $"{GetEffectiveBalanceLabel()}: {FormatRoubles(Math.Max(denial.NewBalance, GetEffectiveBalance()))}.";
			case "RateLimited":
				return "Wait a moment, then try the purchase again.";
			case "ServerConfigUnavailable":
			case "RequestFailed":
			case "InvalidServerResponse":
				return "Check the TSC server and dashboard connection.";
			case "ProfileNotFound":
			case "ProfileSessionMismatch":
				return "The server could not verify the active profile.";
			case "ServiceUnavailable":
				return $"{GetSupportName(supportType)} is disabled in host settings.";
			case "PaymentSourceNotServerBacked":
				return "Use stash-backed payment or carried roubles.";
			case "ProfileSaveFailed":
				return "The debit could not be saved to the profile.";
			default:
				return string.IsNullOrWhiteSpace(denial.Reason)
					? "No authorization was granted."
					: $"Server reason: {denial.Reason}.";
		}
	}

	public static void SetSyncedCosts(
		int strafeCost,
		int doubleStrafeCost,
		int extractionCost,
		int priorityExfilCost,
		int uavCost,
		int focusedSweepCost)
	{
		_syncedStrafeCost = strafeCost;
		_syncedDoubleStrafeCost = doubleStrafeCost;
		_syncedExtractionCost = extractionCost;
		_syncedPriorityExfilCost = priorityExfilCost;
		_syncedUavCost = uavCost;
		_syncedFocusedSweepCost = focusedSweepCost;
		TscDiagnostics.LogPayment(
			$"Using host TSC prices: A-10={FormatRoubles(strafeCost)}, A-10 double pass={FormatRoubles(doubleStrafeCost)}, UH-60={FormatRoubles(extractionCost)}, Priority exfil={FormatRoubles(priorityExfilCost)}, UAV={FormatRoubles(uavCost)}, Focused sweep={FormatRoubles(focusedSweepCost)}");
	}

	public static void ClearSyncedCosts()
	{
		bool hadSyncedSettings = HasSyncedCosts || _syncedPaymentMode.HasValue || _syncedPaymentSource.HasValue;
		_syncedStrafeCost = null;
		_syncedDoubleStrafeCost = null;
		_syncedExtractionCost = null;
		_syncedPriorityExfilCost = null;
		_syncedUavCost = null;
		_syncedFocusedSweepCost = null;
		_syncedPaymentMode = null;
		_syncedPaymentSource = null;
		if (hadSyncedSettings)
		{
			TscDiagnostics.LogPayment("Cleared host TSC prices, payment mode, and payment source.");
		}
	}

	public static void SetServerConfigCosts(
		int strafeCost,
		int doubleStrafeCost,
		int extractionCost,
		int priorityExfilCost,
		int uavCost,
		int focusedSweepCost,
		int revision)
	{
		_serverStrafeCost = strafeCost;
		_serverDoubleStrafeCost = doubleStrafeCost;
		_serverExtractionCost = extractionCost;
		_serverPriorityExfilCost = priorityExfilCost;
		_serverUavCost = uavCost;
		_serverFocusedSweepCost = focusedSweepCost;
		_serverConfigRevision = revision;
		_serverConfigUnavailable = false;
		_serverConfigUnavailableReason = null;
		TscDiagnostics.LogPayment(
			$"Using server URL TSC prices revision={revision}: A-10={FormatRoubles(strafeCost)}, A-10 double pass={FormatRoubles(doubleStrafeCost)}, UH-60={FormatRoubles(extractionCost)}, Priority exfil={FormatRoubles(priorityExfilCost)}, UAV={FormatRoubles(uavCost)}, Focused sweep={FormatRoubles(focusedSweepCost)}");
	}

	public static void SetServerConfigGlobals(
		int strafeCost,
		int doubleStrafeCost,
		int extractionCost,
		int priorityExfilCost,
		int uavCost,
		int focusedSweepCost,
		PaymentMode paymentMode,
		PaymentSource paymentSource)
	{
		_serverStrafeCost = strafeCost;
		_serverDoubleStrafeCost = doubleStrafeCost;
		_serverExtractionCost = extractionCost;
		_serverPriorityExfilCost = priorityExfilCost;
		_serverUavCost = uavCost;
		_serverFocusedSweepCost = focusedSweepCost;
		_serverPaymentMode = paymentMode;
		_serverPaymentSource = paymentSource;
		TscDiagnostics.LogPayment(
			$"Using server URL TSC globals: mode={paymentMode}, source={paymentSource}, A-10={FormatRoubles(strafeCost)}, A-10 double pass={FormatRoubles(doubleStrafeCost)}, UH-60={FormatRoubles(extractionCost)}, Priority exfil={FormatRoubles(priorityExfilCost)}, UAV={FormatRoubles(uavCost)}, Focused sweep={FormatRoubles(focusedSweepCost)}");
	}

	public static void ClearServerConfig()
	{
		bool hadServerSettings = HasServerConfigCosts ||
		                         _serverPaymentMode.HasValue ||
		                         _serverPaymentSource.HasValue ||
		                         _serverStashRoubleBalance.HasValue ||
		                         _serverConfigUnavailable;
		_serverStrafeCost = null;
		_serverDoubleStrafeCost = null;
		_serverExtractionCost = null;
		_serverPriorityExfilCost = null;
		_serverUavCost = null;
		_serverFocusedSweepCost = null;
		_serverPaymentMode = null;
		_serverPaymentSource = null;
		_serverStashRoubleBalance = null;
		_serverConfigRevision = 0;
		_serverConfigUnavailable = false;
		_serverConfigUnavailableReason = null;
		_serverPurchasePersistenceEnabled = false;
		_serverRefundFailedDispatch = true;
		_serverSpendCreditsBeforeCash = true;
		_serverAllowAutoPurchaseOnUse = true;
		if (hadServerSettings)
		{
			TscDiagnostics.LogPayment("Cleared server URL TSC prices and payment settings.");
		}
	}

	public static void ClearServerGlobalConfig()
	{
		bool hadServerGlobalSettings = HasServerConfigCosts ||
		                               _serverPaymentMode.HasValue ||
		                               _serverPaymentSource.HasValue;
		_serverStrafeCost = null;
		_serverDoubleStrafeCost = null;
		_serverExtractionCost = null;
		_serverPriorityExfilCost = null;
		_serverUavCost = null;
		_serverFocusedSweepCost = null;
		_serverPaymentMode = null;
		_serverPaymentSource = null;
		if (hadServerGlobalSettings)
		{
			TscDiagnostics.LogPayment(
				"Cleared server URL TSC global prices and payment settings; preserved profile payment state.");
		}
	}

	public static void ClearServerProfileState()
	{
		bool hadServerProfileState =
			_serverStashRoubleBalance.HasValue || _lastPurchaseDenial != null;
		_serverStashRoubleBalance = null;
		_lastPurchaseDenial = null;
		if (hadServerProfileState)
		{
			TscDiagnostics.LogPayment(
				"Cleared server URL TSC profile balance and purchase denial; preserved global phone configuration.");
		}
	}

	public static void SetSyncedPaymentMode(PaymentMode paymentMode)
	{
		_syncedPaymentMode = paymentMode;
		TscDiagnostics.LogPayment($"Using host TSC payment mode: {paymentMode}");
	}

	public static void SetSyncedPaymentSource(PaymentSource paymentSource)
	{
		_syncedPaymentSource = paymentSource;
		TscDiagnostics.LogPayment($"Using host TSC payment source: {paymentSource}");
	}

	public static void SetServerConfigPayment(
		PaymentMode paymentMode,
		PaymentSource paymentSource,
		int revision,
		int? stashRoubleBalance)
	{
		_serverPaymentMode = paymentMode;
		_serverPaymentSource = paymentSource;
		_serverConfigRevision = revision;
		_serverStashRoubleBalance = stashRoubleBalance;
		_serverConfigUnavailable = false;
		_serverConfigUnavailableReason = null;
		TscDiagnostics.LogPayment(
			$"Using server URL TSC payment revision={revision}: mode={paymentMode}, source={paymentSource}, stashBalance={(stashRoubleBalance.HasValue ? FormatRoubles(stashRoubleBalance.Value) : "unknown")}");
	}

	public static void SetServerProfileState(
		int revision,
		int? stashRoubleBalance,
		bool persistenceEnabled,
		bool refundFailedDispatch,
		bool spendCreditsBeforeCash,
		bool allowAutoPurchaseOnUse)
	{
		_serverConfigRevision = revision;
		_serverStashRoubleBalance = stashRoubleBalance;
		_serverConfigUnavailable = false;
		_serverConfigUnavailableReason = null;
		_serverPurchasePersistenceEnabled = persistenceEnabled;
		_serverRefundFailedDispatch = refundFailedDispatch;
		_serverSpendCreditsBeforeCash = spendCreditsBeforeCash;
		_serverAllowAutoPurchaseOnUse = allowAutoPurchaseOnUse;
		TscDiagnostics.LogPayment(
			$"Using server URL TSC profile state revision={revision}: stashBalance={(stashRoubleBalance.HasValue ? FormatRoubles(stashRoubleBalance.Value) : "unknown")}, persistence={persistenceEnabled}");
	}

	public static void SetServerPurchasePersistence(
		bool enabled,
		bool refundFailedDispatch,
		bool spendCreditsBeforeCash,
		bool allowAutoPurchaseOnUse,
		int revision)
	{
		_serverPurchasePersistenceEnabled = enabled;
		_serverRefundFailedDispatch = refundFailedDispatch;
		_serverSpendCreditsBeforeCash = spendCreditsBeforeCash;
		_serverAllowAutoPurchaseOnUse = allowAutoPurchaseOnUse;
		_serverConfigRevision = revision;
	}

	public static void MarkServerConfigUnavailable(string reason)
	{
		_serverConfigUnavailable = true;
		_serverConfigUnavailableReason = reason;
		FireSupportPlugin.LogSource.LogWarning($"Server URL TSC config unavailable: {reason}");
	}

	public static PaymentMode GetConfiguredPaymentMode()
	{
		return PluginSettings.PaymentMode.Value;
	}

	public static PaymentMode GetActivePaymentMode()
	{
		return _syncedPaymentMode ?? _serverPaymentMode ?? GetConfiguredPaymentMode();
	}

	public static PaymentSource GetConfiguredPaymentSource()
	{
		return PluginSettings.PaymentSource?.Value ?? PaymentSource.CarriedRoubles;
	}

	public static PaymentSource GetActivePaymentSource()
	{
		return _syncedPaymentSource ?? _serverPaymentSource ?? GetConfiguredPaymentSource();
	}

	public static int GetConfiguredCost(ESupportType supportType)
	{
		return supportType switch
		{
			ESupportType.Strafe => PluginSettings.StrafeRequestCostRoubles.Value,
			ESupportType.DoubleStrafe => PluginSettings.DoubleStrafeRequestCostRoubles.Value,
			ESupportType.Extract => PluginSettings.ExtractionRequestCostRoubles.Value,
			ESupportType.PriorityExfil => PluginSettings.PriorityExfilRequestCostRoubles.Value,
			ESupportType.Uav => PluginSettings.UavRequestCostRoubles.Value,
			ESupportType.FocusedSweep => PluginSettings.FocusedSweepRequestCostRoubles.Value,
			_ => 0
		};
	}

	public static int GetActiveCost(ESupportType supportType)
	{
		return GetCost(supportType);
	}

	public static int GetEffectiveCost(ESupportType supportType)
	{
		return GetActiveCost(supportType);
	}

	public static int GetCarriedRoubleBalance()
	{
		return GetCarriedRoubles();
	}

	public static int GetEffectiveBalance()
	{
		PaymentSource paymentSource = GetActivePaymentSource();
		int carriedRoubles = GetCarriedRoubles();
		return paymentSource switch
		{
			PaymentSource.CarriedRoubles => carriedRoubles,
			PaymentSource.StashRoubles => _serverStashRoubleBalance ?? -1,
			PaymentSource.PreferCarriedThenStash => _serverStashRoubleBalance.HasValue
				? carriedRoubles + _serverStashRoubleBalance.Value
				: carriedRoubles,
			PaymentSource.PreferStashThenCarried => _serverStashRoubleBalance.HasValue
				? carriedRoubles + _serverStashRoubleBalance.Value
				: carriedRoubles,
			_ => carriedRoubles
		};
	}

	public static string GetEffectiveBalanceLabel()
	{
		return GetActivePaymentSource() switch
		{
			PaymentSource.StashRoubles => "Stash Roubles",
			PaymentSource.PreferCarriedThenStash => "Available Roubles",
			PaymentSource.PreferStashThenCarried => "Available Roubles",
			_ => "Carried Roubles"
		};
	}

	public static bool CanAfford(ESupportType supportType, bool notify = false)
	{
		if (!FireSupportServiceAvailability.IsServiceEnabled(supportType))
		{
			if (notify)
			{
				NotifyServiceUnavailable(supportType);
			}

			return false;
		}

		int cost = GetCost(supportType);
		if (cost <= 0)
		{
			return true;
		}

		int effectiveBalance = GetEffectiveBalance();
		bool canAfford = effectiveBalance >= cost;

		if (!canAfford && notify)
		{
			NotifyInsufficientFunds(cost, effectiveBalance);
		}

		return canAfford;
	}

	public static bool TryCharge(ESupportType supportType)
	{
		return TryCharge(supportType, notifySuccess: true, notifyFailure: true);
	}

	public static bool TryCharge(ESupportType supportType, bool notifySuccess, bool notifyFailure = true)
	{
		if (!FireSupportServiceAvailability.IsServiceEnabled(supportType))
		{
			if (notifyFailure)
			{
				NotifyServiceUnavailable(supportType);
			}

			return false;
		}

		int cost = GetCost(supportType);
		if (cost <= 0)
		{
			return true;
		}

		if (!CanSpendCarriedForActivePaymentSource(cost))
		{
			if (notifyFailure)
			{
				NotifyServerPaymentRequired(supportType);
			}

			return false;
		}

		if (!TrySpendCarriedRoubles(cost, out int carriedRoubles))
		{
			if (notifyFailure)
			{
				NotifyInsufficientFunds(cost, carriedRoubles);
			}

			return false;
		}

		if (notifySuccess)
		{
			NotificationManagerClass.DisplayMessageNotification(
				$"Paid {FormatRoubles(cost)} for {GetSupportName(supportType)}.",
				ENotificationDurationType.Default,
				ENotificationIconType.Default,
				null);
		}

		return true;
	}

	public static bool CanDeployFromRadial(ESupportType supportType, bool notify = false)
	{
		if (!FireSupportServiceAvailability.IsServiceEnabled(supportType))
		{
			if (notify)
			{
				NotifyServiceUnavailable(supportType);
			}

			return false;
		}

		PaymentMode paymentMode = GetActivePaymentMode();
		if (paymentMode == PaymentMode.PhoneAuthorizations)
		{
			if (FireSupportAuthorizations.HasDeployable(supportType))
			{
				return true;
			}

			if (notify)
			{
				NotifyAuthorizationRequired(supportType);
			}

			return false;
		}

		if (paymentMode == PaymentMode.Hybrid && FireSupportAuthorizations.HasDeployable(supportType))
		{
			return true;
		}

		return CanAfford(supportType, notify);
	}

	public static bool TryPayForDeployment(ESupportType supportType, out bool consumedAuthorization)
	{
		return TryPayForDeployment(supportType, out consumedAuthorization, out _);
	}

	public static bool TryPayForDeployment(
		ESupportType supportType,
		out bool consumedAuthorization,
		out ESupportType consumedAuthorizationType)
	{
		consumedAuthorization = false;
		consumedAuthorizationType = supportType;
		if (!FireSupportServiceAvailability.IsServiceEnabled(supportType))
		{
			NotifyServiceUnavailable(supportType);
			return false;
		}

		PaymentMode paymentMode = GetActivePaymentMode();

		if (paymentMode == PaymentMode.PhoneAuthorizations ||
		    paymentMode == PaymentMode.Hybrid)
		{
			if (FireSupportAuthorizations.TryConsumeForDeployment(supportType, out consumedAuthorizationType))
			{
				consumedAuthorization = true;
				NotificationManagerClass.DisplayMessageNotification(
					$"Used prepaid {GetSupportName(consumedAuthorizationType)} authorization.",
					ENotificationDurationType.Default,
					ENotificationIconType.Default,
					null);
				return true;
			}
		}

		if (paymentMode == PaymentMode.PhoneAuthorizations)
		{
			NotifyAuthorizationRequired(supportType);
			return false;
		}

		return TryCharge(supportType);
	}

	public static async UniTask<FireSupportAuthorizationUse> TryPayForDeploymentAsync(ESupportType supportType)
	{
		string operationId = Guid.NewGuid().ToString("N");
		string serverSessionKey =
			FireSupportServerConfigClient.GetAuthenticatedSessionKey();
		string serverProfileId =
			FireSupportServerConfigClient.GetAuthenticatedProfileId();
		if (!_serverPurchasePersistenceEnabled)
		{
			bool ok = TryPayForDeployment(supportType, out bool consumedAuthorization, out ESupportType localConsumedType);
			return new FireSupportAuthorizationUse
			{
				Ok = ok,
				ConsumedAuthorization = consumedAuthorization,
				ConsumedAuthorizationType = localConsumedType,
				RequestId = ok ? operationId : string.Empty
			};
		}

		if (!FireSupportServiceAvailability.IsServiceEnabled(supportType))
		{
			NotifyServiceUnavailable(supportType);
			return FireSupportAuthorizationUse.Failed(supportType);
		}

		PaymentMode paymentMode = GetActivePaymentMode();
		if ((paymentMode == PaymentMode.PhoneAuthorizations ||
		     paymentMode == PaymentMode.Hybrid && _serverSpendCreditsBeforeCash) &&
		    FireSupportAuthorizations.TryConsumeForDeployment(supportType, out ESupportType consumedType, out bool serverBacked))
		{
			// Local credits (carried-rouble purchases) have no ledger entry; asking
			// the server to consume one gets rejected and the credit becomes
			// unusable. Consume them purely client-side.
			if (!serverBacked)
			{
				NotificationManagerClass.DisplayMessageNotification(
					$"Used prepaid {GetSupportName(consumedType)} authorization.",
					ENotificationDurationType.Default,
					ENotificationIconType.Default,
					null);
				return new FireSupportAuthorizationUse
				{
					Ok = true,
					ConsumedAuthorization = true,
					ConsumedAuthorizationType = consumedType,
					RequestId = operationId,
					ServerBacked = false
				};
			}

			await s_serverLedgerMutationGate.WaitAsync();
			try
			{
				FireSupportPurchaseResponse response = await FireSupportServerConfigClient.ConsumeAuthorizationAsync(
					consumedType,
					operationId,
					_serverConfigRevision,
					serverSessionKey,
					serverProfileId);
				if (!IsMatchingAuthorizationMutationResponse(
					    response,
					    consumedType,
					    operationId))
				{
					response = BuildInvalidAuthorizationMutationResponse(
						consumedType,
						operationId,
						"ConsumeAuthorization");
				}
				bool authorizationsApplied = ApplyIncludedAuthorizations(response);

				if (response.Ok)
				{
					NotificationManagerClass.DisplayMessageNotification(
						$"Used TerraGroup {GetSupportName(consumedType)} authorization.",
						ENotificationDurationType.Default,
						ENotificationIconType.Default,
						null);
					return new FireSupportAuthorizationUse
					{
						Ok = true,
						ConsumedAuthorization = true,
						ConsumedAuthorizationType = consumedType,
						RequestId = operationId,
						ServerBacked = true,
						ServerSessionKey = serverSessionKey,
						ServerProfileId = serverProfileId
					};
				}

				if (!authorizationsApplied)
				{
					FireSupportAuthorizations.Refund(consumedType, serverBacked: true);
				}
				NotifyAuthorizationRequired(supportType);
				return FireSupportAuthorizationUse.Failed(consumedType);
			}
			finally
			{
				s_serverLedgerMutationGate.Release();
			}
		}

		if (paymentMode == PaymentMode.PhoneAuthorizations)
		{
			NotifyAuthorizationRequired(supportType);
			return FireSupportAuthorizationUse.Failed(supportType);
		}

		if (_serverAllowAutoPurchaseOnUse && RequiresServerPurchase(GetActivePaymentSource()))
		{
			FireSupportPurchaseResponse purchase = await PurchaseAuthorizationAsync(supportType, notify: true);
			if (purchase.Ok)
			{
				return await TryPayForDeploymentAsync(supportType);
			}

			return FireSupportAuthorizationUse.Failed(supportType);
		}

		bool charged = TryCharge(supportType);
		return new FireSupportAuthorizationUse
		{
			Ok = charged,
			ConsumedAuthorization = false,
			ConsumedAuthorizationType = supportType,
			RequestId = charged ? operationId : string.Empty
		};
	}

	public static void RefundConsumedAuthorization(FireSupportAuthorizationUse authorizationUse)
	{
		RefundConsumedAuthorizationAsync(authorizationUse).Forget();
	}

	public static void CommitConsumedAuthorization(FireSupportAuthorizationUse authorizationUse)
	{
		CommitConsumedAuthorizationAsync(authorizationUse).Forget();
	}

	public static async UniTask<bool> RefundConsumedAuthorizationAsync(
		FireSupportAuthorizationUse authorizationUse)
	{
		if (authorizationUse == null || !authorizationUse.Ok)
		{
			return false;
		}

		// When failed-dispatch refunds are disabled, a server-backed reservation
		// still needs a deterministic terminal mutation. Explicitly commit it
		// instead of leaving it pending until server timeout cleanup.
		if (authorizationUse.ConsumedAuthorization &&
		    authorizationUse.ServerBacked &&
		    !_serverRefundFailedDispatch)
		{
			return await CommitConsumedAuthorizationAsync(authorizationUse);
		}

		bool selectedIntent = authorizationUse.TrySelectFinalization(
			FireSupportAuthorizationUse.FinalizationIntent.Refund,
			out bool ownsFinalization,
			out Task<bool> completion);
		if (!selectedIntent)
		{
			await completion;
			return false;
		}

		if (ownsFinalization)
		{
			if (!authorizationUse.ConsumedAuthorization)
			{
				authorizationUse.CompleteFinalization(success: true);
			}
			else if (!authorizationUse.ServerBacked)
			{
				try
				{
					FireSupportAuthorizations.Refund(
						authorizationUse.ConsumedAuthorizationType,
						serverBacked: false);
					authorizationUse.CompleteFinalization(success: true);
				}
				catch (Exception ex)
				{
					LogFinalizationFailure(
						authorizationUse,
						FireSupportAuthorizationUse.FinalizationIntent.Refund,
						attempts: 1,
						reason: ex.ToString());
					authorizationUse.CompleteFinalization(success: false);
				}
			}
			else
			{
				FinalizeServerAuthorizationAsync(
					authorizationUse,
					FireSupportAuthorizationUse.FinalizationIntent.Refund).Forget();
			}
		}

		return await completion;
	}

	public static async UniTask<bool> CommitConsumedAuthorizationAsync(
		FireSupportAuthorizationUse authorizationUse)
	{
		if (authorizationUse == null || !authorizationUse.Ok)
		{
			return false;
		}

		bool selectedIntent = authorizationUse.TrySelectFinalization(
			FireSupportAuthorizationUse.FinalizationIntent.Commit,
			out bool ownsFinalization,
			out Task<bool> completion);
		if (!selectedIntent)
		{
			await completion;
			return false;
		}

		if (ownsFinalization)
		{
			if (!authorizationUse.ConsumedAuthorization ||
			    !authorizationUse.ServerBacked)
			{
				authorizationUse.CompleteFinalization(success: true);
			}
			else
			{
				FinalizeServerAuthorizationAsync(
					authorizationUse,
					FireSupportAuthorizationUse.FinalizationIntent.Commit).Forget();
			}
		}

		return await completion;
	}

	private static async UniTaskVoid FinalizeServerAuthorizationAsync(
		FireSupportAuthorizationUse authorizationUse,
		FireSupportAuthorizationUse.FinalizationIntent intent)
	{
		AuthorizationMutationAttempt result = default;
		int attempt = 0;
		bool completedSuccessfully = false;
		try
		{
			for (attempt = 1; attempt <= MaxServerFinalizationAttempts; attempt++)
			{
				result = intent == FireSupportAuthorizationUse.FinalizationIntent.Commit
					? await TryCommitServerAuthorizationAsync(authorizationUse)
					: await TryRefundServerAuthorizationAsync(authorizationUse);

				if (result.Success)
				{
					completedSuccessfully = true;
					authorizationUse.CompleteFinalization(success: true);
					return;
				}

				if (!result.Retryable || attempt == MaxServerFinalizationAttempts)
				{
					break;
				}

				int delaySeconds = GetServerFinalizationRetryDelaySeconds(attempt);
				FireSupportPlugin.LogSource?.LogWarning(
					$"TSC authorization {intent.ToString().ToLowerInvariant()} response was transient; " +
					$"retrying requestId={authorizationUse.RequestId}, attempt={attempt + 1}/{MaxServerFinalizationAttempts}, " +
					$"delaySeconds={delaySeconds}, reason={result.Reason}.");
				await UniTask.Delay(
					TimeSpan.FromSeconds(delaySeconds),
					ignoreTimeScale: true);
			}
		}
		catch (Exception ex)
		{
			result = new AuthorizationMutationAttempt(
				success: false,
				retryable: false,
				reason: ex.ToString());
		}
		finally
		{
			if (!completedSuccessfully)
			{
				LogFinalizationFailure(
					authorizationUse,
					intent,
					Math.Max(attempt, 1),
					result.Reason);
				authorizationUse.CompleteFinalization(success: false);
			}
		}
	}

	private static async UniTask<AuthorizationMutationAttempt> TryRefundServerAuthorizationAsync(
		FireSupportAuthorizationUse authorizationUse)
	{
		await s_serverLedgerMutationGate.WaitAsync();
		try
		{
			FireSupportPurchaseResponse response =
				await FireSupportServerConfigClient.RefundAuthorizationAsync(
					authorizationUse.ConsumedAuthorizationType,
					authorizationUse.RequestId,
					_serverConfigRevision,
					authorizationUse.ServerSessionKey,
					authorizationUse.ServerProfileId);
			if (!IsMatchingAuthorizationMutationResponse(
				    response,
				    authorizationUse.ConsumedAuthorizationType,
				    authorizationUse.RequestId))
			{
				return new AuthorizationMutationAttempt(
					success: false,
					retryable: true,
					reason: "InvalidServerResponse");
			}
			bool authorizationsApplied = ApplyIncludedAuthorizations(response);

			// Older servers can acknowledge the refund without returning a ledger
			// snapshot. Mirror that successful mutation locally until the next
			// profile refresh. A denial or transport failure is never a refund.
			if (response?.Ok == true && !authorizationsApplied)
			{
				FireSupportAuthorizations.Refund(
					authorizationUse.ConsumedAuthorizationType,
					serverBacked: true);
			}

			return ToAuthorizationMutationAttempt(response);
		}
		finally
		{
			s_serverLedgerMutationGate.Release();
		}
	}

	private static async UniTask<AuthorizationMutationAttempt> TryCommitServerAuthorizationAsync(
		FireSupportAuthorizationUse authorizationUse)
	{
		await s_serverLedgerMutationGate.WaitAsync();
		try
		{
			FireSupportPurchaseResponse response =
				await FireSupportServerConfigClient.CommitAuthorizationAsync(
					authorizationUse.ConsumedAuthorizationType,
					authorizationUse.RequestId,
					_serverConfigRevision,
					authorizationUse.ServerSessionKey,
					authorizationUse.ServerProfileId);
			if (!IsMatchingAuthorizationMutationResponse(
				    response,
				    authorizationUse.ConsumedAuthorizationType,
				    authorizationUse.RequestId))
			{
				return new AuthorizationMutationAttempt(
					success: false,
					retryable: true,
					reason: "InvalidServerResponse");
			}
			ApplyIncludedAuthorizations(response);
			return ToAuthorizationMutationAttempt(response);
		}
		finally
		{
			s_serverLedgerMutationGate.Release();
		}
	}

	private static AuthorizationMutationAttempt ToAuthorizationMutationAttempt(
		FireSupportPurchaseResponse response)
	{
		string reason = response?.Reason ?? "NoResponse";
		bool retryable =
			response == null ||
			string.Equals(reason, "ServerConfigUnavailable", StringComparison.Ordinal) ||
			string.Equals(reason, "RequestFailed", StringComparison.Ordinal) ||
			string.Equals(reason, "InvalidServerResponse", StringComparison.Ordinal) ||
			string.Equals(reason, "AuthorizationLedgerSaveFailed", StringComparison.Ordinal) ||
			string.Equals(reason, "ProfileSessionChanged", StringComparison.Ordinal);
		return new AuthorizationMutationAttempt(
			response?.Ok == true,
			retryable,
			reason);
	}

	private static bool IsMatchingAuthorizationMutationResponse(
		FireSupportPurchaseResponse response,
		ESupportType expectedSupportType,
		string expectedRequestId)
	{
		bool matches =
			response != null &&
			string.Equals(
				response.RequestId,
				expectedRequestId,
				StringComparison.Ordinal) &&
			Enum.TryParse(
				response.SupportType,
				ignoreCase: true,
				out ESupportType responseSupportType) &&
			responseSupportType == expectedSupportType;
		if (!matches)
		{
			FireSupportPlugin.LogSource?.LogWarning(
				$"TSC ignored an uncorrelated authorization mutation response. " +
				$"expectedRequestId={expectedRequestId}, actualRequestId={response?.RequestId ?? "<null>"}, " +
				$"expectedSupport={expectedSupportType}, actualSupport={response?.SupportType ?? "<null>"}.");
		}

		return matches;
	}

	private static FireSupportPurchaseResponse BuildInvalidAuthorizationMutationResponse(
		ESupportType supportType,
		string requestId,
		string action)
	{
		return new FireSupportPurchaseResponse
		{
			Ok = false,
			Reason = "InvalidServerResponse",
			SupportType = supportType.ToString(),
			RequestId = requestId ?? string.Empty,
			ServerRevision = _serverConfigRevision,
			AuthorizationConsumed = false,
			AuthorizationGranted = false,
			PaymentSource = action ?? string.Empty
		};
	}

	private static int GetServerFinalizationRetryDelaySeconds(int completedAttempts)
	{
		return Math.Min(
			1 << Math.Min(completedAttempts - 1, 5),
			MaxServerFinalizationRetryDelaySeconds);
	}

	private static void LogFinalizationFailure(
		FireSupportAuthorizationUse authorizationUse,
		FireSupportAuthorizationUse.FinalizationIntent intent,
		int attempts,
		string reason)
	{
		FireSupportPlugin.LogSource?.LogError(
			$"TSC terminal authorization finalization failure intent={intent}, " +
			$"requestId={authorizationUse.RequestId}, support={authorizationUse.ConsumedAuthorizationType}, " +
			$"attempts={attempts}, reason={reason ?? "Unknown"}.");
	}

	public static bool TryPurchaseAuthorization(ESupportType supportType)
	{
		return TryPurchaseAuthorization(supportType, notify: true);
	}

	public static bool TryPurchaseAuthorization(ESupportType supportType, bool notify)
	{
		if (!FireSupportServiceAvailability.IsServiceEnabled(supportType))
		{
			if (notify)
			{
				NotifyServiceUnavailable(supportType);
			}

			return false;
		}

		if (RequiresServerPurchase(GetActivePaymentSource()))
		{
			if (notify)
			{
				NotifyServerPaymentRequired(supportType);
			}

			return false;
		}

		if (!TryCharge(supportType, notifySuccess: false, notifyFailure: notify))
		{
			return false;
		}

		FireSupportAuthorizations.Grant(supportType);
		if (notify)
		{
			NotifyAuthorizationPurchased(supportType);
		}

		return true;
	}

	public static void TryPurchaseAuthorizationAsync(
		ESupportType supportType,
		bool notify,
		Action<bool, FireSupportPurchaseResponse> callback)
	{
		TryPurchaseAuthorizationAsyncInternal(supportType, notify, callback).Forget();
	}

	/// <summary>
	/// Menu-only server purchase path. It never falls back to carried cash or a
	/// local authorization: a successful pre-raid purchase must return a complete
	/// persistent ledger from the authenticated server.
	/// </summary>
	public static async UniTask<FireSupportPurchaseResponse> PurchasePersistentAuthorizationAsync(
		ESupportType supportType,
		string requestId,
		int expectedCost,
		string expectedSessionKey,
		string expectedProfileId)
	{
		var fallback = new FireSupportPurchaseResponse
		{
			Ok = false,
			Reason = "ServerConfigUnavailable",
			SupportType = supportType.ToString(),
			Cost = expectedCost >= 0 ? expectedCost : GetCost(supportType),
			PaymentSource = nameof(PaymentSource.StashRoubles),
			NewBalance = _serverStashRoubleBalance ?? -1,
			AuthorizationGranted = false,
			ServerRevision = _serverConfigRevision,
			RequestId = requestId ?? string.Empty
		};

		if (string.IsNullOrWhiteSpace(requestId))
		{
			fallback.Reason = "InvalidRequestId";
			return fallback;
		}

		if (string.IsNullOrWhiteSpace(expectedSessionKey) ||
		    string.IsNullOrWhiteSpace(expectedProfileId) ||
		    !string.Equals(
			    expectedSessionKey,
			    FireSupportServerConfigClient.GetAuthenticatedSessionKey(),
			    StringComparison.Ordinal) ||
		    !FireSupportServerConfigClient.IsAuthenticatedProfile(expectedProfileId))
		{
			fallback.Reason = "ProfileSessionChanged";
			return fallback;
		}

		await s_serverLedgerMutationGate.WaitAsync();
		try
		{
			if (!string.Equals(
				    expectedSessionKey,
				    FireSupportServerConfigClient.GetAuthenticatedSessionKey(),
				    StringComparison.Ordinal) ||
			    !FireSupportServerConfigClient.IsAuthenticatedProfile(expectedProfileId))
			{
				fallback.Reason = "ProfileSessionChanged";
				return fallback;
			}

			FireSupportPurchaseResponse serverResult =
				await FireSupportServerConfigClient.PurchasePersistentAuthorizationAsync(
					supportType,
					requestId,
					expectedSessionKey,
					expectedProfileId,
					expectedCost,
					_serverConfigRevision);
			serverResult ??= fallback;
			serverResult.SupportType = string.IsNullOrWhiteSpace(serverResult.SupportType)
				? supportType.ToString()
				: serverResult.SupportType;
			serverResult.PaymentSource = string.IsNullOrWhiteSpace(serverResult.PaymentSource)
				? nameof(PaymentSource.StashRoubles)
				: serverResult.PaymentSource;
			serverResult.Cost = serverResult.Cost >= 0
				? serverResult.Cost
				: fallback.Cost;
			serverResult.ServerRevision = serverResult.ServerRevision > 0
				? serverResult.ServerRevision
				: _serverConfigRevision;
			if (!string.Equals(serverResult.RequestId, requestId, StringComparison.Ordinal))
			{
				// A persistent purchase response is not authoritative for this
				// click unless it echoes the exact idempotency key.
				serverResult.Ok = false;
				serverResult.AuthorizationGranted = false;
				serverResult.Reason = "ResponseRequestIdMismatch";
				return serverResult;
			}

			if (!string.Equals(
				    expectedSessionKey,
				    FireSupportServerConfigClient.GetAuthenticatedSessionKey(),
				    StringComparison.Ordinal) ||
			    !FireSupportServerConfigClient.IsAuthenticatedProfile(expectedProfileId))
			{
				// The backend may have completed the old profile's request, but
				// its response must never replace the newly selected ledger.
				serverResult.Ok = false;
				serverResult.AuthorizationGranted = false;
				serverResult.Reason = "ProfileSessionChanged";
				return serverResult;
			}

			if (serverResult.NewBalance >= 0)
			{
				_serverStashRoubleBalance = serverResult.NewBalance;
			}

			bool authorizationsApplied =
				serverResult.AuthorizationsIncluded &&
				serverResult.Authorizations != null &&
				ApplyIncludedAuthorizations(serverResult);
			if (serverResult.Ok && !authorizationsApplied)
			{
				// Do not fabricate a local credit for a pre-raid purchase. The
				// page remains fail-closed until a complete ledger is returned.
				serverResult.Ok = false;
				serverResult.AuthorizationGranted = false;
				serverResult.Reason = "AuthoritativeLedgerMissing";
			}

			if (serverResult.ServerRevision > 0)
			{
				_serverConfigRevision = serverResult.ServerRevision;
			}

			return serverResult;
		}
		finally
		{
			s_serverLedgerMutationGate.Release();
		}
	}

	private static async UniTaskVoid TryPurchaseAuthorizationAsyncInternal(
		ESupportType supportType,
		bool notify,
		Action<bool, FireSupportPurchaseResponse> callback)
	{
		FireSupportPurchaseResponse result = await PurchaseAuthorizationAsync(supportType, notify);
		callback?.Invoke(result.Ok, result);
	}

	private static async UniTask<FireSupportPurchaseResponse> PurchaseAuthorizationAsync(ESupportType supportType, bool notify)
	{
		var result = new FireSupportPurchaseResponse
		{
			Ok = false,
			SupportType = supportType.ToString(),
			Cost = GetCost(supportType),
			PaymentSource = GetActivePaymentSource().ToString(),
			NewBalance = GetEffectiveBalance(),
			AuthorizationGranted = false,
			ServerRevision = _serverConfigRevision
		};

		if (!FireSupportServiceAvailability.IsServiceEnabled(supportType))
		{
			result.Reason = "ServiceUnavailable";
			RememberPurchaseDenial(supportType, result);
			if (notify)
			{
				NotifyServiceUnavailable(supportType);
			}

			return result;
		}

		if (_serverConfigUnavailable && ShouldRequireServerConfig())
		{
			result.Reason = "ServerConfigUnavailable";
			RememberPurchaseDenial(supportType, result);
			if (notify)
			{
				NotifyServerConfigUnavailable(supportType);
			}

			return result;
		}

		if (result.Cost <= 0)
		{
			GrantAuthorization(supportType, notify);
			result.Ok = true;
			result.AuthorizationGranted = true;
			return result;
		}

		PaymentSource paymentSource = GetActivePaymentSource();
		if (ShouldUseCarriedForPurchase(paymentSource, result.Cost))
		{
			if (!TryCharge(supportType, notifySuccess: false, notifyFailure: notify))
			{
				result.Reason = "InsufficientRoubles";
				result.NewBalance = GetEffectiveBalance();
				RememberPurchaseDenial(supportType, result);
				return result;
			}

			result.Ok = true;
			_lastPurchaseDenial = null;
			result.PaymentSource = nameof(PaymentSource.CarriedRoubles);
			result.NewBalance = GetEffectiveBalance();
			GrantAuthorization(supportType, notify);
			result.AuthorizationGranted = true;
			FireSupportPlugin.LogSource.LogInfo($"TSC authorization purchased: {GetSupportName(supportType)}.");
			return result;
		}

		await s_serverLedgerMutationGate.WaitAsync();
		try
		{
			TscDiagnostics.LogPayment(
				$"TSC purchase request sent source=Stash supportType={supportType} cost={result.Cost} revision={_serverConfigRevision}.");
			FireSupportPurchaseResponse serverResult = await FireSupportServerConfigClient.PurchaseAuthorizationAsync(
				supportType,
				_serverConfigRevision);
			serverResult.SupportType = string.IsNullOrWhiteSpace(serverResult.SupportType)
				? supportType.ToString()
				: serverResult.SupportType;
			serverResult.PaymentSource = string.IsNullOrWhiteSpace(serverResult.PaymentSource)
				? nameof(PaymentSource.StashRoubles)
				: serverResult.PaymentSource;
			serverResult.Cost = serverResult.Cost > 0 ? serverResult.Cost : result.Cost;
			serverResult.ServerRevision = serverResult.ServerRevision > 0 ? serverResult.ServerRevision : _serverConfigRevision;

			if (serverResult.NewBalance >= 0)
			{
				_serverStashRoubleBalance = serverResult.NewBalance;
			}

			bool authorizationsApplied = ApplyIncludedAuthorizations(serverResult);
			if (!serverResult.Ok)
			{
				if (TryFallbackToCarriedAfterStashDenial(paymentSource, supportType, serverResult, notify, out FireSupportPurchaseResponse carriedResult))
				{
					return carriedResult;
				}

				RememberPurchaseDenial(supportType, serverResult);
				if (notify)
				{
					NotifyAuthorizationPurchaseDenied(supportType, serverResult);
				}

				FireSupportPlugin.LogSource.LogWarning(
					$"TSC purchase denied source=Stash supportType={supportType} cost={serverResult.Cost} reason={serverResult.Reason} newBalance={serverResult.NewBalance} revision={serverResult.ServerRevision}.");
				return serverResult;
			}

			if (!authorizationsApplied)
			{
				GrantServerAuthorization(supportType, notify);
			}

			serverResult.AuthorizationGranted = true;
			_lastPurchaseDenial = null;
			FireSupportPlugin.LogSource.LogInfo($"TSC authorization purchased: {GetSupportName(supportType)}.");
			return serverResult;
		}
		finally
		{
			s_serverLedgerMutationGate.Release();
		}
	}

	private static bool ApplyIncludedAuthorizations(FireSupportPurchaseResponse response)
	{
		if (response?.Authorizations == null ||
		    !response.AuthorizationsIncluded && response.Authorizations.Count == 0)
		{
			return false;
		}

		// A ledger-bearing response proves persistence is active even if the
		// initial profile config refresh has not completed yet.
		_serverPurchasePersistenceEnabled = true;

		int strafeBefore = FireSupportAuthorizations.Get(ESupportType.Strafe);
		int doubleStrafeBefore = FireSupportAuthorizations.Get(ESupportType.DoubleStrafe);
		int extractionBefore = FireSupportAuthorizations.Get(ESupportType.Extract);
		int priorityExfilBefore = FireSupportAuthorizations.Get(ESupportType.PriorityExfil);
		int uavBefore = FireSupportAuthorizations.Get(ESupportType.Uav);
		int focusedSweepBefore = FireSupportAuthorizations.Get(ESupportType.FocusedSweep);

		FireSupportAuthorizations.SetFromServer(response.Authorizations);
		if (strafeBefore != FireSupportAuthorizations.Get(ESupportType.Strafe) ||
		    doubleStrafeBefore != FireSupportAuthorizations.Get(ESupportType.DoubleStrafe) ||
		    extractionBefore != FireSupportAuthorizations.Get(ESupportType.Extract) ||
		    priorityExfilBefore != FireSupportAuthorizations.Get(ESupportType.PriorityExfil) ||
		    uavBefore != FireSupportAuthorizations.Get(ESupportType.Uav) ||
		    focusedSweepBefore != FireSupportAuthorizations.Get(ESupportType.FocusedSweep))
		{
			NotifySettingsChanged();
		}

		return true;
	}

	private static bool TryFallbackToCarriedAfterStashDenial(
		PaymentSource paymentSource,
		ESupportType supportType,
		FireSupportPurchaseResponse stashResult,
		bool notify,
		out FireSupportPurchaseResponse carriedResult)
	{
		carriedResult = null;
		if (paymentSource != PaymentSource.PreferStashThenCarried ||
		    !string.Equals(stashResult?.Reason, "InsufficientRoubles", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		int cost = stashResult.Cost > 0 ? stashResult.Cost : GetCost(supportType);
		int carriedBeforeCharge = GetCarriedRoubles();
		if (carriedBeforeCharge < cost)
		{
			return false;
		}

		if (!TryCharge(supportType, notifySuccess: false, notifyFailure: false))
		{
			return false;
		}

		GrantAuthorization(supportType, notify);
		_lastPurchaseDenial = null;
		carriedResult = new FireSupportPurchaseResponse
		{
			Ok = true,
			Reason = "Accepted",
			SupportType = supportType.ToString(),
			Cost = cost,
			PaymentSource = nameof(PaymentSource.CarriedRoubles),
			NewBalance = GetEffectiveBalance(),
			AuthorizationGranted = true,
			ServerRevision = stashResult.ServerRevision
		};
		FireSupportPlugin.LogSource.LogInfo(
			$"TSC purchase fallback source=Carried after stash denial supportType={supportType} cost={cost} carriedBeforeCharge={carriedBeforeCharge} stashReason={stashResult.Reason} revision={stashResult.ServerRevision}.");
		return true;
	}

	public static void NotifyAuthorizationPurchased(ESupportType supportType)
	{
		int cost = GetCost(supportType);
		string supportName = GetSupportName(supportType);
		string deployKey = PluginSettings.OpenDeployKey != null
			? PluginSettings.OpenDeployKey.Value.MainKey.ToString()
			: "K";
		string message = cost > 0
			? $"Paid {FormatRoubles(cost)}. {supportName} authorization ready. Press [{deployKey}] to deploy from the Uplink."
			: $"{supportName} authorization ready. Press [{deployKey}] to deploy from the Uplink.";

		NotificationManagerClass.DisplayMessageNotification(
			message,
			ENotificationDurationType.Default,
			ENotificationIconType.Default,
			null);
	}

	public static void NotifyAuthorizationPurchaseDenied(ESupportType supportType)
	{
		NotifyAuthorizationPurchaseDenied(supportType, _lastPurchaseDenial);
	}

	public static void NotifyAuthorizationPurchaseDenied(ESupportType supportType, FireSupportPurchaseResponse response)
	{
		if (!FireSupportServiceAvailability.IsServiceEnabled(supportType))
		{
			NotifyServiceUnavailable(supportType);
			return;
		}

		if (response != null)
		{
			RememberPurchaseDenial(supportType, response);
		}

		string reason = response?.Reason ?? _lastPurchaseDenial?.Reason;
		if (string.Equals(reason, "AuthorizationLimitReached", StringComparison.OrdinalIgnoreCase))
		{
			int held = GetAuthorizationCount(response ?? _lastPurchaseDenial, supportType);
			string countText = held > 0 ? $" You already hold {held}." : string.Empty;
			NotificationManagerClass.DisplayWarningNotification(
				$"{GetSupportName(supportType)} authorization limit reached.{countText} Deploy one from the Uplink before buying more.",
				ENotificationDurationType.Long);
			return;
		}

		if (!string.Equals(reason, "InsufficientRoubles", StringComparison.OrdinalIgnoreCase) &&
		    !string.IsNullOrWhiteSpace(reason))
		{
			NotificationManagerClass.DisplayWarningNotification(
				$"{GetSupportName(supportType)} authorization denied: {GetLastPurchaseDenialDetail(supportType)}",
				ENotificationDurationType.Long);
			return;
		}

		NotifyInsufficientFunds(GetCost(supportType), GetEffectiveBalance());
	}

	public static void NotifyServiceUnavailable(ESupportType supportType)
	{
		NotificationManagerClass.DisplayWarningNotification(
			$"{GetSupportName(supportType)} is unavailable in the host's FireSupport settings.",
			ENotificationDurationType.Long);
	}

	public static void NotifyServerConfigUnavailable(ESupportType supportType)
	{
		NotificationManagerClass.DisplayWarningNotification(
			$"{GetSupportName(supportType)} is unavailable: TerraGroup server config is not synced.",
			ENotificationDurationType.Long);
	}

	public static void NotifySettingsChanged(object source = null)
	{
		SettingsChanged?.Invoke(source, EventArgs.Empty);
	}

	private static FireSupportPurchaseResponse GetLastPurchaseDenial(ESupportType supportType)
	{
		if (_lastPurchaseDenial == null)
		{
			return null;
		}

		if (Enum.TryParse(_lastPurchaseDenial.SupportType, ignoreCase: true, out ESupportType deniedType) &&
		    deniedType != ESupportType.None &&
		    deniedType != supportType)
		{
			return null;
		}

		return _lastPurchaseDenial;
	}

	private static void RememberPurchaseDenial(ESupportType supportType, FireSupportPurchaseResponse response)
	{
		if (response == null)
		{
			return;
		}

		if (string.IsNullOrWhiteSpace(response.SupportType))
		{
			response.SupportType = supportType.ToString();
		}

		_lastPurchaseDenial = response;
	}

	private static int GetAuthorizationCount(FireSupportPurchaseResponse response, ESupportType supportType)
	{
		if (response?.Authorizations != null &&
		    response.Authorizations.TryGetValue(GetAuthorizationLedgerKey(supportType), out int count))
		{
			return Math.Max(0, count);
		}

		return FireSupportAuthorizations.Get(supportType);
	}

	private static string GetAuthorizationLedgerKey(ESupportType supportType)
	{
		return supportType switch
		{
			ESupportType.Strafe => "A10",
			ESupportType.DoubleStrafe => "DoublePass",
			ESupportType.Extract => "Extraction",
			ESupportType.PriorityExfil => "PriorityExfil",
			ESupportType.Uav => "Uav",
			ESupportType.FocusedSweep => "FocusedSweep",
			_ => supportType.ToString()
		};
	}

	private static int GetCost(ESupportType supportType)
	{
		int cost = supportType switch
		{
			ESupportType.Strafe => _syncedStrafeCost ?? _serverStrafeCost ?? GetConfiguredCost(supportType),
			ESupportType.DoubleStrafe => _syncedDoubleStrafeCost ?? _serverDoubleStrafeCost ?? GetConfiguredCost(supportType),
			ESupportType.Extract => _syncedExtractionCost ?? _serverExtractionCost ?? GetConfiguredCost(supportType),
			ESupportType.PriorityExfil => _syncedPriorityExfilCost ?? _serverPriorityExfilCost ?? GetConfiguredCost(supportType),
			ESupportType.Uav => _syncedUavCost ?? _serverUavCost ?? GetConfiguredCost(supportType),
			ESupportType.FocusedSweep => _syncedFocusedSweepCost ?? _serverFocusedSweepCost ?? GetConfiguredCost(supportType),
			_ => 0
		};
		LogEffectiveCostIfChanged(supportType, cost, GetCostSource(supportType));
		return cost;
	}

	private static string GetCostSource(ESupportType supportType)
	{
		return supportType switch
		{
			ESupportType.Strafe when _syncedStrafeCost.HasValue => "FikaHost",
			ESupportType.DoubleStrafe when _syncedDoubleStrafeCost.HasValue => "FikaHost",
			ESupportType.Extract when _syncedExtractionCost.HasValue => "FikaHost",
			ESupportType.PriorityExfil when _syncedPriorityExfilCost.HasValue => "FikaHost",
			ESupportType.Uav when _syncedUavCost.HasValue => "FikaHost",
			ESupportType.FocusedSweep when _syncedFocusedSweepCost.HasValue => "FikaHost",
			ESupportType.Strafe when _serverStrafeCost.HasValue => "ServerURL",
			ESupportType.DoubleStrafe when _serverDoubleStrafeCost.HasValue => "ServerURL",
			ESupportType.Extract when _serverExtractionCost.HasValue => "ServerURL",
			ESupportType.PriorityExfil when _serverPriorityExfilCost.HasValue => "ServerURL",
			ESupportType.Uav when _serverUavCost.HasValue => "ServerURL",
			ESupportType.FocusedSweep when _serverFocusedSweepCost.HasValue => "ServerURL",
			_ => "LocalF12"
		};
	}

	private static void GrantAuthorization(ESupportType supportType, bool notify)
	{
		FireSupportAuthorizations.Grant(supportType);
		if (notify)
		{
			NotifyAuthorizationPurchased(supportType);
		}
	}

	private static void GrantServerAuthorization(ESupportType supportType, bool notify)
	{
		// The server charged for this credit, so it belongs to the ledger-backed
		// store; the next config sync will confirm or correct it.
		FireSupportAuthorizations.GrantServer(supportType);
		if (notify)
		{
			NotifyAuthorizationPurchased(supportType);
		}
	}

	private static bool ShouldRequireServerConfig()
	{
		return PluginSettings.UseServerConfigUrl?.Value == true &&
		       PluginSettings.RequireServerConfigInFika?.Value == true;
	}

	private static bool RequiresServerPurchase(PaymentSource paymentSource)
	{
		return paymentSource == PaymentSource.StashRoubles ||
		       paymentSource == PaymentSource.PreferCarriedThenStash ||
		       paymentSource == PaymentSource.PreferStashThenCarried;
	}

	private static bool ShouldUseCarriedForPurchase(PaymentSource paymentSource, int cost)
	{
		return paymentSource == PaymentSource.CarriedRoubles ||
		       paymentSource == PaymentSource.PreferCarriedThenStash && GetCarriedRoubles() >= cost ||
		       paymentSource == PaymentSource.PreferStashThenCarried &&
		       _serverStashRoubleBalance.HasValue &&
		       _serverStashRoubleBalance.Value < cost &&
		       GetCarriedRoubles() >= cost;
	}

	private static bool CanSpendCarriedForActivePaymentSource(int cost)
	{
		PaymentSource paymentSource = GetActivePaymentSource();
		return paymentSource == PaymentSource.CarriedRoubles ||
		       paymentSource == PaymentSource.PreferCarriedThenStash && GetCarriedRoubles() >= cost ||
		       paymentSource == PaymentSource.PreferStashThenCarried &&
		       _serverStashRoubleBalance.HasValue &&
		       _serverStashRoubleBalance.Value < cost &&
		       GetCarriedRoubles() >= cost;
	}

	private static void LogEffectiveCostIfChanged(ESupportType supportType, int cost, string source)
	{
		if (supportType == ESupportType.None)
		{
			return;
		}

		if (s_lastLoggedCost.TryGetValue(supportType, out CostLogState last) &&
		    last.Cost == cost &&
		    string.Equals(last.Source, source, StringComparison.Ordinal))
		{
			return;
		}

		s_lastLoggedCost[supportType] = new CostLogState(cost, source);
		TscDiagnostics.LogPayment($"Effective TSC cost product={supportType} source={source} cost={cost}");
	}

	private static int GetCarriedRoubles()
	{
		Player player = Singleton<GameWorld>.Instance?.MainPlayer;
		if (player == null)
		{
			return 0;
		}

		int total = 0;
		foreach (Item item in GetCarriedRoubleStacks(player))
		{
			if (item != null && item.StackObjectsCount > 0)
			{
				total += item.StackObjectsCount;
			}
		}

		return total;
	}

	private static bool TrySpendCarriedRoubles(int cost, out int carriedRoubles)
	{
		carriedRoubles = 0;
		Player player = Singleton<GameWorld>.Instance?.MainPlayer;
		if (player == null)
		{
			return false;
		}

		var roubleStacks = new List<Item>();
		foreach (Item item in GetCarriedRoubleStacks(player))
		{
			if (item == null || item.StackObjectsCount <= 0)
			{
				continue;
			}

			roubleStacks.Add(item);
			carriedRoubles += item.StackObjectsCount;
		}

		if (carriedRoubles < cost)
		{
			return false;
		}

		int remainingCost = cost;
		foreach (Item stack in roubleStacks)
		{
			if (remainingCost <= 0)
			{
				break;
			}

			int amountToTake = Math.Min(stack.StackObjectsCount, remainingCost);
			remainingCost -= amountToTake;

			if (amountToTake >= stack.StackObjectsCount)
			{
				RemoveStack(stack);
				continue;
			}

			stack.StackObjectsCount -= amountToTake;
			stack.RaiseRefreshEvent(refreshIcon: true, checkMagazine: false);
		}

		return true;
	}

	private static IEnumerable<Item> GetCarriedRoubleStacks(Player player)
	{
		// Walk the full equipment tree rather than GetReachableItemsOfType:
		// "reachable" excludes the secure container, so money stored there was
		// invisible to carried-rouble counting and spending.
		Item equipmentRoot = player?.Profile?.Inventory?.Equipment;
		if (equipmentRoot != null)
		{
			foreach (Item item in equipmentRoot.GetAllItems())
			{
				if (IsRouble(item))
				{
					yield return item;
				}
			}

			yield break;
		}

		if (player?.InventoryController != null)
		{
			foreach (Item item in player.InventoryController.GetReachableItemsOfType<Item>(IsRouble))
			{
				yield return item;
			}

			yield break;
		}

		foreach (Item item in player.Profile.Inventory.AllRealPlayerItems)
		{
			if (IsRouble(item))
			{
				yield return item;
			}
		}
	}

	private static bool IsRouble(Item item)
	{
		return item != null && item.TemplateId == ItemConstants.ROUBLES_TPL;
	}

	private static void RemoveStack(Item stack)
	{
		ItemAddress address = stack.CurrentAddress ?? stack.Parent;
		if (address != null)
		{
			address.RemoveWithoutRestrictions(stack);
			return;
		}

		stack.StackObjectsCount = 0;
		stack.RaiseRefreshEvent(refreshIcon: true, checkMagazine: false);
	}

	private static void NotifyInsufficientFunds(int cost, int carriedRoubles)
	{
		if (carriedRoubles < 0)
		{
			NotificationManagerClass.DisplayWarningNotification(
				$"Fire support requires {FormatRoubles(cost)}. {GetEffectiveBalanceLabel()} are still syncing.",
				ENotificationDurationType.Long);
			return;
		}

		NotificationManagerClass.DisplayWarningNotification(
			$"Fire support requires {FormatRoubles(cost)}. {GetEffectiveBalanceLabel()}: {FormatRoubles(carriedRoubles)}.",
			ENotificationDurationType.Long);
	}

	private static void NotifyServerPaymentRequired(ESupportType supportType)
	{
		NotificationManagerClass.DisplayWarningNotification(
			$"{GetSupportName(supportType)} requires TerraGroup server payment confirmation.",
			ENotificationDurationType.Long);
	}

	private static void NotifyAuthorizationRequired(ESupportType supportType)
	{
		NotificationManagerClass.DisplayWarningNotification(
			$"{GetSupportName(supportType)} requires a TerraGroup phone authorization.",
			ENotificationDurationType.Long);
	}

	private static string FormatRoubles(int amount)
	{
		return $"{amount:N0} RUB";
	}

	public static string GetSupportName(ESupportType supportType)
	{
		return supportType switch
		{
			ESupportType.Strafe => "A-10 strafe",
			ESupportType.DoubleStrafe => "A-10 double pass",
			ESupportType.Extract => "UH-60 extraction",
			ESupportType.PriorityExfil => "priority exfil",
			ESupportType.Uav => "UAV recon",
			ESupportType.FocusedSweep => "focused sweep",
			_ => "fire support"
		};
	}
}
