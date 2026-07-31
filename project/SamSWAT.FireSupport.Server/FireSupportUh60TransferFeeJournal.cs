using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Utils;
using System.Text.Json;
using IOPath = System.IO.Path;

namespace SamSWAT.FireSupport.ArysReloaded;

/// <summary>
/// Write-ahead journal for stash-backed UH-60 transfer fees. This is kept
/// separate from the authorization ledger because a transfer-fee transaction
/// grants no service credit and has its own prepare/commit/refund lifecycle.
/// </summary>
[Injectable(InjectionType.Singleton)]
public sealed class FireSupportUh60TransferFeeJournal(
	ISptLogger<FireSupportUh60TransferFeeJournal> logger)
{
	public const string DebitPendingState = "DebitPending";
	public const string PreparedState = "Prepared";
	public const string CommittedState = "Committed";
	public const string RefundPendingState = "RefundPending";
	public const string RefundedState = "Refunded";
	public const int MaxTransactionIdLength = 128;
	public const int MaxAmountRoubles = 10_000_000;
	public const int MaxNonterminalTransactionsPerProfile = 8;

	private const int CurrentSchemaVersion = 1;
	internal const int MaxTransactions = 4096;
	private static readonly TimeSpan s_terminalRetention = TimeSpan.FromDays(30);
	private static readonly JsonSerializerOptions s_jsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true
	};

	private readonly object _gate = new();
	private string _journalPath = string.Empty;
	private FireSupportUh60TransferFeeJournalState _state = new();

	public void Initialize(string storageDirectory)
	{
		Directory.CreateDirectory(storageDirectory);
		_journalPath = IOPath.Combine(
			storageDirectory,
			"tsc-uh60-transfer-fees.json");

		lock (_gate)
		{
			_state = LoadLocked();
			NormalizeStateLocked();
			SaveLocked();
		}
	}

	public bool TryGet(
		string transactionId,
		out FireSupportUh60TransferFeeRecord? record)
	{
		record = null;
		string normalizedId = NormalizeTransactionId(transactionId);
		if (normalizedId.Length == 0)
		{
			return false;
		}

		lock (_gate)
		{
			if (!_state.Transactions.TryGetValue(
				    normalizedId,
				    out FireSupportUh60TransferFeeRecord? stored) ||
			    stored == null)
			{
				return false;
			}

			record = CloneRecord(stored);
			return true;
		}
	}

	public bool TryCreate(
		FireSupportUh60TransferFeeRecord record,
		out FireSupportUh60TransferFeeRecord? current,
		out string reason)
	{
		current = null;
		reason = string.Empty;
		if (!IsValidRecord(record))
		{
			reason = "InvalidFeeJournalRecord";
			return false;
		}

		lock (_gate)
		{
			string transactionId = NormalizeTransactionId(record.TransactionId);
			if (_state.Transactions.TryGetValue(
				    transactionId,
				    out FireSupportUh60TransferFeeRecord? existing) &&
			    existing != null)
			{
				current = CloneRecord(existing);
				reason = "AlreadyExists";
				return false;
			}

			FireSupportUh60TransferFeeJournalState snapshot = CloneState(_state);
			PruneTerminalTransactionsLocked(DateTimeOffset.UtcNow);
			if (!IsTerminalState(record.State) &&
			    CountNonterminalTransactionsLocked(record.ProfileId) >=
			    MaxNonterminalTransactionsPerProfile)
			{
				_state = snapshot;
				reason = "FeeProfileTransactionLimitReached";
				return false;
			}

			if (_state.Transactions.Count >= MaxTransactions)
			{
				// Normal retention protects terminal request idempotency. Under
				// actual capacity pressure, sacrifice only the oldest terminal
				// entries so stale successful transfers cannot deny new work.
				// Prepared/pending records are never evicted.
				PruneOldestTerminalTransactionsForCapacityLocked();
				if (_state.Transactions.Count >= MaxTransactions)
				{
					_state = snapshot;
					reason = "FeeJournalCapacityReached";
					return false;
				}
			}

			FireSupportUh60TransferFeeRecord stored = CloneRecord(record);
			stored.TransactionId = transactionId;
			_state.Transactions[transactionId] = stored;
			if (!TrySaveMutationLocked(snapshot, out reason))
			{
				return false;
			}

			current = CloneRecord(stored);
			return true;
		}
	}

	public bool TrySave(
		FireSupportUh60TransferFeeRecord record,
		out string reason)
	{
		reason = string.Empty;
		if (!IsValidRecord(record))
		{
			reason = "InvalidFeeJournalRecord";
			return false;
		}

		lock (_gate)
		{
			string transactionId = NormalizeTransactionId(record.TransactionId);
			if (!_state.Transactions.TryGetValue(
				    transactionId,
				    out FireSupportUh60TransferFeeRecord? existing) ||
			    existing == null)
			{
				reason = "FeeTransactionNotFound";
				return false;
			}

			if (!string.Equals(
				    existing.ProfileId,
				    record.ProfileId,
				    StringComparison.OrdinalIgnoreCase) ||
			    existing.AmountRoubles != record.AmountRoubles)
			{
				reason = "FeeTransactionConflict";
				return false;
			}

			if (!IsAllowedTransition(existing.State, record.State))
			{
				reason = IsTerminalState(existing.State)
					? "FeeTransactionTerminal"
					: "InvalidFeeTransactionTransition";
				return false;
			}

			FireSupportUh60TransferFeeJournalState snapshot = CloneState(_state);
			FireSupportUh60TransferFeeRecord stored = CloneRecord(record);
			stored.TransactionId = transactionId;
			_state.Transactions[transactionId] = stored;
			return TrySaveMutationLocked(snapshot, out reason);
		}
	}

	private FireSupportUh60TransferFeeJournalState LoadLocked()
	{
		if (string.IsNullOrWhiteSpace(_journalPath) ||
		    !File.Exists(_journalPath))
		{
			return new FireSupportUh60TransferFeeJournalState();
		}

		try
		{
			string json = File.ReadAllText(_journalPath);
			return JsonSerializer.Deserialize<FireSupportUh60TransferFeeJournalState>(
				       json,
				       s_jsonOptions) ??
			       new FireSupportUh60TransferFeeJournalState();
		}
		catch (Exception exception)
		{
			logger.Error(
				$"TSC UH-60 transfer-fee journal could not be read: {_journalPath}",
				exception);
			PreserveCorruptJournal();
			FireSupportUh60TransferFeeJournalState? backup = TryLoadBackup();
			if (backup != null)
			{
				logger.Warning(
					"TSC UH-60 transfer-fee journal recovered from its backup file.");
				return backup;
			}

			logger.Warning(
				"TSC UH-60 transfer-fee journal started empty because neither the primary nor backup file was readable.");
			return new FireSupportUh60TransferFeeJournalState();
		}
	}

	private FireSupportUh60TransferFeeJournalState? TryLoadBackup()
	{
		string backupPath = _journalPath + ".bak";
		if (!File.Exists(backupPath))
		{
			return null;
		}

		try
		{
			return JsonSerializer.Deserialize<FireSupportUh60TransferFeeJournalState>(
				File.ReadAllText(backupPath),
				s_jsonOptions);
		}
		catch (Exception exception)
		{
			logger.Error(
				$"TSC UH-60 transfer-fee journal backup could not be read: {backupPath}",
				exception);
			return null;
		}
	}

	private void PreserveCorruptJournal()
	{
		try
		{
			string timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
			File.Move(
				_journalPath,
				_journalPath + $".corrupt-{timestamp}",
				overwrite: true);
		}
		catch
		{
			// A preservation failure must not prevent normal SPT startup.
		}
	}

	private bool TrySaveMutationLocked(
		FireSupportUh60TransferFeeJournalState snapshot,
		out string reason)
	{
		try
		{
			SaveLocked();
			reason = string.Empty;
			return true;
		}
		catch (Exception exception)
		{
			_state = snapshot;
			NormalizeStateLocked();
			reason = "FeeJournalSaveFailed";
			logger.Error(
				"TSC UH-60 transfer-fee journal mutation was rolled back after a disk write failure.",
				exception);
			return false;
		}
	}

	private void SaveLocked()
	{
		if (string.IsNullOrWhiteSpace(_journalPath))
		{
			throw new InvalidOperationException(
				"UH-60 transfer-fee journal is not initialized.");
		}

		string tempPath = _journalPath + ".tmp";
		string backupPath = _journalPath + ".bak";
		try
		{
			File.WriteAllText(
				tempPath,
				JsonSerializer.Serialize(_state, s_jsonOptions));
			if (File.Exists(_journalPath))
			{
				File.Replace(
					tempPath,
					_journalPath,
					backupPath,
					ignoreMetadataErrors: true);
			}
			else
			{
				File.Move(tempPath, _journalPath);
			}
		}
		finally
		{
			if (File.Exists(tempPath))
			{
				File.Delete(tempPath);
			}
		}
	}

	private void NormalizeStateLocked()
	{
		if (_state.SchemaVersion != CurrentSchemaVersion)
		{
			logger.Warning(
				$"TSC UH-60 transfer-fee journal schema {_state.SchemaVersion} is unsupported; starting a new fee journal.");
			_state = new FireSupportUh60TransferFeeJournalState();
			return;
		}

		_state.Transactions ??=
			new Dictionary<string, FireSupportUh60TransferFeeRecord>(
				StringComparer.OrdinalIgnoreCase);
		var normalized =
			new Dictionary<string, FireSupportUh60TransferFeeRecord>(
				StringComparer.OrdinalIgnoreCase);
		foreach (FireSupportUh60TransferFeeRecord record in
		         _state.Transactions.Values)
		{
			if (!IsValidRecord(record))
			{
				continue;
			}

			string transactionId = NormalizeTransactionId(record.TransactionId);
			record.TransactionId = transactionId;
			normalized[transactionId] = record;
		}

		_state.SchemaVersion = CurrentSchemaVersion;
		_state.Transactions = normalized;
		PruneTerminalTransactionsLocked(DateTimeOffset.UtcNow);
	}

	private void PruneTerminalTransactionsLocked(DateTimeOffset now)
	{
		DateTimeOffset cutoff = now - s_terminalRetention;
		foreach (string transactionId in _state.Transactions
			         .Where(pair =>
				         pair.Value != null &&
				         IsTerminalState(pair.Value.State) &&
				         pair.Value.UpdatedUtc < cutoff)
			         .OrderBy(pair => pair.Value.UpdatedUtc)
			         .Select(pair => pair.Key)
			         .ToList())
		{
			_state.Transactions.Remove(transactionId);
		}
	}

	private int CountNonterminalTransactionsLocked(string profileId)
	{
		return _state.Transactions.Values.Count(record =>
			record != null &&
			!IsTerminalState(record.State) &&
			string.Equals(
				record.ProfileId,
				profileId,
				StringComparison.OrdinalIgnoreCase));
	}

	private void PruneOldestTerminalTransactionsForCapacityLocked()
	{
		foreach (string transactionId in _state.Transactions
			         .Where(pair =>
				         pair.Value != null &&
				         IsTerminalState(pair.Value.State))
			         .OrderBy(pair => pair.Value.UpdatedUtc)
			         .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
			         .Select(pair => pair.Key)
			         .ToList())
		{
			if (_state.Transactions.Count < MaxTransactions)
			{
				break;
			}

			_state.Transactions.Remove(transactionId);
		}
	}

	private static bool IsValidRecord(
		FireSupportUh60TransferFeeRecord? record)
	{
		if (record == null ||
		    NormalizeTransactionId(record.TransactionId).Length == 0 ||
		    string.IsNullOrWhiteSpace(record.ProfileId) ||
		    record.AmountRoubles is <= 0 or > MaxAmountRoubles ||
		    !IsKnownState(record.State))
		{
			return false;
		}

		record.Debits ??= new List<FireSupportUh60TransferFeeDebit>();
		record.RefundCredits ??=
			new List<FireSupportUh60TransferFeeRefundCredit>();
		if (!record.Debits.All(debit =>
			debit != null &&
			debit.AmountRoubles > 0 &&
			debit.OriginalItem != null) ||
		    record.Debits.Sum(debit => (long)debit.AmountRoubles) !=
		    record.AmountRoubles)
		{
			return false;
		}

		if (!string.Equals(
			    record.State,
			    RefundPendingState,
			    StringComparison.Ordinal) &&
		    !string.Equals(
			    record.State,
			    RefundedState,
			    StringComparison.Ordinal))
		{
			return true;
		}

		return record.RefundCredits.All(credit =>
			       credit != null &&
			       credit.AmountRoubles > 0 &&
			       credit.BeforeCount >= 0 &&
			       !string.IsNullOrWhiteSpace(credit.TargetItemId)) &&
		       record.RefundCredits.Sum(
			       credit => (long)credit.AmountRoubles) ==
		       record.AmountRoubles;
	}

	private static bool IsKnownState(string state)
	{
		return string.Equals(state, DebitPendingState, StringComparison.Ordinal) ||
		       string.Equals(state, PreparedState, StringComparison.Ordinal) ||
		       string.Equals(state, CommittedState, StringComparison.Ordinal) ||
		       string.Equals(state, RefundPendingState, StringComparison.Ordinal) ||
		       string.Equals(state, RefundedState, StringComparison.Ordinal);
	}

	private static bool IsTerminalState(string state)
	{
		return string.Equals(state, CommittedState, StringComparison.Ordinal) ||
		       string.Equals(state, RefundedState, StringComparison.Ordinal);
	}

	private static bool IsAllowedTransition(
		string currentState,
		string nextState)
	{
		if (string.Equals(
			    currentState,
			    nextState,
			    StringComparison.Ordinal))
		{
			return true;
		}

		return currentState switch
		{
			DebitPendingState =>
				nextState is PreparedState or RefundedState,
			PreparedState =>
				nextState is CommittedState or RefundPendingState,
			RefundPendingState =>
				string.Equals(
					nextState,
					RefundedState,
					StringComparison.Ordinal),
			_ => false
		};
	}

	private static string NormalizeTransactionId(string? transactionId)
	{
		string normalized = transactionId?.Trim() ?? string.Empty;
		return normalized.Length <= MaxTransactionIdLength
			? normalized
			: string.Empty;
	}

	private static FireSupportUh60TransferFeeJournalState CloneState(
		FireSupportUh60TransferFeeJournalState state)
	{
		string json = JsonSerializer.Serialize(state, s_jsonOptions);
		FireSupportUh60TransferFeeJournalState clone =
			JsonSerializer.Deserialize<FireSupportUh60TransferFeeJournalState>(
				json,
				s_jsonOptions) ??
			new FireSupportUh60TransferFeeJournalState();
		clone.Transactions =
			new Dictionary<string, FireSupportUh60TransferFeeRecord>(
				clone.Transactions ??
				new Dictionary<string, FireSupportUh60TransferFeeRecord>(),
				StringComparer.OrdinalIgnoreCase);
		return clone;
	}

	private static FireSupportUh60TransferFeeRecord CloneRecord(
		FireSupportUh60TransferFeeRecord record)
	{
		string json = JsonSerializer.Serialize(record, s_jsonOptions);
		return JsonSerializer.Deserialize<FireSupportUh60TransferFeeRecord>(
			       json,
			       s_jsonOptions) ??
		       throw new InvalidOperationException(
			       "Unable to clone UH-60 transfer-fee journal record.");
	}
}

public sealed class FireSupportUh60TransferFeeJournalState
{
	public int SchemaVersion { get; set; } = 1;
	public Dictionary<string, FireSupportUh60TransferFeeRecord> Transactions
	{
		get;
		set;
	} = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class FireSupportUh60TransferFeeRecord
{
	public string TransactionId { get; set; } = string.Empty;
	public string ProfileId { get; set; } = string.Empty;
	public int AmountRoubles { get; set; }
	public string State { get; set; } =
		FireSupportUh60TransferFeeJournal.DebitPendingState;
	public DateTimeOffset CreatedUtc { get; set; }
	public DateTimeOffset UpdatedUtc { get; set; }
	public string PreDebitFingerprint { get; set; } = string.Empty;
	public string ExpectedPostDebitFingerprint { get; set; } = string.Empty;
	public string PreRefundFingerprint { get; set; } = string.Empty;
	public string ExpectedPostRefundFingerprint { get; set; } = string.Empty;
	public List<FireSupportUh60TransferFeeDebit> Debits { get; set; } = new();
	public List<FireSupportUh60TransferFeeRefundCredit> RefundCredits
	{
		get;
		set;
	} = new();
}

public sealed class FireSupportUh60TransferFeeDebit
{
	public Item? OriginalItem { get; set; }
	public int AmountRoubles { get; set; }
}

public sealed class FireSupportUh60TransferFeeRefundCredit
{
	public string TargetItemId { get; set; } = string.Empty;
	public int AmountRoubles { get; set; }
	public int BeforeCount { get; set; }
	public Item? RestoredItem { get; set; }
}
