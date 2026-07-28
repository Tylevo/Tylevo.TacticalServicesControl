using SamSWAT.FireSupport.ArysReloaded;
using SamSWAT.FireSupport.ArysReloaded.Unity;
using SPTarkov.Server.Core.Models.Utils;
using System.Text.Json.Nodes;

internal static class AuthorizationLedgerMigrationTests
{
	private const string ProfileId = "published-v1.0.8-profile";
	private const int MaxStored = 4;
	private const int MigrationPendingTimeoutSeconds = int.MaxValue;

	[RegressionTest]
	private static void PublishedSchemaOneLedgerMigratesToSchemaFiveExactlyOnce()
	{
		using var storage = new LedgerMigrationTemporaryDirectory();
		string ledgerPath = Path.Combine(storage.Path, "tsc-ledger.json");
		File.WriteAllText(ledgerPath, PublishedV108LedgerFixture);

		FireSupportAuthorizationLedger first = CreateLedger(storage.Path);
		AssertPreservedCredits(first);
		AssertSchemaFiveLedger(ledgerPath);
		string afterFirstMigration = File.ReadAllText(ledgerPath);

		FireSupportAuthorizationLedger reconnected = CreateLedger(storage.Path);
		AssertPreservedCredits(reconnected);
		AssertSchemaFiveLedger(ledgerPath);
		AssertEx.Equal(
			afterFirstMigration,
			File.ReadAllText(ledgerPath),
			"Reconnecting must not mutate an already-migrated ledger.");

		AssertEx.True(
			reconnected.TryConsume(
				ProfileId,
				ESupportType.Uav,
				"legacy-pending",
				MaxStored,
				MigrationPendingTimeoutSeconds,
				out Dictionary<string, int> pendingReplayCredits,
				out string pendingReplayReason));
		AssertEx.Equal("AlreadyConsumed", pendingReplayReason);
		AssertEx.Equal(0, pendingReplayCredits["Uav"]);

		AssertEx.True(
			reconnected.TryCommit(
				ProfileId,
				ESupportType.Extract,
				"legacy-commit",
				MaxStored,
				MigrationPendingTimeoutSeconds,
				out Dictionary<string, int> committedReplayCredits,
				out string committedReplayReason));
		AssertEx.Equal("AlreadyCommitted", committedReplayReason);
		AssertEx.Equal(0, committedReplayCredits["Extraction"]);
		AssertEx.False(
			reconnected.TryRefund(
				ProfileId,
				ESupportType.Extract,
				"legacy-commit",
				MaxStored,
				MigrationPendingTimeoutSeconds,
				"DuplicateRefund",
				out _,
				out string committedRefundReason));
		AssertEx.Equal("AlreadyCommitted", committedRefundReason);

		AssertEx.True(
			reconnected.TryRefund(
				ProfileId,
				ESupportType.FocusedSweep,
				"legacy-refund",
				MaxStored,
				MigrationPendingTimeoutSeconds,
				"DuplicateRefund",
				out Dictionary<string, int> refundedReplayCredits,
				out string refundedReplayReason));
		AssertEx.Equal("AlreadyRefunded", refundedReplayReason);
		AssertEx.Equal(1, refundedReplayCredits["FocusedSweep"]);
		AssertEx.False(
			reconnected.TryCommit(
				ProfileId,
				ESupportType.FocusedSweep,
				"legacy-refund",
				MaxStored,
				MigrationPendingTimeoutSeconds,
				out _,
				out string refundedCommitReason));
		AssertEx.Equal("AlreadyRefunded", refundedCommitReason);

		AssertEx.Equal(
			afterFirstMigration,
			File.ReadAllText(ledgerPath),
			"Terminal and pending request replays must not duplicate debits, refunds, or journal entries.");

		FireSupportAuthorizationLedger reconnectedAgain = CreateLedger(storage.Path);
		AssertPreservedCredits(reconnectedAgain);
		AssertSchemaFiveLedger(ledgerPath);
		AssertEx.Equal(
			afterFirstMigration,
			File.ReadAllText(ledgerPath),
			"Repeated reconnects must leave the schema-5 ledger byte-for-byte stable.");
	}

	private static void AssertPreservedCredits(FireSupportAuthorizationLedger ledger)
	{
		Dictionary<string, int> credits = ledger.GetCredits(
			ProfileId,
			MigrationPendingTimeoutSeconds,
			MaxStored);
		AssertEx.Equal(4, credits.Count);
		AssertEx.Equal(2, credits["A10"]);
		AssertEx.Equal(0, credits["Uav"]);
		AssertEx.Equal(0, credits["Extraction"]);
		AssertEx.Equal(1, credits["FocusedSweep"]);
	}

	private static void AssertSchemaFiveLedger(string ledgerPath)
	{
		JsonObject root = RequiredObject(
			JsonNode.Parse(File.ReadAllText(ledgerPath)),
			"ledger root");
		AssertEx.Equal(5, RequiredValue<int>(root["schemaVersion"], "schemaVersion"));

		JsonObject profiles = RequiredObject(root["profiles"], "profiles");
		AssertEx.Equal(1, profiles.Count);
		JsonObject profile = RequiredObject(profiles[ProfileId], ProfileId);
		AssertEx.Equal(0, RequiredObject(profile["pending"], "pending").Count);

		JsonObject authorizationUses =
			RequiredObject(profile["authorizationUses"], "authorizationUses");
		AssertEx.Equal(3, authorizationUses.Count);
		AssertAuthorizationUse(
			authorizationUses,
			"legacy-pending",
			"Uav",
			"Pending");
		AssertAuthorizationUse(
			authorizationUses,
			"legacy-commit",
			"Extraction",
			"Committed");
		AssertAuthorizationUse(
			authorizationUses,
			"legacy-refund",
			"FocusedSweep",
			"Refunded");

		JsonArray transactions =
			RequiredArray(profile["transactions"], "transactions");
		AssertEx.Equal(7, transactions.Count);
		foreach (JsonNode? transactionNode in transactions)
		{
			JsonObject transaction =
				RequiredObject(transactionNode, "transaction");
			AssertEx.Equal(
				"RUB",
				RequiredValue<string>(transaction["currency"], "transaction.currency"),
				"Transactions from schemas without currency must migrate to RUB.");
		}
	}

	private static void AssertAuthorizationUse(
		JsonObject authorizationUses,
		string requestId,
		string service,
		string state)
	{
		JsonObject authorizationUse =
			RequiredObject(authorizationUses[requestId], requestId);
		AssertEx.Equal(
			requestId,
			RequiredValue<string>(
				authorizationUse["requestId"],
				$"{requestId}.requestId"));
		AssertEx.Equal(
			service,
			RequiredValue<string>(
				authorizationUse["service"],
				$"{requestId}.service"));
		AssertEx.Equal(
			state,
			RequiredValue<string>(
				authorizationUse["state"],
				$"{requestId}.state"));
		AssertEx.Equal(
			1,
			RequiredValue<int>(
				authorizationUse["quantity"],
				$"{requestId}.quantity"));
	}

	private static JsonObject RequiredObject(JsonNode? node, string description)
	{
		return AssertEx.NotNull(
			node as JsonObject,
			$"Expected {description} to be a JSON object.");
	}

	private static JsonArray RequiredArray(JsonNode? node, string description)
	{
		return AssertEx.NotNull(
			node as JsonArray,
			$"Expected {description} to be a JSON array.");
	}

	private static T RequiredValue<T>(JsonNode? node, string description)
	{
		return AssertEx.NotNull(
				node,
				$"Expected {description} to be present.")
			.GetValue<T>();
	}

	private static FireSupportAuthorizationLedger CreateLedger(string storagePath)
	{
		var ledger =
			new FireSupportAuthorizationLedger(new LedgerMigrationTestLogger());
		ledger.Initialize(storagePath);
		return ledger;
	}

	private const string PublishedV108LedgerFixture =
		"""
		{
		  "schemaVersion": 1,
		  "profiles": {
		    "published-v1.0.8-profile": {
		      "credits": {
		        "A10": 2,
		        "Uav": 0,
		        "Extraction": 0,
		        "FocusedSweep": 1
		      },
		      "pending": {
		        "legacy-pending": {
		          "requestId": "legacy-pending",
		          "service": "Uav",
		          "quantity": 1,
		          "createdUtc": "2026-07-13T03:00:00+00:00"
		        }
		      },
		      "transactions": [
		        {
		          "id": "txn-purchase",
		          "type": "Purchase",
		          "service": "A10",
		          "quantity": 2,
		          "price": 500000,
		          "requestId": "",
		          "reason": "",
		          "createdUtc": "2026-07-13T02:55:00+00:00"
		        },
		        {
		          "id": "txn-pending-consume",
		          "type": "Consume",
		          "service": "Uav",
		          "quantity": 1,
		          "price": 0,
		          "requestId": "legacy-pending",
		          "reason": "",
		          "createdUtc": "2026-07-13T03:00:00+00:00"
		        },
		        {
		          "id": "txn-commit-consume",
		          "type": "Consume",
		          "service": "Extraction",
		          "quantity": 1,
		          "price": 0,
		          "requestId": "legacy-commit",
		          "reason": "",
		          "createdUtc": "2026-07-13T03:01:00+00:00"
		        },
		        {
		          "id": "txn-commit",
		          "type": "Commit",
		          "service": "Extraction",
		          "quantity": 1,
		          "price": 0,
		          "requestId": "legacy-commit",
		          "reason": "DispatchAccepted",
		          "createdUtc": "2026-07-13T03:01:01+00:00"
		        },
		        {
		          "id": "txn-refund-consume",
		          "type": "Consume",
		          "service": "FocusedSweep",
		          "quantity": 1,
		          "price": 0,
		          "requestId": "legacy-refund",
		          "reason": "",
		          "createdUtc": "2026-07-13T03:02:00+00:00"
		        },
		        {
		          "id": "txn-refund",
		          "type": "Refund",
		          "service": "FocusedSweep",
		          "quantity": 1,
		          "price": 0,
		          "requestId": "legacy-refund",
		          "reason": "ExecutorRejected",
		          "createdUtc": "2026-07-13T03:02:01+00:00"
		        },
		        {
		          "id": "txn-late-commit",
		          "type": "Commit",
		          "service": "FocusedSweep",
		          "quantity": 1,
		          "price": 0,
		          "requestId": "legacy-refund",
		          "reason": "LegacyFallback",
		          "createdUtc": "2026-07-13T03:02:02+00:00"
		        }
		      ]
		    }
		  }
		}
		""";

	private sealed class LedgerMigrationTestLogger :
		ISptLogger<FireSupportAuthorizationLedger>
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

	private sealed class LedgerMigrationTemporaryDirectory : IDisposable
	{
		public LedgerMigrationTemporaryDirectory()
		{
			Path = System.IO.Path.Combine(
				System.IO.Path.GetTempPath(),
				"tsc-ledger-migration-" + Guid.NewGuid().ToString("N"));
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
