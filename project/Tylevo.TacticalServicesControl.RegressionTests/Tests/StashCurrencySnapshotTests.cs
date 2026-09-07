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
using System.Text.Json;

internal static class StashCurrencySnapshotTests
{
	private const string SessionId = "66f51f3a0000000000003101";
	private const string ProfileId = "66f51f3a0000000000003201";
	private const string OtherProfileId = "66f51f3a0000000000003202";
	private const string StashId = "66f51f3a0000000000003301";
	private const string EquipmentId = "66f51f3a0000000000003302";
	private const string ContainerId = "66f51f3a0000000000003303";
	private const string NestedContainerId = "66f51f3a0000000000003304";
	private const string FirstCashId = "66f51f3a0000000000003401";
	private const string SecondCashId = "66f51f3a0000000000003402";
	private static readonly JsonSerializerOptions ReadJson = new() { PropertyNameCaseInsensitive = true };

	[RegressionTest]
	private static async Task AuthenticatedSnapshotIncludesAllStashCurrenciesAndNativeMetadataButExcludesCarriedCash()
	{
		using var rig = new Rig();
		rig.Items.Add(new Item { Id = ContainerId, Template = ContainerId, ParentId = StashId, SlotId = "hideout" });
		rig.Items.Add(new Item { Id = NestedContainerId, Template = ContainerId, ParentId = ContainerId, SlotId = "main" });
		Item dollars = Cash(SecondCashId, PaymentCurrencyInfo.DollarTemplateId, 400, NestedContainerId);
		dollars.Upd!.SpawnedInSession = true;
		rig.Items.Add(dollars);
		rig.Items.Add(Cash("66f51f3a0000000000003403", PaymentCurrencyInfo.EuroTemplateId, 500, ContainerId));
		rig.Items.Add(Cash("66f51f3a0000000000003404", PaymentCurrencyInfo.RoubleTemplateId, 999, EquipmentId));

		RaidOpsFireSupportServerConfig snapshot = await rig.Snapshot();
		FireSupportStashCurrencyState state = AssertEx.NotNull(snapshot.StashCurrencyState);
		AssertEx.Equal(ProfileId, state.ProfileId);
		AssertEx.Equal(StashId, state.StashId);
		AssertEx.Equal(3, state.Items.Count);
		AssertEx.Equal<int?>(1000, snapshot.StashCurrencyBalance);
		AssertEx.False(state.Items.Any(item => item.ParentId == EquipmentId));
		FireSupportStashCurrencyItem exported = state.Items.Single(item => item.Id == SecondCashId);
		AssertEx.Equal(NestedContainerId, exported.ParentId);
		AssertEx.Equal("hideout", exported.SlotId);
		AssertEx.Equal(400, exported.StackObjectsCount);
		using JsonDocument upd = JsonDocument.Parse(exported.UpdJson);
		AssertEx.Equal(400, upd.RootElement.GetProperty("StackObjectsCount").GetInt32());
		AssertEx.True(upd.RootElement.GetProperty("SpawnedInSession").GetBoolean());
		using JsonDocument location = JsonDocument.Parse(exported.LocationJson);
		AssertEx.Equal(1, location.RootElement.GetProperty("x").GetInt32());
		dollars.Upd.StackObjectsCount = 900;
		dollars.Location = new { x = 8, y = 9, r = 0 };
		AssertEx.Equal(400, exported.StackObjectsCount, "An exported snapshot must not retain live inventory references.");
		AssertEx.Equal(400, upd.RootElement.GetProperty("StackObjectsCount").GetInt32());
		AssertEx.Equal(1, location.RootElement.GetProperty("x").GetInt32());
	}

	[RegressionTest]
	private static async Task MissingNativeStackCountIsExplicitlySerializedAsOneWithoutLosingOtherUpdFields()
	{
		using var rig = new Rig();
		rig.Items[0].Upd = new Upd { StackObjectsCount = null, SpawnedInSession = true };
		FireSupportStashCurrencyItem item = AssertEx.NotNull((await rig.Snapshot()).StashCurrencyState).Items.Single();
		AssertEx.Equal(1, item.StackObjectsCount);
		using JsonDocument upd = JsonDocument.Parse(item.UpdJson);
		AssertEx.Equal(1, upd.RootElement.GetProperty("StackObjectsCount").GetInt32());
		AssertEx.True(upd.RootElement.GetProperty("SpawnedInSession").GetBoolean());
		AssertEx.Null(rig.Items[0].Upd.StackObjectsCount, "Exporting the count must not mutate the live profile.");
		rig.Items[0].Upd = null;
		item = AssertEx.NotNull((await rig.Snapshot()).StashCurrencyState).Items.Single();
		using JsonDocument absentUpd = JsonDocument.Parse(item.UpdJson);
		AssertEx.Equal(1, absentUpd.RootElement.GetProperty("StackObjectsCount").GetInt32());
	}

	[RegressionTest]
	private static async Task DefaultRaidSnapshotKeepsTheAggregateButOmitsNativeCashMetadata()
	{
		using var rig = new Rig();
		RaidOpsFireSupportServerConfig snapshot = Decode(await rig.Service.GetSnapshotAsync(new MongoId(SessionId)));
		AssertEx.True(snapshot.PlayerStateIncluded);
		AssertEx.Equal<int?>(1000, snapshot.StashCurrencyBalance);
		AssertEx.Null(snapshot.StashCurrencyState);
		AssertEx.NotNull((await rig.Snapshot()).StashCurrencyState);
	}

	[RegressionTest]
	private static async Task LargeMenuCashTotalsKeepExactStackCountsAndClampOnlyTheLegacyAggregate()
	{
		using var rig = new Rig();
		rig.Items[0].Upd!.StackObjectsCount = int.MaxValue;
		rig.Items.Add(Cash(SecondCashId, PaymentCurrencyInfo.RoubleTemplateId, int.MaxValue));
		RaidOpsFireSupportServerConfig snapshot = await rig.Snapshot();
		FireSupportStashCurrencyState state = AssertEx.NotNull(snapshot.StashCurrencyState);
		AssertEx.Equal(2, state.Items.Count);
		AssertEx.True(state.Items.All(item => item.StackObjectsCount == int.MaxValue));
		AssertEx.Equal<int?>(int.MaxValue, snapshot.StashCurrencyBalance);
	}

	[RegressionTest]
	private static async Task UnauthenticatedAndMismatchedProfileSnapshotsOmitCashInsteadOfReturningAnEmptyStash()
	{
		using var rig = new Rig();
		foreach ((MongoId session, FireSupportPurchaseRequest? request) in new[]
		{
			(default(MongoId), (FireSupportPurchaseRequest?)null),
			(new MongoId(OtherProfileId), (FireSupportPurchaseRequest?)null),
			(new MongoId(SessionId), new FireSupportPurchaseRequest { ProfileId = OtherProfileId })
		})
		{
			RaidOpsFireSupportServerConfig snapshot = Decode(await rig.Service.GetSnapshotAsync(session, request, includeStashCurrencyState: true));
			AssertEx.False(snapshot.PlayerStateIncluded);
			AssertEx.Null(snapshot.StashCurrencyState);
			AssertEx.Null(snapshot.StashCurrencyBalance);
		}
		AssertEx.NotNull((await rig.Snapshot()).StashCurrencyState);
	}

	[RegressionTest]
	private static async Task AbsoluteSnapshotReflectsDepletedStacksAndLaterPurchasesWhenAnOldRequestIsReplayed()
	{
		using var rig = new Rig();
		rig.Items[0].Upd!.StackObjectsCount = 50;
		rig.Items.Add(Cash(SecondCashId, PaymentCurrencyInfo.RoubleTemplateId, 300));
		AssertEx.True((await rig.Purchase("first")).Ok);
		FireSupportStashCurrencyState first = AssertEx.NotNull((await rig.Snapshot()).StashCurrencyState);
		AssertEx.Equal(1, first.Items.Count);
		AssertEx.Equal(SecondCashId, first.Items[0].Id);
		AssertEx.Equal(250, first.Items[0].StackObjectsCount);
		AssertEx.True((await rig.Purchase("second")).Ok);
		FireSupportPurchaseResponse replay = await rig.Purchase("first");
		AssertEx.True(replay.Ok);
		AssertEx.Equal("AlreadyAccepted", replay.Reason);
		AssertEx.Equal(0, replay.ChargedFromStash);
		RaidOpsFireSupportServerConfig current = await rig.Snapshot();
		FireSupportStashCurrencyState afterReplay = AssertEx.NotNull(current.StashCurrencyState);
		AssertEx.Equal(1, afterReplay.Items.Count);
		AssertEx.Equal(150, afterReplay.Items[0].StackObjectsCount);
		AssertEx.Equal<int?>(150, current.StashCurrencyBalance);
		AssertEx.Equal(2, current.Authorizations["A10"]);
		AssertEx.Equal(250, first.Items[0].StackObjectsCount);
		AssertEx.Equal(2, rig.SaveCount);
	}

	[RegressionTest]
	private static async Task SpendingTheLastStackReturnsAnAuthoritativeEmptyCashSnapshot()
	{
		using var rig = new Rig();
		rig.Items[0].Upd!.StackObjectsCount = 100;
		AssertEx.True((await rig.Purchase("last-cash")).Ok);
		RaidOpsFireSupportServerConfig snapshot = await rig.Snapshot();
		AssertEx.True(snapshot.PlayerStateIncluded);
		AssertEx.Equal(0, AssertEx.NotNull(snapshot.StashCurrencyState).Items.Count);
		AssertEx.Equal<int?>(0, snapshot.StashCurrencyBalance);
	}

	[RegressionTest]
	private static async Task MalformedCashAncestryCannotBecomeAnAuthoritativeEmptySnapshot()
	{
		foreach (string malformed in new[] { "self-cycle", "parent-cycle", "missing-parent", "duplicate-id", "fractional-count" })
		{
			using var rig = new Rig();
			switch (malformed)
			{
				case "self-cycle": rig.Items[0].ParentId = FirstCashId; break;
				case "parent-cycle":
					rig.Items[0].ParentId = ContainerId;
					rig.Items.Add(new Item { Id = ContainerId, Template = ContainerId, ParentId = NestedContainerId });
					rig.Items.Add(new Item { Id = NestedContainerId, Template = ContainerId, ParentId = ContainerId });
					break;
				case "missing-parent": rig.Items[0].ParentId = ContainerId; break;
				case "duplicate-id": rig.Items.Add(Cash(FirstCashId, PaymentCurrencyInfo.RoubleTemplateId, 10)); break;
				case "fractional-count": rig.Items[0].Upd!.StackObjectsCount = 1.5; break;
			}
			RaidOpsFireSupportServerConfig snapshot = await rig.Snapshot().WaitAsync(TimeSpan.FromSeconds(2));
			AssertEx.Null(snapshot.StashCurrencyState, malformed);
			AssertEx.Null(snapshot.StashCurrencyBalance, malformed);
		}
	}

	[RegressionTest]
	private static async Task SnapshotWaitsForThePurchaseSaveAndSeesTheRestoredCashAfterRollback()
	{
		using var rig = new Rig();
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		rig.OnSave = async () =>
		{
			if (rig.SaveCount != 1) return;
			entered.TrySetResult();
			await release.Task;
			throw new IOException("Simulated profile save failure.");
		};
		Task<FireSupportPurchaseResponse> purchase = rig.Purchase("rollback");
		Task<RaidOpsFireSupportServerConfig>? snapshot = null;
		try
		{
			await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
			snapshot = rig.Snapshot();
			AssertEx.False(snapshot.IsCompleted, "Snapshot must wait while debited cash can still be rolled back.");
		}
		finally { release.TrySetResult(); }
		AssertEx.False((await purchase.WaitAsync(TimeSpan.FromSeconds(2))).Ok);
		RaidOpsFireSupportServerConfig restored = await AssertEx.NotNull(snapshot).WaitAsync(TimeSpan.FromSeconds(2));
		AssertEx.Equal<int?>(1000, restored.StashCurrencyBalance);
		AssertEx.Equal(1000, AssertEx.NotNull(restored.StashCurrencyState).Items.Single().StackObjectsCount);
		AssertEx.Equal(0, restored.Authorizations.GetValueOrDefault("A10"));
	}

	[RegressionTest]
	private static void CashSnapshotsAreNotAcceptedAsSharedAdministratorConfiguration()
	{
		using var rig = new Rig();
		RaidOpsFireSupportServerConfig edited = rig.Service.GetConfigSnapshot();
		edited.StashCurrencyState = new FireSupportStashCurrencyState { ProfileId = OtherProfileId, StashId = StashId };
		AssertEx.True(rig.Service.TryUpdateConfig(edited, out string error, out _, edited.Revision), error);
		AssertEx.Null(rig.Service.GetConfigSnapshot().StashCurrencyState);
		RaidOpsFireSupportServerConfig disk = AssertEx.NotNull(JsonSerializer.Deserialize<RaidOpsFireSupportServerConfig>(
			File.ReadAllText(Path.Combine(rig.Root, "config", "tsc-config.json")), ReadJson));
		AssertEx.Null(disk.StashCurrencyState);
	}

	[RegressionTest]
	private static void OversizedOrIncompleteSnapshotsAreOmittedWithoutTruncation()
	{
		using var rig = new Rig();
		for (int index = 0; index < FireSupportStashCurrencyState.MaxItems; index++)
		{
			rig.Items.Add(Cash((index + 1).ToString("x24"), PaymentCurrencyInfo.DollarTemplateId, 1));
		}
		AssertEx.Null(FireSupportStashCurrencySnapshot.Create(rig.Profile));
		rig.Items.RemoveRange(1, rig.Items.Count - 1);
		rig.Items[0].Location = new { payload = new string('x', FireSupportStashCurrencyState.MaxMetadataJsonLength) };
		AssertEx.Null(FireSupportStashCurrencySnapshot.Create(rig.Profile));
		rig.Profile.Inventory!.Items = null;
		AssertEx.Null(FireSupportStashCurrencySnapshot.Create(rig.Profile));
	}

	private static Item Cash(string id, string templateId, int count, string parentId = StashId) => new()
	{
		Id = id, Template = templateId, ParentId = parentId, SlotId = "hideout",
		Location = new { x = 1, y = 2, r = 0 }, Upd = new Upd { StackObjectsCount = count }
	};

	private static RaidOpsFireSupportServerConfig Decode(object payload) => AssertEx.NotNull(
		JsonSerializer.Deserialize<RaidOpsFireSupportServerConfig>(JsonSerializer.Serialize(payload), ReadJson));

	private sealed class Rig : IDisposable
	{
		public string Root { get; } = Path.Combine(Path.GetTempPath(), $"tsc-cash-state-{Guid.NewGuid():N}");
		public PmcData Profile { get; } = new()
		{
			Id = new MongoId(ProfileId), SessionId = new MongoId(SessionId),
			Inventory = new BotBaseInventory
			{
				Stash = new MongoId(StashId), Equipment = new MongoId(EquipmentId),
				Items = [Cash(FirstCashId, PaymentCurrencyInfo.RoubleTemplateId, 1000)]
			}
		};
		public List<Item> Items => Profile.Inventory!.Items!;
		public FireSupportServerConfigService Service { get; }
		public int SaveCount { get; private set; }
		public Func<Task>? OnSave { get; set; }

		public Rig()
		{
			Profile.Quests = [new QuestStatus { QId = new MongoId(TscPilotProgressionService.FinalQuestId),
				StartTime = 0, Status = QuestStatusEnum.Success, StatusTimers = [] }];
			var profileHelper = new ProfileHelper
				{ ResolvePmcProfile = id => id.ToString() is SessionId or ProfileId ? Profile : null };
			Service = new FireSupportServerConfigService(new SilentLogger<FireSupportServerConfigService>(),
				profileHelper,
				new SaveServer { SaveProfile = async _ => { SaveCount++; if (OnSave != null) await OnSave(); } },
				new FireSupportAuthorizationLedger(new SilentLogger<FireSupportAuthorizationLedger>()),
				new FireSupportProfileMutationGate(), new TscPilotProgressionService(profileHelper, PilotPolicyTestFixture.Create()), new JsonCloner());
			Service.Initialize(Root);
			RaidOpsFireSupportServerConfig config = Service.GetConfigSnapshot();
			config.PaymentSource = nameof(PaymentSource.StashRoubles);
			config.PaymentCurrency = "RUB";
			config.Prices["A10"] = 100;
			config.Enabled["A10"] = true;
			config.PurchasePersistence.Enabled = true;
			config.PurchasePersistence.MaxStoredAuthorizationsPerService = 2;
			AssertEx.True(Service.TryUpdateConfig(config, out string error, out _, config.Revision), error);
		}

		public async Task<RaidOpsFireSupportServerConfig> Snapshot() =>
			Decode(await Service.GetSnapshotAsync(new MongoId(SessionId), includeStashCurrencyState: true));

		public Task<FireSupportPurchaseResponse> Purchase(string requestId) =>
			Service.TryPurchaseAsync(new MongoId(SessionId), new FireSupportPurchaseRequest
			{
				Action = "BuyPersistentAuthorization", SessionId = SessionId, ProfileId = ProfileId,
				SupportType = nameof(ESupportType.Strafe), RequestId = requestId,
				ExpectedCost = 100, ExpectedCurrency = "RUB", Quantity = 1
			});

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
