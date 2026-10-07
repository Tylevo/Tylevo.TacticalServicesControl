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

internal static class PurchaseHistoryTests
{
	private const string SessionId = "66f51f3a0000000000007101";
	private const string ProfileId = "66f51f3a0000000000007201";
	private const string OtherProfileId = "66f51f3a0000000000007202";
	private const string StashId = "66f51f3a0000000000007301";
	private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

	[RegressionTest]
	private static async Task ActualCompletedPurchasesAppearOnceAfterReplayWithTheirOriginalPrice()
	{
		using var rig = new Rig();
		AssertEx.Equal(0, AssertEx.NotNull((await rig.Snapshot()).PurchaseHistory).Entries.Count);
		AssertEx.True((await rig.Purchase("first")).Ok);
		AssertEx.True((await rig.Purchase("second")).Ok);
		AssertEx.Equal("AlreadyAccepted", (await rig.Purchase("first")).Reason);
		FireSupportPurchaseHistory history = AssertEx.NotNull((await rig.Snapshot()).PurchaseHistory);
		AssertEx.Equal(ProfileId, history.ProfileId);
		AssertEx.Equal(2, history.Entries.Count, "The transaction and durable receipt must not appear twice.");
		AssertEx.True(history.IsValidFor(ProfileId));
		AssertEx.True(history.Entries.All(entry => entry.Service == "A10" && entry.Price == 100 && entry.Currency == "RUB" && entry.Quantity == 1));
		AssertEx.True(history.Entries[0].PurchasedUtc >= history.Entries[1].PurchasedUtc);
		AssertEx.Equal(2, rig.SaveCount, "Reading history and replaying an accepted request must not charge again.");
		RaidOpsFireSupportServerConfig config = rig.Service.GetConfigSnapshot();
		config.Prices["A10"] = 999;
		AssertEx.True(rig.Service.TryUpdateConfig(config, out string error, out _, config.Revision), error);
		AssertEx.True(AssertEx.NotNull((await rig.Snapshot()).PurchaseHistory).Entries.All(entry => entry.Price == 100));
	}

	[RegressionTest]
	private static async Task HistoryRequiresOptInAndAuthenticatedMatchingProfile()
	{
		using var rig = new Rig();
		AssertEx.True((await rig.Purchase("own-receipt")).Ok);
		AssertEx.Null(Decode(await rig.Service.GetSnapshotAsync(new MongoId(SessionId))).PurchaseHistory);
		foreach ((MongoId session, FireSupportPurchaseRequest? request) in new[]
		{
			(default(MongoId), (FireSupportPurchaseRequest?)null),
			(new MongoId(OtherProfileId), (FireSupportPurchaseRequest?)null),
			(new MongoId(SessionId), new FireSupportPurchaseRequest { ProfileId = OtherProfileId }),
			(new MongoId(SessionId), new FireSupportPurchaseRequest { SessionId = OtherProfileId, ProfileId = ProfileId })
		})
		{
			RaidOpsFireSupportServerConfig denied = Decode(await rig.Service.GetSnapshotAsync(session, request, includePurchaseHistory: true));
			AssertEx.False(denied.PlayerStateIncluded);
			AssertEx.Null(denied.PurchaseHistory);
		}
		AssertEx.Equal(1, AssertEx.NotNull((await rig.Snapshot()).PurchaseHistory).Entries.Count);
	}

	[RegressionTest]
	private static async Task HistoryWaitsForInFlightSaveAndDoesNotPublishRolledBackPurchase()
	{
		using var rig = new Rig();
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		rig.OnSave = async () =>
		{
			if (rig.SaveCount != 1) return;
			entered.TrySetResult();
			await release.Task;
			throw new IOException("Simulated failed profile save.");
		};
		Task<FireSupportPurchaseResponse> purchase = rig.Purchase("failed-save");
		Task<RaidOpsFireSupportServerConfig>? snapshot = null;
		try
		{
			await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
			snapshot = rig.Snapshot();
			AssertEx.False(snapshot.IsCompleted, "A receipt must not be shown while its payment can still roll back.");
		}
		finally { release.TrySetResult(); }
		AssertEx.False((await purchase.WaitAsync(TimeSpan.FromSeconds(2))).Ok);
		FireSupportPurchaseHistory history = AssertEx.NotNull((await AssertEx.NotNull(snapshot).WaitAsync(TimeSpan.FromSeconds(2))).PurchaseHistory);
		AssertEx.Equal(0, history.Entries.Count);
	}

	[RegressionTest]
	private static void LedgerHistoryFiltersUnsettledRecordsAndMergesRetainedLegacyReceiptsWithoutWriting()
	{
		using var storage = new TemporaryDirectory();
		DateTimeOffset time = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
		FireSupportPersistentPurchaseRecord accepted = Receipt("accepted", time);
		var prepared = Receipt("prepared", time.AddHours(3));
		prepared.State = "Prepared";
		var invalid = Receipt("invalid", time.AddHours(4));
		invalid.Currency = "GBP";
		var profile = new FireSupportPlayerAuthorizations
		{
			PersistentPurchases = new() { ["accepted"] = accepted, ["prepared"] = prepared, ["invalid"] = invalid },
			Transactions =
			[
				Transaction("duplicate", "Purchase", "accepted", time),
				Transaction("unsettled", "Purchase", "prepared", time.AddHours(3)),
				Transaction("legacy", "Purchase", "", time.AddHours(1)),
				Transaction("refund", "Refund", "refund", time.AddHours(2)),
				Transaction("use", "Consume", "use", time.AddHours(2))
			]
		};
		FireSupportAuthorizationLedger ledger = SeedLedger(storage.Path, profile);
		string path = Path.Combine(storage.Path, "tsc-ledger.json");
		string before = File.ReadAllText(path);
		FireSupportPurchaseHistory history = ledger.GetPurchaseHistory(ProfileId);
		AssertEx.Equal(2, history.Entries.Count);
		AssertEx.Equal(time.AddHours(1), history.Entries[0].PurchasedUtc);
		AssertEx.Equal(time, history.Entries[1].PurchasedUtc);
		AssertEx.False(history.HasMore);
		AssertEx.Equal(0, ledger.GetPurchaseHistory(OtherProfileId).Entries.Count);
		history.Entries[0].Price = 777;
		history.Entries.Clear();
		AssertEx.True(ledger.GetPurchaseHistory(ProfileId).Entries.All(entry => entry.Price == 100));
		AssertEx.Equal(before, File.ReadAllText(path), "A read must not persist or edit the ledger.");
	}

	[RegressionTest]
	private static void DurableHistorySurvivesPrunedTransactionsAndIsBoundedNewestFirst()
	{
		using var storage = new TemporaryDirectory();
		DateTimeOffset time = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
		var profile = new FireSupportPlayerAuthorizations();
		for (int index = 0; index < FireSupportPurchaseHistory.MaxEntries + 3; index++)
			profile.PersistentPurchases.Add("receipt-" + index, Receipt("receipt-" + index, time.AddMinutes(index)));
		FireSupportAuthorizationLedger ledger = SeedLedger(storage.Path, profile);
		FireSupportPurchaseHistory history = ledger.GetPurchaseHistory(ProfileId);
		AssertEx.Equal(FireSupportPurchaseHistory.MaxEntries, history.Entries.Count);
		AssertEx.True(history.HasMore);
		AssertEx.Equal(time.AddMinutes(52), history.Entries[0].PurchasedUtc);
		AssertEx.Equal(time.AddMinutes(3), history.Entries[^1].PurchasedUtc);
		AssertEx.True(history.IsValidFor(ProfileId));
	}

	[RegressionTest]
	private static void HistoryCannotBeInjectedThroughSharedAdministratorConfiguration()
	{
		using var rig = new Rig();
		RaidOpsFireSupportServerConfig edited = rig.Service.GetConfigSnapshot();
		edited.PurchaseHistory = new FireSupportPurchaseHistory { ProfileId = OtherProfileId };
		AssertEx.True(rig.Service.TryUpdateConfig(edited, out string error, out _, edited.Revision), error);
		AssertEx.Null(rig.Service.GetConfigSnapshot().PurchaseHistory);
		RaidOpsFireSupportServerConfig disk = AssertEx.NotNull(JsonSerializer.Deserialize<RaidOpsFireSupportServerConfig>(
			File.ReadAllText(Path.Combine(rig.Root, "config", "tsc-config.json")), Json));
		AssertEx.Null(disk.PurchaseHistory);
	}

	[RegressionTest]
	private static void ClientContractRejectsCrossProfileOrMalformedReceipts()
	{
		var history = new FireSupportPurchaseHistory { ProfileId = ProfileId };
		AssertEx.True(history.IsValidFor(ProfileId));
		AssertEx.False(history.IsValidFor(OtherProfileId));
		var entry = new FireSupportPurchaseHistoryEntry { Service = "Uav", Quantity = 1, Price = 0, Currency = "EUR", PurchasedUtc = DateTimeOffset.UtcNow };
		history.Entries.Add(entry);
		AssertEx.True(history.IsValidFor(ProfileId), "A completed free authorization is still a valid receipt.");
		entry.Price = -1;
		AssertEx.False(history.IsValidFor(ProfileId));
		entry.Price = 0;
		entry.Service = "Unknown";
		AssertEx.False(history.IsValidFor(ProfileId));
		entry.Service = "Uav";
		entry.PurchasedUtc = default;
		AssertEx.False(history.IsValidFor(ProfileId));
		entry.PurchasedUtc = DateTimeOffset.UtcNow;
		entry.Currency = "GBP";
		AssertEx.False(history.IsValidFor(ProfileId));
		entry.Currency = "EUR";
		history.Entries = Enumerable.Repeat(entry, FireSupportPurchaseHistory.MaxEntries + 1).ToList();
		AssertEx.False(history.IsValidFor(ProfileId));
	}

	private static FireSupportPersistentPurchaseRecord Receipt(string id, DateTimeOffset time) => new()
	{
		RequestId = id, RequestIdentity = "BuyPersistentAuthorization", State = "Accepted", Service = "A10",
		Quantity = 1, Price = 100, Currency = "RUB", CreatedUtc = time, AcceptedUtc = time
	};
	private static FireSupportAuthorizationTransaction Transaction(string id, string type, string requestId, DateTimeOffset time) => new()
	{
		Id = id, Type = type, RequestId = requestId, Service = "A10", Quantity = 1, Price = 100, Currency = "RUB", CreatedUtc = time
	};
	private static FireSupportAuthorizationLedger SeedLedger(string root, FireSupportPlayerAuthorizations profile)
	{
		File.WriteAllText(Path.Combine(root, "tsc-ledger.json"), JsonSerializer.Serialize(new FireSupportAuthorizationLedgerState
		{
			Profiles = new() { [ProfileId] = profile, [OtherProfileId] = new FireSupportPlayerAuthorizations() }
		}));
		var ledger = new FireSupportAuthorizationLedger(new SilentLogger<FireSupportAuthorizationLedger>());
		ledger.Initialize(root);
		return ledger;
	}
	private static RaidOpsFireSupportServerConfig Decode(object payload) => AssertEx.NotNull(
		JsonSerializer.Deserialize<RaidOpsFireSupportServerConfig>(JsonSerializer.Serialize(payload), Json));

	private sealed class Rig : IDisposable
	{
		public string Root { get; } = Path.Combine(Path.GetTempPath(), $"tsc-history-{Guid.NewGuid():N}");
		public FireSupportServerConfigService Service { get; }
		public int SaveCount { get; private set; }
		public Func<Task>? OnSave { get; set; }
		public Rig()
		{
			var profile = new PmcData
			{
				Id = new MongoId(ProfileId), SessionId = new MongoId(SessionId),
				Inventory = new BotBaseInventory
				{
					Stash = new MongoId(StashId), Items = [new Item
					{
						Id = "66f51f3a0000000000007401", Template = PaymentCurrencyInfo.RoubleTemplateId,
						ParentId = StashId, SlotId = "hideout", Upd = new Upd { StackObjectsCount = 1000 }
					}]
				},
				Quests = [new QuestStatus { QId = new MongoId(TscPilotProgressionService.FinalQuestId),
					StartTime = 0, Status = QuestStatusEnum.Success, StatusTimers = [] }]
			};
			var helper = new ProfileHelper { ResolvePmcProfile = id => id.ToString() is SessionId or ProfileId ? profile : null };
			Service = new FireSupportServerConfigService(new SilentLogger<FireSupportServerConfigService>(), helper,
				new SaveServer { SaveProfile = async _ => { SaveCount++; if (OnSave != null) await OnSave(); } },
				new FireSupportAuthorizationLedger(new SilentLogger<FireSupportAuthorizationLedger>()),
				new FireSupportProfileMutationGate(), new TscPilotProgressionService(helper, PilotPolicyTestFixture.Create()), new JsonCloner());
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
			Decode(await Service.GetSnapshotAsync(new MongoId(SessionId), includePurchaseHistory: true));
		public Task<FireSupportPurchaseResponse> Purchase(string requestId) => Service.TryPurchaseAsync(new MongoId(SessionId), new FireSupportPurchaseRequest
		{
			Action = "BuyPersistentAuthorization", SessionId = SessionId, ProfileId = ProfileId,
			SupportType = nameof(ESupportType.Strafe), RequestId = requestId, ExpectedCost = 100, ExpectedCurrency = "RUB", Quantity = 1
		});
		public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }
	}
	private sealed class JsonCloner : ICloner
	{
		public T? Clone<T>(T? value) => value == null ? default : JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value));
	}
	private sealed class TemporaryDirectory : IDisposable
	{
		public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"tsc-history-ledger-{Guid.NewGuid():N}");
		public TemporaryDirectory() => Directory.CreateDirectory(Path);
		public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
	}
	private sealed class SilentLogger<T> : ISptLogger<T>
	{
		public void Warning(string message) { }
		public void Error(string message) { }
		public void Error(string message, Exception exception) { }
	}
}
