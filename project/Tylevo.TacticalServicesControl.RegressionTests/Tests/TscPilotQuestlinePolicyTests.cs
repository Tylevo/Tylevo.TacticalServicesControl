using SamSWAT.FireSupport.ArysReloaded;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using System.Text.Json.Nodes;

internal static class TscPilotQuestlinePolicyTests
{
	[RegressionTest]
	private static void UninitializedAndLoadedButUnverifiedModesNeverGrantPermission()
	{
		using var files = new AddonFiles();
		var policy = new TscPilotQuestlinePolicy();
		PmcData profile = new() { Id = new MongoId("66f51f3a0000000000005201"),
			Quests = [new QuestStatus { QId = new MongoId(TscPilotProgressionService.FinalQuestId), Status = QuestStatusEnum.Success,
				StartTime = 0, StatusTimers = [] }] };
		var progression = new TscPilotProgressionService(new ProfileHelper(), policy);
		AssertEx.True(policy.QuestlineRequired);
		AssertEx.False(progression.HasUnlockedUplink(profile));
		AssertEx.Throws<InvalidOperationException>(policy.Activate);
		policy.Initialize(files.Root, "1.3.11", "4.1.5");
		AssertEx.True(policy.IsInitialized);
		AssertEx.True(policy.QuestlineRequired);
		AssertEx.False(policy.IsActive);
		AssertEx.False(progression.HasUnlockedUplink(profile));
		policy.Activate();
		AssertEx.True(progression.HasUnlockedUplink(profile));
	}

	[RegressionTest]
	private static void AbsentAddonSelectsBaseOnceAndCannotBeChangedDuringTheProcess()
	{
		using var files = new AddonFiles(copyAddon: false);
		var policy = new TscPilotQuestlinePolicy();
		policy.Initialize(files.Root, "1.3.11", "4.1.5");
		AssertEx.False(policy.QuestlineRequired);
		AssertEx.False(policy.IsActive);
		files.CopyAddon();
		AssertEx.Throws<InvalidOperationException>(() => policy.Initialize(files.Root, "1.3.11", "4.1.5"));
		AssertEx.False(policy.QuestlineRequired);
		policy.Activate();
		AssertEx.True(policy.IsActive);
	}

	[RegressionTest]
	private static void EmptyMalformedMissingAndMismatchedAddonsFailStartupClosed()
	{
		foreach (Action<AddonFiles> corrupt in new Action<AddonFiles>[]
		{
			files => File.Delete(files.Path("addon.json")),
			files => File.WriteAllText(files.Path("addon.json"), "{invalid"),
			files => File.WriteAllText(files.Path("addon.json"), "[]"),
			files => files.ChangeManifest("schemaVersion", 2),
			files => files.ChangeManifest("id", "some-other-addon"),
			files => files.ChangeManifest("version", "1.3.10"),
			files => files.ChangeManifest("targetSptVersion", "4.1.4"),
			files => File.Delete(files.Path("db/CustomAssortSchemes/pilot_repeater.json")),
			files => File.WriteAllText(files.Path("db/CustomQuests/66f51f3a0000000000000a60/Quests/pilot_introduction.json"), "{}"),
			files => File.WriteAllText(files.Path("db/CustomQuests/66f51f3a0000000000000a60/QuestAssort/pilot_introduction.json"), "{}"),
			files => File.WriteAllText(files.Path("db/CustomQuests/5a7c2eca46aef81a7ca2145d/Locales/en.json"), "{}")
		})
		{
			using var files = new AddonFiles();
			corrupt(files);
			var policy = new TscPilotQuestlinePolicy();
			AssertEx.Contains("incomplete or incompatible", AssertEx.Throws<InvalidOperationException>(
				() => policy.Initialize(files.Root, "1.3.11", "4.1.5")).Message);
			AssertEx.True(policy.QuestlineRequired);
			AssertEx.False(policy.IsInitialized);
			AssertEx.False(policy.IsActive);
			AssertEx.Throws<InvalidOperationException>(policy.Activate);
			AssertEx.Throws<InvalidOperationException>(() => policy.Initialize(files.Root, "1.3.11", "4.1.5"));
		}
	}

	[RegressionTest]
	private static void FileAtAddonDirectoryCannotSilentlySelectBaseMode()
	{
		using var files = new AddonFiles(copyAddon: false);
		Directory.CreateDirectory(System.IO.Path.Combine(files.Root, "addons"));
		File.WriteAllText(System.IO.Path.Combine(files.Root, TscPilotQuestlinePolicy.AddonRelativePath), "incomplete install");
		var policy = new TscPilotQuestlinePolicy();
		AssertEx.Throws<InvalidOperationException>(() => policy.Initialize(files.Root, "1.3.11", "4.1.5"));
		AssertEx.False(policy.IsActive);
		AssertEx.True(policy.QuestlineRequired);
	}

	private sealed class AddonFiles : IDisposable
	{
		public string Root { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tsc-addon-policy-" + Guid.NewGuid().ToString("N"));
		public AddonFiles(bool copyAddon = true)
		{
			Directory.CreateDirectory(Root);
			if (copyAddon) CopyAddon();
		}
		public string Path(string relative) => System.IO.Path.Combine(Root, TscPilotQuestlinePolicy.AddonRelativePath, relative);
		public void CopyAddon()
		{
			string source = System.IO.Path.Combine(PilotPolicyTestFixture.RepositoryRoot, TscPilotQuestlinePolicy.AddonRelativePath);
			foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
			{
				string target = Path(System.IO.Path.GetRelativePath(source, file));
				Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
				File.Copy(file, target, true);
			}
		}
		public void ChangeManifest(string key, JsonNode value)
		{
			JsonNode manifest = JsonNode.Parse(File.ReadAllText(Path("addon.json")))!;
			manifest[key] = value;
			File.WriteAllText(Path("addon.json"), manifest.ToJsonString());
		}
		public void Dispose() => Directory.Delete(Root, recursive: true);
	}
}
