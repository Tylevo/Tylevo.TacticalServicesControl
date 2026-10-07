using SamSWAT.FireSupport.ArysReloaded;
using SamSWAT.FireSupport.ArysReloaded.Unity;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class CargoGridConfigTests
{
	[RegressionTest]
	private static void GridDimensionsAcceptNativeDefaultsAndValidateEachBoundIndependently()
	{
		foreach ((int width, int height) in new[] { (0, 0), (0, 30), (10, 0), (1, 1), (10, 30) })
		{
			AssertEx.True(CargoGridPolicy.TryValidate(width, height, "cargo", out string error), error);
			AssertEx.Equal(string.Empty, error);
		}

		foreach ((int width, int height, string field) in new[]
		{
			(-1, 5, "gridWidth"), (11, 5, "gridWidth"),
			(int.MinValue, 0, "gridWidth"), (int.MaxValue, 0, "gridWidth"),
			(4, -1, "gridHeight"), (4, 31, "gridHeight"),
			(0, int.MinValue, "gridHeight"), (0, int.MaxValue, "gridHeight")
		})
		{
			AssertEx.False(CargoGridPolicy.TryValidate(width, height, "cargo", out string error));
			AssertEx.Contains($"cargo.{field}", error);
			AssertEx.Contains("0 preserves", error);
		}
	}

	[RegressionTest]
	private static void LegacyConfigsWithMissingGridFieldsKeepNativeDimensions()
	{
		using var rig = new ServerConfigTestRig();
		JsonObject legacy = JsonNode.Parse(rig.ReadDiskText())!.AsObject();
		JsonObject cargo = legacy["priorityExfil"]!.AsObject();
		cargo.Remove("gridWidth");
		cargo.Remove("gridHeight");
		cargo["extractTimeSeconds"] = 42;
		File.WriteAllText(rig.ConfigPath, legacy.ToJsonString());

		AssertEx.True(rig.Service.TryReloadConfig(out var reloaded, out string error), error);
		AssertGrid(reloaded, 0, 0);
		AssertGrid(rig.ReadDisk(), 0, 0);
		AssertEx.Equal(42f, reloaded.PriorityExfil.ExtractTimeSeconds);
		var view = FireSupportConfigEditorView.FromConfig(reloaded);
		AssertEx.Equal(0, view.PriorityExfil.GridWidth);
		AssertEx.Equal(0, view.PriorityExfil.GridHeight);
	}

	[RegressionTest]
	private static void DashboardGridEditsAcceptBoundsAndRejectInvalidValuesAtomically()
	{
		using var rig = new ServerConfigTestRig();
		foreach ((int width, int height) in new[] { (10, 30), (1, 1), (0, 30), (10, 0), (0, 0) })
		{
			var edited = rig.Service.GetConfigSnapshot();
			edited.PriorityExfil.GridWidth = width;
			edited.PriorityExfil.GridHeight = height;
			AssertEx.True(rig.Service.TryUpdateConfig(edited, out string error, edited.Revision), error);
			AssertGrid(rig.Service.GetConfigSnapshot(), width, height);
			AssertGrid(rig.ReadDisk(), width, height);
		}

		foreach ((int width, int height) in new[] { (-1, 5), (11, 5), (4, -1), (4, 31) })
		{
			var edited = rig.Service.GetConfigSnapshot();
			string runtimeBefore = JsonSerializer.Serialize(edited);
			string diskBefore = rig.ReadDiskText();
			int revision = edited.Revision;
			edited.PriorityExfil.GridWidth = width;
			edited.PriorityExfil.GridHeight = height;
			edited.RequestCooldownSeconds = 15;
			AssertEx.False(rig.Service.TryUpdateConfig(edited, out string error, out bool conflict, revision));
			AssertEx.False(conflict);
			AssertEx.Contains("priorityExfil.grid", error);
			AssertEx.False(rig.Service.TryApplyConfig(edited, out error, revision));
			AssertEx.False(rig.Service.TrySaveConfig(edited, out error, revision));
			AssertEx.Equal(runtimeBefore, JsonSerializer.Serialize(rig.Service.GetConfigSnapshot()));
			AssertEx.Equal(diskBefore, rig.ReadDiskText());
			AssertEx.Equal(revision, edited.Revision);
		}
	}

	[RegressionTest]
	private static void StartupRepairsOnlyInvalidGridDimensionsAndDiskReloadRemainsReadOnly()
	{
		foreach ((int width, int height, int expectedWidth, int expectedHeight) in new[]
		{
			(-1, 5, 0, 5), (11, 5, 0, 5), (4, -1, 4, 0), (4, 31, 4, 0), (11, 31, 0, 0)
		})
		{
			using var rig = new ServerConfigTestRig();
			var invalid = rig.ReadDisk();
			invalid.PriorityExfil.GridWidth = width;
			invalid.PriorityExfil.GridHeight = height;
			invalid.PriorityExfil.ExtractTimeSeconds = 42;
			rig.WriteDisk(invalid);
			string diskBefore = rig.ReadDiskText();
			string runtimeBefore = JsonSerializer.Serialize(rig.Service.GetConfigSnapshot());
			AssertEx.False(rig.Service.TryGetDiskConfigSnapshot(out _, out string error));
			AssertEx.Contains("priorityExfil.grid", error);
			AssertEx.False(rig.Service.TryReloadConfig(out _, out error));
			AssertEx.Equal(runtimeBefore, JsonSerializer.Serialize(rig.Service.GetConfigSnapshot()));
			AssertEx.Equal(diskBefore, rig.ReadDiskText());

			rig.Service.Initialize(Path.GetDirectoryName(Path.GetDirectoryName(rig.ConfigPath))!);
			AssertGrid(rig.Service.GetConfigSnapshot(), expectedWidth, expectedHeight);
			AssertGrid(rig.ReadDisk(), expectedWidth, expectedHeight);
			AssertEx.Equal(42f, rig.ReadDisk().PriorityExfil.ExtractTimeSeconds);
		}
	}

	[RegressionTest]
	private static async Task SicGridApplyAndSaveRoundTripWithoutCrossingTargets()
	{
		using var rig = new ServerConfigTestRig();
		var registration = new FireSupportConfigEditorProvider(rig.Service).GetConfigs().Single();
		var draft = FireSupportConfigEditorView.FromConfig(rig.Service.GetConfigSnapshot());
		draft.PriorityExfil.GridWidth = 7;
		draft.PriorityExfil.GridHeight = 18;
		string originalDisk = rig.ReadDiskText();
		await registration.ApplyToRuntimeAsync!(draft, CancellationToken.None);
		AssertGrid(rig.Service.GetConfigSnapshot(), 7, 18);
		AssertEx.Equal(originalDisk, rig.ReadDiskText());

		var applied = JsonSerializer.Deserialize<FireSupportConfigEditorView>(
			JsonSerializer.Serialize(registration.RuntimeConfig))!;
		AssertEx.Equal(7, applied.PriorityExfil.GridWidth);
		AssertEx.Equal(18, applied.PriorityExfil.GridHeight);
		await registration.SaveToDiskAsync!(applied, CancellationToken.None);
		AssertGrid(rig.ReadDisk(), 7, 18);
		var loaded = (FireSupportConfigEditorView)(await registration.LoadFromDiskAsync!(CancellationToken.None))!;
		AssertEx.Equal(7, loaded.PriorityExfil.GridWidth);
		AssertEx.Equal(18, loaded.PriorityExfil.GridHeight);

		loaded.PriorityExfil.GridWidth = 0;
		loaded.PriorityExfil.GridHeight = 30;
		await registration.SaveToDiskAsync!(loaded, CancellationToken.None);
		AssertGrid(rig.ReadDisk(), 0, 30);
		AssertGrid(rig.Service.GetConfigSnapshot(), 7, 18);
		// SIC retains its original serialized draft after Save.
		await registration.ApplyToRuntimeAsync!(loaded, CancellationToken.None);
		AssertGrid(rig.Service.GetConfigSnapshot(), 0, 30);
	}

	[RegressionTest]
	private static async Task SicRejectsInvalidAndStaleGridDraftsWithoutOverwritingEitherTarget()
	{
		using var rig = new ServerConfigTestRig();
		var registration = new FireSupportConfigEditorProvider(rig.Service).GetConfigs().Single();
		var invalid = FireSupportConfigEditorView.FromConfig(rig.Service.GetConfigSnapshot());
		invalid.PriorityExfil.GridHeight = 31;
		string diskBefore = rig.ReadDiskText();
		string runtimeBefore = JsonSerializer.Serialize(rig.Service.GetConfigSnapshot());
		await AssertEx.ThrowsAsync<InvalidOperationException>(() =>
			registration.ApplyToRuntimeAsync!(invalid, CancellationToken.None).AsTask());
		await AssertEx.ThrowsAsync<InvalidOperationException>(() =>
			registration.SaveToDiskAsync!(invalid, CancellationToken.None).AsTask());
		AssertEx.Equal(diskBefore, rig.ReadDiskText());
		AssertEx.Equal(runtimeBefore, JsonSerializer.Serialize(rig.Service.GetConfigSnapshot()));

		var saved = FireSupportConfigEditorView.FromConfig(rig.Service.GetConfigSnapshot());
		saved.PriorityExfil.GridWidth = 6;
		saved.PriorityExfil.GridHeight = 12;
		await registration.SaveToDiskAsync!(saved, CancellationToken.None);
		var dashboardEdit = rig.Service.GetConfigSnapshot();
		dashboardEdit.PriorityExfil.GridWidth = 8;
		dashboardEdit.PriorityExfil.GridHeight = 20;
		AssertEx.True(rig.Service.TryUpdateConfig(dashboardEdit, out string error, dashboardEdit.Revision), error);
		diskBefore = rig.ReadDiskText();
		runtimeBefore = JsonSerializer.Serialize(rig.Service.GetConfigSnapshot());
		await AssertEx.ThrowsAsync<InvalidOperationException>(() =>
			registration.ApplyToRuntimeAsync!(saved, CancellationToken.None).AsTask());
		await AssertEx.ThrowsAsync<InvalidOperationException>(() =>
			registration.SaveToDiskAsync!(saved, CancellationToken.None).AsTask());
		AssertEx.Equal(diskBefore, rig.ReadDiskText());
		AssertEx.Equal(runtimeBefore, JsonSerializer.Serialize(rig.Service.GetConfigSnapshot()));
	}

	[RegressionTest]
	private static void DashboardSchemaExposesIntegerGridControlsWithinUh60Services()
	{
		using var rig = new ServerConfigTestRig();
		JsonElement schema = JsonSerializer.SerializeToElement(rig.Service.GetDashboardSchema());
		JsonElement section = schema.GetProperty("sections").EnumerateArray()
			.Single(item => item.GetProperty("label").GetString() == "UH-60 Services");
		JsonElement[] fields = section.GetProperty("fields").EnumerateArray().ToArray();
		foreach ((string path, string label, int maximum) in new[]
		{
			("priorityExfil.gridWidth", "Cargo Grid Columns", 10),
			("priorityExfil.gridHeight", "Cargo Grid Rows", 30)
		})
		{
			JsonElement field = fields.Single(item => item.GetProperty("path").GetString() == path);
			AssertEx.Equal(label, field.GetProperty("label").GetString());
			AssertEx.Equal("number", field.GetProperty("type").GetString());
			AssertEx.Equal(0d, field.GetProperty("min").GetDouble());
			AssertEx.Equal((double)maximum, field.GetProperty("max").GetDouble());
			AssertEx.Equal(1d, field.GetProperty("step").GetDouble());
		}
	}

	private static void AssertGrid(RaidOpsFireSupportServerConfig config, int width, int height)
	{
		AssertEx.Equal(width, config.PriorityExfil.GridWidth);
		AssertEx.Equal(height, config.PriorityExfil.GridHeight);
	}
}
