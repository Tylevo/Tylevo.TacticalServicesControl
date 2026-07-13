using SamSWAT.FireSupport.ArysReloaded.Unity;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Utils;
using System.Text.Json;

namespace SamSWAT.FireSupport.ArysReloaded;

[Injectable]
public sealed class FireSupportAuthorizationLedger(
	ISptLogger<FireSupportAuthorizationLedger> logger)
{
	private static readonly JsonSerializerOptions s_jsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true
	};

	private readonly object _gate = new();
	private string _ledgerPath = string.Empty;
	private FireSupportAuthorizationLedgerState _state = new();

	public void Initialize(string storageDirectory)
	{
		Directory.CreateDirectory(storageDirectory);
		_ledgerPath = Path.Combine(storageDirectory, "tsc-ledger.json");
		string legacyLedgerPath = Path.Combine(storageDirectory, "raidops-firesupport-ledger.json");
		if (!File.Exists(_ledgerPath) && File.Exists(legacyLedgerPath))
		{
			File.Copy(legacyLedgerPath, _ledgerPath, overwrite: false);
		}

		lock (_gate)
		{
			_state = Load();
			SaveLocked();
		}
	}

	public Dictionary<string, int> GetCredits(string profileId, int pendingTimeoutSeconds)
	{
		lock (_gate)
		{
			FireSupportAuthorizationLedgerState snapshot = CloneState(_state);
			if (PruneExpiredPendingLocked(TimeSpan.FromSeconds(Math.Max(1, pendingTimeoutSeconds))) &&
			    !TrySaveMutationLocked(snapshot, out _))
			{
				logger.Warning("TSC authorization ledger could not persist expired pending-use cleanup.");
			}

			FireSupportPlayerAuthorizations profile = GetProfileLocked(profileId);
			return new Dictionary<string, int>(profile.Credits, StringComparer.OrdinalIgnoreCase);
		}
	}

	public bool TryGrant(
		string profileId,
		ESupportType supportType,
		int quantity,
		int price,
		int maxStored,
		int pendingTimeoutSeconds,
		out Dictionary<string, int> credits,
		out string reason)
	{
		credits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		reason = string.Empty;
		string service = ToLedgerKey(supportType);
		if (string.IsNullOrWhiteSpace(profileId) || string.IsNullOrWhiteSpace(service) || quantity <= 0)
		{
			reason = "InvalidAuthorizationRequest";
			return false;
		}

		lock (_gate)
		{
			FireSupportAuthorizationLedgerState snapshot = CloneState(_state);
			PruneExpiredPendingLocked(TimeSpan.FromSeconds(Math.Max(1, pendingTimeoutSeconds)));
			FireSupportPlayerAuthorizations profile = GetProfileLocked(profileId);
			int current = GetCredit(profile, service);
			int limit = Math.Max(1, maxStored);
			if (current + quantity > limit)
			{
				credits = new Dictionary<string, int>(profile.Credits, StringComparer.OrdinalIgnoreCase);
				reason = "AuthorizationLimitReached";
				return false;
			}

			profile.Credits[service] = current + quantity;
			AddTransactionLocked(profile, "Purchase", service, quantity, price, requestId: string.Empty, reason: string.Empty);
			if (!TrySaveMutationLocked(snapshot, out reason))
			{
				credits = GetCreditsFromStateLocked(profileId);
				return false;
			}

			credits = new Dictionary<string, int>(profile.Credits, StringComparer.OrdinalIgnoreCase);
			return true;
		}
	}

	public bool TryConsume(
		string profileId,
		ESupportType supportType,
		string requestId,
		int pendingTimeoutSeconds,
		out Dictionary<string, int> credits,
		out string reason)
	{
		credits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		reason = string.Empty;
		string service = ToLedgerKey(supportType);
		if (string.IsNullOrWhiteSpace(profileId) || string.IsNullOrWhiteSpace(service))
		{
			reason = "InvalidAuthorizationRequest";
			return false;
		}

		requestId = string.IsNullOrWhiteSpace(requestId) ? Guid.NewGuid().ToString("N") : requestId.Trim();
		lock (_gate)
		{
			FireSupportAuthorizationLedgerState snapshot = CloneState(_state);
			PruneExpiredPendingLocked(TimeSpan.FromSeconds(Math.Max(1, pendingTimeoutSeconds)));
			FireSupportPlayerAuthorizations profile = GetProfileLocked(profileId);
			if (HasTransactionLocked(profile, "Consume", requestId))
			{
				credits = new Dictionary<string, int>(profile.Credits, StringComparer.OrdinalIgnoreCase);
				reason = "AlreadyConsumed";
				return true;
			}

			int current = GetCredit(profile, service);
			if (current <= 0)
			{
				credits = new Dictionary<string, int>(profile.Credits, StringComparer.OrdinalIgnoreCase);
				reason = "AuthorizationRequired";
				return false;
			}

			profile.Credits[service] = current - 1;
			profile.Pending[requestId] = new FireSupportPendingAuthorizationUse
			{
				RequestId = requestId,
				Service = service,
				Quantity = 1,
				CreatedUtc = DateTimeOffset.UtcNow
			};
			AddTransactionLocked(profile, "Consume", service, 1, 0, requestId, reason: string.Empty);
			if (!TrySaveMutationLocked(snapshot, out reason))
			{
				credits = GetCreditsFromStateLocked(profileId);
				return false;
			}

			credits = new Dictionary<string, int>(profile.Credits, StringComparer.OrdinalIgnoreCase);
			return true;
		}
	}

	public bool TryRefund(
		string profileId,
		ESupportType supportType,
		string requestId,
		int maxStored,
		int pendingTimeoutSeconds,
		string refundReason,
		out Dictionary<string, int> credits,
		out string reason)
	{
		credits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		reason = string.Empty;
		string service = ToLedgerKey(supportType);
		if (string.IsNullOrWhiteSpace(profileId) || string.IsNullOrWhiteSpace(service) || string.IsNullOrWhiteSpace(requestId))
		{
			reason = "InvalidAuthorizationRequest";
			return false;
		}

		lock (_gate)
		{
			FireSupportAuthorizationLedgerState snapshot = CloneState(_state);
			PruneExpiredPendingLocked(TimeSpan.FromSeconds(Math.Max(1, pendingTimeoutSeconds)));
			FireSupportPlayerAuthorizations profile = GetProfileLocked(profileId);
			string trimmedRequestId = requestId.Trim();
			if (HasTransactionLocked(profile, "Refund", trimmedRequestId))
			{
				credits = new Dictionary<string, int>(profile.Credits, StringComparer.OrdinalIgnoreCase);
				reason = "AlreadyRefunded";
				return true;
			}

			if (!profile.Pending.Remove(trimmedRequestId, out FireSupportPendingAuthorizationUse? pending))
			{
				if (HasTransactionLocked(profile, "Commit", trimmedRequestId))
				{
					credits = new Dictionary<string, int>(profile.Credits, StringComparer.OrdinalIgnoreCase);
					reason = "AlreadyCommitted";
					return false;
				}

				if (!HasTransactionLocked(profile, "Consume", trimmedRequestId))
				{
					credits = new Dictionary<string, int>(profile.Credits, StringComparer.OrdinalIgnoreCase);
					reason = "ConsumedAuthorizationNotFound";
					return false;
				}

				pending = new FireSupportPendingAuthorizationUse
				{
					RequestId = trimmedRequestId,
					Service = service,
					Quantity = 1,
					CreatedUtc = DateTimeOffset.UtcNow
				};
			}

			string refundService = string.IsNullOrWhiteSpace(pending.Service) ? service : pending.Service;
			int limit = Math.Max(1, maxStored);
			profile.Credits[refundService] = Math.Min(limit, GetCredit(profile, refundService) + Math.Max(1, pending.Quantity));
			AddTransactionLocked(profile, "Refund", refundService, Math.Max(1, pending.Quantity), 0, requestId, refundReason);
			if (!TrySaveMutationLocked(snapshot, out reason))
			{
				credits = GetCreditsFromStateLocked(profileId);
				return false;
			}

			credits = new Dictionary<string, int>(profile.Credits, StringComparer.OrdinalIgnoreCase);
			return true;
		}
	}

	public bool TryCommit(
		string profileId,
		ESupportType supportType,
		string requestId,
		int pendingTimeoutSeconds,
		out Dictionary<string, int> credits,
		out string reason)
	{
		credits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		reason = string.Empty;
		string service = ToLedgerKey(supportType);
		if (string.IsNullOrWhiteSpace(profileId) || string.IsNullOrWhiteSpace(service) || string.IsNullOrWhiteSpace(requestId))
		{
			reason = "InvalidAuthorizationRequest";
			return false;
		}

		lock (_gate)
		{
			FireSupportAuthorizationLedgerState snapshot = CloneState(_state);
			PruneExpiredPendingLocked(TimeSpan.FromSeconds(Math.Max(1, pendingTimeoutSeconds)));
			FireSupportPlayerAuthorizations profile = GetProfileLocked(profileId);
			string trimmedRequestId = requestId.Trim();
			if (profile.Pending.Remove(trimmedRequestId, out FireSupportPendingAuthorizationUse? pending))
			{
				string committedService = string.IsNullOrWhiteSpace(pending.Service) ? service : pending.Service;
				AddTransactionLocked(profile, "Commit", committedService, Math.Max(1, pending.Quantity), 0, requestId, "DispatchAccepted");
				if (!TrySaveMutationLocked(snapshot, out reason))
				{
					credits = GetCreditsFromStateLocked(profileId);
					return false;
				}

				credits = new Dictionary<string, int>(profile.Credits, StringComparer.OrdinalIgnoreCase);
				return true;
			}

			if (HasTransactionLocked(profile, "Commit", trimmedRequestId))
			{
				credits = new Dictionary<string, int>(profile.Credits, StringComparer.OrdinalIgnoreCase);
				reason = "AlreadyConsumed";
				return true;
			}

			if (HasTransactionLocked(profile, "Consume", trimmedRequestId))
			{
				AddTransactionLocked(profile, "Commit", service, 1, 0, requestId, "DispatchAcceptedLegacyConsume");
				if (!TrySaveMutationLocked(snapshot, out reason))
				{
					credits = GetCreditsFromStateLocked(profileId);
					return false;
				}

				credits = new Dictionary<string, int>(profile.Credits, StringComparer.OrdinalIgnoreCase);
				return true;
			}

			credits = new Dictionary<string, int>(profile.Credits, StringComparer.OrdinalIgnoreCase);
			reason = "ConsumedAuthorizationNotFound";
			return false;
		}
	}

	private FireSupportAuthorizationLedgerState Load()
	{
		if (!File.Exists(_ledgerPath))
		{
			return TryLoadBackup() ?? new FireSupportAuthorizationLedgerState();
		}

		try
		{
			string json = File.ReadAllText(_ledgerPath);
			return JsonSerializer.Deserialize<FireSupportAuthorizationLedgerState>(json, s_jsonOptions) ??
			       new FireSupportAuthorizationLedgerState();
		}
		catch (Exception ex)
		{
			logger.Error($"TSC authorization ledger could not be read: {_ledgerPath}", ex);
			PreserveCorruptLedger();
			FireSupportAuthorizationLedgerState? backup = TryLoadBackup();
			if (backup != null)
			{
				logger.Warning("TSC authorization ledger recovered from its backup file.");
				return backup;
			}

			logger.Warning("TSC authorization ledger started empty because neither the primary nor backup file was readable.");
			return new FireSupportAuthorizationLedgerState();
		}
	}

	private void SaveLocked()
	{
		string tempPath = _ledgerPath + ".tmp";
		string backupPath = _ledgerPath + ".bak";
		try
		{
			File.WriteAllText(tempPath, JsonSerializer.Serialize(_state, s_jsonOptions));
			if (File.Exists(_ledgerPath))
			{
				File.Replace(tempPath, _ledgerPath, backupPath, ignoreMetadataErrors: true);
			}
			else
			{
				File.Move(tempPath, _ledgerPath);
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

	private bool TrySaveMutationLocked(FireSupportAuthorizationLedgerState snapshot, out string reason)
	{
		try
		{
			SaveLocked();
			reason = string.Empty;
			return true;
		}
		catch (Exception ex)
		{
			_state = snapshot;
			reason = "AuthorizationLedgerSaveFailed";
			logger.Error("TSC authorization ledger mutation was rolled back after a disk write failure.", ex);
			return false;
		}
	}

	private FireSupportAuthorizationLedgerState? TryLoadBackup()
	{
		string backupPath = _ledgerPath + ".bak";
		if (!File.Exists(backupPath))
		{
			return null;
		}

		try
		{
			string json = File.ReadAllText(backupPath);
			return JsonSerializer.Deserialize<FireSupportAuthorizationLedgerState>(json, s_jsonOptions);
		}
		catch (Exception ex)
		{
			logger.Error($"TSC authorization ledger backup could not be read: {backupPath}", ex);
			return null;
		}
	}

	private void PreserveCorruptLedger()
	{
		try
		{
			string timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
			File.Move(_ledgerPath, _ledgerPath + $".corrupt-{timestamp}", overwrite: true);
		}
		catch (Exception ex)
		{
			logger.Error("TSC authorization ledger could not preserve the corrupt primary file.", ex);
		}
	}

	private static FireSupportAuthorizationLedgerState CloneState(FireSupportAuthorizationLedgerState state)
	{
		string json = JsonSerializer.Serialize(state, s_jsonOptions);
		return JsonSerializer.Deserialize<FireSupportAuthorizationLedgerState>(json, s_jsonOptions) ?? new FireSupportAuthorizationLedgerState();
	}

	private Dictionary<string, int> GetCreditsFromStateLocked(string profileId)
	{
		FireSupportPlayerAuthorizations profile = GetProfileLocked(profileId);
		return new Dictionary<string, int>(profile.Credits, StringComparer.OrdinalIgnoreCase);
	}

	private FireSupportPlayerAuthorizations GetProfileLocked(string profileId)
	{
		if (!_state.Profiles.TryGetValue(profileId, out FireSupportPlayerAuthorizations? profile))
		{
			profile = new FireSupportPlayerAuthorizations();
			_state.Profiles[profileId] = profile;
		}

		return profile;
	}

	private bool PruneExpiredPendingLocked(TimeSpan timeout)
	{
		bool changed = false;
		DateTimeOffset cutoff = DateTimeOffset.UtcNow - timeout;
		foreach (FireSupportPlayerAuthorizations profile in _state.Profiles.Values)
		{
			foreach (string requestId in profile.Pending
				         .Where(pair => pair.Value.CreatedUtc < cutoff)
				         .Select(pair => pair.Key)
				         .ToList())
			{
				FireSupportPendingAuthorizationUse pending = profile.Pending[requestId];
				profile.Pending.Remove(requestId);
				changed = true;
				if (!string.IsNullOrWhiteSpace(pending.Service))
				{
					AddTransactionLocked(profile, "CommitExpiredPending", pending.Service, Math.Max(1, pending.Quantity), 0, requestId, "PendingUseTimeout");
				}
			}
		}

		return changed;
	}

	private static int GetCredit(FireSupportPlayerAuthorizations profile, string service)
	{
		return profile.Credits.TryGetValue(service, out int count) ? Math.Max(0, count) : 0;
	}

	private static bool HasTransactionLocked(
		FireSupportPlayerAuthorizations profile,
		string type,
		string requestId)
	{
		return !string.IsNullOrWhiteSpace(requestId) &&
		       profile.Transactions.Any(transaction =>
			       string.Equals(transaction.Type, type, StringComparison.OrdinalIgnoreCase) &&
			       string.Equals(transaction.RequestId, requestId, StringComparison.OrdinalIgnoreCase));
	}

	private static void AddTransactionLocked(
		FireSupportPlayerAuthorizations profile,
		string type,
		string service,
		int quantity,
		int price,
		string requestId,
		string reason)
	{
		profile.Transactions.Add(new FireSupportAuthorizationTransaction
		{
			Id = "txn_" + Guid.NewGuid().ToString("N"),
			Type = type,
			Service = service,
			Quantity = quantity,
			Price = price,
			RequestId = requestId,
			Reason = reason,
			CreatedUtc = DateTimeOffset.UtcNow
		});
		if (profile.Transactions.Count > 100)
		{
			profile.Transactions.RemoveRange(0, profile.Transactions.Count - 100);
		}
	}

	private static string ToLedgerKey(ESupportType supportType)
	{
		return supportType switch
		{
			ESupportType.Strafe => "A10",
			ESupportType.DoubleStrafe => "DoublePass",
			ESupportType.Extract => "Extraction",
			ESupportType.PriorityExfil => "PriorityExfil",
			ESupportType.Uav => "Uav",
			ESupportType.FocusedSweep => "FocusedSweep",
			_ => string.Empty
		};
	}
}

public sealed class FireSupportAuthorizationLedgerState
{
	public int SchemaVersion { get; set; } = 1;
	public Dictionary<string, FireSupportPlayerAuthorizations> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class FireSupportPlayerAuthorizations
{
	public Dictionary<string, int> Credits { get; set; } = new(StringComparer.OrdinalIgnoreCase);
	public Dictionary<string, FireSupportPendingAuthorizationUse> Pending { get; set; } = new(StringComparer.OrdinalIgnoreCase);
	public List<FireSupportAuthorizationTransaction> Transactions { get; set; } = new();
}

public sealed class FireSupportPendingAuthorizationUse
{
	public string RequestId { get; set; } = string.Empty;
	public string Service { get; set; } = string.Empty;
	public int Quantity { get; set; }
	public DateTimeOffset CreatedUtc { get; set; }
}

public sealed class FireSupportAuthorizationTransaction
{
	public string Id { get; set; } = string.Empty;
	public string Type { get; set; } = string.Empty;
	public string Service { get; set; } = string.Empty;
	public int Quantity { get; set; }
	public int Price { get; set; }
	public string RequestId { get; set; } = string.Empty;
	public string Reason { get; set; } = string.Empty;
	public DateTimeOffset CreatedUtc { get; set; }
}
