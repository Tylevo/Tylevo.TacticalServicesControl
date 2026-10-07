using SamSWAT.FireSupport.ArysReloaded;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class ServerPresetStoreTests
{
	private static readonly JsonSerializerOptions s_json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

	[RegressionTest]
	private static void LegacySavedAndImportedWalletsAreDiscardedWithoutChangingPresetPrices()
	{
		using var rig = new ServerConfigTestRig();
		foreach (string source in new[] { "CarriedRoubles", "StashRoubles", "PreferCarriedThenStash", "PreferStashThenCarried" })
		{
			AssertEx.True(rig.Service.TrySavePreset(Payload(source, new()
			{
				["paymentSource"] = source, ["paymentCurrency"] = "USD", ["prices.A10"] = 73
			}), out FireSupportPreset? saved, out string error, out bool failure), error);
			AssertEx.False(failure);
			AssertEx.False(saved!.Settings.ContainsKey("paymentSource"));
			string path = Path.Combine(PresetDirectory(rig), saved.Id + ".json");
			JsonNode disk = JsonNode.Parse(File.ReadAllText(path))!;
			AssertEx.False(disk["settings"]!.AsObject().ContainsKey("paymentSource"));
			disk["settings"]!["paymentSource"] = source;
			File.WriteAllText(path, disk.ToJsonString());
			FireSupportPreset loaded = rig.Service.GetSavedPresets().Presets.Single(preset => preset.Id == saved.Id);
			AssertEx.False(loaded.Settings.ContainsKey("paymentSource"));
			AssertEx.Equal("\"USD\"", JsonSerializer.Serialize(loaded.Settings["paymentCurrency"]));
			AssertEx.Equal("73", JsonSerializer.Serialize(loaded.Settings["prices.A10"]));
		}
		AssertEx.Null(rig.Service.GetSavedPresets().Warning);
	}

	[RegressionTest]
	private static void CustomPresetFilesSurviveRestartAndNeverApplyTheSavedDraft()
	{
		using var rig = new ServerConfigTestRig();
		string configBefore = rig.ReadDiskText();
		AssertEx.Equal(0, rig.Service.GetSavedPresets().Presets.Count);
		AssertEx.True(rig.Service.TrySavePreset(Payload("Squad's low cost / fast support", new() { ["prices.A10"] = 73, ["serviceCurrencies.A10"] = "GP" }), out FireSupportPreset? saved, out string error, out bool failure), error);
		AssertEx.False(failure);
		string id = saved!.Id;
		AssertEx.True(id.StartsWith("custom-", StringComparison.Ordinal));
		AssertEx.Equal(39, id.Length);
		string path = Path.Combine(PresetDirectory(rig), id + ".json");
		AssertEx.True(File.Exists(path));
		AssertEx.Equal(configBefore, rig.ReadDiskText());
		AssertEx.Equal(150000, rig.Service.GetConfigSnapshot().Prices["A10"]);
		rig.Service.Initialize(Directory.GetParent(Path.GetDirectoryName(rig.ConfigPath)!)!.FullName);
		FireSupportPreset loaded = rig.Service.GetSavedPresets().Presets.Single();
		AssertEx.Equal(id, loaded.Id);
		AssertEx.Equal("Squad's low cost / fast support", loaded.Name);
		AssertEx.Equal("73", JsonSerializer.Serialize(loaded.Settings["prices.A10"]));
		AssertEx.True(rig.Service.TrySavePreset(Payload("Updated", new() { ["prices.A10"] = 0 }, id), out saved, out error, out failure), error);
		AssertEx.Equal(id, saved!.Id);
		AssertEx.Equal(1, Directory.GetFiles(PresetDirectory(rig), "*.json").Length);
		AssertEx.Equal("Updated", rig.Service.GetSavedPresets().Presets.Single().Name);
		AssertEx.True(rig.Service.TryRemovePreset(JsonSerializer.SerializeToElement(new { id }), out error, out failure), error);
		AssertEx.False(File.Exists(path));
		AssertEx.Equal(0, rig.Service.GetSavedPresets().Presets.Count);
	}

	[RegressionTest]
	private static void EveryBuiltInCanBeSavedAsAnIndependentCustomPreset()
	{
		using var rig = new ServerConfigTestRig();
		foreach (FireSupportPreset builtIn in rig.Service.GetPresets().Presets)
		{
			AssertEx.True(rig.Service.TrySavePreset(Payload(builtIn.Name, builtIn.Settings), out FireSupportPreset? saved, out string error, out bool failure), $"{builtIn.Id}: {error}");
			AssertEx.False(failure);
			AssertEx.NotEqual(builtIn.Id, saved!.Id);
			AssertEx.Equal(builtIn.Settings.Count, saved.Settings.Count);
		}
		AssertEx.Equal(5, rig.Service.GetSavedPresets().Presets.Count);
		AssertEx.Null(rig.Service.GetSavedPresets().Warning);
	}

	[RegressionTest]
	private static void InvalidMetadataSettingsAndUnsafeIdsAreRejectedBeforeAnyFileWrite()
	{
		using var rig = new ServerConfigTestRig();
		var invalid = new List<JsonElement>();
		foreach (string field in new[] { "revision", "configSchemaVersion", "adminDashboard", "profileId", "progressionPermit", "filePath", "__proto__" })
		{
			JsonNode node = JsonNode.Parse(Payload("Bad", new() { ["prices.A10"] = 1 }).GetRawText())!;
			node[field] = 1;
			invalid.Add(JsonSerializer.SerializeToElement(node));
		}
		foreach (string path in new[] { "adminDashboard.enabled", "revision", "configSchemaVersion", "authorizations.A10", "stashCurrencyBalance", "preparedPurchases.A10", "purchasePersistence.refundFailedDispatch", "priorityExfil.extractTimeSeconds", "a10.secondPassDelaySeconds", "prices.Unknown", "prices", "__proto__.enabled" })
			invalid.Add(Payload("Bad", new() { [path] = 1 }));
		foreach ((string path, object value) in new (string, object)[]
		{
			("prices.A10", -1), ("prices.A10", 10000001), ("prices.A10", 1.5), ("prices.A10", "100"),
			("prices.A10", true), ("prices.A10", new { amount = 1 }), ("paymentCurrency", "Roubles"),
			("serviceCurrencies.A10", "Gold"), ("enabled.A10", 1), ("enabled.A10", "true"),
			("purchasePersistence.pendingUseTimeoutSeconds", 1), ("priorityExfil.gridWidth", 1000),
			("uav.scanIntervalSeconds", 0), ("paymentSource", "Wallet"),
			("paymentSource", "carriedroubles"), ("paymentSource", 1), ("paymentSource", true)
		}) invalid.Add(Payload("Bad", new() { [path] = value }));
		foreach (string id in new[] { "../tsc-config", "..\\tsc-config", "C:/outside", "balanced", "custom-../../outside", "custom-" + new string('a', 100), "" })
			invalid.Add(Payload("Bad", new() { ["prices.A10"] = 1 }, id));
		invalid.Add(Payload("", new() { ["prices.A10"] = 1 }));
		invalid.Add(Payload(new string('x', 81), new() { ["prices.A10"] = 1 }));
		invalid.Add(Payload("bad\nname", new() { ["prices.A10"] = 1 }));
		invalid.Add(Payload("Bad", new()));
		invalid.Add(Parse("{\"format\":\"tsc-preset\",\"formatVersion\":2,\"name\":\"Bad\",\"settings\":{\"prices.A10\":1}}"));
		invalid.Add(Parse("{\"format\":\"tsc-preset\",\"formatVersion\":1,\"name\":\"Bad\",\"name\":\"Duplicate\",\"settings\":{\"prices.A10\":1}}"));
		invalid.Add(Parse("{\"format\":\"tsc-preset\",\"formatVersion\":1,\"name\":\"Bad\",\"settings\":{\"prices.A10\":1,\"prices.A10\":2}}"));
		invalid.Add(Parse("{\"format\":\"tsc-preset\",\"formatVersion\":1,\"name\":\"Bad\",\"settings\":{\"paymentSource\":\"CarriedRoubles\",\"paymentSource\":\"StashRoubles\",\"prices.A10\":1}}"));
		invalid.Add(Parse("{\"format\":\"tsc-preset\",\"formatVersion\":1,\"name\":\"Bad\",\"settings\":{\"uav.rangeMeters\":1e500}}"));
		string configBefore = rig.ReadDiskText();
		foreach (JsonElement payload in invalid)
		{
			AssertEx.False(rig.Service.TrySavePreset(payload, out FireSupportPreset? saved, out string error, out bool failure), payload.GetRawText());
			AssertEx.Null(saved);
			AssertEx.False(failure);
			AssertEx.True(error.Length > 0);
			AssertEx.False(Directory.Exists(PresetDirectory(rig)));
			AssertEx.Equal(configBefore, rig.ReadDiskText());
		}
	}

	[RegressionTest]
	private static void PairedExtractionTimingMustBeConsistentWhilePartialTimingRemainsPortable()
	{
		using var rig = new ServerConfigTestRig();
		AssertEx.False(rig.Service.TrySavePreset(Payload("Unsafe pair", new() { ["extraction.waitTimeSeconds"] = 10, ["extraction.extractTimeSeconds"] = 10 }), out _, out string error, out bool failure));
		AssertEx.False(failure);
		AssertEx.Contains("at least one second", error);
		AssertEx.False(Directory.Exists(PresetDirectory(rig)));
		AssertEx.True(rig.Service.TrySavePreset(Payload("Valid pair", new() { ["extraction.waitTimeSeconds"] = 11, ["extraction.extractTimeSeconds"] = 10 }), out _, out error, out _), error);
		AssertEx.True(rig.Service.TrySavePreset(Payload("Partial timing", new() { ["extraction.extractTimeSeconds"] = 30 }), out _, out error, out _), error);
		JsonNode padded = JsonNode.Parse(Payload(" Padded name ", new() { ["prices.A10"] = 1 }).GetRawText())!;
		padded["description"] = "  Padded description  ";
		AssertEx.True(rig.Service.TrySavePreset(JsonSerializer.SerializeToElement(padded), out FireSupportPreset? saved, out error, out _), error);
		AssertEx.Equal("Padded name", saved!.Name);
		AssertEx.Equal("Padded description", saved.Description);
	}

	[RegressionTest]
	private static void CorruptOversizedAndMismatchedFilesAreSkippedAndPreserved()
	{
		using var rig = new ServerConfigTestRig();
		AssertEx.True(rig.Service.TrySavePreset(Payload("Valid", new() { ["prices.A10"] = 1 }), out FireSupportPreset? saved, out string error, out _), error);
		string directory = PresetDirectory(rig);
		var files = new Dictionary<string, string>
		{
			["custom-" + new string('a', 32) + ".json"] = "{broken",
			["custom-" + new string('b', 32) + ".json"] = new string(' ', FireSupportPresetStore.MaxPresetBytes + 1),
			["custom-" + new string('c', 32) + ".json"] = Payload("Wrong ID", new() { ["prices.A10"] = 2 }, saved!.Id).GetRawText(),
			["hand-authored.json"] = Payload("Unsafe filename", new() { ["prices.A10"] = 3 }).GetRawText()
		};
		foreach ((string name, string contents) in files) File.WriteAllText(Path.Combine(directory, name), contents);
		FireSupportSavedPresetCollection list = rig.Service.GetSavedPresets();
		AssertEx.Equal(1, list.Presets.Count);
		AssertEx.Equal(saved.Id, list.Presets[0].Id);
		AssertEx.True(!string.IsNullOrEmpty(list.Warning));
		foreach ((string name, string contents) in files) AssertEx.Equal(contents, File.ReadAllText(Path.Combine(directory, name)));
	}

	[RegressionTest]
	private static void LibraryLimitAllowsUpdatesAndReleasesCapacityOnlyAfterRemoval()
	{
		using var rig = new ServerConfigTestRig();
		var ids = new List<string>();
		for (int index = 0; index < FireSupportPresetStore.MaxPresets; index++)
		{
			AssertEx.True(rig.Service.TrySavePreset(Payload($"Preset {index}", new() { ["prices.A10"] = index }), out FireSupportPreset? saved, out string error, out _), error);
			ids.Add(saved!.Id);
		}
		AssertEx.False(rig.Service.TrySavePreset(Payload("Overflow", new() { ["prices.A10"] = 31 }), out _, out string fullError, out bool failure));
		AssertEx.Contains("30 presets", fullError);
		AssertEx.False(failure);
		AssertEx.True(rig.Service.TrySavePreset(Payload("Updated at capacity", new() { ["prices.A10"] = 42 }, ids[0]), out _, out string updateError, out _), updateError);
		AssertEx.Equal(30, rig.Service.GetSavedPresets().Presets.Count);
		AssertEx.True(rig.Service.TryRemovePreset(JsonSerializer.SerializeToElement(new { id = ids[1] }), out _, out _));
		AssertEx.True(rig.Service.TrySavePreset(Payload("New after removal", new() { ["prices.A10"] = 7 }), out _, out string saveError, out _), saveError);
		AssertEx.Equal(30, rig.Service.GetSavedPresets().Presets.Count);
	}

	[RegressionTest]
	private static void FailedReplacementRetainsOriginalFileAndDoesNotReportSuccess()
	{
		using var rig = new ServerConfigTestRig();
		AssertEx.True(rig.Service.TrySavePreset(Payload("Original", new() { ["prices.A10"] = 1 }), out FireSupportPreset? saved, out string error, out _), error);
		string path = Path.Combine(PresetDirectory(rig), saved!.Id + ".json");
		string before = File.ReadAllText(path);
		if (OperatingSystem.IsWindows())
		{
			using var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
			AssertEx.False(rig.Service.TrySavePreset(Payload("Replacement", new() { ["prices.A10"] = 2 }, saved.Id), out FireSupportPreset? failed, out error, out bool storageFailure));
			AssertEx.Null(failed);
			AssertEx.True(storageFailure);
			AssertEx.Equal(before, File.ReadAllText(path));
		}
		else
		{
			// A directory at this exact generated ID cannot be atomically replaced by a file.
			string blockedId = "custom-" + new string('d', 32);
			Directory.CreateDirectory(Path.Combine(PresetDirectory(rig), blockedId + ".json"));
			AssertEx.False(rig.Service.TrySavePreset(Payload("Replacement", new() { ["prices.A10"] = 2 }, blockedId), out _, out error, out bool storageFailure));
			AssertEx.True(storageFailure);
			AssertEx.Equal(before, File.ReadAllText(path));
		}
		AssertEx.Equal(0, Directory.GetFiles(PresetDirectory(rig), "*.tmp").Length);
	}

	[RegressionTest]
	private static void DeleteRejectsPathsExtraFieldsAndMissingIdsWithoutTouchingConfig()
	{
		using var rig = new ServerConfigTestRig();
		string before = rig.ReadDiskText();
		foreach (JsonElement payload in new[]
		{
			Parse("{\"id\":\"../tsc-config\"}"), Parse("{\"id\":\"custom-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"path\":\"tsc-config.json\"}"),
			Parse("{\"id\":\"custom-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"id\":\"custom-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\"}"),
			Parse("{}"), Parse("{\"id\":\"custom-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"}")
		})
		{
			AssertEx.False(rig.Service.TryRemovePreset(payload, out string error, out bool failure));
			AssertEx.False(failure);
			AssertEx.True(error.Length > 0);
			AssertEx.Equal(before, rig.ReadDiskText());
		}
	}

	[RegressionTest]
	private static async Task RequestReaderBoundsUnknownLengthBodiesAndJsonDepth()
	{
		byte[] valid = Encoding.UTF8.GetBytes(Payload("Valid", new() { ["prices.A10"] = 1 }).GetRawText());
		using var accepted = new MemoryStream(valid);
		AssertEx.Equal("Valid", (await FireSupportPresetStore.ReadRequestAsync(accepted, CancellationToken.None)).GetProperty("name").GetString());
		using var oversized = new MemoryStream(new byte[FireSupportPresetStore.MaxPresetBytes * 3]);
		await AssertEx.ThrowsAsync<InvalidDataException>(() => FireSupportPresetStore.ReadRequestAsync(oversized, CancellationToken.None));
		AssertEx.Equal((long)FireSupportPresetStore.MaxPresetBytes + 1, oversized.Position);
		using var deep = new MemoryStream(Encoding.UTF8.GetBytes(new string('[', 9) + "0" + new string(']', 9)));
		await AssertEx.ThrowsAsync<JsonException>(() => FireSupportPresetStore.ReadRequestAsync(deep, CancellationToken.None));
		using var empty = new MemoryStream();
		await AssertEx.ThrowsAsync<JsonException>(() => FireSupportPresetStore.ReadRequestAsync(empty, CancellationToken.None));
	}

	private static JsonElement Payload(string name, Dictionary<string, object> settings, string? id = null)
	{
		var payload = new Dictionary<string, object> { ["format"] = "tsc-preset", ["formatVersion"] = 1, ["name"] = name, ["description"] = "", ["settings"] = settings };
		if (id != null) payload["id"] = id;
		return JsonSerializer.SerializeToElement(payload, s_json);
	}

	private static JsonElement Parse(string text)
	{
		using JsonDocument document = JsonDocument.Parse(text);
		return document.RootElement.Clone();
	}

	private static string PresetDirectory(ServerConfigTestRig rig) => Path.Combine(Path.GetDirectoryName(rig.ConfigPath)!, "presets");
}
