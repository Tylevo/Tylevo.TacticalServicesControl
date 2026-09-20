using SamSWAT.FireSupport.ArysReloaded;
using SamSWAT.FireSupport.ArysReloaded.Unity;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class ServerPresetTests
{
	private static readonly JsonSerializerOptions s_json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };

	[RegressionTest]
	private static void FreshDefaultsAndPackagedReferenceUseBalancedPricesAndStashWallet()
	{
		using var rig = new ServerConfigTestRig();
		AssertEx.Equal("StashRoubles", new RaidOpsFireSupportServerConfig().PaymentSource);
		var nativeView = (FireSupportConfigEditorView)new FireSupportConfigEditorProvider(rig.Service).GetConfigs().Single().RuntimeConfig;
		AssertEx.False(JsonSerializer.SerializeToElement(nativeView, s_json).TryGetProperty("paymentSource", out _));
		var expected = new Dictionary<string, int>
		{
			["A10"] = 150000, ["DoublePass"] = 250000, ["Uav"] = 50000,
			["FocusedSweep"] = 25000, ["Extraction"] = 125000, ["PriorityExfil"] = 75000
		};
		foreach (RaidOpsFireSupportServerConfig config in new[] { rig.Service.GetConfigSnapshot(), rig.ReadDisk(), ReadReference(4) })
		{
			AssertEx.Equal(4, config.ConfigSchemaVersion);
			AssertEx.Equal("RUB", config.PaymentCurrency);
			AssertEx.Equal("StashRoubles", config.PaymentSource);
			foreach ((string service, int price) in expected) AssertEx.Equal(price, config.Prices[service]);
			AssertSettingsEqual(FireSupportPresetCatalog.ProjectSettings(config), rig.Service.GetPresets().Presets.Single(preset => preset.Id == "balanced").Settings);
		}
	}

	[RegressionTest]
	private static void ExistingWalletsMigrateToStashWithoutChangingPricesOrCurrencyOnInitializationAndReload()
	{
		foreach (int schema in new[] { 3, 4 })
		foreach (string source in new[] { "CarriedRoubles", "StashRoubles", "PreferCarriedThenStash", "PreferStashThenCarried" })
		{
			using var rig = new ServerConfigTestRig();
			RaidOpsFireSupportServerConfig existing = ReadReference(3);
			existing.ConfigSchemaVersion = schema;
			existing.PaymentCurrency = "EUR";
			existing.PaymentSource = source;
			existing.Prices["FocusedSweep"] = 0;
			existing.Prices["Uav"] = 73;
			existing.ServiceCurrencies["Extraction"] = "BTC";
			rig.WriteDisk(existing);
			rig.Service.Initialize(Directory.GetParent(Path.GetDirectoryName(rig.ConfigPath)!)!.FullName);
			AssertEx.True(rig.Service.TryReloadConfig(out RaidOpsFireSupportServerConfig loaded, out string error), error);
			AssertEx.Equal("EUR", loaded.PaymentCurrency);
			AssertEx.Equal("StashRoubles", loaded.PaymentSource);
			AssertEx.Equal("StashRoubles", rig.ReadDisk().PaymentSource);
			AssertEx.False(JsonSerializer.SerializeToElement(new FireSupportConfigEditorProvider(rig.Service)
				.GetConfigs().Single().RuntimeConfig, s_json).TryGetProperty("paymentSource", out _));
			AssertEx.Equal("BTC", loaded.ServiceCurrencies["Extraction"]);
			foreach ((string service, int price) in existing.Prices)
			{
				AssertEx.Equal(price, loaded.Prices[service]);
				AssertEx.Equal(price, rig.ReadDisk().Prices[service]);
			}
		}
	}

	[RegressionTest]
	private static void OmittedWalletUsesStashWhenLoadingExistingConfig()
	{
		foreach (int schema in new[] { 3, 4 })
		{
			using var rig = new ServerConfigTestRig();
			JsonObject document = JsonNode.Parse(rig.ReadDiskText())!.AsObject();
			document["configSchemaVersion"] = schema;
			document["paymentCurrency"] = "USD";
			document.Remove("paymentSource");
			File.WriteAllText(rig.ConfigPath, document.ToJsonString());
			rig.Service.Initialize(Directory.GetParent(Path.GetDirectoryName(rig.ConfigPath)!)!.FullName);
			AssertEx.True(rig.Service.TryReloadConfig(out RaidOpsFireSupportServerConfig loaded, out string error), error);
			AssertEx.Equal("StashRoubles", loaded.PaymentSource);
			AssertEx.Equal("StashRoubles", rig.ReadDisk().PaymentSource);
			AssertEx.Equal("USD", loaded.PaymentCurrency);
		}
	}

	[RegressionTest]
	private static async Task ResetRestoresStashWalletInRuntimeDiskAndNativeEditor()
	{
		using var rig = new ServerConfigTestRig();
		RaidOpsFireSupportServerConfig carried = rig.Service.GetConfigSnapshot();
		carried.PaymentSource = "CarriedRoubles";
		AssertEx.True(rig.Service.TryUpdateConfig(carried, out string error, carried.Revision), error);
		int previousRevision = rig.Service.GetConfigSnapshot().Revision;
		AssertEx.Equal("StashRoubles", rig.ReadDisk().PaymentSource);
		AssertEx.True(rig.Service.TryResetConfig(out RaidOpsFireSupportServerConfig reset, out error), error);
		AssertEx.Equal(previousRevision + 1, reset.Revision);
		AssertEx.Equal("StashRoubles", reset.PaymentSource);
		AssertEx.Equal("StashRoubles", rig.Service.GetConfigSnapshot().PaymentSource);
		AssertEx.Equal("StashRoubles", rig.ReadDisk().PaymentSource);
		var registration = new FireSupportConfigEditorProvider(rig.Service).GetConfigs().Single();
		AssertEx.False(JsonSerializer.SerializeToElement(registration.RuntimeConfig, s_json).TryGetProperty("paymentSource", out _));
		AssertEx.False(JsonSerializer.SerializeToElement(await registration.LoadFromDiskAsync!(CancellationToken.None), s_json)
			.TryGetProperty("paymentSource", out _));
		AssertEx.True(rig.Service.TryReloadConfig(out RaidOpsFireSupportServerConfig reloaded, out error), error);
		AssertEx.Equal("StashRoubles", reloaded.PaymentSource);
	}

	[RegressionTest]
	private static void PresetsOmitWalletSelectionWithoutRewritingHistoricalReference()
	{
		using var rig = new ServerConfigTestRig();
		RaidOpsFireSupportServerConfig custom = rig.Service.GetConfigSnapshot();
		custom.PaymentSource = "PreferCarriedThenStash";
		AssertEx.True(rig.Service.TryUpdateConfig(custom, out string error, custom.Revision), error);
		custom = rig.Service.GetConfigSnapshot();
		foreach (FireSupportPreset preset in rig.Service.GetPresets().Presets)
		{
			string expected = "StashRoubles";
			RaidOpsFireSupportServerConfig applied = ApplySettings(custom, preset.Settings);
			AssertEx.Equal(expected, applied.PaymentSource);
			AssertEx.False(preset.Settings.ContainsKey("paymentSource"));
		}
		AssertEx.Equal("StashRoubles", rig.Service.GetConfigSnapshot().PaymentSource);
		AssertEx.Equal("CarriedRoubles", ReadReference(3).PaymentSource);
	}

	[RegressionTest]
	private static void PresetsContainExactlyEditableGameplayFieldsAndValidPrimitiveValues()
	{
		using var rig = new ServerConfigTestRig();
		JsonElement schema = JsonSerializer.SerializeToElement(rig.Service.GetDashboardSchema(), s_json);
		Dictionary<string, JsonElement> fields = schema.GetProperty("sections").EnumerateArray()
			.SelectMany(section => section.GetProperty("fields").EnumerateArray())
			.Where(field => field.GetProperty("type").GetString() != "readonly" && !field.GetProperty("path").GetString()!.StartsWith("adminDashboard.", StringComparison.Ordinal))
			.ToDictionary(field => field.GetProperty("path").GetString()!, field => field, StringComparer.Ordinal);
		AssertEx.False(fields.ContainsKey("paymentSource"));
		FireSupportPresetCollection catalog = rig.Service.GetPresets();
		JsonElement envelope = JsonSerializer.SerializeToElement(catalog, s_json);
		AssertEx.Equal("tsc-preset", envelope.GetProperty("format").GetString());
		AssertEx.Equal(1, envelope.GetProperty("formatVersion").GetInt32());
		AssertEx.SequenceEqual(new[] { "balanced", "casual", "hardcore", "barter", "classic" }, catalog.Presets.Select(preset => preset.Id));
		foreach (FireSupportPreset preset in catalog.Presets)
		{
			AssertEx.Equal("tsc-preset", preset.Format);
			AssertEx.Equal(1, preset.FormatVersion);
			AssertEx.True(preset.Name.Length > 0 && preset.Description.Length > 0);
			AssertEx.SequenceEqual(fields.Keys.Order(), preset.Settings.Keys.Order(), preset.Id);
			foreach ((string path, object value) in preset.Settings)
			{
				JsonElement field = fields[path];
				JsonElement scalar = JsonSerializer.SerializeToElement(value, s_json);
				switch (field.GetProperty("type").GetString())
				{
					case "toggle":
						AssertEx.True(scalar.ValueKind is JsonValueKind.True or JsonValueKind.False, path);
						break;
					case "select":
						AssertEx.Equal(JsonValueKind.String, scalar.ValueKind, path);
						AssertEx.Contains(scalar.GetString(), field.GetProperty("options").EnumerateArray().Select(option => option.GetString()), path);
						break;
					case "number":
						AssertEx.Equal(JsonValueKind.Number, scalar.ValueKind, path);
						double number = scalar.GetDouble();
						AssertEx.True(double.IsFinite(number) && number >= field.GetProperty("min").GetDouble() && number <= field.GetProperty("max").GetDouble(), $"{preset.Id}: {path}");
						break;
					default: throw new RegressionAssertionException($"Unexpected preset field type: {path}");
				}
			}
		}
	}

	[RegressionTest]
	private static void AllPresetsSaveThroughRevisionChecksWithoutChangingSecurityOrDormantFields()
	{
		using var rig = new ServerConfigTestRig();
		foreach (FireSupportPreset preset in rig.Service.GetPresets().Presets)
		{
			RaidOpsFireSupportServerConfig current = rig.Service.GetConfigSnapshot();
			current.AdminDashboard.AllowRemoteAccess = true;
			current.AdminDashboard.RequireTokenForLocalhost = true;
			current.PriorityExfil.ExtractTimeSeconds = 37;
			current.A10.SecondPassDelaySeconds = 29;
			current.PurchasePersistence.RefundFailedDispatch = false;
			RaidOpsFireSupportServerConfig staged = ApplySettings(current, preset.Settings);
			AssertEx.True(rig.Service.TryUpdateConfig(staged, out string error, out bool conflict, current.Revision), $"{preset.Id}: {error}");
			AssertEx.False(conflict);
			RaidOpsFireSupportServerConfig applied = rig.Service.GetConfigSnapshot();
			AssertEx.Equal(current.Revision + 1, applied.Revision);
			AssertSettingsEqual(preset.Settings, FireSupportPresetCatalog.ProjectSettings(applied));
			AssertEx.True(applied.AdminDashboard.AllowRemoteAccess);
			AssertEx.True(applied.AdminDashboard.RequireTokenForLocalhost);
			AssertEx.Equal(37f, applied.PriorityExfil.ExtractTimeSeconds);
			AssertEx.Equal(29f, applied.A10.SecondPassDelaySeconds);
			AssertEx.False(applied.PurchasePersistence.RefundFailedDispatch);
			staged.RequestCooldownSeconds++;
			AssertEx.False(rig.Service.TryUpdateConfig(staged, out _, out conflict, current.Revision));
			AssertEx.True(conflict);
			AssertSettingsEqual(preset.Settings, FireSupportPresetCatalog.ProjectSettings(rig.ReadDisk()));
		}
	}

	[RegressionTest]
	private static void CatalogReadsAndEditsAreDetachedFromLiveConfigAndOtherPresets()
	{
		using var rig = new ServerConfigTestRig();
		RaidOpsFireSupportServerConfig custom = rig.Service.GetConfigSnapshot();
		custom.Prices["A10"] = 999;
		custom.AdminDashboard.AllowRemoteAccess = true;
		AssertEx.True(rig.Service.TryUpdateConfig(custom, out string error, custom.Revision), error);
		string diskBefore = rig.ReadDiskText();
		FireSupportPresetCollection first = rig.Service.GetPresets();
		string before = JsonSerializer.Serialize(first, s_json);
		first.Presets[0].Settings["prices.A10"] = -1;
		first.Presets[0].Settings["progressionPermit"] = "should-never-escape";
		AssertEx.Equal(75000, first.Presets[1].Settings["prices.A10"]);
		AssertEx.Equal(before, JsonSerializer.Serialize(rig.Service.GetPresets(), s_json));
		AssertEx.Equal(diskBefore, rig.ReadDiskText());
		AssertEx.Equal(999, rig.Service.GetConfigSnapshot().Prices["A10"]);
		AssertEx.True(rig.Service.GetConfigSnapshot().AdminDashboard.AllowRemoteAccess);
	}

	[RegressionTest]
	private static void ClassicMatchesHistoricalGameplayEvenWhenCurrentDefaultsChange()
	{
		using var rig = new ServerConfigTestRig();
		RaidOpsFireSupportServerConfig changed = rig.Service.GetConfigSnapshot();
		changed.PaymentCurrency = "BTC";
		changed.RequestCooldownSeconds = 17;
		changed.PurchasePersistence.Enabled = false;
		changed.PriorityExfil.GridWidth = 12;
		changed.Prices["A10"] = 7;
		changed.Uav.DurationSeconds = 8;
		Dictionary<string, object> historical = FireSupportPresetCatalog.ProjectSettings(ReadReference(3));
		AssertSettingsEqual(historical, FireSupportPresetCatalog.Create(changed).Presets.Single(preset => preset.Id == "classic").Settings);
	}

	[RegressionTest]
	private static void BarterResolvesEachServiceToItsNamedAssetAndStashWallet()
	{
		using var rig = new ServerConfigTestRig();
		FireSupportPreset preset = rig.Service.GetPresets().Presets.Single(preset => preset.Id == "barter");
		RaidOpsFireSupportServerConfig barter = ApplySettings(rig.Service.GetConfigSnapshot(), preset.Settings);
		foreach ((ESupportType service, PaymentCurrency currency, int price) in new[]
		{
			(ESupportType.Strafe, PaymentCurrency.GP, 1), (ESupportType.DoubleStrafe, PaymentCurrency.GP, 2),
			(ESupportType.Uav, PaymentCurrency.RUB, 10000), (ESupportType.FocusedSweep, PaymentCurrency.GP, 1),
			(ESupportType.Extract, PaymentCurrency.BTC, 1), (ESupportType.PriorityExfil, PaymentCurrency.GP, 2)
		})
		{
			AssertEx.True(ServicePaymentPolicy.TryResolveCurrency(barter, service, out PaymentCurrency actual));
			AssertEx.Equal(currency, actual);
			AssertEx.Equal(price, barter.Prices[ServicePaymentPolicy.GetServiceKey(service)]);
			AssertEx.Equal(nameof(PaymentSource.StashRoubles), barter.PaymentSource);
		}
	}

	private static RaidOpsFireSupportServerConfig ApplySettings(RaidOpsFireSupportServerConfig config, Dictionary<string, object> settings)
	{
		JsonNode document = JsonSerializer.SerializeToNode(config, s_json)!;
		foreach ((string path, object value) in settings)
		{
			string[] segments = path.Split('.');
			JsonNode parent = document;
			foreach (string segment in segments.SkipLast(1)) parent = parent[segment]!;
			parent[segments[^1]] = JsonSerializer.SerializeToNode(value, s_json);
		}
		return document.Deserialize<RaidOpsFireSupportServerConfig>(s_json)!;
	}

	private static RaidOpsFireSupportServerConfig ReadReference(int schema)
	{
		string path = Path.Combine(PilotPolicyTestFixture.RepositoryRoot, "project", "SamSWAT.FireSupport.Server", "ConfigSources", $"tsc-config.schema{schema}.json");
		RaidOpsFireSupportServerConfig config = JsonSerializer.Deserialize<RaidOpsFireSupportServerConfig>(File.ReadAllText(path), s_json)!;
		foreach (string service in config.Prices.Keys) config.ServiceCurrencies.TryAdd(service, "Inherit");
		return config;
	}

	private static void AssertSettingsEqual(Dictionary<string, object> expected, Dictionary<string, object> actual)
	{
		AssertEx.SequenceEqual(expected.Keys.Order(), actual.Keys.Order());
		foreach ((string path, object value) in expected)
			AssertEx.Equal(JsonSerializer.Serialize(value, s_json), JsonSerializer.Serialize(actual[path], s_json), path);
	}
}
