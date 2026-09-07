using SamSWAT.FireSupport.ArysReloaded;
using SamSWAT.FireSupport.ArysReloaded.Unity;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils.Cloners;
using System.Reflection;
using System.Text.Json;

internal static class TscPilotProgressionTests
{
	private const string SessionId = "66f51f3a0000000000005101";
	private const string ProfileId = "66f51f3a0000000000005201";
	private const string OtherSessionId = "66f51f3a0000000000005102";
	private const string OtherProfileId = "66f51f3a0000000000005202";
	private const string StashId = "66f51f3a0000000000005301";
	private const int InitialBalance = 10_000;
	private const int Price = 100;

	[RegressionTest]
	private static async Task BaseTscAllowsFreshProfilesWithAuthenticatedPermitsAndNormalServicePrices()
	{
		foreach (ESupportType type in Enum.GetValues<ESupportType>().Where(type => type != ESupportType.None))
		foreach (bool persistent in new[] { false, true })
		{
			using var rig = new Rig(questlineRequired: false);
			AssertEx.Null(rig.Profile.Quests);
			RaidOpsFireSupportServerConfig snapshot = rig.Snapshot();
			AssertEx.Equal<bool?>(true, snapshot.UplinkUnlocked);
			AssertEx.Equal(64, snapshot.ProgressionPermit.Length);
			AssertEx.True(rig.Verify(snapshot.ProgressionPermit).Ok);
			AssertEx.Equal("ProgressionProfileMismatch", rig.Verify(snapshot.ProgressionPermit, OtherProfileId).Reason);
			AssertEx.Equal("ProgressionPermitInvalid", rig.Verify(string.Empty).Reason);
			FireSupportPurchaseResponse purchase = await rig.Purchase("base-purchase", type, persistent);
			AssertEx.True(purchase.Ok, purchase.Reason);
			AssertEx.Equal(Price, purchase.ChargedFromStash);
			AssertEx.Equal(InitialBalance - Price, rig.Balance);
		}
	}

	[RegressionTest]
	private static void BaseTscStillRequiresAnIdentifiableAuthenticatedProfile()
	{
		using var rig = new Rig(questlineRequired: false);
		AssertEx.False(rig.Progression.HasUnlockedUplink(null));
		AssertEx.False(rig.Progression.HasUnlockedUplink(new PmcData()));
		RaidOpsFireSupportServerConfig anonymous = rig.Snapshot("66f51f3a0000000000005999");
		AssertEx.Null(anonymous.UplinkUnlocked);
		AssertEx.Equal(string.Empty, anonymous.ProgressionPermit);
		AssertEx.Equal(string.Empty, rig.Progression.GetPermitForAuthenticatedProfile(rig.Profile, default));
	}

	[RegressionTest]
	private static async Task InstallingOrRemovingTheAddonRequiresFreshPermitsAndPreservesRecovery()
	{
		using var baseRig = new Rig(questlineRequired: false);
		string oldPermit = baseRig.Snapshot().ProgressionPermit;
		AssertEx.True((await baseRig.Purchase("before-addon")).Ok);
		var addonProgression = new TscPilotProgressionService(baseRig.ProfileHelper, PilotPolicyTestFixture.Create(true));
		AssertEx.Equal("ProgressionPermitInvalid", addonProgression.Verify(Request(oldPermit)).Reason);
		AssertEx.False(addonProgression.HasUnlockedUplink(baseRig.Profile));
		AssertEx.Equal(string.Empty, addonProgression.GetPermitForAuthenticatedProfile(baseRig.Profile, new MongoId(SessionId)));
		var restartedService = new FireSupportServerConfigService(new SilentLogger<FireSupportServerConfigService>(),
			baseRig.ProfileHelper, new SaveServer(), baseRig.Ledger, new FireSupportProfileMutationGate(), addonProgression, new JsonCloner());
		restartedService.Initialize(baseRig.Root);
		AssertEx.Equal("UplinkLocked", restartedService.TryConsumeAuthorization(new MongoId(SessionId), baseRig.Mutation("new-dispatch")).Reason);
		FireSupportPurchaseRequest replay = baseRig.Mutation("before-addon");
		replay.Action = "BuyPersistentAuthorization";
		replay.ExpectedCost = Price;
		replay.ExpectedCurrency = "RUB";
		replay.Quantity = 1;
		AssertEx.True((await restartedService.TryPurchaseAsync(new MongoId(SessionId), replay)).Ok,
			"Installing the add-on must not invalidate an already accepted purchase replay.");
		AssertEx.Equal(1, baseRig.Ledger.GetCredits(ProfileId, 180, 2)["Uav"]);

		baseRig.SetStatus(QuestStatusEnum.Success);
		string addonPermit = addonProgression.GetPermitForAuthenticatedProfile(baseRig.Profile, new MongoId(SessionId));
		var removedProgression = new TscPilotProgressionService(baseRig.ProfileHelper, PilotPolicyTestFixture.Create(false));
		AssertEx.Equal("ProgressionPermitInvalid", removedProgression.Verify(Request(addonPermit)).Reason);
		baseRig.SetStatus(QuestStatusEnum.Started);
		string freshPermit = removedProgression.GetPermitForAuthenticatedProfile(baseRig.Profile, new MongoId(SessionId));
		AssertEx.True(removedProgression.Verify(Request(freshPermit)).Ok);
	}

	[RegressionTest]
	private static async Task BaseModePreservesConfiguredServiceDisableAndAuthorizationLifecycle()
	{
		using var rig = new Rig(questlineRequired: false);
		RaidOpsFireSupportServerConfig config = rig.Service.GetConfigSnapshot();
		config.Enabled["Uav"] = false;
		AssertEx.True(rig.Service.TryUpdateConfig(config, out string error, out _, config.Revision), error);
		AssertEx.Equal("ServiceUnavailable", (await rig.Purchase("disabled")).Reason);
		config = rig.Service.GetConfigSnapshot();
		config.Enabled["Uav"] = true;
		AssertEx.True(rig.Service.TryUpdateConfig(config, out error, out _, config.Revision), error);
		AssertEx.True((await rig.Purchase("base-credit")).Ok);
		AssertEx.True(rig.Service.TryConsumeAuthorization(new MongoId(SessionId), rig.Mutation("base-refund")).Ok);
		AssertEx.True(rig.Service.TryRefundAuthorization(new MongoId(SessionId), rig.Mutation("base-refund")).Ok);
		AssertEx.True(rig.Service.TryConsumeAuthorization(new MongoId(SessionId), rig.Mutation("base-commit")).Ok);
		AssertEx.True(rig.Service.TryCommitAuthorization(new MongoId(SessionId), rig.Mutation("base-commit")).Ok);
		AssertEx.Equal(0, rig.Ledger.GetCredits(ProfileId, 180, 2)["Uav"]);
	}

	[RegressionTest]
	private static void OnlyFinalQuestSuccessUnlocksUplinkIncludingNoReadyToFinishShortcut()
	{
		PmcData profile = Profile();
		var progression = new TscPilotProgressionService(new ProfileHelper(), PilotPolicyTestFixture.Create(true));
		AssertEx.False(progression.HasUnlockedUplink(profile));
		AssertEx.Equal(string.Empty, progression.GetPermitForAuthenticatedProfile(profile, new MongoId(SessionId)));
		foreach (QuestStatusEnum status in Enum.GetValues<QuestStatusEnum>())
		{
			profile.Quests = [Quest(status)];
			AssertEx.Equal(status == QuestStatusEnum.Success, progression.HasUnlockedUplink(profile));
			AssertEx.Equal(status == QuestStatusEnum.Success,
				progression.GetPermitForAuthenticatedProfile(profile, new MongoId(SessionId)).Length == 64);
		}
		profile.Quests = [Quest(QuestStatusEnum.Success, "66f51f3a0000000000000b02")];
		AssertEx.False(progression.HasUnlockedUplink(profile));
	}

	[RegressionTest]
	private static void AuthenticatedSnapshotsKeepProfilePermissionsIsolatedAndOmitAnonymousAccess()
	{
		using var rig = new Rig();
		RaidOpsFireSupportServerConfig locked = rig.Snapshot();
		AssertEx.Equal<bool?>(false, locked.UplinkUnlocked);
		AssertEx.Equal(string.Empty, locked.ProgressionPermit);
		rig.SetStatus(QuestStatusEnum.Success);
		RaidOpsFireSupportServerConfig first = rig.Snapshot();
		AssertEx.Equal<bool?>(true, first.UplinkUnlocked);
		AssertEx.Equal(64, first.ProgressionPermit.Length);
		AssertEx.Equal(first.ProgressionPermit, rig.Snapshot().ProgressionPermit);

		PmcData other = Profile(OtherProfileId, OtherSessionId);
		rig.Profiles[OtherSessionId] = other;
		RaidOpsFireSupportServerConfig second = rig.Snapshot(OtherSessionId);
		AssertEx.Equal<bool?>(false, second.UplinkUnlocked);
		AssertEx.Equal(string.Empty, second.ProgressionPermit);
		other.Quests = [Quest(QuestStatusEnum.Success)];
		second = rig.Snapshot(OtherSessionId);
		AssertEx.False(first.ProgressionPermit == second.ProgressionPermit);
		AssertEx.True(rig.Verify(first.ProgressionPermit).Ok);
		AssertEx.True(rig.Verify(second.ProgressionPermit, OtherProfileId).Ok);

		RaidOpsFireSupportServerConfig anonymous = rig.Snapshot("66f51f3a0000000000005999");
		AssertEx.False(anonymous.PlayerStateIncluded);
		AssertEx.Null(anonymous.UplinkUnlocked);
		AssertEx.Equal(string.Empty, anonymous.ProgressionPermit);
	}

	[RegressionTest]
	private static void ForgedAndCrossProfilePermitsNeverCauseCallerSelectedProfileLookups()
	{
		using var rig = new Rig();
		rig.SetStatus(QuestStatusEnum.Success);
		string permit = rig.Snapshot().ProgressionPermit;
		int lookups = rig.LookupCount;
		AssertEx.Equal("ProgressionPermitInvalid", rig.Verify(new string('0', 64)).Reason);
		AssertEx.Equal("ProgressionPermitInvalid", rig.Verify(string.Empty).Reason);
		AssertEx.Equal("ProgressionProfileMismatch", rig.Verify(permit, OtherProfileId).Reason);
		AssertEx.Equal(lookups, rig.LookupCount,
			"Invalid or another player's permit must never select a server profile lookup.");
		AssertEx.True(rig.Verify(permit).Ok);
		AssertEx.Equal(SessionId, rig.LastLookup);
	}

	[RegressionTest]
	private static void QuestResetProfileReplacementAndServerRestartInvalidatePermits()
	{
		using var rig = new Rig();
		rig.SetStatus(QuestStatusEnum.Success);
		string permit = rig.Snapshot().ProgressionPermit;
		rig.SetStatus(QuestStatusEnum.AvailableForFinish);
		AssertEx.Equal("UplinkLocked", rig.Verify(permit).Reason);
		rig.SetStatus(QuestStatusEnum.Success);
		AssertEx.Equal("ProgressionPermitInvalid", rig.Verify(permit).Reason);
		string refreshed = rig.Snapshot().ProgressionPermit;
		AssertEx.False(permit == refreshed);
		AssertEx.True(rig.Verify(refreshed).Ok);
		var restarted = new TscPilotProgressionService(rig.ProfileHelper, PilotPolicyTestFixture.Create(true));
		AssertEx.Equal("ProgressionPermitInvalid", restarted.Verify(Request(refreshed)).Reason);
		rig.Profiles[SessionId] = Profile(OtherProfileId, SessionId);
		AssertEx.Equal("ProfileNotFound", rig.Verify(refreshed).Reason);
	}

	[RegressionTest]
	private static void ClientSuppliedSnapshotHintsCannotMintAnotherPlayersPermit()
	{
		using var rig = new Rig();
		PmcData other = Profile(OtherProfileId, OtherSessionId);
		other.Quests = [Quest(QuestStatusEnum.Success)];
		rig.Profiles[OtherSessionId] = other;
		RaidOpsFireSupportServerConfig result = Decode(rig.Service.GetSnapshot(new MongoId(SessionId),
			new FireSupportPurchaseRequest { ProfileId = OtherProfileId, SessionId = OtherSessionId }));
		AssertEx.False(result.PlayerStateIncluded);
		AssertEx.Null(result.UplinkUnlocked);
		AssertEx.Equal(string.Empty, result.ProgressionPermit);
	}

	[RegressionTest]
	private static async Task LockedAndReadyToFinishProfilesCannotBuyAnyServiceEvenWithBorrowedUplink()
	{
		foreach (QuestStatusEnum status in new[] { QuestStatusEnum.Started, QuestStatusEnum.AvailableForFinish })
		foreach (ESupportType type in Enum.GetValues<ESupportType>().Where(type => type != ESupportType.None))
		foreach (bool persistent in new[] { false, true })
		{
			using var rig = new Rig();
			rig.SetStatus(status);
			rig.Profile.Inventory!.Items!.Add(new Item { Id = "66f51f3a0000000000005402",
				Template = "66f51f3a0000000000000a01", ParentId = StashId, SlotId = "hideout" });
			string ledgerBefore = rig.LedgerText;
			FireSupportPurchaseResponse denied = await rig.Purchase("locked", type, persistent);
			AssertEx.False(denied.Ok);
			AssertEx.Equal("UplinkLocked", denied.Reason);
			AssertEx.False(denied.AuthorizationGranted);
			AssertEx.Equal(0, denied.ChargedFromStash);
			AssertEx.Equal(InitialBalance, rig.Balance);
			AssertEx.Equal(0, rig.SaveCount);
			AssertEx.Equal(ledgerBefore, rig.LedgerText);
		}
	}

	[RegressionTest]
	private static async Task CompletedIntroductionRetainsGlobalServiceDisableAndNormalPricing()
	{
		using var rig = new Rig();
		rig.SetStatus(QuestStatusEnum.Success);
		RaidOpsFireSupportServerConfig config = rig.Service.GetConfigSnapshot();
		config.Enabled["Uav"] = false;
		AssertEx.True(rig.Service.TryUpdateConfig(config, out string error, out _, config.Revision), error);
		AssertEx.Equal("ServiceUnavailable", (await rig.Purchase("disabled")).Reason);
		FireSupportPurchaseResponse purchased = await rig.Purchase("enabled", ESupportType.Strafe);
		AssertEx.True(purchased.Ok, purchased.Reason);
		AssertEx.Equal(Price, purchased.ChargedFromStash);
		AssertEx.Equal(InitialBalance - Price, rig.Balance);
	}

	[RegressionTest]
	private static async Task LockingProgressionPreservesCreditsAndAllowsExistingRefundCommitAndPurchaseReplay()
	{
		using var rig = new Rig();
		rig.SetStatus(QuestStatusEnum.Success);
		AssertEx.True((await rig.Purchase("purchase")).Ok);
		rig.SetStatus(QuestStatusEnum.Started);
		string ledgerBefore = rig.LedgerText;
		FireSupportPurchaseResponse denied = rig.Service.TryConsumeAuthorization(new MongoId(SessionId), rig.Mutation("denied"));
		AssertEx.Equal("UplinkLocked", denied.Reason);
		AssertEx.False(denied.AuthorizationConsumed);
		AssertEx.Equal(ledgerBefore, rig.LedgerText);
		AssertEx.True((await rig.Purchase("purchase")).Ok, "Accepted checkout replay must still settle.");
		AssertEx.Equal(1, rig.SaveCount);

		rig.SetStatus(QuestStatusEnum.Success);
		AssertEx.True(rig.Service.TryConsumeAuthorization(new MongoId(SessionId), rig.Mutation("refund")).Ok);
		rig.SetStatus(QuestStatusEnum.Started);
		AssertEx.True(rig.Service.TryRefundAuthorization(new MongoId(SessionId), rig.Mutation("refund")).Ok);
		AssertEx.Equal(1, rig.Ledger.GetCredits(ProfileId, 180, 2)["Uav"]);
		rig.SetStatus(QuestStatusEnum.Success);
		AssertEx.True(rig.Service.TryConsumeAuthorization(new MongoId(SessionId), rig.Mutation("commit")).Ok);
		rig.SetStatus(QuestStatusEnum.Started);
		AssertEx.True(rig.Service.TryCommitAuthorization(new MongoId(SessionId), rig.Mutation("commit")).Ok);
		AssertEx.Equal(0, rig.Ledger.GetCredits(ProfileId, 180, 2)["Uav"]);
	}

	[RegressionTest]
	private static async Task PreparedCheckoutRecoveryStillSettlesAfterProgressionBecomesLocked()
	{
		foreach (bool alreadyDebited in new[] { false, true })
		{
			using var rig = new Rig();
			string before = rig.Fingerprint();
			rig.Balance = InitialBalance - Price;
			string after = rig.Fingerprint();
			rig.Balance = alreadyDebited ? InitialBalance - Price : InitialBalance;
			AssertEx.True(rig.Ledger.TryPreparePersistentPurchase(ProfileId, ESupportType.Uav,
				1, Price, "RUB", InitialBalance, before, after, 2, "prepared", out _, out _, out string reason), reason);
			FireSupportPurchaseResponse recovered = await rig.Purchase("prepared");
			AssertEx.True(recovered.Ok, recovered.Reason);
			AssertEx.Equal(InitialBalance - Price, rig.Balance);
			AssertEx.Equal(1, rig.Ledger.GetCredits(ProfileId, 180, 2)["Uav"]);
			AssertEx.Equal(alreadyDebited ? 0 : 1, rig.SaveCount);
		}
	}

	[RegressionTest]
	private static void AdministratorConfigCannotFabricateOrPersistPlayerProgression()
	{
		using var rig = new Rig();
		RaidOpsFireSupportServerConfig config = rig.Service.GetConfigSnapshot();
		config.UplinkUnlocked = true;
		config.ProgressionPermit = new string('a', 64);
		AssertEx.True(rig.Service.TryUpdateConfig(config, out string error, out _, config.Revision), error);
		RaidOpsFireSupportServerConfig saved = Decode(JsonSerializer.Deserialize<JsonElement>(
			File.ReadAllText(Path.Combine(rig.Root, "config", "tsc-config.json"))));
		AssertEx.Null(saved.UplinkUnlocked);
		AssertEx.Equal(string.Empty, saved.ProgressionPermit);
		AssertEx.Equal<bool?>(false, rig.Snapshot().UplinkUnlocked);
		AssertEx.Equal(string.Empty, rig.Snapshot().ProgressionPermit);
	}

	private static QuestStatus Quest(QuestStatusEnum status, string questId = TscPilotProgressionService.FinalQuestId) =>
		new() { QId = new MongoId(questId), StartTime = 0, Status = status, StatusTimers = [] };

	private static PmcData Profile(string profileId = ProfileId, string sessionId = SessionId) => new()
	{
		Id = new MongoId(profileId), SessionId = new MongoId(sessionId),
		Inventory = new BotBaseInventory { Stash = new MongoId(StashId), Items =
			[new Item { Id = "66f51f3a0000000000005401", Template = PaymentCurrencyInfo.RoubleTemplateId,
				ParentId = StashId, SlotId = "hideout", Upd = new Upd { StackObjectsCount = InitialBalance } }] }
	};

	private static FireSupportProgressionVerifyRequest Request(string permit, string profileId = ProfileId) =>
		new() { Permit = permit, RequesterProfileId = profileId };

	private static RaidOpsFireSupportServerConfig Decode(object payload) => AssertEx.NotNull(
		JsonSerializer.Deserialize<RaidOpsFireSupportServerConfig>(JsonSerializer.Serialize(payload),
			new JsonSerializerOptions { PropertyNameCaseInsensitive = true }));

	private sealed class Rig : IDisposable
	{
		public string Root { get; } = Path.Combine(Path.GetTempPath(), $"tsc-progression-{Guid.NewGuid():N}");
		public PmcData Profile { get; } = TscPilotProgressionTests.Profile();
		public Dictionary<string, PmcData> Profiles { get; } = new(StringComparer.OrdinalIgnoreCase);
		public ProfileHelper ProfileHelper { get; }
		public TscPilotProgressionService Progression { get; }
		public FireSupportServerConfigService Service { get; }
		public FireSupportAuthorizationLedger Ledger { get; }
		public int SaveCount { get; private set; }
		public int LookupCount { get; private set; }
		public string LastLookup { get; private set; } = string.Empty;
		public string LedgerText => File.ReadAllText(Path.Combine(Root, "storage", "tsc-ledger.json"));
		public int Balance
		{
			get => (int)Profile.Inventory!.Items![0].Upd!.StackObjectsCount!;
			set => Profile.Inventory!.Items![0].Upd!.StackObjectsCount = value;
		}

		public Rig(bool questlineRequired = true)
		{
			Profiles[SessionId] = Profile;
			ProfileHelper = new ProfileHelper { ResolvePmcProfile = id =>
			{
				LookupCount++;
				LastLookup = id.ToString();
				return Profiles.GetValueOrDefault(id.ToString()) ?? Profiles.Values.FirstOrDefault(profile =>
					string.Equals(profile.Id?.ToString(), id.ToString(), StringComparison.OrdinalIgnoreCase));
			} };
			Progression = new TscPilotProgressionService(ProfileHelper, PilotPolicyTestFixture.Create(questlineRequired));
			Ledger = new FireSupportAuthorizationLedger(new SilentLogger<FireSupportAuthorizationLedger>());
			Service = new FireSupportServerConfigService(new SilentLogger<FireSupportServerConfigService>(),
				ProfileHelper, new SaveServer { SaveProfile = _ => { SaveCount++; return Task.CompletedTask; } },
				Ledger, new FireSupportProfileMutationGate(), Progression, new JsonCloner());
			Service.Initialize(Root);
			RaidOpsFireSupportServerConfig config = Service.GetConfigSnapshot();
			config.PaymentSource = nameof(PaymentSource.StashRoubles);
			config.PaymentCurrency = "RUB";
			foreach (string key in config.Prices.Keys) config.Prices[key] = Price;
			AssertEx.True(Service.TryUpdateConfig(config, out string error, out _, config.Revision), error);
		}

		public void SetStatus(QuestStatusEnum status) => Profile.Quests = [Quest(status)];
		public RaidOpsFireSupportServerConfig Snapshot(string sessionId = SessionId) => Decode(Service.GetSnapshot(new MongoId(sessionId)));
		public FireSupportProgressionVerifyResponse Verify(string permit, string profileId = ProfileId) => Progression.Verify(Request(permit, profileId));
		public FireSupportPurchaseRequest Mutation(string requestId) => new()
		{
			SessionId = SessionId, ProfileId = ProfileId, SupportType = nameof(ESupportType.Uav), RequestId = requestId
		};
		public Task<FireSupportPurchaseResponse> Purchase(string requestId, ESupportType type = ESupportType.Uav, bool persistent = true) =>
			Service.TryPurchaseAsync(new MongoId(SessionId), new FireSupportPurchaseRequest
			{
				Action = persistent ? "BuyPersistentAuthorization" : "BuyAuthorization", SessionId = SessionId,
				ProfileId = ProfileId, SupportType = type.ToString(), RequestId = requestId,
				ExpectedCost = Price, ExpectedCurrency = "RUB", Quantity = 1
			});

		public string Fingerprint() => (string)typeof(FireSupportServerConfigService).GetMethod(
			"ComputeCurrencyInventoryFingerprint", BindingFlags.Static | BindingFlags.NonPublic,
			[typeof(PmcData), typeof(string)])!.Invoke(null, [Profile, PaymentCurrencyInfo.RoubleTemplateId])!;

		public void Dispose()
		{
			if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
		}
	}

	private sealed class JsonCloner : ICloner
	{
		public T? Clone<T>(T? value) => value == null ? default : JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value));
	}

	private sealed class SilentLogger<T> : ISptLogger<T>
	{
		public void Warning(string message) { }
		public void Error(string message) { }
		public void Error(string message, Exception exception) { }
	}
}
