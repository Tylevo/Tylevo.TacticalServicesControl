using Cysharp.Threading.Tasks;
using EFT.Communications;
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
	private static PaymentCurrency? _syncedPaymentCurrency;
	private static int? _serverStrafeCost;
	private static int? _serverDoubleStrafeCost;
	private static int? _serverExtractionCost;
	private static int? _serverPriorityExfilCost;
	private static int? _serverUavCost;
	private static int? _serverFocusedSweepCost;
	private static PaymentMode? _serverPaymentMode;
	private static PaymentCurrency? _serverPaymentCurrency;
	private static int? _serverStashCurrencyBalance;
	private static PaymentCurrency? _serverStashBalanceCurrency;
	private static readonly Dictionary<PaymentCurrency, int> s_stashBalances = new();
	private static Dictionary<string, string> s_serverServiceCurrencies = new();
	private static Dictionary<string, string> s_syncedServiceCurrencies;
	private static int _serverConfigRevision;
	private static bool _serverConfigUnavailable;
	private static bool _serverPaymentCurrencyInvalid;
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
			"UplinkLocked" => FireSupportProgression.LockedMessage,
			"AuthorizationLimitReached" => "AUTHORIZATION LIMIT REACHED",
			"InsufficientRoubles" or "InsufficientFunds" => "INSUFFICIENT FUNDS",
			"RateLimited" => "PURCHASE ALREADY PROCESSING",
			"ServerConfigUnavailable" or "RequestFailed" or "InvalidServerResponse" => "SERVER PAYMENT UNAVAILABLE",
			"ProfileNotFound" or "ProfileSessionMismatch" => "PROFILE VERIFY FAILED",
			"ServiceUnavailable" => "SERVICE UNAVAILABLE",
			"PaymentSourceNotServerBacked" => "SERVER PAYMENT DISABLED",
			"PurchaseCurrencyMismatch" => "PURCHASE CURRENCY CHANGED",
			"InvalidPaymentCurrency" => "SERVER CURRENCY INVALID",
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
			case "UplinkLocked":
				return FireSupportProgression.LockedMessage;
			case "AuthorizationLimitReached":
				int held = GetAuthorizationCount(denial, supportType);
				return held > 0
					? $"{held} held. Deploy one from the Uplink before buying more."
					: "Deploy an existing authorization from the Uplink before buying more.";
			case "InsufficientRoubles":
			case "InsufficientFunds":
				return $"{GetEffectiveBalanceLabel(supportType)}: {FormatCurrency(Math.Max(denial.NewBalance, GetEffectiveBalance(supportType)), GetActivePaymentCurrency(supportType))}.";
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
				return "Update the TSC server to enable stash payments.";
			case "PurchaseCurrencyMismatch":
				return "Refresh TSC pricing and confirm the purchase again.";
			case "InvalidPaymentCurrency":
				return "The server administrator must select RUB, USD, EUR, GP, or BTC.";
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
			$"Using host TSC prices: A-10={FormatCurrency(strafeCost)}, A-10 double pass={FormatCurrency(doubleStrafeCost)}, UH-60 extraction={FormatCurrency(extractionCost)}, UH-60 cargo transfer={FormatCurrency(priorityExfilCost)}, UAV={FormatCurrency(uavCost)}, Focused sweep={FormatCurrency(focusedSweepCost)}");
	}

	public static void ClearSyncedCosts()
	{
		bool hadSyncedSettings =
			HasSyncedCosts ||
			_syncedPaymentMode.HasValue ||
			_syncedPaymentCurrency.HasValue;
		_syncedStrafeCost = null;
		_syncedDoubleStrafeCost = null;
		_syncedExtractionCost = null;
		_syncedPriorityExfilCost = null;
		_syncedUavCost = null;
		_syncedFocusedSweepCost = null;
		_syncedPaymentMode = null;
		_syncedPaymentCurrency = null;
		s_syncedServiceCurrencies = null;
		if (hadSyncedSettings)
		{
			TscDiagnostics.LogPayment("Cleared host TSC prices, payment mode, and currency.");
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
			$"Using server URL TSC prices revision={revision}: A-10={FormatCurrency(strafeCost)}, A-10 double pass={FormatCurrency(doubleStrafeCost)}, UH-60 extraction={FormatCurrency(extractionCost)}, UH-60 cargo transfer={FormatCurrency(priorityExfilCost)}, UAV={FormatCurrency(uavCost)}, Focused sweep={FormatCurrency(focusedSweepCost)}");
	}

	public static void SetServerConfigGlobals(
		int strafeCost,
		int doubleStrafeCost,
		int extractionCost,
		int priorityExfilCost,
		int uavCost,
		int focusedSweepCost,
		PaymentMode paymentMode,
		PaymentSource paymentSource,
		PaymentCurrency paymentCurrency) =>
		// Compatibility for older interop callers. The source argument is ignored.
		SetServerConfigGlobals(strafeCost, doubleStrafeCost, extractionCost, priorityExfilCost,
			uavCost, focusedSweepCost, paymentMode, paymentCurrency);

	public static void SetServerConfigGlobals(
		int strafeCost,
		int doubleStrafeCost,
		int extractionCost,
		int priorityExfilCost,
		int uavCost,
		int focusedSweepCost,
		PaymentMode paymentMode,
		PaymentCurrency paymentCurrency)
	{
		_serverStrafeCost = strafeCost;
		_serverDoubleStrafeCost = doubleStrafeCost;
		_serverExtractionCost = extractionCost;
		_serverPriorityExfilCost = priorityExfilCost;
		_serverUavCost = uavCost;
		_serverFocusedSweepCost = focusedSweepCost;
		_serverPaymentMode = paymentMode;
		_serverPaymentCurrency = PaymentCurrencyInfo.Normalize(paymentCurrency);
		_serverPaymentCurrencyInvalid = false;
		TscDiagnostics.LogPayment(
			$"Using server URL TSC globals: mode={paymentMode}, source=StashRoubles, currency={GetActivePaymentCurrency()}, A-10={FormatCurrency(strafeCost)}, A-10 double pass={FormatCurrency(doubleStrafeCost)}, UH-60 extraction={FormatCurrency(extractionCost)}, UH-60 cargo transfer={FormatCurrency(priorityExfilCost)}, UAV={FormatCurrency(uavCost)}, Focused sweep={FormatCurrency(focusedSweepCost)}");
	}

	public static void ClearServerConfig()
	{
		bool hadServerSettings = HasServerConfigCosts ||
		                         _serverPaymentMode.HasValue ||
		                         _serverPaymentCurrency.HasValue ||
		                         _serverStashCurrencyBalance.HasValue ||
		                         _serverConfigUnavailable;
		_serverStrafeCost = null;
		_serverDoubleStrafeCost = null;
		_serverExtractionCost = null;
		_serverPriorityExfilCost = null;
		_serverUavCost = null;
		_serverFocusedSweepCost = null;
		_serverPaymentMode = null;
		_serverPaymentCurrency = null;
		s_serverServiceCurrencies.Clear();
		_serverStashCurrencyBalance = null;
		s_stashBalances.Clear();
		_serverStashBalanceCurrency = null;
		_serverConfigRevision = 0;
		_serverConfigUnavailable = false;
		_serverPaymentCurrencyInvalid = false;
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
		                               _serverPaymentCurrency.HasValue;
		_serverStrafeCost = null;
		_serverDoubleStrafeCost = null;
		_serverExtractionCost = null;
		_serverPriorityExfilCost = null;
		_serverUavCost = null;
		_serverFocusedSweepCost = null;
		_serverPaymentMode = null;
		_serverPaymentCurrency = null;
		s_serverServiceCurrencies.Clear();
		if (hadServerGlobalSettings)
		{
			TscDiagnostics.LogPayment(
				"Cleared server URL TSC global prices and payment settings; preserved profile payment state.");
		}
	}

	public static void ClearServerProfileState()
	{
		bool hadServerProfileState =
			_serverStashCurrencyBalance.HasValue ||
			_serverStashBalanceCurrency.HasValue ||
			_lastPurchaseDenial != null;
		_serverStashCurrencyBalance = null;
		s_stashBalances.Clear();
		_serverStashBalanceCurrency = null;
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
		// Legacy Fika packet field: read it without restoring a payment selector.
	}

	public static void SetSyncedPaymentCurrency(PaymentCurrency paymentCurrency)
	{
		_syncedPaymentCurrency = PaymentCurrencyInfo.Normalize(paymentCurrency);
		_serverPaymentCurrencyInvalid = false;
		TscDiagnostics.LogPayment($"Using host TSC payment currency: {_syncedPaymentCurrency}");
	}

	public static void SetServerConfigPayment(
		PaymentMode paymentMode,
		PaymentSource paymentSource,
		PaymentCurrency paymentCurrency,
		int revision,
		int? stashCurrencyBalance)
	{
		_serverPaymentMode = paymentMode;
		_serverPaymentCurrency = PaymentCurrencyInfo.Normalize(paymentCurrency);
		_serverConfigRevision = revision;
		_serverStashCurrencyBalance = stashCurrencyBalance;
		_serverStashBalanceCurrency = stashCurrencyBalance.HasValue
			? _serverPaymentCurrency
			: null;
		_serverConfigUnavailable = false;
		_serverPaymentCurrencyInvalid = false;
		_serverConfigUnavailableReason = null;
		TscDiagnostics.LogPayment(
			$"Using server URL TSC payment revision={revision}: mode={paymentMode}, source=StashRoubles, currency={_serverPaymentCurrency}, stashBalance={(stashCurrencyBalance.HasValue ? FormatCurrency(stashCurrencyBalance.Value, _serverPaymentCurrency.Value) : "unknown")}");
	}

	public static void SetServerProfileState(
		int revision,
		int? stashCurrencyBalance,
		PaymentCurrency paymentCurrency,
		bool persistenceEnabled,
		bool refundFailedDispatch,
		bool spendCreditsBeforeCash,
		bool allowAutoPurchaseOnUse)
	{
		PaymentCurrency normalizedCurrency =
			PaymentCurrencyInfo.Normalize(paymentCurrency);
		_serverConfigRevision = revision;
		_serverStashCurrencyBalance = stashCurrencyBalance;
		_serverStashBalanceCurrency = stashCurrencyBalance.HasValue
			? normalizedCurrency
			: null;
		_serverConfigUnavailable = false;
		_serverPaymentCurrencyInvalid = false;
		_serverConfigUnavailableReason = null;
		_serverPurchasePersistenceEnabled = persistenceEnabled;
		_serverRefundFailedDispatch = refundFailedDispatch;
		_serverSpendCreditsBeforeCash = spendCreditsBeforeCash;
		_serverAllowAutoPurchaseOnUse = allowAutoPurchaseOnUse;
		TscDiagnostics.LogPayment(
			$"Using server URL TSC profile state revision={revision}: currency={normalizedCurrency}, stashBalance={(stashCurrencyBalance.HasValue ? FormatCurrency(stashCurrencyBalance.Value, normalizedCurrency) : "unknown")}, persistence={persistenceEnabled}");
	}

	internal static void ApplyAuthenticatedStashBalance(
		PaymentCurrency paymentCurrency,
		int balance,
		string source)
	{
		if (balance < 0)
		{
			return;
		}

		PaymentCurrency normalizedCurrency =
			PaymentCurrencyInfo.Normalize(paymentCurrency);
		s_stashBalances[normalizedCurrency] = balance;
		_serverStashCurrencyBalance = balance;
		_serverStashBalanceCurrency = normalizedCurrency;
		TscDiagnostics.LogPayment(
			$"Applied authenticated stash balance from {source}: {FormatCurrency(balance, normalizedCurrency)}");
		NotifySettingsChanged(source);
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

	public static void MarkServerPaymentCurrencyInvalid(string reason)
	{
		_serverPaymentCurrencyInvalid = true;
		_serverConfigUnavailable = true;
		_serverConfigUnavailableReason = reason;
		FireSupportPlugin.LogSource.LogError(
			$"Server URL TSC payment currency is invalid; purchases are blocked: {reason}");
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
		return PaymentSource.StashRoubles;
	}

	public static PaymentSource GetPaymentSourcePolicy() =>
		PaymentSource.StashRoubles;

	public static PaymentSource GetActivePaymentSource(ESupportType supportType = ESupportType.None)
	{
		return PaymentSource.StashRoubles;
	}

	public static PaymentCurrency GetConfiguredPaymentCurrency()
	{
		return PaymentCurrencyInfo.Normalize(
			PluginSettings.PaymentCurrency?.Value ?? PaymentCurrency.RUB);
	}

	public static PaymentCurrency GetActivePaymentCurrency(ESupportType supportType = ESupportType.None)
	{
		PaymentCurrency global = _syncedPaymentCurrency ?? _serverPaymentCurrency ?? GetConfiguredPaymentCurrency();
		if (supportType == ESupportType.None) return global;
		string code = ServicePaymentPolicy.GetCurrencyCode(global.ToString(),
			s_syncedServiceCurrencies ?? s_serverServiceCurrencies, supportType);
		return PaymentCurrencyInfo.TryParse(code, out PaymentCurrency currency) ? currency : (PaymentCurrency)(-1);
	}

	public static Dictionary<string, string> GetServiceCurrencies() =>
		new(s_syncedServiceCurrencies ?? s_serverServiceCurrencies);

	public static void SetServiceCurrencies(Dictionary<string, string> currencies, bool synced = false)
	{
		var copy = currencies == null ? new Dictionary<string, string>() : new Dictionary<string, string>(currencies);
		if (synced) s_syncedServiceCurrencies = copy;
		else s_serverServiceCurrencies = copy;
	}

	public static void SetStashBalances(Dictionary<string, int> balances)
	{
		s_stashBalances.Clear();
		if (balances != null)
		{
			foreach (var entry in balances)
				if (PaymentCurrencyInfo.TryParse(entry.Key, out PaymentCurrency currency) && entry.Value >= 0)
					s_stashBalances[currency] = entry.Value;
		}
		else if (_serverStashBalanceCurrency.HasValue && _serverStashCurrencyBalance.HasValue)
			s_stashBalances[_serverStashBalanceCurrency.Value] = _serverStashCurrencyBalance.Value;
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

	public static int GetEffectiveBalance(ESupportType supportType = ESupportType.None)
	{
		if (!PaymentCurrencyInfo.TryParse(GetActivePaymentCurrency(supportType).ToString(), out _)) return -1;
		return GetServerStashBalance(GetActivePaymentCurrency(supportType)) ?? -1;
	}

	public static string GetEffectiveBalanceLabel(ESupportType supportType = ESupportType.None)
	{
		string currencyName = PaymentCurrencyInfo.GetDisplayName(GetActivePaymentCurrency(supportType));
		return $"Stash {currencyName}";
	}

	public static string FormatCurrency(int amount)
	{
		return FormatCurrency(amount, GetActivePaymentCurrency());
	}

	public static string FormatCurrency(int amount, PaymentCurrency currency)
	{
		return PaymentCurrencyInfo.TryParse(currency.ToString(), out _) ? PaymentCurrencyInfo.FormatCode(amount, currency) : "UNAVAILABLE";
	}

	public static bool CanAfford(ESupportType supportType, bool notify = false)
	{
		if (!PaymentCurrencyInfo.TryParse(GetActivePaymentCurrency(supportType).ToString(), out _) ||
		    !FireSupportServiceAvailability.IsServiceEnabled(supportType))
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

		int effectiveBalance = GetEffectiveBalance(supportType);
		bool canAfford = effectiveBalance >= cost;

		if (!canAfford && notify)
		{
			NotifyInsufficientFunds(cost, effectiveBalance, supportType);
		}

		return canAfford;
	}

	public static bool CanDeployFromRadial(ESupportType supportType, bool notify = false)
	{
		if (!PaymentCurrencyInfo.TryParse(GetActivePaymentCurrency(supportType).ToString(), out _) ||
		    !FireSupportServiceAvailability.IsServiceEnabled(supportType))
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
		if (paymentMode == PaymentMode.DirectRadial && !_serverPurchasePersistenceEnabled &&
		    FireSupportAuthorizations.HasLocalDeployable(supportType))
		{
			// A rejected nonpersistent dispatch refunds its already-paid credit.
			// The retry must remain available even when that purchase spent the last cash.
			return true;
		}

		return CanAfford(supportType, notify);
	}

	public static UniTask<FireSupportAuthorizationUse> TryPayForDeploymentAsync(ESupportType supportType) =>
		TryPayForDeploymentAsync(supportType, requirePrepaidAuthorization: false);

	public static UniTask<FireSupportAuthorizationUse> TryPayForDeploymentAsync(
		ESupportType supportType, bool requirePrepaidAuthorization) =>
		TryPayForDeploymentCoreAsync(supportType, consumePurchasedAuthorization: false,
			requirePrepaidAuthorization: requirePrepaidAuthorization);

	private static async UniTask<FireSupportAuthorizationUse> TryPayForDeploymentCoreAsync(
		ESupportType supportType, bool consumePurchasedAuthorization,
		string operationId = null, string serverSessionKey = null, string serverProfileId = null,
		bool? purchasedAuthorizationServerBacked = null, bool requirePrepaidAuthorization = false)
	{
		operationId ??= Guid.NewGuid().ToString("N");
		serverSessionKey ??= FireSupportServerConfigClient.GetAuthenticatedSessionKey();
		serverProfileId ??= FireSupportServerConfigClient.GetAuthenticatedProfileId();
		bool IsBoundProfile() => !string.IsNullOrWhiteSpace(serverSessionKey) &&
			string.Equals(serverSessionKey, FireSupportServerConfigClient.GetAuthenticatedSessionKey(), StringComparison.Ordinal) &&
			FireSupportServerConfigClient.IsAuthenticatedProfile(serverProfileId);
		if (!IsBoundProfile()) return FireSupportAuthorizationUse.Failed(supportType);
		if (!await FireSupportServerConfigClient.EnsureLocalProgressionVerifiedAsync())
		{
			NotifyServiceUnavailable(supportType);
			return FireSupportAuthorizationUse.Failed(supportType);
		}
		if (!IsBoundProfile()) return FireSupportAuthorizationUse.Failed(supportType);
		if (!PaymentCurrencyInfo.TryParse(GetActivePaymentCurrency(supportType).ToString(), out _) ||
		    !FireSupportServiceAvailability.IsServiceEnabled(supportType))
		{
			NotifyServiceUnavailable(supportType);
			return FireSupportAuthorizationUse.Failed(supportType);
		}

		PaymentMode paymentMode = GetActivePaymentMode();
		if (consumePurchasedAuthorization && !purchasedAuthorizationServerBacked.HasValue)
			return FireSupportAuthorizationUse.Failed(supportType);
		if (AuthorizationConsumePolicy.ShouldConsumeBeforeCash(paymentMode, _serverPurchasePersistenceEnabled,
			    _serverSpendCreditsBeforeCash, consumePurchasedAuthorization, requirePrepaidAuthorization) &&
		    FireSupportAuthorizations.TryConsumeForDeployment(supportType, out ESupportType consumedType, out bool serverBacked,
			    requiredServerBacked: consumePurchasedAuthorization ? purchasedAuthorizationServerBacked :
				    paymentMode == PaymentMode.DirectRadial && !_serverPurchasePersistenceEnabled && !requirePrepaidAuthorization
					    ? false : null))
		{
			// Free, legacy carried, and nonpersistent stash credits have no ledger
			// entry. Retain the existing local-use behavior when persistence is off.
			if (!serverBacked || !_serverPurchasePersistenceEnabled)
			{
				NotificationManager.DisplayMessageNotification(
					$"Used prepaid {GetSupportName(consumedType)} authorization.",
					ENotificationDurationType.Default,
					ENotificationIconType.Default,
					null);
				return new FireSupportAuthorizationUse
				{
					Ok = true,
					ConsumedAuthorization = true,
					PurchasedForBaseRequest = AuthorizationConsumePolicy.PurchasedForBaseRequest(paymentMode,
						_serverPurchasePersistenceEnabled, consumePurchasedAuthorization, requirePrepaidAuthorization),
					ConsumedAuthorizationType = consumedType,
					RequestId = operationId,
					ServerBacked = false
				};
			}

			await s_serverLedgerMutationGate.WaitAsync();
			try
			{
				if (!IsBoundProfile()) return FireSupportAuthorizationUse.Failed(supportType);
				FireSupportPurchaseResponse response = await FireSupportServerConfigClient.ConsumeAuthorizationAsync(
					consumedType,
					operationId,
					_serverConfigRevision,
					serverSessionKey,
					serverProfileId);
				if (!IsBoundProfile()) return FireSupportAuthorizationUse.Failed(supportType);
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
					NotificationManager.DisplayMessageNotification(
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

		if (requirePrepaidAuthorization || paymentMode == PaymentMode.PhoneAuthorizations)
		{
			NotifyAuthorizationRequired(supportType);
			return FireSupportAuthorizationUse.Failed(supportType);
		}

		// A successful auto-purchase gets one consume attempt in every payment
		// mode. If its ledger is missing, retain the purchased credit for recovery
		// instead of recursively purchasing until the authorization cap.
		if (consumePurchasedAuthorization) return FireSupportAuthorizationUse.Failed(supportType);
		if (!_serverPurchasePersistenceEnabled || _serverAllowAutoPurchaseOnUse)
		{
			FireSupportPurchaseResponse purchase = await PurchaseAuthorizationAsync(supportType, notify: true);
			if (AuthorizationConsumePolicy.TryGetPurchasedSource(purchase, out bool purchasedServerBacked))
			{
				return await TryPayForDeploymentCoreAsync(supportType, consumePurchasedAuthorization: true,
					operationId, serverSessionKey, serverProfileId, purchasedServerBacked);
			}

			return FireSupportAuthorizationUse.Failed(supportType);
		}

		// An explicitly free direct request needs neither a debit nor a new credit.
		// Paid requests require the server-confirmed purchase above.
		if (GetCost(supportType) > 0)
		{
			NotifyServerPaymentRequired(supportType);
			return FireSupportAuthorizationUse.Failed(supportType);
		}
		return new FireSupportAuthorizationUse
		{
			Ok = true,
			ConsumedAuthorization = false,
			ConsumedAuthorizationType = supportType,
			RequestId = operationId
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
			PaymentSource = action ?? string.Empty,
			Currency = PaymentCurrencyInfo.GetCode(GetActivePaymentCurrency(supportType))
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

	public static void TryPurchaseAuthorizationAsync(
		ESupportType supportType,
		bool notify,
		Action<bool, FireSupportPurchaseResponse> callback)
	{
		TryPurchaseAuthorizationAsyncInternal(supportType, notify, callback).Forget();
	}

	/// <summary>
	/// Menu-only server purchase path. A successful pre-raid purchase must return
	/// a complete persistent ledger from the authenticated server.
	/// </summary>
	public static async UniTask<FireSupportPurchaseResponse> PurchasePersistentAuthorizationAsync(
		ESupportType supportType,
		string requestId,
		int expectedCost,
		PaymentCurrency expectedCurrency,
		string expectedSessionKey,
		string expectedProfileId)
	{
		bool validExpectedCurrency = PaymentCurrencyInfo.TryParse(expectedCurrency.ToString(), out _);
		var fallback = new FireSupportPurchaseResponse
		{
			Ok = false,
			Reason = "ServerConfigUnavailable",
			SupportType = supportType.ToString(),
			Cost = expectedCost >= 0 ? expectedCost : GetCost(supportType),
			PaymentSource = nameof(PaymentSource.StashRoubles),
			Currency = PaymentCurrencyInfo.GetCode(expectedCurrency),
			NewBalance = expectedCurrency == GetActivePaymentCurrency(supportType)
				? GetServerStashBalance(expectedCurrency) ?? -1
				: -1,
			AuthorizationGranted = false,
			ServerRevision = _serverConfigRevision,
			RequestId = requestId ?? string.Empty
		};

		if (!validExpectedCurrency) { fallback.Reason = "InvalidPaymentCurrency"; return fallback; }

		// The menu gates new purchases. Send known interrupted transactions to
		// the backend even when progression is now locked, so recovery can finish.
		// The backend enforces progression before any new purchase is charged.

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
					expectedCurrency,
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

			bool responseCurrencyMatches =
				TryNormalizeResponseCurrency(serverResult, expectedCurrency);
			PaymentCurrency replayCurrency = PaymentCurrency.RUB;
			bool acceptedPinnedCurrencyReplay =
				!responseCurrencyMatches &&
				serverResult.Ok &&
				serverResult.AuthorizationGranted &&
				string.Equals(
					serverResult.Reason,
					"AlreadyAccepted",
					StringComparison.OrdinalIgnoreCase) &&
				PaymentCurrencyInfo.TryParse(
					serverResult.Currency,
					out replayCurrency);
			if (acceptedPinnedCurrencyReplay)
			{
				// A lost response can be replayed after the administrator changes
				// currency. The exact request ID and authenticated profile above
				// correlate this accepted journal entry. Apply its authoritative
				// credits, but leave the current currency's balance and price
				// untouched; the caller refreshes immediately after success.
				serverResult.Currency =
					PaymentCurrencyInfo.GetCode(replayCurrency);
			}
			else if (!responseCurrencyMatches)
			{
				serverResult.Ok = false;
				serverResult.AuthorizationGranted = false;
				serverResult.Reason = "PurchaseCurrencyMismatch";
				return serverResult;
			}

			if (responseCurrencyMatches &&
			    serverResult.NewBalance >= 0 &&
			    expectedCurrency == GetActivePaymentCurrency(supportType))
			{
				_serverStashCurrencyBalance = serverResult.NewBalance;
				_serverStashBalanceCurrency = expectedCurrency;
				s_stashBalances[expectedCurrency] = serverResult.NewBalance;
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
		string expectedSessionKey = FireSupportServerConfigClient.GetAuthenticatedSessionKey();
		string expectedProfileId = FireSupportServerConfigClient.GetAuthenticatedProfileId();
		bool IsBoundProfile() => !string.IsNullOrWhiteSpace(expectedSessionKey) &&
			string.Equals(expectedSessionKey, FireSupportServerConfigClient.GetAuthenticatedSessionKey(), StringComparison.Ordinal) &&
			FireSupportServerConfigClient.IsAuthenticatedProfile(expectedProfileId);
		FireSupportPurchaseResponse ProfileChanged() => new FireSupportPurchaseResponse
		{
			Ok = false,
			SupportType = supportType.ToString(),
			Reason = "ProfileSessionChanged",
			NewBalance = -1,
			AuthorizationGranted = false
		};
		if (!IsBoundProfile()) return ProfileChanged();
		bool progressionVerified = await FireSupportServerConfigClient.EnsureLocalProgressionVerifiedAsync();
		if (!IsBoundProfile()) return ProfileChanged();
		PaymentCurrency paymentCurrency = GetActivePaymentCurrency(supportType);
		var result = new FireSupportPurchaseResponse
		{
			Ok = false,
			SupportType = supportType.ToString(),
			Cost = GetCost(supportType),
			PaymentSource = GetActivePaymentSource(supportType).ToString(),
			Currency = PaymentCurrencyInfo.GetCode(paymentCurrency),
			NewBalance = GetEffectiveBalance(supportType),
			AuthorizationGranted = false,
			ServerRevision = _serverConfigRevision
		};

		if (!progressionVerified || !FireSupportServiceAvailability.IsServiceEnabled(supportType))
		{
			result.Reason = progressionVerified ? "ServiceUnavailable" : "UplinkLocked";
			RememberPurchaseDenial(supportType, result);
			if (notify)
			{
				NotifyServiceUnavailable(supportType);
			}

			return result;
		}

		if (!PaymentCurrencyInfo.TryParse(GetActivePaymentCurrency(supportType).ToString(), out _) ||
		    _serverPaymentCurrencyInvalid ||
		    _serverConfigUnavailable && ShouldRequireServerConfig())
		{
			result.Reason = _serverPaymentCurrencyInvalid
				? "InvalidPaymentCurrency"
				: "ServerConfigUnavailable";
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
			result.PurchasedAuthorizationServerBacked = false;
			return result;
		}

		bool purchasePersistenceEnabled = _serverPurchasePersistenceEnabled;
		await s_serverLedgerMutationGate.WaitAsync();
		try
		{
			if (!IsBoundProfile()) return ProfileChanged();
			TscDiagnostics.LogPayment(
				$"TSC purchase request sent source=Stash supportType={supportType} cost={result.Cost} revision={_serverConfigRevision}.");
			FireSupportPurchaseResponse serverResult = await FireSupportServerConfigClient.PurchaseAuthorizationAsync(
				supportType,
				paymentCurrency,
				_serverConfigRevision,
				expectedSessionKey,
				expectedProfileId);
			if (!IsBoundProfile()) return ProfileChanged();
			serverResult.SupportType = string.IsNullOrWhiteSpace(serverResult.SupportType)
				? supportType.ToString()
				: serverResult.SupportType;
			serverResult.PaymentSource = string.IsNullOrWhiteSpace(serverResult.PaymentSource)
				? nameof(PaymentSource.StashRoubles)
				: serverResult.PaymentSource;
			serverResult.Cost = serverResult.Cost > 0 ? serverResult.Cost : result.Cost;
			serverResult.ServerRevision = serverResult.ServerRevision > 0 ? serverResult.ServerRevision : _serverConfigRevision;
			bool responseCurrencyMatches =
				TryNormalizeResponseCurrency(serverResult, paymentCurrency);
			bool activeCurrencyMatchesRequest =
				paymentCurrency == GetActivePaymentCurrency(supportType);
			if (!responseCurrencyMatches)
			{
				serverResult.Ok = false;
				serverResult.AuthorizationGranted = false;
				serverResult.Reason = "PurchaseCurrencyMismatch";
			}
			else if (!activeCurrencyMatchesRequest && !serverResult.Ok)
			{
				// The denial belongs to the currency pinned before the await.
				// Do not display an old-currency denial against the new quote.
				serverResult.AuthorizationGranted = false;
				serverResult.Reason = "PurchaseCurrencyMismatch";
				serverResult.NewBalance = -1;
			}

			if (responseCurrencyMatches &&
			    activeCurrencyMatchesRequest &&
			    serverResult.NewBalance >= 0)
			{
				_serverStashCurrencyBalance = serverResult.NewBalance;
				_serverStashBalanceCurrency = paymentCurrency;
				s_stashBalances[paymentCurrency] = serverResult.NewBalance;
			}

			bool authorizationsApplied =
				responseCurrencyMatches && ApplyIncludedAuthorizations(serverResult);
			if (!serverResult.Ok)
			{
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
				if (purchasePersistenceEnabled) GrantServerAuthorization(supportType, notify);
				else GrantAuthorization(supportType, notify);
			}

			serverResult.PurchasedAuthorizationServerBacked = authorizationsApplied || purchasePersistenceEnabled;
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
		if (response == null ||
		    !AuthorizationSnapshotPresence.ShouldApply(
			    response.AuthorizationsIncluded,
			    response.Authorizations))
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

	private static bool TryNormalizeResponseCurrency(
		FireSupportPurchaseResponse response,
		PaymentCurrency expectedCurrency)
	{
		expectedCurrency = PaymentCurrencyInfo.Normalize(expectedCurrency);
		if (response == null)
		{
			return false;
		}

		if (string.IsNullOrWhiteSpace(response.Currency))
		{
			// Pre-currency servers were RUB-only. Their omitted field is safe
			// only when this request was also quoted in RUB.
			if (expectedCurrency != PaymentCurrency.RUB)
			{
				return false;
			}

			response.Currency = PaymentCurrencyInfo.GetCode(PaymentCurrency.RUB);
			return true;
		}

		if (!PaymentCurrencyInfo.TryParse(response.Currency, out PaymentCurrency actualCurrency) ||
		    actualCurrency != expectedCurrency)
		{
			FireSupportPlugin.LogSource?.LogWarning(
				$"TSC ignored a purchase response in an unexpected currency. " +
				$"expected={PaymentCurrencyInfo.GetCode(expectedCurrency)}, actual={response.Currency}.");
			return false;
		}

		response.Currency = PaymentCurrencyInfo.GetCode(actualCurrency);
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
			? $"Paid {FormatCurrency(cost, GetActivePaymentCurrency(supportType))}. {supportName} authorization ready. Press [{deployKey}] to deploy from the Uplink."
			: $"{supportName} authorization ready. Press [{deployKey}] to deploy from the Uplink.";

		NotificationManager.DisplayMessageNotification(
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
		if (!PaymentCurrencyInfo.TryParse(GetActivePaymentCurrency(supportType).ToString(), out _) ||
		    !FireSupportServiceAvailability.IsServiceEnabled(supportType))
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
			NotificationManager.DisplayWarningNotification(
				$"{GetSupportName(supportType)} authorization limit reached.{countText} Deploy one from the Uplink before buying more.",
				ENotificationDurationType.Long);
			return;
		}

		if (!IsInsufficientFundsReason(reason) &&
		    !string.IsNullOrWhiteSpace(reason))
		{
			NotificationManager.DisplayWarningNotification(
				$"{GetSupportName(supportType)} authorization denied: {GetLastPurchaseDenialDetail(supportType)}",
				ENotificationDurationType.Long);
			return;
		}

		NotifyInsufficientFunds(GetCost(supportType), GetEffectiveBalance(supportType), supportType);
	}

	private static bool IsInsufficientFundsReason(string reason)
	{
		return string.Equals(reason, "InsufficientRoubles", StringComparison.OrdinalIgnoreCase) ||
		       string.Equals(reason, "InsufficientFunds", StringComparison.OrdinalIgnoreCase);
	}

	public static void NotifyServiceUnavailable(ESupportType supportType)
	{
		string localRestriction =
			FireSupportServiceAvailability.GetLocalRestrictionReason(supportType);
		NotificationManager.DisplayWarningNotification(
			string.IsNullOrWhiteSpace(localRestriction)
				? $"{GetSupportName(supportType)} is unavailable in the host's FireSupport settings."
				: localRestriction,
			ENotificationDurationType.Long);
	}

	public static void NotifyServerConfigUnavailable(ESupportType supportType)
	{
		NotificationManager.DisplayWarningNotification(
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

	private static int? GetServerStashBalance(PaymentCurrency currency)
	{
		if (s_stashBalances.TryGetValue(currency, out int balance)) return balance;
		return _serverStashCurrencyBalance.HasValue &&
		       _serverStashBalanceCurrency.HasValue &&
		       _serverStashBalanceCurrency.Value == currency
			? _serverStashCurrencyBalance
			: null;
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

	private static void NotifyInsufficientFunds(int cost, int availableBalance, ESupportType supportType)
	{
		if (availableBalance < 0)
		{
			NotificationManager.DisplayWarningNotification(
				$"Fire support requires {FormatCurrency(cost, GetActivePaymentCurrency(supportType))}. {GetEffectiveBalanceLabel(supportType)} are still syncing.",
				ENotificationDurationType.Long);
			return;
		}

		NotificationManager.DisplayWarningNotification(
			$"Fire support requires {FormatCurrency(cost, GetActivePaymentCurrency(supportType))}. {GetEffectiveBalanceLabel(supportType)}: {FormatCurrency(availableBalance, GetActivePaymentCurrency(supportType))}.",
			ENotificationDurationType.Long);
	}

	private static void NotifyServerPaymentRequired(ESupportType supportType)
	{
		NotificationManager.DisplayWarningNotification(
			$"{GetSupportName(supportType)} requires TerraGroup server payment confirmation.",
			ENotificationDurationType.Long);
	}

	private static void NotifyAuthorizationRequired(ESupportType supportType)
	{
		NotificationManager.DisplayWarningNotification(
			$"{GetSupportName(supportType)} requires a TerraGroup phone authorization.",
			ENotificationDurationType.Long);
	}

	public static string GetSupportName(ESupportType supportType)
	{
		return supportType switch
		{
			ESupportType.Strafe => "A-10 strafe",
			ESupportType.DoubleStrafe => "A-10 double pass",
			ESupportType.Extract => "UH-60 extraction",
			ESupportType.PriorityExfil => "UH-60 cargo transfer",
			ESupportType.Uav => "UAV recon",
			ESupportType.FocusedSweep => "focused sweep",
			_ => "fire support"
		};
	}
}
