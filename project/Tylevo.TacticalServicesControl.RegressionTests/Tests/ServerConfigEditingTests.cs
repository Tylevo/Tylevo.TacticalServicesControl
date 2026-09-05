using SamSWAT.FireSupport.ArysReloaded;
using SamSWAT.FireSupport.ArysReloaded.Unity;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils.Cloners;
using System.Text.Json;

internal static class ServerConfigEditingTests
{
	[RegressionTest]
	private static void BriefWindowsReplacementLocksAreRetriedButPartialReplacementErrorsAreNot()
	{
		foreach (int code in new[] { 32, 33, 1175 })
		{
			int calls = 0;
			var waits = new List<int>();
			FireSupportServerConfigService.ReplaceConfigFile(() =>
			{
				if (++calls < 3) throw new IOException("Temporary file lock", unchecked((int)0x80070000) | code);
			}, waits.Add);
			AssertEx.Equal(3, calls);
			AssertEx.Equal(2, waits.Count);
		}
		foreach (int code in new[] { 32, 33, 1175, 1176, 1177, 5, 112 })
		{
			int calls = 0;
			try
			{
				FireSupportServerConfigService.ReplaceConfigFile(() =>
				{
					calls++;
					throw new IOException("Persistent or unsafe replacement error", unchecked((int)0x80070000) | code);
				}, _ => { });
				throw new Exception("Expected replacement failure");
			}
			catch (IOException exception)
			{
				AssertEx.Equal(code, exception.HResult & 0xffff);
			}
			AssertEx.Equal(code is 32 or 33 or 1175 ? 4 : 1, calls);
		}
	}

	[RegressionTest]
	private static void DashboardSavePublishesCompleteConfigAndAdvancesRevision()
	{
		using var rig = new ServerConfigTestRig();
		RaidOpsFireSupportServerConfig edited = rig.Service.GetConfigSnapshot();
		int revision = edited.Revision;
		edited.Prices["Uav"] = 1234;
		AssertEx.True(rig.Service.TryUpdateConfig(edited, out string error, out bool conflict, revision), error);
		AssertEx.False(conflict);
		AssertEx.Equal(revision + 1, rig.ReadDisk().Revision);
		AssertEx.Equal(1234, rig.ReadDisk().Prices["Uav"]);
		edited.Prices["Uav"] = 9999;
		AssertEx.Equal(1234, rig.Service.GetConfigSnapshot().Prices["Uav"]);
		AssertNoTemporaryFiles(rig);
	}

	[RegressionTest]
	private static void StaleDashboardCannotOverwriteNativeChanges()
	{
		using var rig = new ServerConfigTestRig();
		RaidOpsFireSupportServerConfig dashboard = rig.Service.GetConfigSnapshot();
		RaidOpsFireSupportServerConfig native = rig.Service.GetConfigSnapshot();
		native.Prices["Uav"] = 1234;
		AssertEx.True(rig.Service.TryApplyConfig(native, out string error, native.Revision), error);
		dashboard.Prices["Uav"] = 9999;
		string diskBefore = rig.ReadDiskText();
		AssertEx.False(rig.Service.TryUpdateConfig(dashboard, out error, out bool conflict, dashboard.Revision));
		AssertEx.True(conflict);
		AssertEx.Contains("Reload the editor", error);
		AssertEx.Equal(1234, rig.Service.GetConfigSnapshot().Prices["Uav"]);
		AssertEx.Equal(diskBefore, rig.ReadDiskText());
	}

	[RegressionTest]
	private static async Task ConcurrentDashboardSavesPublishExactlyOneRevision()
	{
		using var rig = new ServerConfigTestRig();
		RaidOpsFireSupportServerConfig first = rig.Service.GetConfigSnapshot();
		RaidOpsFireSupportServerConfig second = rig.Service.GetConfigSnapshot();
		int revision = first.Revision;
		first.Prices["Uav"] = 1234;
		second.Prices["Uav"] = 5678;
		(bool Success, bool Conflict)[] results = await Task.WhenAll(new[] { first, second }.Select(candidate =>
			Task.Run(() =>
			{
				bool success = rig.Service.TryUpdateConfig(candidate, out _, out bool conflict, revision);
				return (success, conflict);
			})));
		AssertEx.Equal(1, results.Count(result => result.Success));
		AssertEx.Equal(1, results.Count(result => result.Conflict));
		AssertEx.Equal(revision + 1, rig.Service.GetConfigSnapshot().Revision);
		AssertEx.Equal(rig.Service.GetConfigSnapshot().Prices["Uav"], rig.ReadDisk().Prices["Uav"]);
		AssertNoTemporaryFiles(rig);
	}

	[RegressionTest]
	private static void NativeApplyAndSaveHaveSeparateEffects()
	{
		using var rig = new ServerConfigTestRig();
		string diskBefore = rig.ReadDiskText();
		RaidOpsFireSupportServerConfig edited = rig.Service.GetConfigSnapshot();
		edited.Prices["Uav"] = 1234;
		AssertEx.True(rig.Service.TryApplyConfig(edited, out string error, edited.Revision), error);
		AssertEx.Equal(diskBefore, rig.ReadDiskText());
		AssertEx.Equal(1234, rig.Service.GetConfigSnapshot().Prices["Uav"]);
		edited.Prices["Uav"] = 5678;
		AssertEx.True(rig.Service.TrySaveConfig(edited, out error, edited.Revision), error);
		AssertEx.Equal(5678, rig.ReadDisk().Prices["Uav"]);
		AssertEx.Equal(1234, rig.Service.GetConfigSnapshot().Prices["Uav"]);
		AssertEx.Equal(edited.Revision, rig.Service.GetConfigSnapshot().Revision);
	}

	[RegressionTest]
	private static void SaveThenApplyAcceptsOnlyTheLatestExactSavedDraft()
	{
		using var rig = new ServerConfigTestRig();
		RaidOpsFireSupportServerConfig draft = rig.Service.GetConfigSnapshot();
		int originalRevision = draft.Revision;
		draft.Prices["Uav"] = 1234;
		AssertEx.True(rig.Service.TrySaveConfig(draft, out string error, originalRevision), error);
		// SIC retains the original JSON revision after Save.
		draft.Revision = originalRevision;
		AssertEx.True(rig.Service.TryApplyConfig(draft, out error, originalRevision), error);
		AssertEx.Equal(1234, rig.Service.GetConfigSnapshot().Prices["Uav"]);

		RaidOpsFireSupportServerConfig next = rig.Service.GetConfigSnapshot();
		originalRevision = next.Revision;
		next.Prices["Uav"] = 5678;
		AssertEx.True(rig.Service.TrySaveConfig(next, out error, originalRevision), error);
		next.Prices["Uav"] = 9999;
		AssertEx.False(rig.Service.TryApplyConfig(next, out error, originalRevision));
		AssertEx.Contains("revision changed", error);
		AssertEx.Equal(1234, rig.Service.GetConfigSnapshot().Prices["Uav"]);
		AssertEx.Equal(5678, rig.ReadDisk().Prices["Uav"]);
	}

	[RegressionTest]
	private static void InterveningEditInvalidatesSaveThenApplyBridge()
	{
		using var rig = new ServerConfigTestRig();
		RaidOpsFireSupportServerConfig savedDraft = rig.Service.GetConfigSnapshot();
		int originalRevision = savedDraft.Revision;
		savedDraft.Prices["Uav"] = 1234;
		AssertEx.True(rig.Service.TrySaveConfig(savedDraft, out string error, originalRevision), error);
		RaidOpsFireSupportServerConfig newer = rig.Service.GetConfigSnapshot();
		newer.RequestCooldownSeconds += 10;
		AssertEx.True(rig.Service.TryApplyConfig(newer, out error, newer.Revision), error);
		AssertEx.False(rig.Service.TryApplyConfig(savedDraft, out error, originalRevision));
		AssertEx.Equal(newer.RequestCooldownSeconds, rig.Service.GetConfigSnapshot().RequestCooldownSeconds);
	}

	[RegressionTest]
	private static void DiskOnlySaveRejectsStaleWritersAndCannotBeUndoneByStaleNoOpDashboard()
	{
		using var rig = new ServerConfigTestRig();
		RaidOpsFireSupportServerConfig dashboard = rig.Service.GetConfigSnapshot();
		RaidOpsFireSupportServerConfig first = rig.Service.GetConfigSnapshot();
		RaidOpsFireSupportServerConfig second = rig.Service.GetConfigSnapshot();
		first.Prices["Uav"] = 1234;
		second.Prices["Uav"] = 5678;
		AssertEx.True(rig.Service.TrySaveConfig(first, out string error, first.Revision), error);
		AssertEx.False(rig.Service.TrySaveConfig(second, out error, second.Revision));
		AssertEx.False(rig.Service.TryUpdateConfig(dashboard, out error, out bool conflict, dashboard.Revision));
		AssertEx.True(conflict);
		AssertEx.Equal(1234, rig.ReadDisk().Prices["Uav"]);
		AssertEx.Equal(dashboard.Prices["Uav"], rig.Service.GetConfigSnapshot().Prices["Uav"]);
	}

	[RegressionTest]
	private static void RepeatedUnchangedSaveDoesNotAdvanceRevision()
	{
		using var rig = new ServerConfigTestRig();
		RaidOpsFireSupportServerConfig edited = rig.Service.GetConfigSnapshot();
		int originalRevision = edited.Revision;
		edited.Prices["Uav"] = 1234;
		AssertEx.True(rig.Service.TrySaveConfig(edited, out string error, originalRevision), error);
		int savedRevision = rig.Service.GetConfigSnapshot().Revision;
		string savedText = rig.ReadDiskText();
		AssertEx.True(rig.Service.TrySaveConfig(edited, out error, originalRevision), error);
		AssertEx.Equal(savedRevision, rig.Service.GetConfigSnapshot().Revision);
		AssertEx.Equal(savedText, rig.ReadDiskText());
	}

	[RegressionTest]
	private static void ReadingDiskIsReadOnlyAndReloadInvalidatesOldEditors()
	{
		using var rig = new ServerConfigTestRig();
		RaidOpsFireSupportServerConfig before = rig.Service.GetConfigSnapshot();
		RaidOpsFireSupportServerConfig edited = rig.ReadDisk();
		edited.Prices["Uav"] = 1234;
		edited.Revision = 1;
		rig.WriteDisk(edited);
		string diskBefore = rig.ReadDiskText();
		AssertEx.True(rig.Service.TryGetDiskConfigSnapshot(out RaidOpsFireSupportServerConfig disk, out string error), error);
		AssertEx.Equal(1234, disk.Prices["Uav"]);
		AssertEx.Equal(before.Revision, disk.Revision);
		AssertEx.Equal(before.Prices["Uav"], rig.Service.GetConfigSnapshot().Prices["Uav"]);
		AssertEx.Equal(diskBefore, rig.ReadDiskText());
		AssertEx.True(rig.Service.TryReloadConfig(out RaidOpsFireSupportServerConfig reloaded, out error), error);
		AssertEx.Equal(1234, reloaded.Prices["Uav"]);
		AssertEx.True(reloaded.Revision > before.Revision);
		AssertEx.Equal(reloaded.Revision, rig.ReadDisk().Revision);
		before.RequestCooldownSeconds += 10;
		AssertEx.False(rig.Service.TryUpdateConfig(before, out error, out bool conflict, before.Revision));
		AssertEx.True(conflict);
	}

	[RegressionTest]
	private static void MalformedNullMissingAndInvalidDiskConfigsDoNotResetRuntime()
	{
		foreach (string? invalid in new[] { "{broken", "null", "{\"configSchemaVersion\":3,\"paymentCurrency\":\"INVALID\"}", null })
		{
			using var rig = new ServerConfigTestRig();
			RaidOpsFireSupportServerConfig before = rig.Service.GetConfigSnapshot();
			if (invalid == null)
			{
				File.Delete(rig.ConfigPath);
			}
			else
			{
				File.WriteAllText(rig.ConfigPath, invalid);
			}

			AssertEx.False(rig.Service.TryGetDiskConfigSnapshot(out _, out string error));
			AssertEx.True(error.Length > 0);
			AssertEx.False(rig.Service.TryReloadConfig(out _, out error));
			AssertEx.True(error.Length > 0);
			AssertEx.Equal(before.Revision, rig.Service.GetConfigSnapshot().Revision);
			AssertEx.Equal(before.Prices["Uav"], rig.Service.GetConfigSnapshot().Prices["Uav"]);
			AssertEx.Equal(invalid != null, File.Exists(rig.ConfigPath));
			if (invalid != null) AssertEx.Equal(invalid, rig.ReadDiskText());
		}
	}

	[RegressionTest]
	private static void ExplicitDashboardSaveCanRepairMissingMalformedAndNullDiskFiles()
	{
		foreach (string? invalid in new[] { "{broken", "null", null })
		{
			using var rig = new ServerConfigTestRig();
			RaidOpsFireSupportServerConfig edited = rig.Service.GetConfigSnapshot();
			if (invalid == null) File.Delete(rig.ConfigPath);
			else File.WriteAllText(rig.ConfigPath, invalid);
			edited.Prices["Uav"] = 1234;
			AssertEx.True(rig.Service.TryUpdateConfig(edited, out string error, edited.Revision), error);
			AssertEx.Equal(1234, rig.ReadDisk().Prices["Uav"]);
			AssertEx.Equal(1234, rig.Service.GetConfigSnapshot().Prices["Uav"]);
			AssertNoTemporaryFiles(rig);
		}
	}

	[RegressionTest]
	private static void ReloadPersistsCanonicalNormalizationEvenWhenRuntimeValuesMatch()
	{
		using var rig = new ServerConfigTestRig();
		RaidOpsFireSupportServerConfig saved = rig.ReadDisk();
		saved.PaymentCurrency = "rub";
		rig.WriteDisk(saved);
		AssertEx.True(rig.Service.TryReloadConfig(out RaidOpsFireSupportServerConfig reloaded, out string error), error);
		AssertEx.Equal("RUB", reloaded.PaymentCurrency);
		AssertEx.Equal("RUB", rig.ReadDisk().PaymentCurrency);
		AssertEx.True(reloaded.Revision > saved.Revision);
	}

	[RegressionTest]
	private static void InvalidUpdateDoesNotChangeRuntimeOrDisk()
	{
		using var rig = new ServerConfigTestRig();
		RaidOpsFireSupportServerConfig edited = rig.Service.GetConfigSnapshot();
		int revision = edited.Revision;
		string before = rig.ReadDiskText();
		edited.Extraction.WaitTimeSeconds = 5;
		edited.Extraction.ExtractTimeSeconds = 30;
		AssertEx.False(rig.Service.TryUpdateConfig(edited, out string error, out bool conflict, revision));
		AssertEx.False(conflict);
		AssertEx.Contains("extraction", error);
		AssertEx.Equal(revision, rig.Service.GetConfigSnapshot().Revision);
		AssertEx.Equal(before, rig.ReadDiskText());
	}

	[RegressionTest]
	private static void FailedPublicationPreservesRuntimeRevisionAndExistingFile()
	{
		using var rig = new ServerConfigTestRig();
		RaidOpsFireSupportServerConfig edited = rig.Service.GetConfigSnapshot();
		int revision = edited.Revision;
		int originalPrice = edited.Prices["Uav"];
		string before = rig.ReadDiskText();
		edited.Prices["Uav"] = 1234;
		using (rig.BlockConfigPublication())
		{
			AssertEx.False(rig.Service.TryUpdateConfig(edited, out string error, out bool conflict, revision));
			AssertEx.False(conflict);
			AssertEx.True(error.Length > 0);
			AssertEx.False(rig.Service.TrySaveConfig(edited, out error, revision));
			AssertEx.False(rig.Service.TryResetConfig(out _, out error));
			AssertEx.Equal(revision, rig.Service.GetConfigSnapshot().Revision);
			AssertEx.Equal(originalPrice, rig.Service.GetConfigSnapshot().Prices["Uav"]);
			AssertNoTemporaryFiles(rig);
		}
		AssertEx.Equal(before, rig.ReadDiskText());
	}

	private static void AssertNoTemporaryFiles(ServerConfigTestRig rig)
	{
		AssertEx.Equal(0, Directory.GetFiles(Path.GetDirectoryName(rig.ConfigPath)!, "*.tmp").Length);
	}
}

internal sealed class ServerConfigTestRig : IDisposable
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true
	};
	private readonly string _root = Path.Combine(Path.GetTempPath(), $"tsc-config-editing-{Guid.NewGuid():N}");
	public FireSupportServerConfigService Service { get; }
	public string ConfigPath => Path.Combine(_root, "config", "tsc-config.json");

	public ServerConfigTestRig()
	{
		Service = new FireSupportServerConfigService(
			new SilentLogger<FireSupportServerConfigService>(),
			new ProfileHelper(), new SaveServer(),
			new FireSupportAuthorizationLedger(new SilentLogger<FireSupportAuthorizationLedger>()),
			new FireSupportProfileMutationGate(), new JsonCloner());
		Service.Initialize(_root);
	}

	public RaidOpsFireSupportServerConfig ReadDisk() =>
		JsonSerializer.Deserialize<RaidOpsFireSupportServerConfig>(ReadDiskText(), JsonOptions)!;
	public string ReadDiskText() => File.ReadAllText(ConfigPath);
	public void WriteDisk(RaidOpsFireSupportServerConfig config) =>
		File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, JsonOptions));

	public IDisposable BlockConfigPublication()
	{
		if (OperatingSystem.IsWindows())
		{
			// Readers remain allowed, but replacing this file must fail.
			return new FileStream(ConfigPath, FileMode.Open, FileAccess.Read, FileShare.Read);
		}

		// Unix rename ignores open-file sharing. Occupy the destination with a
		// directory to exercise publication failure after the temporary write.
		string preservedPath = ConfigPath + ".preserved";
		File.Move(ConfigPath, preservedPath);
		Directory.CreateDirectory(ConfigPath);
		return new RestoreConfigFile(ConfigPath, preservedPath);
	}

	public void Dispose()
	{
		if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
	}

	private sealed class RestoreConfigFile(string configPath, string preservedPath) : IDisposable
	{
		public void Dispose()
		{
			Directory.Delete(configPath);
			File.Move(preservedPath, configPath);
		}
	}

	private sealed class JsonCloner : ICloner
	{
		public T? Clone<T>(T? value) => value == null ? default :
			JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value));
	}

	private sealed class SilentLogger<T> : ISptLogger<T>
	{
		public void Warning(string message) { }
		public void Error(string message) { }
		public void Error(string message, Exception exception) { }
	}
}
