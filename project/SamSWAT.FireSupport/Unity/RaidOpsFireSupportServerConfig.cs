using System.Collections.Generic;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public sealed class RaidOpsFireSupportServerConfig
{
	public int ConfigSchemaVersion { get; set; }
	public int Revision { get; set; }
	public string PaymentMode { get; set; } = nameof(global::SamSWAT.FireSupport.ArysReloaded.Unity.PaymentMode.PhoneAuthorizations);
	public string PaymentSource { get; set; } = nameof(global::SamSWAT.FireSupport.ArysReloaded.Unity.PaymentSource.CarriedRoubles);
	public string PaymentCurrency { get; set; } = string.Empty;
	public int RequestCooldownSeconds { get; set; } = 300;
	/// <summary>
	/// True when the snapshot contains state for the resolved player profile.
	/// False means profile-scoped fields such as the stash balance and
	/// authorizations were omitted and must not clear previously synced state.
	/// </summary>
	public bool PlayerStateIncluded { get; set; }
	/// <summary>
	/// Balance of the selected payment currency in the authenticated PMC stash.
	/// </summary>
	public int? StashCurrencyBalance { get; set; }
	/// <summary>
	/// Legacy RUB-only balance returned by pre-currency TSC servers.
	/// </summary>
	public int? StashRoubleBalance { get; set; }
	public Dictionary<string, int> Prices { get; set; } = new();
	public Dictionary<string, bool> Enabled { get; set; } = new();
	public AdminDashboardSettings AdminDashboard { get; set; } = new();
	public UavSettings Uav { get; set; } = new();
	public UavSettings FocusedSweep { get; set; } = new();
	public ExtractionSettings Extraction { get; set; } = new();
	public CargoSettings PriorityExfil { get; set; } = new();
	public A10Settings A10 { get; set; } = new();
	public A10Settings DoublePass { get; set; } = new();
	public PurchasePersistenceSettings PurchasePersistence { get; set; } = new();
	public Dictionary<string, int> Authorizations { get; set; } = new();
	#nullable enable
	/// <summary>
	/// Authenticated, profile-scoped write-ahead purchase records that still
	/// require recovery. Null means the server omitted the recovery contract;
	/// an empty dictionary means no purchase is pending.
	/// </summary>
	public Dictionary<string, string>? PreparedPurchases { get; set; }
	/// <summary>
	/// Optional quote details for prepared purchases. Keys mirror
	/// <see cref="PreparedPurchases"/> service keys. Legacy servers may omit
	/// this field, in which case recovery retries use the current snapshot terms.
	/// </summary>
	public Dictionary<string, FireSupportPreparedPurchaseQuote>? PreparedPurchaseDetails { get; set; }
	#nullable restore

	public sealed class UavSettings
	{
		public int DurationSeconds { get; set; }
		public float RangeMeters { get; set; }
		public float ScanIntervalSeconds { get; set; }
	}

	public sealed class ExtractionSettings
	{
		public float DispatchDelaySeconds { get; set; }
		public int WaitTimeSeconds { get; set; }
		public float ExtractTimeSeconds { get; set; }
		public float SpeedMultiplier { get; set; }
	}

	public sealed class CargoSettings
	{
		public float DispatchDelaySeconds { get; set; }
		public int WaitTimeSeconds { get; set; }
		/// <summary>
		/// Dormant released-schema value retained only so existing config files
		/// round-trip without data loss. Cargo runtime never consumes it.
		/// </summary>
		public float ExtractTimeSeconds { get; set; }
		public float SpeedMultiplier { get; set; }
	}

	public sealed class A10Settings
	{
		public float SecondPassDelaySeconds { get; set; }
	}
	public sealed class AdminDashboardSettings
	{
		public bool Enabled { get; set; } = true;
		public bool AllowRemoteAccess { get; set; }
		public bool RequireTokenForLocalhost { get; set; }
	}

	public sealed class PurchasePersistenceSettings
	{
		public bool Enabled { get; set; } = true;
		public string Mode { get; set; } = "PersistentAuthorizations";
		public string ConsumeOn { get; set; } = "AuthorizationAccepted";
		public bool RefundFailedDispatch { get; set; } = true;
		public int MaxStoredAuthorizationsPerService { get; set; } = 2;
		public int PendingUseTimeoutSeconds { get; set; } = 180;
		public bool SpendCreditsBeforeCash { get; set; } = true;
		public bool AllowAutoPurchaseOnUse { get; set; } = true;
	}
}

public sealed class FireSupportPreparedPurchaseQuote
{
	public string RequestId { get; set; } = string.Empty;
	public int Price { get; set; } = -1;
	public string Currency { get; set; } = string.Empty;
}

public sealed class FireSupportPurchaseRequest
{
	public string Action { get; set; } = string.Empty;
	public string SessionId { get; set; } = string.Empty;
	public string ProfileId { get; set; } = string.Empty;
	public string SupportType { get; set; } = string.Empty;
	public string RequestId { get; set; } = string.Empty;
	public int ClientKnownRevision { get; set; }
	/// <summary>
	/// Optional quote accepted by the player. Persistent menu purchases must not
	/// debit a different amount; omitted for legacy and in-raid purchase flows.
	/// </summary>
	public int? ExpectedCost { get; set; }
	/// <summary>
	/// Currency code accepted by the player with <see cref="ExpectedCost"/>.
	/// Empty is accepted only by legacy request flows.
	/// </summary>
	public string ExpectedCurrency { get; set; } = string.Empty;
	public int Quantity { get; set; } = 1;
}

public sealed class FireSupportPurchaseResponse
{
	public bool Ok { get; set; }
	public string Reason { get; set; } = string.Empty;
	public string SupportType { get; set; } = string.Empty;
	public int Cost { get; set; } = -1;
	public string PaymentSource { get; set; } = string.Empty;
	public string Currency { get; set; } = string.Empty;
	/// <summary>
	/// Authoritative post-mutation stash balance, or -1 when the server omitted
	/// balance state (for example, an early validation denial).
	/// </summary>
	public int NewBalance { get; set; } = -1;
	public bool AuthorizationGranted { get; set; }
	public bool AuthorizationConsumed { get; set; }
	public int ServerRevision { get; set; }
	public int ChargedFromStash { get; set; }
	public string RequestId { get; set; } = string.Empty;
	/// <summary>
	/// True when <see cref="Authorizations"/> is a complete, authoritative
	/// snapshot of the profile ledger. False means the authorization state was
	/// omitted; an empty authoritative snapshot must not be treated as omitted.
	/// </summary>
	public bool AuthorizationsIncluded { get; set; }
	public Dictionary<string, int> Authorizations { get; set; } = new();
}

/// <summary>
/// Authenticated transaction request for the native UH-60 cargo-transfer
/// handling fee. The HTTP session is authoritative; <see cref="ProfileId"/>
/// is only a required anti-confusion hint and must match that session.
/// </summary>
public sealed class FireSupportUh60TransferFeeRequest
{
	public string Action { get; set; } = string.Empty;
	public string ProfileId { get; set; } = string.Empty;
	public string TransactionId { get; set; } = string.Empty;
	public int AmountRoubles { get; set; }
}

public sealed class FireSupportUh60TransferFeeResponse
{
	public bool Ok { get; set; }
	public string Reason { get; set; } = string.Empty;
	public string State { get; set; } = string.Empty;
	public string TransactionId { get; set; } = string.Empty;
	public int AmountRoubles { get; set; }
	public int StashRoubleBalance { get; set; } = -1;
}
