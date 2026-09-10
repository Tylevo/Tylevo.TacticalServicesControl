using SamSWAT.FireSupport.ArysReloaded;
using SamSWAT.FireSupport.ArysReloaded.Unity;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils.Cloners;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class Uh60TransferFeeServiceTests
{
	private const string SessionA = "66f51f3a0000000000001101";
	private const string ProfileA = "66f51f3a0000000000001201";
	private const string SessionB = "66f51f3a0000000000001102";
	private const string ProfileB = "66f51f3a0000000000001202";

	[RegressionTest]
	private static async Task NewHandlingFeeRequestsNeverDebitOrCreateRecoverableTransactions()
	{
		foreach (int stashFunds in new[] { 0, 100 })
		{
			using var rig = new TestRig();
			PmcData profile = CreateProfile(SessionA, ProfileA,
				("nested-rub", stashFunds), ("direct-rub", stashFunds), ("carried-rub", 500));
			rig.AddProfile(SessionA, profile);
			string inventoryBefore = JsonSerializer.Serialize(profile.Inventory);
			string journalBefore = File.ReadAllText(rig.JournalPath);
			const string id = "new-fee-is-included";
			foreach (string action in new[] { "Prepare", "Prepare", "Status", "Commit", "Refund" })
			{
				FireSupportUh60TransferFeeResponse response = await rig.Service.TryHandleAsync(
					new MongoId(SessionA), Request(action, ProfileA, id, 80));
				AssertEx.False(response.Ok);
				AssertEx.Equal(action == "Prepare" ? FireSupportUh60TransferFeeService.IncludedInServiceReason
					: "FeeTransactionNotFound", response.Reason);
				AssertEx.Equal(string.Empty, response.State);
				AssertEx.Equal(stashFunds * 2, response.StashRoubleBalance);
			}
			AssertEx.Equal(inventoryBefore, JsonSerializer.Serialize(profile.Inventory));
			AssertEx.Equal(journalBefore, File.ReadAllText(rig.JournalPath));
			AssertEx.Equal(0, rig.SaveCount);
			AssertEx.False(rig.Journal.TryGet(id, out _));
		}
	}

	[RegressionTest]
	private static async Task LegacyPreparedPaymentCanReplayAndCommitWithoutChargeOrRefund()
	{
		using var rig = new TestRig();
		PmcData profile = StandardProfile();
		rig.AddProfile(SessionA, profile);
		const string id = "legacy-commit";
		SeedLegacyFee(rig, profile, id, FireSupportUh60TransferFeeJournal.PreparedState, true, ("nested-rub", 70));
		string inventoryBefore = JsonSerializer.Serialize(profile.Inventory);
		foreach ((string action, string reason) in new[]
		{
			("Prepare", "AlreadyPrepared"), ("Prepare", "AlreadyPrepared"),
			("Commit", "Committed"), ("Commit", "AlreadyCommitted"), ("Prepare", "AlreadyCommitted")
		})
		{
			FireSupportUh60TransferFeeResponse response = await rig.Service.TryHandleAsync(
				new MongoId(SessionA), Request(action, ProfileA, id, 70));
			AssertEx.True(response.Ok, response.Reason);
			AssertEx.Equal(reason, response.Reason);
		}
		rig.RestartJournal();
		FireSupportUh60TransferFeeResponse refund = await rig.Service.TryHandleAsync(
			new MongoId(SessionA), Request("Refund", ProfileA, id, 70));
		AssertEx.False(refund.Ok);
		AssertEx.Equal("FeeTransactionCommitted", refund.Reason);
		AssertEx.Equal(inventoryBefore, JsonSerializer.Serialize(profile.Inventory));
		AssertEx.Equal(0, rig.SaveCount);
	}

	[RegressionTest]
	private static async Task LegacyRefundRestoresOnlyPaidStacksOnceAndPreservesLaterItems()
	{
		using var rig = new TestRig();
		PmcData profile = CreateProfile(SessionA, ProfileA,
			("nested-rub", 60), ("direct-rub", 50), ("carried-rub", 900));
		rig.AddProfile(SessionA, profile);
		const string id = "legacy-refund";
		SeedLegacyFee(rig, profile, id, FireSupportUh60TransferFeeJournal.PreparedState, true,
			("nested-rub", 60), ("direct-rub", 20));
		AssertEx.Null(FindItem(profile, "nested-rub"));
		profile.Inventory!.Items!.Add(Roubles("later-roubles", $"stash-{ProfileA}", 7));
		profile.Inventory!.Items!.Add(new Item { Id = "later-bitcoin", Template = PaymentCurrencyInfo.BitcoinTemplateId,
			ParentId = $"stash-{ProfileA}", SlotId = "hideout" });
		FireSupportUh60TransferFeeResponse refunded = await rig.Service.TryHandleAsync(
			new MongoId(SessionA), Request("Refund", ProfileA, id, 80));
		AssertEx.True(refunded.Ok, refunded.Reason);
		AssertEx.Equal(FireSupportUh60TransferFeeJournal.RefundedState, refunded.State);
		rig.RestartJournal();
		FireSupportUh60TransferFeeResponse replay = await rig.Service.TryHandleAsync(
			new MongoId(SessionA), Request("Refund", ProfileA, id, 80));
		AssertEx.True(replay.Ok, replay.Reason);
		AssertEx.Equal("AlreadyRefunded", replay.Reason);
		AssertEx.Equal(60, StackCount(profile, "nested-rub"));
		AssertEx.Equal(50, StackCount(profile, "direct-rub"));
		AssertEx.Equal(7, StackCount(profile, "later-roubles"));
		AssertEx.Equal(900, StackCount(profile, "carried-rub"));
		AssertEx.NotNull(FindItem(profile, "later-bitcoin"));
		AssertEx.Equal(1, rig.SaveCount);
		FireSupportUh60TransferFeeResponse prepare = await rig.Service.TryHandleAsync(
			new MongoId(SessionA), Request("Prepare", ProfileA, id, 80));
		AssertEx.False(prepare.Ok);
		AssertEx.Equal("FeeTransactionRefunded", prepare.Reason);
	}

	[RegressionTest]
	private static async Task LegacyTransactionsKeepAuthenticatedProfileAndAmountGuards()
	{
		using var rig = new TestRig();
		PmcData profileA = StandardProfile();
		PmcData profileB = CreateProfile(SessionB, ProfileB,
			("nested-rub", 300), ("direct-rub", 300), ("carried-rub", 500));
		rig.AddProfile(SessionA, profileA);
		rig.AddProfile(SessionB, profileB);
		const string id = "legacy-conflict";
		SeedLegacyFee(rig, profileA, id, FireSupportUh60TransferFeeJournal.PreparedState, true, ("nested-rub", 40));
		string beforeA = JsonSerializer.Serialize(profileA.Inventory);
		string beforeB = JsonSerializer.Serialize(profileB.Inventory);
		foreach (string action in new[] { "Prepare", "Status", "Commit", "Refund" })
		{
			FireSupportUh60TransferFeeResponse amountConflict = await rig.Service.TryHandleAsync(
				new MongoId(SessionA), Request(action, ProfileA, id, 41));
			FireSupportUh60TransferFeeResponse profileConflict = await rig.Service.TryHandleAsync(
				new MongoId(SessionB), Request(action, ProfileB, id, 40));
			FireSupportUh60TransferFeeResponse hintMismatch = await rig.Service.TryHandleAsync(
				new MongoId(SessionA), Request(action, ProfileB, id, 40));
			AssertEx.False(amountConflict.Ok);
			AssertEx.Equal("FeeTransactionConflict", amountConflict.Reason);
			AssertEx.False(profileConflict.Ok);
			AssertEx.Equal("FeeTransactionConflict", profileConflict.Reason);
			AssertEx.False(hintMismatch.Ok);
			AssertEx.Equal("ProfileMismatch", hintMismatch.Reason);
		}
		AssertEx.Equal(beforeA, JsonSerializer.Serialize(profileA.Inventory));
		AssertEx.Equal(beforeB, JsonSerializer.Serialize(profileB.Inventory));
		AssertEx.Equal(0, rig.SaveCount);
	}

	[RegressionTest]
	private static async Task LegacyPostDebitJournalFailureRecoversWithoutSecondCharge()
	{
		using var rig = new TestRig();
		PmcData profile = StandardProfile();
		rig.AddProfile(SessionA, profile);
		const string id = "legacy-finalize-recovery";
		SeedLegacyFee(rig, profile, id, FireSupportUh60TransferFeeJournal.DebitPendingState, true, ("nested-rub", 70));
		string inventoryBefore = JsonSerializer.Serialize(profile.Inventory);
		string blockingDirectory = rig.JournalPath + ".tmp";
		Directory.CreateDirectory(blockingDirectory);
		FireSupportUh60TransferFeeResponse failed = await rig.Service.TryHandleAsync(
			new MongoId(SessionA), Request("Prepare", ProfileA, id, 70));
		AssertEx.False(failed.Ok);
		AssertEx.Equal("FeeJournalSaveFailed", failed.Reason);
		AssertEx.Equal(FireSupportUh60TransferFeeJournal.DebitPendingState, failed.State);
		Directory.Delete(blockingDirectory);
		rig.RestartJournal();
		FireSupportUh60TransferFeeResponse recovered = await rig.Service.TryHandleAsync(
			new MongoId(SessionA), Request("Prepare", ProfileA, id, 70));
		AssertEx.True(recovered.Ok, recovered.Reason);
		AssertEx.Equal("RecoveredPrepared", recovered.Reason);
		AssertEx.Equal(FireSupportUh60TransferFeeJournal.PreparedState, recovered.State);
		AssertEx.Equal(inventoryBefore, JsonSerializer.Serialize(profile.Inventory));
		AssertEx.Equal(0, rig.SaveCount);
	}

	[RegressionTest]
	private static async Task LegacyPaidPendingTransactionsCanRecoverThroughStatusCommitOrRefund()
	{
		foreach (string action in new[] { "Status", "Commit", "Refund" })
		{
			using var rig = new TestRig();
			PmcData profile = StandardProfile();
			rig.AddProfile(SessionA, profile);
			const string id = "legacy-pending-recovery";
			SeedLegacyFee(rig, profile, id, FireSupportUh60TransferFeeJournal.DebitPendingState, true, ("nested-rub", 70));
			FireSupportUh60TransferFeeResponse response = await rig.Service.TryHandleAsync(
				new MongoId(SessionA), Request(action, ProfileA, id, 70));
			AssertEx.True(response.Ok, response.Reason);
			AssertEx.Equal(action == "Status" ? FireSupportUh60TransferFeeJournal.PreparedState :
				action == "Commit" ? FireSupportUh60TransferFeeJournal.CommittedState :
				FireSupportUh60TransferFeeJournal.RefundedState, response.State);
			AssertEx.Equal(action == "Refund" ? 100 : 30, StackCount(profile, "nested-rub"));
			AssertEx.Equal(action == "Refund" ? 1 : 0, rig.SaveCount);
		}
	}

	[RegressionTest]
	private static async Task LegacyUndebitedTransactionsCancelAcrossRestartWithoutMintingOrChargingMoney()
	{
		foreach (string action in new[] { "Prepare", "Refund" })
		{
			using var rig = new TestRig();
			PmcData profile = StandardProfile();
			rig.AddProfile(SessionA, profile);
			const string id = "legacy-never-debited";
			SeedLegacyFee(rig, profile, id, FireSupportUh60TransferFeeJournal.DebitPendingState, false, ("nested-rub", 70));
			string inventoryBefore = JsonSerializer.Serialize(profile.Inventory);
			FireSupportUh60TransferFeeResponse response = await rig.Service.TryHandleAsync(
				new MongoId(SessionA), Request(action, ProfileA, id, 70));
			AssertEx.Equal(action == "Refund", response.Ok);
			AssertEx.Equal(action == "Prepare" ? FireSupportUh60TransferFeeService.IncludedInServiceReason :
				"RefundedBeforeDebit", response.Reason);
			AssertEx.Equal(FireSupportUh60TransferFeeJournal.RefundedState, response.State);
			rig.RestartJournal();
			AssertEx.True(rig.Journal.TryGet(id, out FireSupportUh60TransferFeeRecord? record));
			AssertEx.Equal(0, record!.RefundCredits.Count);
			AssertEx.Equal(record.PreDebitFingerprint, record.ExpectedPostRefundFingerprint);
			FireSupportUh60TransferFeeResponse replay = await rig.Service.TryHandleAsync(
				new MongoId(SessionA), Request("Refund", ProfileA, id, 70));
			AssertEx.True(replay.Ok, replay.Reason);
			AssertEx.Equal("AlreadyRefunded", replay.Reason);
			FireSupportUh60TransferFeeResponse commit = await rig.Service.TryHandleAsync(
				new MongoId(SessionA), Request("Commit", ProfileA, id, 70));
			AssertEx.False(commit.Ok);
			AssertEx.Equal("FeeTransactionRefunded", commit.Reason);
			AssertEx.Equal(inventoryBefore, JsonSerializer.Serialize(profile.Inventory));
			AssertEx.Equal(0, rig.SaveCount);
		}
	}

	[RegressionTest]
	private static async Task AmbiguousLegacyPaymentCannotDebitRefundOrCommit()
	{
		using var rig = new TestRig();
		PmcData profile = StandardProfile();
		rig.AddProfile(SessionA, profile);
		const string id = "legacy-ambiguous";
		SeedLegacyFee(rig, profile, id, FireSupportUh60TransferFeeJournal.DebitPendingState, true, ("nested-rub", 70));
		FindItem(profile, "direct-rub")!.Upd!.StackObjectsCount += 1;
		string inventoryBefore = JsonSerializer.Serialize(profile.Inventory);
		string journalBefore = File.ReadAllText(rig.JournalPath);
		foreach (string action in new[] { "Prepare", "Refund", "Commit" })
		{
			FireSupportUh60TransferFeeResponse response = await rig.Service.TryHandleAsync(
				new MongoId(SessionA), Request(action, ProfileA, id, 70));
			AssertEx.False(response.Ok);
			AssertEx.Equal(action == "Commit" ? "FeeTransactionNotPrepared" : "FeePaymentStateAmbiguous", response.Reason);
		}
		AssertEx.Equal(inventoryBefore, JsonSerializer.Serialize(profile.Inventory));
		AssertEx.Equal(journalBefore, File.ReadAllText(rig.JournalPath));
		AssertEx.Equal(0, rig.SaveCount);
	}

	[RegressionTest]
	private static async Task LegacyRefundJournalFailureRecoversWithoutSecondCredit()
	{
		using var rig = new TestRig();
		PmcData profile = StandardProfile();
		rig.AddProfile(SessionA, profile);
		const string id = "legacy-refund-finalize";
		SeedLegacyFee(rig, profile, id, FireSupportUh60TransferFeeJournal.PreparedState, true, ("nested-rub", 70));
		string blockingDirectory = rig.JournalPath + ".tmp";
		rig.OnSave = _ => { Directory.CreateDirectory(blockingDirectory); return Task.CompletedTask; };
		FireSupportUh60TransferFeeResponse failed = await rig.Service.TryHandleAsync(
			new MongoId(SessionA), Request("Refund", ProfileA, id, 70));
		AssertEx.False(failed.Ok);
		AssertEx.Equal("FeeJournalSaveFailed", failed.Reason);
		AssertEx.Equal(FireSupportUh60TransferFeeJournal.RefundPendingState, failed.State);
		AssertEx.Equal(100, StackCount(profile, "nested-rub"));
		Directory.Delete(blockingDirectory);
		rig.OnSave = null;
		rig.RestartJournal();
		FireSupportUh60TransferFeeResponse status = await rig.Service.TryHandleAsync(
			new MongoId(SessionA), Request("Status", ProfileA, id, 70));
		AssertEx.True(status.Ok, status.Reason);
		AssertEx.Equal(FireSupportUh60TransferFeeJournal.RefundedState, status.State);
		FireSupportUh60TransferFeeResponse replay = await rig.Service.TryHandleAsync(
			new MongoId(SessionA), Request("Refund", ProfileA, id, 70));
		AssertEx.True(replay.Ok, replay.Reason);
		AssertEx.Equal("AlreadyRefunded", replay.Reason);
		AssertEx.Equal(100, StackCount(profile, "nested-rub"));
		AssertEx.Equal(100, StackCount(profile, "direct-rub"));
		AssertEx.Equal(500, StackCount(profile, "carried-rub"));
		AssertEx.Equal(1, rig.SaveCount);
	}

	[RegressionTest]
	private static async Task InvalidFeeRequestsStillFailBeforeMutation()
	{
		using var rig = new TestRig();
		PmcData profile = StandardProfile();
		rig.AddProfile(SessionA, profile);
		foreach (int amount in new[] { 0, -1, FireSupportUh60TransferFeeJournal.MaxAmountRoubles + 1 })
		{
			FireSupportUh60TransferFeeResponse response = await rig.Service.TryHandleAsync(
				new MongoId(SessionA), Request("Prepare", ProfileA, "invalid-fee", amount));
			AssertEx.False(response.Ok);
			AssertEx.Equal("InvalidFeeAmount", response.Reason);
		}
		FireSupportUh60TransferFeeResponse unauthenticated = await rig.Service.TryHandleAsync(
			default, Request("Prepare", ProfileA, "invalid-fee", 70));
		AssertEx.False(unauthenticated.Ok);
		AssertEx.Equal("AuthenticatedSessionRequired", unauthenticated.Reason);
		AssertEx.False(rig.Journal.TryGet("invalid-fee", out _));
		AssertEx.Equal(100, StackCount(profile, "nested-rub"));
		AssertEx.Equal(100, StackCount(profile, "direct-rub"));
		AssertEx.Equal(500, StackCount(profile, "carried-rub"));
		AssertEx.Equal(0, rig.SaveCount);
	}

	private static PmcData StandardProfile() => CreateProfile(SessionA, ProfileA,
		("nested-rub", 100), ("direct-rub", 100), ("carried-rub", 500));

	private static void SeedLegacyFee(TestRig rig, PmcData profile, string id, string state,
		bool debitReachedProfile, params (string ItemId, int Amount)[] paidStacks)
	{
		// Model an existing installation's durable journal/profile directly. The retired
		// endpoint must not be used to set up fixtures by creating fresh charges.
		var originalCounts = new Dictionary<string, int>
		{
			["nested-rub"] = StackCount(profile, "nested-rub"),
			["direct-rub"] = StackCount(profile, "direct-rub")
		};
		var afterCounts = new Dictionary<string, int>(originalCounts);
		var record = new FireSupportUh60TransferFeeRecord
		{
			TransactionId = id, ProfileId = profile.Id!.Value.ToString(),
			AmountRoubles = paidStacks.Sum(stack => stack.Amount), State = state,
			CreatedUtc = DateTimeOffset.UtcNow, UpdatedUtc = DateTimeOffset.UtcNow,
			PreDebitFingerprint = Fingerprint(originalCounts)
		};
		foreach ((string itemId, int amount) in paidStacks)
		{
			Item original = AssertEx.NotNull(new JsonCloner().Clone(FindItem(profile, itemId)));
			record.Debits.Add(new FireSupportUh60TransferFeeDebit { OriginalItem = original, AmountRoubles = amount });
			afterCounts[itemId] -= amount;
			if (!debitReachedProfile) continue;
			Item current = AssertEx.NotNull(FindItem(profile, itemId));
			if (afterCounts[itemId] == 0) profile.Inventory!.Items!.Remove(current);
			else current.Upd!.StackObjectsCount = afterCounts[itemId];
		}
		record.ExpectedPostDebitFingerprint = Fingerprint(afterCounts);
		AssertEx.True(rig.Journal.TryCreate(record, out _, out string reason), reason);
		rig.RestartJournal();
	}

	private static string Fingerprint(Dictionary<string, int> counts)
	{
		string text = string.Concat(counts.Where(pair => pair.Value > 0).OrderBy(pair => pair.Key, StringComparer.Ordinal)
			.Select(pair => pair.Key + ":" + pair.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n"));
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
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

		public void RestartJournal() => Journal.Initialize(Path.GetDirectoryName(JournalPath)!);

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
