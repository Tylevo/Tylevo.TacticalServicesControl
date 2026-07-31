using SamSWAT.FireSupport.ArysReloaded;
using SamSWAT.FireSupport.ArysReloaded.Unity;
using System.Text.Json;

internal static class ServerConfigMigrationTests
{
	private static readonly JsonSerializerOptions s_jsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	[RegressionTest]
	private static void PublishedV108ConfigMigratesWithoutLosingAdminSettings()
	{
		// Canonical tsc-config.json from the verified public v1.0.8 archive.
		const string publishedV108Config = """
		{
		  "revision": 1,
		  "paymentMode": "PhoneAuthorizations",
		  "paymentSource": "CarriedRoubles",
		  "requestCooldownSeconds": 300,
		  "prices": {
		    "A10": 250000,
		    "DoublePass": 450000,
		    "Extraction": 300000,
		    "PriorityExfil": 450000,
		    "Uav": 125000,
		    "FocusedSweep": 90000
		  },
		  "enabled": {
		    "A10": true,
		    "DoublePass": true,
		    "Extraction": true,
		    "PriorityExfil": true,
		    "Uav": true,
		    "FocusedSweep": true
		  },
		  "adminDashboard": {
		    "enabled": true,
		    "allowRemoteAccess": false,
		    "requireTokenForLocalhost": false
		  },
		  "purchasePersistence": {
		    "enabled": true,
		    "mode": "PersistentAuthorizations",
		    "consumeOn": "AuthorizationAccepted",
		    "refundFailedDispatch": true,
		    "maxStoredAuthorizationsPerService": 2,
		    "pendingUseTimeoutSeconds": 180,
		    "spendCreditsBeforeCash": true,
		    "allowAutoPurchaseOnUse": true
		  },
		  "uav": {
		    "durationSeconds": 45,
		    "rangeMeters": 200,
		    "scanIntervalSeconds": 1
		  },
		  "focusedSweep": {
		    "durationSeconds": 30,
		    "rangeMeters": 100,
		    "scanIntervalSeconds": 0.5
		  },
		  "extraction": {
		    "dispatchDelaySeconds": 0,
		    "waitTimeSeconds": 30,
		    "extractTimeSeconds": 10,
		    "speedMultiplier": 1
		  },
		  "priorityExfil": {
		    "dispatchDelaySeconds": 3,
		    "waitTimeSeconds": 20,
		    "extractTimeSeconds": 10,
		    "speedMultiplier": 1.35
		  },
		  "a10": {
		    "secondPassDelaySeconds": 0
		  },
		  "doublePass": {
		    "secondPassDelaySeconds": 14
		  }
		}
		""";

		RaidOpsFireSupportServerConfig config =
			Deserialize(publishedV108Config);
		SeedResponseOnlyFields(config);

		int sourceSchemaVersion =
			FireSupportServerConfigMigration.NormalizePersistedFields(
				config,
				CreateMigrationDefaults());

		AssertEx.Equal(0, sourceSchemaVersion);
		AssertEx.Equal(3, config.ConfigSchemaVersion);
		AssertEx.Equal(1, config.Revision);
		AssertEx.Equal("PhoneAuthorizations", config.PaymentMode);
		AssertEx.Equal("CarriedRoubles", config.PaymentSource);
		AssertEx.Equal("RUB", config.PaymentCurrency);
		AssertEx.Equal(300, config.RequestCooldownSeconds);

		AssertEx.Equal(250000, config.Prices["A10"]);
		AssertEx.Equal(450000, config.Prices["DoublePass"]);
		AssertEx.Equal(300000, config.Prices["Extraction"]);
		AssertEx.Equal(450000, config.Prices["PriorityExfil"]);
		AssertEx.Equal(125000, config.Prices["Uav"]);
		AssertEx.Equal(90000, config.Prices["FocusedSweep"]);
		AssertEx.True(config.Enabled.Values.All(enabled => enabled));
		AssertEx.True(config.AdminDashboard.Enabled);
		AssertEx.False(config.AdminDashboard.AllowRemoteAccess);
		AssertEx.False(config.AdminDashboard.RequireTokenForLocalhost);

		AssertEx.True(config.PurchasePersistence.Enabled);
		AssertEx.Equal(
			"PersistentAuthorizations",
			config.PurchasePersistence.Mode);
		AssertEx.Equal(
			"AuthorizationAccepted",
			config.PurchasePersistence.ConsumeOn);
		AssertEx.True(config.PurchasePersistence.RefundFailedDispatch);
		AssertEx.Equal(
			2,
			config.PurchasePersistence.MaxStoredAuthorizationsPerService);
		AssertEx.Equal(
			180,
			config.PurchasePersistence.PendingUseTimeoutSeconds);
		AssertEx.True(config.PurchasePersistence.SpendCreditsBeforeCash);
		AssertEx.True(config.PurchasePersistence.AllowAutoPurchaseOnUse);

		AssertUav(config.Uav, duration: 45, range: 200f, interval: 1f);
		AssertUav(
			config.FocusedSweep,
			duration: 30,
			range: 100f,
			interval: 0.5f);
		AssertEx.Near(8f, config.Extraction.DispatchDelaySeconds, 0.0001f);
		AssertEx.Equal(30, config.Extraction.WaitTimeSeconds);
		AssertEx.Near(10f, config.Extraction.ExtractTimeSeconds, 0.0001f);
		AssertEx.Near(1f, config.Extraction.SpeedMultiplier, 0.0001f);
		AssertEx.Near(
			3f,
			config.PriorityExfil.DispatchDelaySeconds,
			0.0001f);
		AssertEx.Equal(20, config.PriorityExfil.WaitTimeSeconds);
		AssertEx.Near(
			10f,
			config.PriorityExfil.ExtractTimeSeconds,
			0.0001f);
		AssertEx.Near(
			1.35f,
			config.PriorityExfil.SpeedMultiplier,
			0.0001f);
		string persistedJson = JsonSerializer.Serialize(config, s_jsonOptions);
		AssertEx.Contains("\"PriorityExfil\"", persistedJson);
		AssertEx.Contains("\"priorityExfil\"", persistedJson);
		AssertEx.False(
			persistedJson.Contains("\"cargo", StringComparison.OrdinalIgnoreCase),
			"Cargo conversion must not rename the released PriorityExfil configuration contract.");
		AssertResponseOnlyFieldsCleared(config);
	}

	[RegressionTest]
	private static void SchemaTwoConfigDefaultsCurrencyWithoutRepeatingTimingMigration()
	{
		var config = new RaidOpsFireSupportServerConfig
		{
			ConfigSchemaVersion = 2,
			Revision = 27,
			PaymentMode = "Hybrid",
			PaymentSource = "PreferStashThenCarried",
			PaymentCurrency = "USD",
			RequestCooldownSeconds = 17,
			Prices = new Dictionary<string, int>
			{
				["A10"] = 101,
				["FocusedSweep"] = 606
			},
			Enabled = new Dictionary<string, bool>
			{
				["A10"] = false,
				["FocusedSweep"] = true
			},
			PurchasePersistence =
				new RaidOpsFireSupportServerConfig.PurchasePersistenceSettings
				{
					Enabled = false,
					Mode = "PersistentAuthorizations",
					ConsumeOn = "AuthorizationAccepted",
					RefundFailedDispatch = false,
					MaxStoredAuthorizationsPerService = 7,
					PendingUseTimeoutSeconds = 321,
					SpendCreditsBeforeCash = false,
					AllowAutoPurchaseOnUse = false
				},
			Uav = new RaidOpsFireSupportServerConfig.UavSettings
			{
				DurationSeconds = 222,
				RangeMeters = 333f,
				ScanIntervalSeconds = 4.5f
			},
			FocusedSweep = new RaidOpsFireSupportServerConfig.UavSettings
			{
				DurationSeconds = 44,
				RangeMeters = 55f,
				ScanIntervalSeconds = 0.25f
			},
			Extraction =
				new RaidOpsFireSupportServerConfig.ExtractionSettings
				{
					DispatchDelaySeconds = 11f,
					WaitTimeSeconds = 41,
					ExtractTimeSeconds = 12f,
					SpeedMultiplier = 1.1f
				},
			PriorityExfil =
				new RaidOpsFireSupportServerConfig.CargoSettings
				{
					DispatchDelaySeconds = 2.25f,
					WaitTimeSeconds = 19,
					ExtractTimeSeconds = 7f,
					SpeedMultiplier = 1.8f
				}
		};

		int sourceSchemaVersion =
			FireSupportServerConfigMigration.NormalizePersistedFields(
				config,
				CreateMigrationDefaults());

		AssertEx.Equal(2, sourceSchemaVersion);
		AssertEx.Equal(3, config.ConfigSchemaVersion);
		AssertEx.Equal(27, config.Revision);
		AssertEx.Equal("Hybrid", config.PaymentMode);
		AssertEx.Equal("PreferStashThenCarried", config.PaymentSource);
		AssertEx.Equal("RUB", config.PaymentCurrency);
		AssertEx.Equal(17, config.RequestCooldownSeconds);
		AssertEx.Equal(101, config.Prices["A10"]);
		AssertEx.Equal(606, config.Prices["FocusedSweep"]);
		AssertEx.False(config.Enabled["A10"]);
		AssertEx.True(config.Enabled["FocusedSweep"]);

		AssertEx.False(config.PurchasePersistence.Enabled);
		AssertEx.False(config.PurchasePersistence.RefundFailedDispatch);
		AssertEx.Equal(
			7,
			config.PurchasePersistence.MaxStoredAuthorizationsPerService);
		AssertEx.Equal(
			321,
			config.PurchasePersistence.PendingUseTimeoutSeconds);
		AssertEx.False(config.PurchasePersistence.SpendCreditsBeforeCash);
		AssertEx.False(config.PurchasePersistence.AllowAutoPurchaseOnUse);
		AssertUav(config.Uav, duration: 222, range: 333f, interval: 4.5f);
		AssertUav(
			config.FocusedSweep,
			duration: 44,
			range: 55f,
			interval: 0.25f);
		AssertEx.Near(11f, config.Extraction.DispatchDelaySeconds, 0.0001f);
		AssertEx.Near(
			2.25f,
			config.PriorityExfil.DispatchDelaySeconds,
			0.0001f);
	}

	[RegressionTest]
	private static void CurrentSchemaInvalidCurrencyRemainsFailClosed()
	{
		var config = new RaidOpsFireSupportServerConfig
		{
			ConfigSchemaVersion = 3,
			PaymentCurrency = "BTC"
		};
		SeedResponseOnlyFields(config);

		int sourceSchemaVersion =
			FireSupportServerConfigMigration.NormalizePersistedFields(
				config,
				CreateMigrationDefaults());

		AssertEx.Equal(3, sourceSchemaVersion);
		AssertEx.Equal(3, config.ConfigSchemaVersion);
		AssertEx.Equal("BTC", config.PaymentCurrency);
		AssertResponseOnlyFieldsCleared(config);
	}

	private static RaidOpsFireSupportServerConfig Deserialize(string json)
	{
		return AssertEx.NotNull(
			JsonSerializer.Deserialize<RaidOpsFireSupportServerConfig>(
				json,
				s_jsonOptions));
	}

	private static RaidOpsFireSupportServerConfig CreateMigrationDefaults()
	{
		return new RaidOpsFireSupportServerConfig
		{
			ConfigSchemaVersion = 3,
			PaymentCurrency = "RUB",
			Extraction =
				new RaidOpsFireSupportServerConfig.ExtractionSettings
				{
					DispatchDelaySeconds = 8f,
					WaitTimeSeconds = 30,
					ExtractTimeSeconds = 10f,
					SpeedMultiplier = 1f
				},
			PriorityExfil =
				new RaidOpsFireSupportServerConfig.CargoSettings
				{
					DispatchDelaySeconds = 3f,
					WaitTimeSeconds = 20,
					ExtractTimeSeconds = 10f,
					SpeedMultiplier = 1.35f
				}
		};
	}

	private static void SeedResponseOnlyFields(
		RaidOpsFireSupportServerConfig config)
	{
		config.PlayerStateIncluded = true;
		config.StashCurrencyBalance = 123456;
		config.StashRoubleBalance = 654321;
		config.Authorizations = new Dictionary<string, int>
		{
			["A10"] = 2
		};
		config.PreparedPurchases = new Dictionary<string, string>
		{
			["A10"] = "request-1"
		};
		config.PreparedPurchaseDetails =
			new Dictionary<string, FireSupportPreparedPurchaseQuote>
			{
				["A10"] = new FireSupportPreparedPurchaseQuote
				{
					RequestId = "request-1",
					Price = 250000,
					Currency = "RUB"
				}
			};
	}

	private static void AssertResponseOnlyFieldsCleared(
		RaidOpsFireSupportServerConfig config)
	{
		AssertEx.False(config.PlayerStateIncluded);
		AssertEx.Null(config.StashCurrencyBalance);
		AssertEx.Null(config.StashRoubleBalance);
		AssertEx.Equal(0, config.Authorizations.Count);
		AssertEx.Null(config.PreparedPurchases);
		AssertEx.Null(config.PreparedPurchaseDetails);
	}

	private static void AssertUav(
		RaidOpsFireSupportServerConfig.UavSettings settings,
		int duration,
		float range,
		float interval)
	{
		AssertEx.Equal(duration, settings.DurationSeconds);
		AssertEx.Near(range, settings.RangeMeters, 0.0001f);
		AssertEx.Near(interval, settings.ScanIntervalSeconds, 0.0001f);
	}
}
