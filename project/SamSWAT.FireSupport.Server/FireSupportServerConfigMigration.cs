using SamSWAT.FireSupport.ArysReloaded.Unity;

namespace SamSWAT.FireSupport.ArysReloaded;

/// <summary>
/// Dependency-free migration for the persisted server configuration contract.
/// Keep this separate from the SPT-backed service so released config documents
/// can be regression-tested without proprietary assemblies.
/// </summary>
internal static class FireSupportServerConfigMigration
{
	internal const int CurrentConfigSchemaVersion = 3;
	internal const float LegacyStandardExtractionDispatchDelaySeconds = 8f;

	/// <summary>
	/// Migrates persisted fields and removes fields that belong only in an
	/// authenticated response. Returns the schema version read from disk so the
	/// caller can retain schema-aware validation behavior.
	/// </summary>
	internal static int NormalizePersistedFields(
		RaidOpsFireSupportServerConfig config,
		RaidOpsFireSupportServerConfig defaults)
	{
		ArgumentNullException.ThrowIfNull(config);
		ArgumentNullException.ThrowIfNull(defaults);

		int sourceSchemaVersion = config.ConfigSchemaVersion;
		if (sourceSchemaVersion < CurrentConfigSchemaVersion)
		{
			if (sourceSchemaVersion < 2)
			{
				// Before schema 2 this dashboard field was dead and runtime
				// always used eight seconds. Preserve that effective behavior
				// once; a v2 config must never be run through this migration.
				config.Extraction ??= defaults.Extraction;
				config.PriorityExfil ??= defaults.PriorityExfil;
				config.Extraction.DispatchDelaySeconds =
					LegacyStandardExtractionDispatchDelaySeconds;
				config.Extraction.WaitTimeSeconds =
					config.Extraction.WaitTimeSeconds <= 0
						? defaults.Extraction.WaitTimeSeconds
						: config.Extraction.WaitTimeSeconds;
				config.Extraction.ExtractTimeSeconds =
					config.Extraction.ExtractTimeSeconds <= 0f
						? defaults.Extraction.ExtractTimeSeconds
						: config.Extraction.ExtractTimeSeconds;
				config.Extraction.SpeedMultiplier =
					config.Extraction.SpeedMultiplier <= 0f
						? defaults.Extraction.SpeedMultiplier
						: config.Extraction.SpeedMultiplier;
				config.PriorityExfil.WaitTimeSeconds =
					config.PriorityExfil.WaitTimeSeconds <= 0
						? defaults.PriorityExfil.WaitTimeSeconds
						: config.PriorityExfil.WaitTimeSeconds;
				// PriorityExfil.extractTimeSeconds is a dormant released-schema
				// field. Preserve the exact loaded value; Cargo has no countdown.
				config.PriorityExfil.SpeedMultiplier =
					config.PriorityExfil.SpeedMultiplier <= 0f
						? defaults.PriorityExfil.SpeedMultiplier
						: config.PriorityExfil.SpeedMultiplier;
			}

			// Existing prices were authored as RUB amounts. The new currency
			// selector therefore defaults to RUB without converting any values.
			config.PaymentCurrency = nameof(PaymentCurrency.RUB);
			config.ConfigSchemaVersion = CurrentConfigSchemaVersion;
		}

		// These fields are populated only on authenticated response snapshots.
		// Never accept or persist them as shared administrator configuration.
		config.PlayerStateIncluded = false;
		config.StashCurrencyBalance = null;
		config.StashRoubleBalance = null;
		config.Authorizations = new Dictionary<string, int>();
		config.PreparedPurchases = null;
		config.PreparedPurchaseDetails = null;

		return sourceSchemaVersion;
	}
}
