using SamSWAT.FireSupport.ArysReloaded;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Utils;
using System.Text.Json;

internal static class Uh60TransferFeeJournalTests
{
	private const string ProfileId = "66f51f3a0000000000000102";
	private const string OtherProfileId =
		"66f51f3a0000000000000103";
	private const string CommitTransactionId =
		"tsc-uh60-fee-66f51f3a0000000000000102-commit";
	private const string RefundTransactionId =
		"tsc-uh60-fee-66f51f3a0000000000000102-refund";

	[RegressionTest]
	private static void JournalPersistsPrepareCommitAndRefundLifecycle()
	{
		string directory = CreateTemporaryDirectory();
		try
		{
			var firstProcess =
				new FireSupportUh60TransferFeeJournal(new TestLogger());
			firstProcess.Initialize(directory);

			FireSupportUh60TransferFeeRecord commit =
				CreatePending(CommitTransactionId, 12_345);
			AssertEx.True(
				firstProcess.TryCreate(
					commit,
					out FireSupportUh60TransferFeeRecord? created,
					out string createReason),
				createReason);
			AssertEx.Equal(
				FireSupportUh60TransferFeeJournal.DebitPendingState,
				AssertEx.NotNull(created).State);

			commit.State =
				FireSupportUh60TransferFeeJournal.PreparedState;
			commit.UpdatedUtc = DateTimeOffset.UtcNow;
			AssertEx.True(
				firstProcess.TrySave(commit, out string prepareReason),
				prepareReason);

			var afterPrepare =
				new FireSupportUh60TransferFeeJournal(new TestLogger());
			afterPrepare.Initialize(directory);
			AssertEx.True(
				afterPrepare.TryGet(
					CommitTransactionId,
					out FireSupportUh60TransferFeeRecord? prepared));
			FireSupportUh60TransferFeeRecord preparedRecord =
				AssertEx.NotNull(prepared);
			AssertEx.Equal(
				FireSupportUh60TransferFeeJournal.PreparedState,
				preparedRecord.State);

			preparedRecord.State =
				FireSupportUh60TransferFeeJournal.CommittedState;
			preparedRecord.UpdatedUtc = DateTimeOffset.UtcNow;
			AssertEx.True(
				afterPrepare.TrySave(
					preparedRecord,
					out string commitReason),
				commitReason);

			FireSupportUh60TransferFeeRecord refund =
				CreatePending(RefundTransactionId, 9_876);
			AssertEx.True(
				afterPrepare.TryCreate(refund, out _, out string refundCreateReason),
				refundCreateReason);
			refund.State =
				FireSupportUh60TransferFeeJournal.PreparedState;
			refund.UpdatedUtc = DateTimeOffset.UtcNow;
			AssertEx.True(
				afterPrepare.TrySave(refund, out string refundPrepareReason),
				refundPrepareReason);
			refund.State =
				FireSupportUh60TransferFeeJournal.RefundPendingState;
			refund.UpdatedUtc = DateTimeOffset.UtcNow;
			refund.RefundCredits =
			[
				new FireSupportUh60TransferFeeRefundCredit
				{
					TargetItemId =
						"66f51f3a0000000000000201",
					AmountRoubles = refund.AmountRoubles,
					BeforeCount = 0,
					RestoredItem = refund.Debits[0].OriginalItem
				}
			];
			AssertEx.True(
				afterPrepare.TrySave(refund, out string refundPendingReason),
				refundPendingReason);
			refund.State =
				FireSupportUh60TransferFeeJournal.RefundedState;
			refund.UpdatedUtc = DateTimeOffset.UtcNow;
			AssertEx.True(
				afterPrepare.TrySave(refund, out string refundedReason),
				refundedReason);

			var restarted =
				new FireSupportUh60TransferFeeJournal(new TestLogger());
			restarted.Initialize(directory);
			AssertEx.True(
				restarted.TryGet(
					CommitTransactionId,
					out FireSupportUh60TransferFeeRecord? committed));
			FireSupportUh60TransferFeeRecord committedRecord =
				AssertEx.NotNull(committed);
			AssertEx.Equal(
				FireSupportUh60TransferFeeJournal.CommittedState,
				committedRecord.State);
			AssertEx.True(
				restarted.TryGet(
					RefundTransactionId,
					out FireSupportUh60TransferFeeRecord? refunded));
			FireSupportUh60TransferFeeRecord refundedRecord =
				AssertEx.NotNull(refunded);
			AssertEx.Equal(
				FireSupportUh60TransferFeeJournal.RefundedState,
				refundedRecord.State);

			committedRecord.State =
				FireSupportUh60TransferFeeJournal.PreparedState;
			AssertEx.False(
				restarted.TrySave(
					committedRecord,
					out string committedTerminalReason));
			AssertEx.Equal(
				"FeeTransactionTerminal",
				committedTerminalReason);
			refundedRecord.State =
				FireSupportUh60TransferFeeJournal.PreparedState;
			AssertEx.False(
				restarted.TrySave(
					refundedRecord,
					out string refundedTerminalReason));
			AssertEx.Equal(
				"FeeTransactionTerminal",
				refundedTerminalReason);
			AssertEx.False(
				File.Exists(
					Path.Combine(
						directory,
						"tsc-uh60-transfer-fees.json.tmp")),
				"Durable fee-journal writes must not leave a temporary file behind.");
		}
		finally
		{
			DeleteTemporaryDirectory(directory);
		}
	}

	[RegressionTest]
	private static void JournalReplaysExactTransactionsAndRejectsConflictingAmount()
	{
		string directory = CreateTemporaryDirectory();
		try
		{
			var journal =
				new FireSupportUh60TransferFeeJournal(new TestLogger());
			journal.Initialize(directory);
			FireSupportUh60TransferFeeRecord original =
				CreatePending(CommitTransactionId, 25_000);
			AssertEx.True(
				journal.TryCreate(
					original,
					out _,
					out string createReason),
				createReason);

			AssertEx.False(
				journal.TryCreate(
					CreatePending(CommitTransactionId, 25_000),
					out FireSupportUh60TransferFeeRecord? replay,
					out string replayReason));
			AssertEx.Equal("AlreadyExists", replayReason);
			AssertEx.Equal(25_000, AssertEx.NotNull(replay).AmountRoubles);

			FireSupportUh60TransferFeeRecord conflicting =
				AssertEx.NotNull(replay);
			conflicting.AmountRoubles = 25_001;
			conflicting.Debits[0].AmountRoubles = 25_001;
			AssertEx.False(
				journal.TrySave(
					conflicting,
					out string conflictReason));
			AssertEx.Equal("FeeTransactionConflict", conflictReason);

			AssertEx.True(
				journal.TryGet(
					CommitTransactionId,
					out FireSupportUh60TransferFeeRecord? unchanged));
			AssertEx.Equal(25_000, AssertEx.NotNull(unchanged).AmountRoubles);
		}
		finally
		{
			DeleteTemporaryDirectory(directory);
		}
	}

	[RegressionTest]
	private static async Task SharedProfileMutationGateSerializesConcurrentOperations()
	{
		var gate = new FireSupportProfileMutationGate();
		int active = 0;
		int maximumActive = 0;
		int completed = 0;

		Task<int>[] operations = Enumerable.Range(0, 24)
			.Select(index =>
				gate.RunAsync(async () =>
				{
					int nowActive = Interlocked.Increment(ref active);
					UpdateMaximum(ref maximumActive, nowActive);
					await Task.Delay(2);
					Interlocked.Decrement(ref active);
					Interlocked.Increment(ref completed);
					return index;
				}))
			.ToArray();

		await Task.WhenAll(operations);
		AssertEx.Equal(1, maximumActive);
		AssertEx.Equal(24, completed);
		AssertEx.Equal(0, active);
	}

	[RegressionTest]
	private static void NonterminalTransactionsAreCappedPerProfileWithoutBlockingReplay()
	{
		string directory = CreateTemporaryDirectory();
		try
		{
			var journal =
				new FireSupportUh60TransferFeeJournal(new TestLogger());
			journal.Initialize(directory);
			for (int index = 0;
			     index <
			     FireSupportUh60TransferFeeJournal
				     .MaxNonterminalTransactionsPerProfile;
			     index++)
			{
				AssertEx.True(
					journal.TryCreate(
						CreatePending(
							$"profile-cap-{index}",
							1),
						out _,
						out string reason),
					reason);
			}

			AssertEx.False(
				journal.TryCreate(
					CreatePending("profile-cap-denied", 1),
					out _,
					out string limitReason));
			AssertEx.Equal(
				"FeeProfileTransactionLimitReached",
				limitReason);

			AssertEx.False(
				journal.TryCreate(
					CreatePending("profile-cap-0", 1),
					out FireSupportUh60TransferFeeRecord? replay,
					out string replayReason));
			AssertEx.Equal("AlreadyExists", replayReason);
			AssertEx.NotNull(replay);

			AssertEx.True(
				journal.TryCreate(
					CreatePending(
						"other-profile-allowed",
						1,
						OtherProfileId),
					out _,
					out string otherReason),
				otherReason);
		}
		finally
		{
			DeleteTemporaryDirectory(directory);
		}
	}

	[RegressionTest]
	private static void CapacityPressurePrunesOnlyTerminalTransactions()
	{
		string withTerminal = CreateTemporaryDirectory();
		string allPending = CreateTemporaryDirectory();
		try
		{
			WriteCapacityJournal(
				withTerminal,
				includeTerminal: true);
			var reclaiming =
				new FireSupportUh60TransferFeeJournal(new TestLogger());
			reclaiming.Initialize(withTerminal);
			AssertEx.True(
				reclaiming.TryCreate(
					CreatePending(
						"pressure-new",
						1,
						"pressure-new-profile"),
					out _,
					out string reclaimReason),
				reclaimReason);
			AssertEx.False(
				reclaiming.TryGet("pressure-terminal", out _),
				"The oldest terminal record should be sacrificed under actual global capacity pressure.");
			AssertEx.True(
				reclaiming.TryGet("pressure-0000", out _),
				"A nonterminal record must never be evicted to make room.");
			AssertEx.True(
				reclaiming.TryGet("pressure-new", out _));

			WriteCapacityJournal(
				allPending,
				includeTerminal: false);
			var saturated =
				new FireSupportUh60TransferFeeJournal(new TestLogger());
			saturated.Initialize(allPending);
			AssertEx.False(
				saturated.TryCreate(
					CreatePending(
						"pressure-denied",
						1,
						"pressure-denied-profile"),
					out _,
					out string capacityReason));
			AssertEx.Equal(
				"FeeJournalCapacityReached",
				capacityReason);
			AssertEx.True(
				saturated.TryGet("pressure-0000", out _));
			AssertEx.True(
				saturated.TryGet(
					$"pressure-{FireSupportUh60TransferFeeJournal.MaxTransactions - 1:D4}",
					out _));
		}
		finally
		{
			DeleteTemporaryDirectory(withTerminal);
			DeleteTemporaryDirectory(allPending);
		}
	}

	private static FireSupportUh60TransferFeeRecord CreatePending(
		string transactionId,
		int amountRoubles,
		string profileId = ProfileId)
	{
		DateTimeOffset now = DateTimeOffset.UtcNow;
		return new FireSupportUh60TransferFeeRecord
		{
			TransactionId = transactionId,
			ProfileId = profileId,
			AmountRoubles = amountRoubles,
			State =
				FireSupportUh60TransferFeeJournal.DebitPendingState,
			CreatedUtc = now,
			UpdatedUtc = now,
			PreDebitFingerprint = "pre",
			ExpectedPostDebitFingerprint = "post",
			Debits =
			[
				new FireSupportUh60TransferFeeDebit
				{
					AmountRoubles = amountRoubles,
					OriginalItem = new Item
					{
						Id = "66f51f3a0000000000000201",
						Template = "5449016a4bdc2d6f028b456f",
						ParentId = "66f51f3a0000000000000202",
						SlotId = "hideout"
					}
				}
			]
		};
	}

	private static void WriteCapacityJournal(
		string directory,
		bool includeTerminal)
	{
		var state = new FireSupportUh60TransferFeeJournalState();
		int pendingCount =
			FireSupportUh60TransferFeeJournal.MaxTransactions -
			(includeTerminal ? 1 : 0);
		for (int index = 0; index < pendingCount; index++)
		{
			string transactionId = $"pressure-{index:D4}";
			state.Transactions[transactionId] =
				CreatePending(
					transactionId,
					1,
					$"pressure-profile-{index / 8:D4}");
		}

		if (includeTerminal)
		{
			FireSupportUh60TransferFeeRecord terminal =
				CreatePending(
					"pressure-terminal",
					1,
					"pressure-terminal-profile");
			terminal.State =
				FireSupportUh60TransferFeeJournal.CommittedState;
			terminal.UpdatedUtc =
				DateTimeOffset.UtcNow - TimeSpan.FromDays(1);
			state.Transactions[terminal.TransactionId] =
				terminal;
		}

		File.WriteAllText(
			Path.Combine(
				directory,
				"tsc-uh60-transfer-fees.json"),
			JsonSerializer.Serialize(state));
	}

	private static void UpdateMaximum(ref int target, int candidate)
	{
		int observed;
		do
		{
			observed = Volatile.Read(ref target);
			if (candidate <= observed)
			{
				return;
			}
		}
		while (Interlocked.CompareExchange(
			       ref target,
			       candidate,
			       observed) != observed);
	}

	private static string CreateTemporaryDirectory()
	{
		string path = Path.Combine(
			Path.GetTempPath(),
			$"tsc-uh60-fees-{Guid.NewGuid():N}");
		Directory.CreateDirectory(path);
		return path;
	}

	private static void DeleteTemporaryDirectory(string path)
	{
		if (Directory.Exists(path))
		{
			Directory.Delete(path, recursive: true);
		}
	}

	private sealed class TestLogger :
		ISptLogger<FireSupportUh60TransferFeeJournal>
	{
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
