using SamSWAT.FireSupport.ArysReloaded;
using SamSWAT.FireSupport.ArysReloaded.Unity;
using System.Text.Json;

internal static class FireSupportConfigEditorProviderTests
{
	[RegressionTest]
	private static async Task NativeRegistrationsHaveIndependentSnapshotsForConcurrentSessions()
	{
		using var rig = new ServerConfigTestRig();
		var provider = new FireSupportConfigEditorProvider(rig.Service);
		var first = provider.GetConfigs().Single();
		var second = provider.GetConfigs().Single();
		AssertEx.False(ReferenceEquals(first.RuntimeConfig, second.RuntimeConfig));
		var secondView = (FireSupportConfigEditorView)second.RuntimeConfig;
		int secondRevision = secondView.Revision;
		int secondCooldown = secondView.RequestCooldownSeconds;
		var draft = FireSupportConfigEditorView.FromConfig(rig.Service.GetConfigSnapshot());
		draft.RequestCooldownSeconds = 111;
		await first.ApplyToRuntimeAsync!(draft, CancellationToken.None);
		AssertEx.Equal(111, ((FireSupportConfigEditorView)first.RuntimeConfig).RequestCooldownSeconds);
		AssertEx.Equal(secondRevision, secondView.Revision);
		AssertEx.Equal(secondCooldown, secondView.RequestCooldownSeconds);
	}

	[RegressionTest]
	private static async Task NativeApplyThenSaveUsesSeparateTargetsAndRefreshesRuntimeRevision()
	{
		using var rig = new ServerConfigTestRig();
		var provider = new FireSupportConfigEditorProvider(rig.Service);
		var registration = provider.GetConfigs().Single();
		var draft = FireSupportConfigEditorView.FromConfig(rig.Service.GetConfigSnapshot());
		string originalDisk = rig.ReadDiskText();
		draft.RequestCooldownSeconds = 123;

		await registration.ApplyToRuntimeAsync!(draft, CancellationToken.None);
		AssertEx.Equal(123, rig.Service.GetConfigSnapshot().RequestCooldownSeconds);
		AssertEx.Equal(originalDisk, rig.ReadDiskText());
		// SIC replaces editor JSON with the registered runtime view after Apply.
		var applied = JsonSerializer.Deserialize<FireSupportConfigEditorView>(
			JsonSerializer.Serialize(registration.RuntimeConfig))!;
		AssertEx.Equal(rig.Service.GetConfigSnapshot().Revision, applied.Revision);
		var diskView = (FireSupportConfigEditorView)(await registration.LoadFromDiskAsync!(CancellationToken.None))!;
		AssertEx.NotEqual(123, diskView.RequestCooldownSeconds);
		AssertEx.Equal(applied.Revision, diskView.Revision);

		await registration.SaveToDiskAsync!(applied, CancellationToken.None);
		AssertEx.Equal(123, rig.ReadDisk().RequestCooldownSeconds);
		AssertEx.Equal(123, rig.Service.GetConfigSnapshot().RequestCooldownSeconds);
		// SIC retains the original editor JSON after Save; unchanged repeats are safe.
		await registration.SaveToDiskAsync!(applied, CancellationToken.None);
		await registration.ApplyToRuntimeAsync!(applied, CancellationToken.None);
	}

	[RegressionTest]
	private static async Task NativeSaveThenApplyAcceptsSameSavedDraftButRejectsAnInterveningChange()
	{
		using var rig = new ServerConfigTestRig();
		var registration = new FireSupportConfigEditorProvider(rig.Service).GetConfigs().Single();
		var draft = FireSupportConfigEditorView.FromConfig(rig.Service.GetConfigSnapshot());
		int previousCooldown = draft.RequestCooldownSeconds;
		draft.RequestCooldownSeconds = 177;
		await registration.SaveToDiskAsync!(draft, CancellationToken.None);
		AssertEx.Equal(previousCooldown, rig.Service.GetConfigSnapshot().RequestCooldownSeconds);
		AssertEx.Equal(177, rig.ReadDisk().RequestCooldownSeconds);
		await registration.ApplyToRuntimeAsync!(draft, CancellationToken.None);
		AssertEx.Equal(177, rig.Service.GetConfigSnapshot().RequestCooldownSeconds);

		var laterDraft = FireSupportConfigEditorView.FromConfig(rig.Service.GetConfigSnapshot());
		laterDraft.RequestCooldownSeconds = 188;
		await registration.SaveToDiskAsync!(laterDraft, CancellationToken.None);
		var dashboardEdit = rig.Service.GetConfigSnapshot();
		dashboardEdit.RequestCooldownSeconds = 199;
		AssertEx.True(rig.Service.TryUpdateConfig(dashboardEdit, out string error, dashboardEdit.Revision), error);
		await AssertEx.ThrowsAsync<InvalidOperationException>(() =>
			registration.ApplyToRuntimeAsync!(laterDraft, CancellationToken.None).AsTask());
		AssertEx.Equal(199, rig.Service.GetConfigSnapshot().RequestCooldownSeconds);
		AssertEx.Equal(199, rig.ReadDisk().RequestCooldownSeconds);
	}

	[RegressionTest]
	private static async Task NativeDiskLoadReadsFileAndSavePreservesHiddenDiskSettings()
	{
		using var rig = new ServerConfigTestRig();
		var provider = new FireSupportConfigEditorProvider(rig.Service);
		var registration = provider.GetConfigs().Single();
		var originalRuntime = rig.Service.GetConfigSnapshot();
		var disk = rig.ReadDisk();
		disk.RequestCooldownSeconds = 222;
		disk.AdminDashboard.Enabled = false;
		disk.PurchasePersistence.RefundFailedDispatch = false;
		disk.PriorityExfil.ExtractTimeSeconds = 42;
		rig.WriteDisk(disk);

		var loaded = (FireSupportConfigEditorView)(await registration.LoadFromDiskAsync!(CancellationToken.None))!;
		AssertEx.Equal(222, loaded.RequestCooldownSeconds);
		AssertEx.Equal(originalRuntime.RequestCooldownSeconds, rig.Service.GetConfigSnapshot().RequestCooldownSeconds);
		loaded.RequestCooldownSeconds = 233;
		await registration.SaveToDiskAsync!(loaded, CancellationToken.None);
		disk = rig.ReadDisk();
		AssertEx.Equal(233, disk.RequestCooldownSeconds);
		AssertEx.False(disk.AdminDashboard.Enabled);
		AssertEx.False(disk.PurchasePersistence.RefundFailedDispatch);
		AssertEx.Equal(42f, disk.PriorityExfil.ExtractTimeSeconds);
		AssertEx.True(rig.Service.GetConfigSnapshot().AdminDashboard.Enabled);

		File.WriteAllText(rig.ConfigPath, "{broken");
		await AssertEx.ThrowsAsync<InvalidOperationException>(() =>
			registration.LoadFromDiskAsync!(CancellationToken.None).AsTask());
		AssertEx.Equal("{broken", rig.ReadDiskText());
		AssertEx.Equal(originalRuntime.RequestCooldownSeconds, rig.Service.GetConfigSnapshot().RequestCooldownSeconds);
	}

	[RegressionTest]
	private static async Task NativeStaleEditsAndCanceledOperationsLeaveBothTargetsUnchanged()
	{
		using var rig = new ServerConfigTestRig();
		var provider = new FireSupportConfigEditorProvider(rig.Service);
		var registration = provider.GetConfigs().Single();
		var stale = FireSupportConfigEditorView.FromConfig(rig.Service.GetConfigSnapshot());
		var dashboardEdit = rig.Service.GetConfigSnapshot();
		dashboardEdit.RequestCooldownSeconds = 244;
		AssertEx.True(rig.Service.TryUpdateConfig(dashboardEdit, out string error, dashboardEdit.Revision), error);
		stale.RequestCooldownSeconds = 255;
		await AssertEx.ThrowsAsync<InvalidOperationException>(() =>
			registration.ApplyToRuntimeAsync!(stale, CancellationToken.None).AsTask());
		await AssertEx.ThrowsAsync<InvalidOperationException>(() =>
			registration.SaveToDiskAsync!(stale, CancellationToken.None).AsTask());
		var refreshed = (FireSupportConfigEditorView)provider.GetConfigs().Single().RuntimeConfig;
		AssertEx.Equal(244, refreshed.RequestCooldownSeconds);
		AssertEx.Equal(rig.Service.GetConfigSnapshot().Revision, refreshed.Revision);
		string disk = rig.ReadDiskText();
		using var canceled = new CancellationTokenSource();
		canceled.Cancel();
		await AssertEx.ThrowsAsync<OperationCanceledException>(() =>
			registration.ApplyToRuntimeAsync!(stale, canceled.Token).AsTask());
		await AssertEx.ThrowsAsync<OperationCanceledException>(() =>
			registration.SaveToDiskAsync!(stale, canceled.Token).AsTask());
		await AssertEx.ThrowsAsync<OperationCanceledException>(() =>
			registration.LoadFromDiskAsync!(canceled.Token).AsTask());
		AssertEx.Equal(disk, rig.ReadDiskText());
		AssertEx.Equal(244, rig.Service.GetConfigSnapshot().RequestCooldownSeconds);
	}

	[RegressionTest]
	private static void CuratedOverlayPreservesUnexposedSettingsAndPlayerState()
	{
		var config = new RaidOpsFireSupportServerConfig
		{
			Revision = 12,
			ConfigSchemaVersion = 5,
			PlayerStateIncluded = true,
			StashCurrencyBalance = 12345,
			StashRoubleBalance = 54321,
			PreparedPurchases = new() { ["Strafe"] = "purchase-1" },
			PreparedPurchaseDetails = new()
			{
				["Strafe"] = new() { RequestId = "purchase-1", Price = 500, Currency = "RUB" }
			},
			Authorizations = new() { ["Strafe"] = 2 },
			AdminDashboard = new() { Enabled = false, AllowRemoteAccess = true, RequireTokenForLocalhost = true },
			PriorityExfil = new() { ExtractTimeSeconds = 42 },
			A10 = new() { SecondPassDelaySeconds = 19 },
			PurchasePersistence = new()
			{
				Mode = "test-mode", ConsumeOn = "test-consume", RefundFailedDispatch = false
			}
		};
		FireSupportConfigEditorView edited = FireSupportConfigEditorView.FromConfig(config);
		edited.RequestCooldownSeconds = 47;
		edited.Prices["Strafe"] = 250;
		edited.Uav.DurationSeconds = 150;
		edited.ApplyTo(config);

		AssertEx.Equal(47, config.RequestCooldownSeconds);
		AssertEx.Equal(250, config.Prices["Strafe"]);
		AssertEx.Equal(150, config.Uav.DurationSeconds);
		AssertEx.Equal(12, config.Revision);
		AssertEx.Equal(5, config.ConfigSchemaVersion);
		AssertEx.True(config.PlayerStateIncluded);
		AssertEx.Equal<int?>(12345, config.StashCurrencyBalance);
		AssertEx.Equal<int?>(54321, config.StashRoubleBalance);
		AssertEx.Equal(2, config.Authorizations["Strafe"]);
		AssertEx.Equal("purchase-1", config.PreparedPurchases!["Strafe"]);
		AssertEx.Equal(500, config.PreparedPurchaseDetails!["Strafe"].Price);
		AssertEx.False(config.AdminDashboard.Enabled);
		AssertEx.True(config.AdminDashboard.AllowRemoteAccess);
		AssertEx.True(config.AdminDashboard.RequireTokenForLocalhost);
		AssertEx.Equal(42f, config.PriorityExfil.ExtractTimeSeconds);
		AssertEx.Equal(19f, config.A10.SecondPassDelaySeconds);
		AssertEx.Equal("test-mode", config.PurchasePersistence.Mode);
		AssertEx.Equal("test-consume", config.PurchasePersistence.ConsumeOn);
		AssertEx.False(config.PurchasePersistence.RefundFailedDispatch);

		string editorJson = JsonSerializer.Serialize(edited);
		AssertEx.False(editorJson.Contains("AdminDashboard", StringComparison.OrdinalIgnoreCase));
		AssertEx.False(editorJson.Contains("PreparedPurchases", StringComparison.OrdinalIgnoreCase));
		AssertEx.False(editorJson.Contains("StashCurrencyBalance", StringComparison.OrdinalIgnoreCase));
	}

	[RegressionTest]
	private static void EditingDraftDoesNotMutateItsSourceAndNullSectionsFailBeforeOverlay()
	{
		var source = new RaidOpsFireSupportServerConfig
		{
			Prices = new() { ["Strafe"] = 600 },
			Enabled = new() { ["Strafe"] = true },
			Uav = new() { DurationSeconds = 90 }
		};
		FireSupportConfigEditorView draft = FireSupportConfigEditorView.FromConfig(source);
		draft.Prices["Strafe"] = 800;
		draft.Enabled["Strafe"] = false;
		draft.Uav.DurationSeconds = 60;
		AssertEx.Equal(600, source.Prices["Strafe"]);
		AssertEx.True(source.Enabled["Strafe"]);
		AssertEx.Equal(90, source.Uav.DurationSeconds);

		draft.RequestCooldownSeconds = 10;
		draft.Extraction = null!;
		AssertEx.Contains("cannot be null", AssertEx.Throws<InvalidOperationException>(() => draft.ApplyTo(source)).Message);
		AssertEx.Equal(300, source.RequestCooldownSeconds);
		AssertEx.Equal(600, source.Prices["Strafe"]);
	}
}
