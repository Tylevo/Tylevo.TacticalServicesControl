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
using System.Diagnostics;
using System.Text.Json;

internal static class PersistentPurchaseHandlerTests
{
	private const string SessionId = "66f51f3a0000000000002101";
	private const string ProfileId = "66f51f3a0000000000002201";
	private const string StashId = "66f51f3a0000000000002301";
	private const int InitialBalance = 10_000;
	private const int Price = 100;
	private static readonly (ESupportType Type, string Key)[] Services =
	[
		(ESupportType.Strafe, "A10"),
		(ESupportType.DoubleStrafe, "DoublePass"),
		(ESupportType.Extract, "Extraction"),
		(ESupportType.PriorityExfil, "PriorityExfil"),
		(ESupportType.Uav, "Uav"),
		(ESupportType.FocusedSweep, "FocusedSweep")
	];

	[RegressionTest]
	private static async Task ImmediateDistinctPersistentPurchasesFillTwoSlotsThenRejectTheThirdForEveryService()
	{
		foreach ((ESupportType type, string key) in Services)
		{
			using var rig = new TestRig();
			AssertEx.Equal(0, rig.Credits(key));
			FireSupportPurchaseResponse first = await rig.Purchase(type, "first");
			AssertAccepted(first, key, 1, InitialBalance - Price);

			// No sleep: this is the next distinct checkout immediately after the
			// first completed. The legacy two-second throttle rejected it at 1/2.
			Stopwatch elapsed = Stopwatch.StartNew();
			FireSupportPurchaseResponse second = await rig.Purchase(type, "second");
			AssertEx.True(second.Ok,
				$"{type}: second checkout after {elapsed.Elapsed.TotalMilliseconds:0.0} ms was rejected: {second.Reason}");
			AssertAccepted(second, key, 2, InitialBalance - 2 * Price);
			AssertEx.Equal(2, rig.SaveCount);
			AssertEx.Equal(2, rig.Credits(key));
			AssertEx.Equal(InitialBalance - 2 * Price, rig.Balance);
			RaidOpsFireSupportServerConfig snapshot = rig.AuthenticatedSnapshot();
			AssertEx.True(snapshot.PlayerStateIncluded);
			AssertEx.Equal(2, snapshot.Authorizations[key],
				"Refreshing the store must return both purchased authorizations.");
			AssertEx.Equal<int?>(InitialBalance - 2 * Price, snapshot.StashCurrencyBalance);

			FireSupportPurchaseResponse third = await rig.Purchase(type, "third");
			AssertEx.False(third.Ok);
			AssertEx.Equal("AuthorizationLimitReached", third.Reason);
			AssertEx.False(third.AuthorizationGranted);
			AssertEx.Equal(0, third.ChargedFromStash);
			AssertEx.True(third.AuthorizationsIncluded);
			AssertEx.Equal(2, third.Authorizations[key]);
			AssertEx.Equal(2, rig.Credits(key));
			AssertEx.Equal(InitialBalance - 2 * Price, rig.Balance);
			AssertEx.Equal(2, rig.SaveCount, "The capped checkout must neither charge nor save the profile again.");
		}
	}

	[RegressionTest]
	private static async Task ExactPersistentRequestReplayReturnsTheAcceptedCreditWithoutChargingAgain()
	{
		foreach ((ESupportType type, string key) in Services)
		{
			using var rig = new TestRig();
			FireSupportPurchaseResponse first = await rig.Purchase(type, "same-request");
			AssertAccepted(first, key, 1, InitialBalance - Price);
			FireSupportPurchaseResponse replay = await rig.Purchase(type, "same-request");
			AssertEx.True(replay.Ok, replay.Reason);
			AssertEx.Equal("AlreadyAccepted", replay.Reason);
			AssertEx.Equal("same-request", replay.RequestId);
			AssertEx.True(replay.AuthorizationGranted);
			AssertEx.True(replay.AuthorizationsIncluded);
			AssertEx.Equal(1, replay.Authorizations[key]);
			AssertEx.Equal(0, replay.ChargedFromStash);
			AssertEx.Equal(InitialBalance - Price, replay.NewBalance);
			AssertEx.Equal(InitialBalance - Price, rig.Balance);
			AssertEx.Equal(1, rig.Credits(key));
			AssertEx.Equal(1, rig.SaveCount);
		}
	}

	[RegressionTest]
	private static async Task ConcurrentCopiesOfOnePersistentRequestWaitForTheOriginalSaveAndGrantOnce()
	{
		foreach ((ESupportType type, string key) in Services)
		{
			using var rig = new TestRig();
			var saveEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			var releaseSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			rig.OnSave = async () =>
			{
				saveEntered.TrySetResult();
				await releaseSave.Task;
			};
			Task<FireSupportPurchaseResponse> first = rig.Purchase(type, "concurrent-request");
			Task<FireSupportPurchaseResponse>? duplicate = null;
			try
			{
				await saveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
				duplicate = rig.Purchase(type, "concurrent-request");
				AssertEx.False(duplicate.IsCompleted,
					"The second request must wait for the shared profile mutation gate while the original save is in progress.");
				AssertEx.Equal(1, rig.SaveCount);
				AssertEx.Equal(InitialBalance - Price, rig.Balance);
			}
			finally
			{
				releaseSave.TrySetResult();
				await first.WaitAsync(TimeSpan.FromSeconds(5));
				if (duplicate != null) await duplicate.WaitAsync(TimeSpan.FromSeconds(5));
			}

			AssertAccepted(await first, key, 1, InitialBalance - Price);
			FireSupportPurchaseResponse replay = await AssertEx.NotNull(duplicate);
			AssertEx.True(replay.Ok, replay.Reason);
			AssertEx.Equal("AlreadyAccepted", replay.Reason);
			AssertEx.Equal(0, replay.ChargedFromStash);
			AssertEx.Equal(1, replay.Authorizations[key]);
			AssertEx.Equal(InitialBalance - Price, rig.Balance);
			AssertEx.Equal(1, rig.Credits(key));
			AssertEx.Equal(1, rig.SaveCount);
		}
	}

	[RegressionTest]
	private static async Task LegacyAuthorizationPurchasesStillRejectAnImmediateSecondRequest()
	{
		foreach ((ESupportType type, string key) in Services)
		{
			using var rig = new TestRig();
			FireSupportPurchaseResponse first = await rig.Purchase(type, "", persistent: false);
			AssertAccepted(first, key, 1, InitialBalance - Price);
			FireSupportPurchaseResponse second = await rig.Purchase(type, "", persistent: false);
			AssertEx.False(second.Ok);
			AssertEx.Equal("RateLimited", second.Reason);
			AssertEx.Equal(0, second.ChargedFromStash);
			AssertEx.False(second.AuthorizationGranted);
			AssertEx.Equal(InitialBalance - Price, rig.Balance);
			AssertEx.Equal(1, rig.Credits(key));
			AssertEx.Equal(1, rig.SaveCount);
		}
	}

	private static void AssertAccepted(FireSupportPurchaseResponse response, string key, int credits, int balance)
	{
		AssertEx.True(response.Ok, response.Reason);
		AssertEx.Equal("Accepted", response.Reason);
		AssertEx.True(response.AuthorizationGranted);
		AssertEx.True(response.AuthorizationsIncluded);
		AssertEx.Equal(credits, response.Authorizations[key]);
		AssertEx.Equal(Price, response.Cost);
		AssertEx.Equal(Price, response.ChargedFromStash);
		AssertEx.Equal(balance, response.NewBalance);
	}

	private sealed class TestRig : IDisposable
	{
		private readonly string _root = Path.Combine(Path.GetTempPath(), $"tsc-purchase-handler-{Guid.NewGuid():N}");
		private readonly PmcData _profile;
		private readonly FireSupportAuthorizationLedger _ledger;
		public FireSupportServerConfigService Service { get; }
		public int SaveCount { get; private set; }
		public Func<Task>? OnSave { get; set; }
		public int Balance => (int)(_profile.Inventory!.Items!.Single(item => item.Template == PaymentCurrencyInfo.RoubleTemplateId).Upd!.StackObjectsCount ?? 1d);

		public TestRig()
		{
			_profile = new PmcData
			{
				Id = new MongoId(ProfileId),
				SessionId = new MongoId(SessionId),
				Quests = [new QuestStatus { QId = new MongoId(TscPilotProgressionService.FinalQuestId),
					StartTime = 0, Status = QuestStatusEnum.Success, StatusTimers = [] }],
				Inventory = new BotBaseInventory
				{
					Stash = new MongoId(StashId),
					Items =
					[
						new Item
						{
							Id = "66f51f3a0000000000002401",
							Template = PaymentCurrencyInfo.RoubleTemplateId,
							ParentId = StashId,
							SlotId = "hideout",
							Upd = new Upd { StackObjectsCount = InitialBalance }
						}
					]
				}
			};
			var profileHelper = new ProfileHelper
			{
				ResolvePmcProfile = id => id.ToString() is SessionId or ProfileId ? _profile : null
			};
			var saveServer = new SaveServer
			{
				SaveProfile = async id =>
				{
					AssertEx.Equal(SessionId, id.ToString());
					SaveCount++;
					if (OnSave != null) await OnSave();
				}
			};
			_ledger = new FireSupportAuthorizationLedger(new SilentLogger<FireSupportAuthorizationLedger>());
			Service = new FireSupportServerConfigService(new SilentLogger<FireSupportServerConfigService>(),
				profileHelper, saveServer, _ledger, new FireSupportProfileMutationGate(),
				new TscPilotProgressionService(profileHelper, PilotPolicyTestFixture.Create()), new JsonCloner());
			Service.Initialize(_root);
			RaidOpsFireSupportServerConfig config = Service.GetConfigSnapshot();
			config.PaymentSource = nameof(PaymentSource.StashRoubles);
			config.PaymentCurrency = nameof(PaymentCurrency.RUB);
			config.PurchasePersistence.Enabled = true;
			config.PurchasePersistence.MaxStoredAuthorizationsPerService = 2;
			foreach ((_, string key) in Services)
			{
				config.Prices[key] = Price;
				config.Enabled[key] = true;
			}
			AssertEx.True(Service.TryUpdateConfig(config, out string error, out bool conflict, config.Revision), error);
			AssertEx.False(conflict);
		}

		public Task<FireSupportPurchaseResponse> Purchase(ESupportType type, string requestId, bool persistent = true) =>
			Service.TryPurchaseAsync(new MongoId(SessionId), new FireSupportPurchaseRequest
			{
				Action = persistent ? "BuyPersistentAuthorization" : "BuyAuthorization",
				SessionId = SessionId,
				ProfileId = ProfileId,
				SupportType = type.ToString(),
				RequestId = requestId,
				ExpectedCost = Price,
				ExpectedCurrency = "RUB",
				Quantity = 1
			});

		public int Credits(string key) => _ledger.GetCredits(ProfileId, 180, 2).GetValueOrDefault(key);

		public RaidOpsFireSupportServerConfig AuthenticatedSnapshot() =>
			AssertEx.NotNull(JsonSerializer.Deserialize<RaidOpsFireSupportServerConfig>(
				JsonSerializer.Serialize(Service.GetSnapshot(new MongoId(SessionId))),
				new JsonSerializerOptions { PropertyNameCaseInsensitive = true }));

		public void Dispose()
		{
			if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
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
