using SamSWAT.FireSupport.ArysReloaded;
using SamSWAT.FireSupport.ArysReloaded.Unity;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils.Cloners;
using System.Text.Json;

internal static class Uh60TransferFeeServiceTests
{
	private const string SessionA = "66f51f3a0000000000001101";
	private const string ProfileA = "66f51f3a0000000000001201";
	private const string SessionB = "66f51f3a0000000000001102";
	private const string ProfileB = "66f51f3a0000000000001202";

	[RegressionTest]
	private static async Task PrepareDebitsOnlyNestedStashRoublesAcrossExactStacks()
	{
		using var rig = new TestRig();
		PmcData profile = CreateProfile(
			SessionA,
			ProfileA,
			("nested-rub", 60),
			("direct-rub", 50),
			("carried-rub", 1_000));
		rig.AddProfile(SessionA, profile);

		FireSupportUh60TransferFeeResponse response =
			await rig.Service.TryHandleAsync(
				new MongoId(SessionA),
				Request("Prepare", ProfileA, "nested-exact", 80));

		AssertEx.True(response.Ok, response.Reason);
		AssertEx.Equal(
			FireSupportUh60TransferFeeJournal.PreparedState,
			response.State);
		AssertEx.Equal(30, response.StashRoubleBalance);
		AssertEx.Null(FindItem(profile, "nested-rub"));
		AssertEx.Equal(30, StackCount(profile, "direct-rub"));
		AssertEx.Equal(1_000, StackCount(profile, "carried-rub"));
		AssertEx.Equal(1, rig.SaveCount);

		AssertEx.True(
			rig.Journal.TryGet(
				"nested-exact",
				out FireSupportUh60TransferFeeRecord? record));
		FireSupportUh60TransferFeeRecord stored =
			AssertEx.NotNull(record);
		AssertEx.Equal(2, stored.Debits.Count);
		AssertEx.Equal(
			80,
			stored.Debits.Sum(debit => debit.AmountRoubles));
	}

	[RegressionTest]
	private static async Task InsufficientStashFundsNeverMutateOrJournal()
	{
		using var rig = new TestRig();
		PmcData profile = CreateProfile(
			SessionA,
			ProfileA,
			("nested-rub", 20),
			("direct-rub", 20),
			("carried-rub", 5_000));
		rig.AddProfile(SessionA, profile);

		FireSupportUh60TransferFeeResponse response =
			await rig.Service.TryHandleAsync(
				new MongoId(SessionA),
				Request("Prepare", ProfileA, "insufficient", 41));

		AssertEx.False(response.Ok);
		AssertEx.Equal("InsufficientRoubles", response.Reason);
		AssertEx.Equal(20, StackCount(profile, "nested-rub"));
		AssertEx.Equal(20, StackCount(profile, "direct-rub"));
		AssertEx.Equal(5_000, StackCount(profile, "carried-rub"));
		AssertEx.Equal(0, rig.SaveCount);
		AssertEx.False(
			rig.Journal.TryGet("insufficient", out _));
	}

	[RegressionTest]
	private static async Task ExactPrepareReplayNeverDebitsOrSavesTwice()
	{
		using var rig = new TestRig();
		PmcData profile = CreateProfile(
			SessionA,
			ProfileA,
			("nested-rub", 100),
			("direct-rub", 100),
			("carried-rub", 500));
		rig.AddProfile(SessionA, profile);
		FireSupportUh60TransferFeeRequest request =
			Request("Prepare", ProfileA, "prepare-replay", 70);

		FireSupportUh60TransferFeeResponse first =
			await rig.Service.TryHandleAsync(
				new MongoId(SessionA),
				request);
		FireSupportUh60TransferFeeResponse replay =
			await rig.Service.TryHandleAsync(
				new MongoId(SessionA),
				request);

		AssertEx.True(first.Ok, first.Reason);
		AssertEx.True(replay.Ok, replay.Reason);
		AssertEx.Equal("AlreadyPrepared", replay.Reason);
		AssertEx.Equal(30, StackCount(profile, "nested-rub"));
		AssertEx.Equal(100, StackCount(profile, "direct-rub"));
		AssertEx.Equal(500, StackCount(profile, "carried-rub"));
		AssertEx.Equal(1, rig.SaveCount);
	}

	[RegressionTest]
	private static async Task NativeFailureRefundRestoresExactlyOnce()
	{
		using var rig = new TestRig();
		PmcData profile = CreateProfile(
			SessionA,
			ProfileA,
			("nested-rub", 60),
			("direct-rub", 50),
			("carried-rub", 900));
		rig.AddProfile(SessionA, profile);
		const string transactionId = "native-failure-refund";

		FireSupportUh60TransferFeeResponse prepared =
			await rig.Service.TryHandleAsync(
				new MongoId(SessionA),
				Request("Prepare", ProfileA, transactionId, 80));
		AssertEx.True(prepared.Ok, prepared.Reason);

		FireSupportUh60TransferFeeResponse refunded =
			await rig.Service.TryHandleAsync(
				new MongoId(SessionA),
				Request("Refund", ProfileA, transactionId, 80));
		FireSupportUh60TransferFeeResponse replay =
			await rig.Service.TryHandleAsync(
				new MongoId(SessionA),
				Request("Refund", ProfileA, transactionId, 80));

		AssertEx.True(refunded.Ok, refunded.Reason);
		AssertEx.Equal(
			FireSupportUh60TransferFeeJournal.RefundedState,
			refunded.State);
		AssertEx.True(replay.Ok, replay.Reason);
		AssertEx.Equal("AlreadyRefunded", replay.Reason);
		AssertEx.Equal(60, StackCount(profile, "nested-rub"));
		AssertEx.Equal(50, StackCount(profile, "direct-rub"));
		AssertEx.Equal(900, StackCount(profile, "carried-rub"));
		AssertEx.Equal(
			2,
			rig.SaveCount,
			"Prepare and the first Refund save once each; replay must not save or credit again.");
	}

	[RegressionTest]
	private static async Task TransactionConflictsCannotCrossAmountOrProfile()
	{
		using var rig = new TestRig();
		PmcData profileA = CreateProfile(
			SessionA,
			ProfileA,
			("nested-rub", 100),
			("direct-rub", 100),
			("carried-rub", 500));
		PmcData profileB = CreateProfile(
			SessionB,
			ProfileB,
			("nested-rub", 300),
			("direct-rub", 300),
			("carried-rub", 500));
		rig.AddProfile(SessionA, profileA);
		rig.AddProfile(SessionB, profileB);
		const string transactionId = "isolated-conflict";

		FireSupportUh60TransferFeeResponse prepared =
			await rig.Service.TryHandleAsync(
				new MongoId(SessionA),
				Request("Prepare", ProfileA, transactionId, 40));
		AssertEx.True(prepared.Ok, prepared.Reason);

		FireSupportUh60TransferFeeResponse amountConflict =
			await rig.Service.TryHandleAsync(
				new MongoId(SessionA),
				Request("Prepare", ProfileA, transactionId, 41));
		FireSupportUh60TransferFeeResponse profileConflict =
			await rig.Service.TryHandleAsync(
				new MongoId(SessionB),
				Request("Prepare", ProfileB, transactionId, 40));
		FireSupportUh60TransferFeeResponse hintMismatch =
			await rig.Service.TryHandleAsync(
				new MongoId(SessionA),
				Request("Prepare", ProfileB, "wrong-hint", 40));

		AssertEx.False(amountConflict.Ok);
		AssertEx.Equal(
			"FeeTransactionConflict",
			amountConflict.Reason);
		AssertEx.False(profileConflict.Ok);
		AssertEx.Equal(
			"FeeTransactionConflict",
			profileConflict.Reason);
		AssertEx.False(hintMismatch.Ok);
		AssertEx.Equal("ProfileMismatch", hintMismatch.Reason);
		AssertEx.Equal(60, StackCount(profileA, "nested-rub"));
		AssertEx.Equal(100, StackCount(profileA, "direct-rub"));
		AssertEx.Equal(300, StackCount(profileB, "nested-rub"));
		AssertEx.Equal(300, StackCount(profileB, "direct-rub"));
		AssertEx.Equal(1, rig.SaveCount);
	}

	[RegressionTest]
	private static async Task PostDebitJournalFailureRecoversWithoutSecondCharge()
	{
		using var rig = new TestRig();
		PmcData profile = CreateProfile(
			SessionA,
			ProfileA,
			("nested-rub", 100),
			("direct-rub", 100),
			("carried-rub", 500));
		rig.AddProfile(SessionA, profile);
		string blockingTempDirectory =
			rig.JournalPath + ".tmp";
		rig.OnSave = _ =>
		{
			Directory.CreateDirectory(blockingTempDirectory);
			return Task.CompletedTask;
		};

		FireSupportUh60TransferFeeResponse failedFinalize =
			await rig.Service.TryHandleAsync(
				new MongoId(SessionA),
				Request(
					"Prepare",
					ProfileA,
					"finalize-recovery",
					70));

		AssertEx.False(failedFinalize.Ok);
		AssertEx.Equal(
			"FeeJournalSaveFailed",
			failedFinalize.Reason);
		AssertEx.Equal(
			FireSupportUh60TransferFeeJournal.DebitPendingState,
			failedFinalize.State);
		AssertEx.Equal(30, StackCount(profile, "nested-rub"));
		AssertEx.Equal(100, StackCount(profile, "direct-rub"));
		AssertEx.Equal(1, rig.SaveCount);

		Directory.Delete(blockingTempDirectory);
		rig.OnSave = null;
		FireSupportUh60TransferFeeResponse recovered =
			await rig.Service.TryHandleAsync(
				new MongoId(SessionA),
				Request(
					"Prepare",
					ProfileA,
					"finalize-recovery",
					70));

		AssertEx.True(recovered.Ok, recovered.Reason);
		AssertEx.Equal("RecoveredPrepared", recovered.Reason);
		AssertEx.Equal(
			FireSupportUh60TransferFeeJournal.PreparedState,
			recovered.State);
		AssertEx.Equal(30, StackCount(profile, "nested-rub"));
		AssertEx.Equal(100, StackCount(profile, "direct-rub"));
		AssertEx.Equal(
			1,
			rig.SaveCount,
			"Fingerprint recovery must finalize the journal without another debit or profile save.");
	}

	[RegressionTest]
	private static async Task ZeroAndNegativeFeeAmountsFailBeforeMutation()
	{
		using var rig = new TestRig();
		PmcData profile = CreateProfile(
			SessionA,
			ProfileA,
			("nested-rub", 100),
			("direct-rub", 100),
			("carried-rub", 500));
		rig.AddProfile(SessionA, profile);

		foreach ((string id, int amount) in new[]
		         {
			         ("zero-fee", 0),
			         ("negative-fee", -1)
		         })
		{
			FireSupportUh60TransferFeeResponse response =
				await rig.Service.TryHandleAsync(
					new MongoId(SessionA),
					Request("Prepare", ProfileA, id, amount));
			AssertEx.False(response.Ok);
			AssertEx.Equal("InvalidFeeAmount", response.Reason);
			AssertEx.False(rig.Journal.TryGet(id, out _));
		}

		AssertEx.Equal(100, StackCount(profile, "nested-rub"));
		AssertEx.Equal(100, StackCount(profile, "direct-rub"));
		AssertEx.Equal(500, StackCount(profile, "carried-rub"));
		AssertEx.Equal(0, rig.SaveCount);
	}

	private static FireSupportUh60TransferFeeRequest Request(
		string action,
		string profileId,
		string transactionId,
		int amountRoubles)
	{
		return new FireSupportUh60TransferFeeRequest
		{
			Action = action,
			ProfileId = profileId,
			TransactionId = transactionId,
			AmountRoubles = amountRoubles
		};
	}

	private static PmcData CreateProfile(
		string sessionId,
		string profileId,
		(string Id, int Count) nested,
		(string Id, int Count) direct,
		(string Id, int Count) carried)
	{
		string stashId = $"stash-{profileId}";
		string containerId = $"container-{profileId}";
		string carriedRootId = $"pockets-{profileId}";
		return new PmcData
		{
			Id = new MongoId(profileId),
			SessionId = new MongoId(sessionId),
			Inventory = new BotBaseInventory
			{
				Stash = new MongoId(stashId),
				Items =
				[
					new Item
					{
						Id = containerId,
						Template = "container-template",
						ParentId = stashId,
						SlotId = "hideout"
					},
					Roubles(
						nested.Id,
						containerId,
						nested.Count),
					Roubles(
						direct.Id,
						stashId,
						direct.Count),
					new Item
					{
						Id = carriedRootId,
						Template = "pockets-template"
					},
					Roubles(
						carried.Id,
						carriedRootId,
						carried.Count)
				]
			}
		};
	}

	private static Item Roubles(
		string id,
		string parentId,
		int count)
	{
		return new Item
		{
			Id = id,
			Template = PaymentCurrencyInfo.RoubleTemplateId,
			ParentId = parentId,
			SlotId = "hideout",
			Upd = new Upd
			{
				StackObjectsCount = count
			}
		};
	}

	private static Item? FindItem(PmcData profile, string itemId)
	{
		return profile.Inventory?.Items?.FirstOrDefault(
			item => string.Equals(
				item.Id,
				itemId,
				StringComparison.Ordinal));
	}

	private static int StackCount(PmcData profile, string itemId)
	{
		Item item = AssertEx.NotNull(FindItem(profile, itemId));
		return (int)Math.Floor(
			item.Upd?.StackObjectsCount ?? 1d);
	}

	private sealed class TestRig : IDisposable
	{
		private readonly string _root;
		private readonly Dictionary<string, PmcData> _profiles =
			new(StringComparer.OrdinalIgnoreCase);

		public TestRig()
		{
			_root = Path.Combine(
				Path.GetTempPath(),
				$"tsc-uh60-fee-service-{Guid.NewGuid():N}");
			Directory.CreateDirectory(_root);

			var journalLogger =
				new SilentLogger<FireSupportUh60TransferFeeJournal>();
			Journal =
				new FireSupportUh60TransferFeeJournal(journalLogger);
			var profileHelper = new ProfileHelper
			{
				ResolvePmcProfile = sessionId =>
					_profiles.TryGetValue(
						sessionId.ToString(),
						out PmcData? profile)
						? profile
						: null
			};
			var saveServer = new SaveServer
			{
				SaveProfile = async sessionId =>
				{
					SaveCount++;
					if (OnSave != null)
					{
						await OnSave(sessionId);
					}
				}
			};
			Service = new FireSupportUh60TransferFeeService(
				new SilentLogger<FireSupportUh60TransferFeeService>(),
				profileHelper,
				saveServer,
				new JsonCloner(),
				new FireSupportProfileMutationGate(),
				Journal);
			Service.Initialize(_root);
		}

		public FireSupportUh60TransferFeeService Service { get; }
		public FireSupportUh60TransferFeeJournal Journal { get; }
		public int SaveCount { get; private set; }
		public Func<MongoId, Task>? OnSave { get; set; }
		public string JournalPath =>
			Path.Combine(
				_root,
				"storage",
				"tsc-uh60-transfer-fees.json");

		public void AddProfile(string sessionId, PmcData profile)
		{
			_profiles[sessionId] = profile;
		}

		public void Dispose()
		{
			if (Directory.Exists(_root))
			{
				Directory.Delete(_root, recursive: true);
			}
		}
	}

	private sealed class JsonCloner : ICloner
	{
		public T? Clone<T>(T? value)
		{
			if (value == null)
			{
				return default;
			}

			return JsonSerializer.Deserialize<T>(
				JsonSerializer.Serialize(value));
		}
	}

	private sealed class SilentLogger<T> : ISptLogger<T>
	{
		public void Success(string message)
		{
		}

		public void Warning(string message)
		{
		}

		public void Error(string message)
		{
		}

		public void Error(string message, Exception exception)
		{
		}
	}
}
