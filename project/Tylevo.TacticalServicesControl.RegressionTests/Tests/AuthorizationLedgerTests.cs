using SamSWAT.FireSupport.ArysReloaded;
using SamSWAT.FireSupport.ArysReloaded.Unity;
using SPTarkov.Server.Core.Models.Utils;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class AuthorizationLedgerTests
{
	private const int MaxStored = 2;
	private const int PendingTimeoutSeconds = 180;
	private const string FingerprintBefore =
		"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
	private const string FingerprintAfter =
		"BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

	private readonly record struct GrantAttempt(
		bool Succeeded,
		string Reason);

	private readonly record struct TerminalAttempt(
		bool IsCommit,
		bool Succeeded,
		string Reason);

	[RegressionTest]
	private static void GrantHonorsLimitAndReturnsAuthoritativeUnchangedCredits()
	{
		using var storage = new TemporaryDirectory();
		FireSupportAuthorizationLedger ledger = CreateLedger(storage.Path);

		AssertEx.True(
			ledger.TryGrant(
				"profile-a",
				ESupportType.Strafe,
				quantity: 2,
				price: 250,
				currency: "RUB",
				maxStored: MaxStored,
				pendingTimeoutSeconds: PendingTimeoutSeconds,
				out Dictionary<string, int> granted,
				out string grantReason));
		AssertEx.Equal(string.Empty, grantReason);
		AssertEx.Equal(2, granted["A10"]);

		AssertEx.False(
			ledger.TryGrant(
				"profile-a",
				ESupportType.Strafe,
				quantity: 1,
				price: 250,
				currency: "RUB",
				maxStored: MaxStored,
				pendingTimeoutSeconds: PendingTimeoutSeconds,
				out Dictionary<string, int> denied,
				out string denialReason));
		AssertEx.Equal("AuthorizationLimitReached", denialReason);
		AssertEx.Equal(2, denied["A10"]);
		AssertEx.Equal(
			2,
			ledger.GetCredits(
				"profile-a",
				PendingTimeoutSeconds,
				MaxStored)["A10"]);
	}

	[RegressionTest]
	private static async Task ConcurrentGrantsNeverExceedMaxStoredCapacity()
	{
		using var storage = new TemporaryDirectory();
		FireSupportAuthorizationLedger ledger = CreateLedger(storage.Path);
		Func<GrantAttempt>[] contenders = Enumerable
			.Range(0, 8)
			.Select(_ => new Func<GrantAttempt>(
				() => GrantAtCapacity(
					ledger,
					"profile-concurrent-grant",
					ESupportType.Strafe,
					maxStored: 1)))
			.ToArray();

		GrantAttempt[] attempts =
			await ConcurrentTest.RunTogether(contenders);
		GrantAttempt[] winners =
			attempts.Where(attempt => attempt.Succeeded).ToArray();

		AssertEx.Equal(1, winners.Length);
		AssertEx.Equal(string.Empty, winners[0].Reason);
		foreach (GrantAttempt denied in
		         attempts.Where(attempt => !attempt.Succeeded))
		{
			AssertEx.Equal("AuthorizationLimitReached", denied.Reason);
		}

		AssertEx.Equal(
			1,
			ledger.GetCredits(
				"profile-concurrent-grant",
				PendingTimeoutSeconds,
				maxStored: 1)["A10"]);
		FireSupportAuthorizationLedger reconnected =
			CreateLedger(storage.Path);
		AssertEx.Equal(
			1,
			reconnected.GetCredits(
				"profile-concurrent-grant",
				PendingTimeoutSeconds,
				maxStored: 1)["A10"]);
	}

	[RegressionTest]
	private static void PreparedPurchaseReservesCapacityAndFinalizesExactlyOnce()
	{
		using var storage = new TemporaryDirectory();
		FireSupportAuthorizationLedger ledger = CreateLedger(storage.Path);

		AssertEx.True(
			ledger.TryPreparePersistentPurchase(
				"profile-a",
				ESupportType.Strafe,
				quantity: 1,
				price: 250,
				currency: "USD",
				preDebitBalance: 1000,
				preDebitFingerprint: FingerprintBefore,
				expectedPostDebitFingerprint: FingerprintAfter,
				maxStored: 1,
				requestId: "purchase-1",
				out Dictionary<string, int> beforeFinalize,
				out FireSupportPersistentPurchaseRecord? prepared,
				out string prepareReason));
		AssertEx.Equal(string.Empty, prepareReason);
		AssertEx.Equal(0, beforeFinalize.GetValueOrDefault("A10"));
		FireSupportPersistentPurchaseRecord preparedRecord =
			AssertEx.NotNull(prepared);
		AssertEx.Equal("Prepared", preparedRecord.State);
		AssertEx.Equal("USD", preparedRecord.Currency);

		AssertEx.False(
			ledger.TryGrant(
				"profile-a",
				ESupportType.Strafe,
				quantity: 1,
				price: 250,
				currency: "USD",
				maxStored: 1,
				pendingTimeoutSeconds: PendingTimeoutSeconds,
				out _,
				out string reservationReason));
		AssertEx.Equal("AuthorizationLimitReached", reservationReason);

		AssertEx.Equal(
			PersistentPurchaseReplayStatus.Prepared,
			ledger.GetPersistentPurchaseReplay(
				"profile-a",
				ESupportType.Strafe,
				quantity: 1,
				requestId: "purchase-1",
				out _,
				out FireSupportPersistentPurchaseRecord? replayPrepared));
		AssertEx.Equal("USD", AssertEx.NotNull(replayPrepared).Currency);

		AssertEx.True(
			ledger.TryFinalizePersistentPurchase(
				"profile-a",
				ESupportType.Strafe,
				quantity: 1,
				requestId: "purchase-1",
				out Dictionary<string, int> finalized,
				out FireSupportPersistentPurchaseRecord? accepted,
				out string finalizeReason));
		AssertEx.Equal(string.Empty, finalizeReason);
		AssertEx.Equal(1, finalized["A10"]);
		AssertEx.Equal("Accepted", AssertEx.NotNull(accepted).State);

		AssertEx.True(
			ledger.TryFinalizePersistentPurchase(
				"profile-a",
				ESupportType.Strafe,
				quantity: 1,
				requestId: "purchase-1",
				out Dictionary<string, int> replayed,
				out _,
				out string replayReason));
		AssertEx.Equal("AlreadyAccepted", replayReason);
		AssertEx.Equal(1, replayed["A10"]);
		AssertEx.Equal(
			PersistentPurchaseReplayStatus.Accepted,
			ledger.GetPersistentPurchaseReplay(
				"profile-a",
				ESupportType.Strafe,
				quantity: 1,
				requestId: "purchase-1",
				out Dictionary<string, int> replayCredits,
				out _));
		AssertEx.Equal(1, replayCredits["A10"]);
	}

	[RegressionTest]
	private static void CancellingPreparedPurchaseReleasesItsReservation()
	{
		using var storage = new TemporaryDirectory();
		FireSupportAuthorizationLedger ledger = CreateLedger(storage.Path);

		AssertEx.True(
			ledger.TryPreparePersistentPurchase(
				"profile-a",
				ESupportType.Uav,
				quantity: 1,
				price: 125,
				currency: "EUR",
				preDebitBalance: 500,
				preDebitFingerprint: FingerprintBefore,
				expectedPostDebitFingerprint: FingerprintAfter,
				maxStored: 1,
				requestId: "purchase-cancel",
				out _,
				out _,
				out _));
		AssertEx.True(
			ledger.TryCancelPreparedPersistentPurchase(
				"profile-a",
				ESupportType.Uav,
				quantity: 1,
				requestId: "purchase-cancel",
				out string cancelReason));
		AssertEx.Equal(string.Empty, cancelReason);
		AssertEx.True(
			ledger.TryGrant(
				"profile-a",
				ESupportType.Uav,
				quantity: 1,
				price: 125,
				currency: "EUR",
				maxStored: 1,
				pendingTimeoutSeconds: PendingTimeoutSeconds,
				out Dictionary<string, int> credits,
				out _));
		AssertEx.Equal(1, credits["Uav"]);
	}

	[RegressionTest]
	private static void ConsumeAndCommitAreIdempotentAndTerminal()
	{
		using var storage = new TemporaryDirectory();
		FireSupportAuthorizationLedger ledger = CreateLedger(storage.Path);
		GrantOne(ledger, "profile-a", ESupportType.Extract);

		AssertEx.True(
			ledger.TryConsume(
				"profile-a",
				ESupportType.Extract,
				"dispatch-1",
				MaxStored,
				PendingTimeoutSeconds,
				out Dictionary<string, int> consumed,
				out string consumeReason));
		AssertEx.Equal(string.Empty, consumeReason);
		AssertEx.Equal(0, consumed["Extraction"]);

		AssertEx.True(
			ledger.TryConsume(
				"profile-a",
				ESupportType.Extract,
				"dispatch-1",
				MaxStored,
				PendingTimeoutSeconds,
				out Dictionary<string, int> duplicateConsume,
				out string duplicateConsumeReason));
		AssertEx.Equal("AlreadyConsumed", duplicateConsumeReason);
		AssertEx.Equal(0, duplicateConsume["Extraction"]);

		AssertEx.True(
			ledger.TryCommit(
				"profile-a",
				ESupportType.Extract,
				"dispatch-1",
				MaxStored,
				PendingTimeoutSeconds,
				out Dictionary<string, int> committed,
				out string commitReason));
		AssertEx.Equal(string.Empty, commitReason);
		AssertEx.Equal(0, committed["Extraction"]);

		AssertEx.True(
			ledger.TryCommit(
				"profile-a",
				ESupportType.Extract,
				"dispatch-1",
				MaxStored,
				PendingTimeoutSeconds,
				out _,
				out string duplicateCommitReason));
		AssertEx.Equal("AlreadyCommitted", duplicateCommitReason);

		AssertEx.False(
			ledger.TryRefund(
				"profile-a",
				ESupportType.Extract,
				"dispatch-1",
				MaxStored,
				PendingTimeoutSeconds,
				"LateFailure",
				out Dictionary<string, int> refusedRefund,
				out string refundReason));
		AssertEx.Equal("AlreadyCommitted", refundReason);
		AssertEx.Equal(0, refusedRefund["Extraction"]);

		AssertEx.False(
			ledger.TryConsume(
				"profile-a",
				ESupportType.Extract,
				"dispatch-1",
				MaxStored,
				PendingTimeoutSeconds,
				out _,
				out string terminalConsumeReason));
		AssertEx.Equal("AlreadyCommitted", terminalConsumeReason);
	}

	[RegressionTest]
	private static void RefundRestoresExactlyOnceAndBecomesTerminal()
	{
		using var storage = new TemporaryDirectory();
		FireSupportAuthorizationLedger ledger = CreateLedger(storage.Path);
		GrantOne(ledger, "profile-a", ESupportType.FocusedSweep);
		AssertEx.True(
			ledger.TryConsume(
				"profile-a",
				ESupportType.FocusedSweep,
				"dispatch-refund",
				MaxStored,
				PendingTimeoutSeconds,
				out _,
				out _));

		AssertEx.True(
			ledger.TryRefund(
				"profile-a",
				ESupportType.FocusedSweep,
				"dispatch-refund",
				MaxStored,
				PendingTimeoutSeconds,
				"ExecutorRejected",
				out Dictionary<string, int> refunded,
				out string refundReason));
		AssertEx.Equal(string.Empty, refundReason);
		AssertEx.Equal(1, refunded["FocusedSweep"]);

		AssertEx.True(
			ledger.TryRefund(
				"profile-a",
				ESupportType.FocusedSweep,
				"dispatch-refund",
				MaxStored,
				PendingTimeoutSeconds,
				"Duplicate",
				out Dictionary<string, int> duplicateRefund,
				out string duplicateReason));
		AssertEx.Equal("AlreadyRefunded", duplicateReason);
		AssertEx.Equal(1, duplicateRefund["FocusedSweep"]);

		AssertEx.False(
			ledger.TryCommit(
				"profile-a",
				ESupportType.FocusedSweep,
				"dispatch-refund",
				MaxStored,
				PendingTimeoutSeconds,
				out _,
				out string commitReason));
		AssertEx.Equal("AlreadyRefunded", commitReason);
	}

	[RegressionTest]
	private static async Task ConcurrentCommitAndRefundPersistOneTerminalState()
	{
		using var storage = new TemporaryDirectory();
		const string profileId = "profile-concurrent-terminal";
		const string requestId = "dispatch-concurrent-terminal";
		FireSupportAuthorizationLedger ledger = CreateLedger(storage.Path);
		GrantOne(ledger, profileId, ESupportType.Uav);
		AssertEx.True(
			ledger.TryConsume(
				profileId,
				ESupportType.Uav,
				requestId,
				MaxStored,
				PendingTimeoutSeconds,
				out _,
				out string consumeReason),
			consumeReason);

		TerminalAttempt[] attempts = await ConcurrentTest.RunTogether(
			() => Commit(
				ledger,
				profileId,
				ESupportType.Uav,
				requestId),
			() => Refund(
				ledger,
				profileId,
				ESupportType.Uav,
				requestId));
		TerminalAttempt[] winners =
			attempts.Where(attempt => attempt.Succeeded).ToArray();

		AssertEx.Equal(1, winners.Length);
		AssertEx.Equal(string.Empty, winners[0].Reason);
		TerminalAttempt loser =
			attempts.Single(attempt => !attempt.Succeeded);
		AssertEx.Equal(
			winners[0].IsCommit
				? "AlreadyCommitted"
				: "AlreadyRefunded",
			loser.Reason);
		int expectedCredits = winners[0].IsCommit ? 0 : 1;
		AssertEx.Equal(
			expectedCredits,
			ledger.GetCredits(
				profileId,
				PendingTimeoutSeconds,
				MaxStored)["Uav"]);

		FireSupportAuthorizationLedger reconnected =
			CreateLedger(storage.Path);
		AssertEx.Equal(
			expectedCredits,
			reconnected.GetCredits(
				profileId,
				PendingTimeoutSeconds,
				MaxStored)["Uav"]);
		if (winners[0].IsCommit)
		{
			AssertEx.True(
				reconnected.TryCommit(
					profileId,
					ESupportType.Uav,
					requestId,
					MaxStored,
					PendingTimeoutSeconds,
					out Dictionary<string, int> committed,
					out string commitReason));
			AssertEx.Equal("AlreadyCommitted", commitReason);
			AssertEx.Equal(0, committed["Uav"]);
			AssertEx.False(
				reconnected.TryRefund(
					profileId,
					ESupportType.Uav,
					requestId,
					MaxStored,
					PendingTimeoutSeconds,
					"ReplayRefund",
					out _,
					out string refundReason));
			AssertEx.Equal("AlreadyCommitted", refundReason);
		}
		else
		{
			AssertEx.True(
				reconnected.TryRefund(
					profileId,
					ESupportType.Uav,
					requestId,
					MaxStored,
					PendingTimeoutSeconds,
					"ReplayRefund",
					out Dictionary<string, int> refunded,
					out string refundReason));
			AssertEx.Equal("AlreadyRefunded", refundReason);
			AssertEx.Equal(1, refunded["Uav"]);
			AssertEx.False(
				reconnected.TryCommit(
					profileId,
					ESupportType.Uav,
					requestId,
					MaxStored,
					PendingTimeoutSeconds,
					out _,
					out string commitReason));
			AssertEx.Equal("AlreadyRefunded", commitReason);
		}

		AssertEx.False(
			reconnected.TryConsume(
				profileId,
				ESupportType.Uav,
				requestId,
				MaxStored,
				PendingTimeoutSeconds,
				out _,
				out string replayReason));
		AssertEx.Equal(
			winners[0].IsCommit
				? "AlreadyCommitted"
				: "AlreadyRefunded",
			replayReason);
	}

	[RegressionTest]
	private static void ReconnectPreservesCommittedDebitAndTerminalRequestId()
	{
		using var storage = new TemporaryDirectory();
		FireSupportAuthorizationLedger first = CreateLedger(storage.Path);
		GrantOne(first, "profile-a", ESupportType.PriorityExfil);
		AssertEx.True(
			first.TryConsume(
				"profile-a",
				ESupportType.PriorityExfil,
				"reconnect-commit",
				MaxStored,
				PendingTimeoutSeconds,
				out _,
				out _));
		AssertEx.True(
			first.TryCommit(
				"profile-a",
				ESupportType.PriorityExfil,
				"reconnect-commit",
				MaxStored,
				PendingTimeoutSeconds,
				out _,
				out _));

		FireSupportAuthorizationLedger reconnected = CreateLedger(storage.Path);
		AssertEx.Equal(
			0,
			reconnected.GetCredits(
				"profile-a",
				PendingTimeoutSeconds,
				MaxStored)["PriorityExfil"]);
		AssertEx.False(
			reconnected.TryConsume(
				"profile-a",
				ESupportType.PriorityExfil,
				"reconnect-commit",
				MaxStored,
				PendingTimeoutSeconds,
				out _,
				out string replayReason));
		AssertEx.Equal("AlreadyCommitted", replayReason);
	}

	[RegressionTest]
	private static void ExpiredPendingUseRefundsOnceAcrossReconnects()
	{
		using var storage = new TemporaryDirectory();
		FireSupportAuthorizationLedger first = CreateLedger(storage.Path);
		GrantOne(first, "profile-a", ESupportType.Uav);
		AssertEx.True(
			first.TryConsume(
				"profile-a",
				ESupportType.Uav,
				"expired-pending",
				MaxStored,
				PendingTimeoutSeconds,
				out _,
				out _));

		BackdateAuthorizationUse(
			storage.Path,
			"profile-a",
			"expired-pending",
			DateTimeOffset.UtcNow.AddMinutes(-10));

		FireSupportAuthorizationLedger reconnected = CreateLedger(storage.Path);
		AssertEx.Equal(
			1,
			reconnected.GetCredits(
				"profile-a",
				pendingTimeoutSeconds: 1,
				maxStored: MaxStored)["Uav"]);

		FireSupportAuthorizationLedger reconnectedAgain = CreateLedger(storage.Path);
		AssertEx.Equal(
			1,
			reconnectedAgain.GetCredits(
				"profile-a",
				pendingTimeoutSeconds: 1,
				maxStored: MaxStored)["Uav"]);
		AssertEx.False(
			reconnectedAgain.TryConsume(
				"profile-a",
				ESupportType.Uav,
				"expired-pending",
				MaxStored,
				pendingTimeoutSeconds: 1,
				out _,
				out string replayReason));
		AssertEx.Equal("AuthorizationUseExpired", replayReason);
	}

	[RegressionTest]
	private static void ProfilesAreIsolatedEvenWhenRequestIdsMatch()
	{
		using var storage = new TemporaryDirectory();
		FireSupportAuthorizationLedger ledger = CreateLedger(storage.Path);
		GrantOne(ledger, "profile-a", ESupportType.Strafe);
		GrantOne(ledger, "profile-b", ESupportType.Uav);

		AssertEx.True(
			ledger.TryConsume(
				"profile-a",
				ESupportType.Strafe,
				"shared-request-id",
				MaxStored,
				PendingTimeoutSeconds,
				out _,
				out _));
		AssertEx.True(
			ledger.TryConsume(
				"profile-b",
				ESupportType.Uav,
				"shared-request-id",
				MaxStored,
				PendingTimeoutSeconds,
				out _,
				out _));
		AssertEx.True(
			ledger.TryRefund(
				"profile-a",
				ESupportType.Strafe,
				"shared-request-id",
				MaxStored,
				PendingTimeoutSeconds,
				"ProfileAOnly",
				out _,
				out _));

		Dictionary<string, int> profileA =
			ledger.GetCredits("profile-a", PendingTimeoutSeconds, MaxStored);
		Dictionary<string, int> profileB =
			ledger.GetCredits("profile-b", PendingTimeoutSeconds, MaxStored);
		AssertEx.Equal(1, profileA["A10"]);
		AssertEx.Equal(0, profileB["Uav"]);

		FireSupportAuthorizationLedger reconnected = CreateLedger(storage.Path);
		AssertEx.Equal(
			1,
			reconnected.GetCredits(
				"profile-a",
				PendingTimeoutSeconds,
				MaxStored)["A10"]);
		AssertEx.Equal(
			0,
			reconnected.GetCredits(
				"profile-b",
				PendingTimeoutSeconds,
				MaxStored)["Uav"]);
	}

	private static FireSupportAuthorizationLedger CreateLedger(string storagePath)
	{
		var ledger = new FireSupportAuthorizationLedger(new TestLogger());
		ledger.Initialize(storagePath);
		return ledger;
	}

	private static void GrantOne(
		FireSupportAuthorizationLedger ledger,
		string profileId,
		ESupportType supportType)
	{
		AssertEx.True(
			ledger.TryGrant(
				profileId,
				supportType,
				quantity: 1,
				price: 100,
				currency: "RUB",
				maxStored: MaxStored,
				pendingTimeoutSeconds: PendingTimeoutSeconds,
				out _,
				out string reason),
			reason);
	}

	private static GrantAttempt GrantAtCapacity(
		FireSupportAuthorizationLedger ledger,
		string profileId,
		ESupportType supportType,
		int maxStored)
	{
		bool succeeded = ledger.TryGrant(
			profileId,
			supportType,
			quantity: 1,
			price: 100,
			currency: "RUB",
			maxStored: maxStored,
			pendingTimeoutSeconds: PendingTimeoutSeconds,
			out _,
			out string reason);
		return new GrantAttempt(succeeded, reason);
	}

	private static TerminalAttempt Commit(
		FireSupportAuthorizationLedger ledger,
		string profileId,
		ESupportType supportType,
		string requestId)
	{
		bool succeeded = ledger.TryCommit(
			profileId,
			supportType,
			requestId,
			MaxStored,
			PendingTimeoutSeconds,
			out _,
			out string reason);
		return new TerminalAttempt(
			IsCommit: true,
			succeeded,
			reason);
	}

	private static TerminalAttempt Refund(
		FireSupportAuthorizationLedger ledger,
		string profileId,
		ESupportType supportType,
		string requestId)
	{
		bool succeeded = ledger.TryRefund(
			profileId,
			supportType,
			requestId,
			MaxStored,
			PendingTimeoutSeconds,
			refundReason: "ConcurrentRefund",
			out _,
			out string reason);
		return new TerminalAttempt(
			IsCommit: false,
			succeeded,
			reason);
	}

	private static void BackdateAuthorizationUse(
		string storagePath,
		string profileId,
		string requestId,
		DateTimeOffset createdUtc)
	{
		string path = Path.Combine(storagePath, "tsc-ledger.json");
		JsonNode root = AssertEx.NotNull(JsonNode.Parse(File.ReadAllText(path)));
		JsonNode use = AssertEx.NotNull(
			root["profiles"]?[profileId]?["authorizationUses"]?[requestId]);
		use["createdUtc"] = createdUtc.ToString("O");
		File.WriteAllText(
			path,
			root.ToJsonString(new JsonSerializerOptions
			{
				WriteIndented = true
			}));
	}

	private sealed class TestLogger : ISptLogger<FireSupportAuthorizationLedger>
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

	private sealed class TemporaryDirectory : IDisposable
	{
		public TemporaryDirectory()
		{
			Path = System.IO.Path.Combine(
				System.IO.Path.GetTempPath(),
				"tsc-regression-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(Path);
		}

		public string Path { get; }

		public void Dispose()
		{
			try
			{
				if (Directory.Exists(Path))
				{
					Directory.Delete(Path, recursive: true);
				}
			}
			catch
			{
				// A failed cleanup must not hide the tested assertion.
			}
		}
	}
}
