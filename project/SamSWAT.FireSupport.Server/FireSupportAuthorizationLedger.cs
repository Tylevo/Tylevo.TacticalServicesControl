using SamSWAT.FireSupport.ArysReloaded.Unity;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Utils;
using System.Text.Json;

namespace SamSWAT.FireSupport.ArysReloaded;

[Injectable]
public sealed class FireSupportAuthorizationLedger(
	ISptLogger<FireSupportAuthorizationLedger> logger)
{
	public const int MaxPersistentPurchaseRequestIdLength = 128;
	private const int CurrentSchemaVersion = 3;
	private const int MaxTransactionsPerProfile = 512;
	private const string PersistentPurchaseIdentity = "BuyPersistentAuthorization";
	private const string PersistentPurchasePreparedState = "Prepared";
	private const string PersistentPurchaseAcceptedState = "Accepted";

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
			NormalizeStateLocked();
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

	public Dictionary<string, string> GetPreparedPersistentPurchaseRequestIds(string profileId)
	{
		var preparedByService = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (string.IsNullOrWhiteSpace(profileId))
		{
			return preparedByService;
		}

		lock (_gate)
		{
			FireSupportPlayerAuthorizations profile = GetProfileLocked(profileId);
			foreach (FireSupportPersistentPurchaseRecord purchase in profile.PersistentPurchases.Values
				         .Where(purchase =>
					         purchase != null &&
					         IsPersistentPurchasePrepared(purchase) &&
					         string.Equals(
						         purchase.RequestIdentity,
						         PersistentPurchaseIdentity,
						         StringComparison.OrdinalIgnoreCase) &&
					         !string.IsNullOrWhiteSpace(purchase.Service) &&
					         purchase.Quantity > 0 &&
					         !string.IsNullOrWhiteSpace(purchase.RequestId))
				         .OrderBy(purchase => purchase.CreatedUtc))
			{
				preparedByService.TryAdd(purchase.Service, purchase.RequestId);
			}
		}

		return preparedByService;
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
		if (string.IsNullOrWhiteSpace(profileId) ||
		    string.IsNullOrWhiteSpace(service) ||
		    quantity <= 0)
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
			int preparedReservations = GetPreparedReservationCountLocked(profile, service);
			int pendingUses = GetPendingAuthorizationUseCountLocked(profile, service);
			if ((long)current + pendingUses + preparedReservations + quantity > limit)
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

	public PersistentPurchaseReplayStatus GetPersistentPurchaseReplay(
		string profileId,
		ESupportType supportType,
		int quantity,
		string requestId,
		out Dictionary<string, int> credits,
		out FireSupportPersistentPurchaseRecord? purchase)
	{
		credits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		purchase = null;
		string service = ToLedgerKey(supportType);
		requestId = requestId?.Trim() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(profileId) ||
		    string.IsNullOrWhiteSpace(service) ||
		    quantity <= 0 ||
		    string.IsNullOrWhiteSpace(requestId) ||
		    requestId.Length > MaxPersistentPurchaseRequestIdLength)
		{
			return PersistentPurchaseReplayStatus.Conflict;
		}

		lock (_gate)
		{
			FireSupportPlayerAuthorizations profile = GetProfileLocked(profileId);
			credits = new Dictionary<string, int>(profile.Credits, StringComparer.OrdinalIgnoreCase);
			FireSupportPersistentPurchaseRecord? journalEntry =
				FindPersistentPurchaseLocked(profile, requestId, out _);
			if (journalEntry != null)
			{
				purchase = ClonePersistentPurchase(journalEntry);
				if (!IsMatchingPersistentPurchase(journalEntry, service, quantity))
				{
					return PersistentPurchaseReplayStatus.Conflict;
				}

				if (IsPersistentPurchaseAccepted(journalEntry))
				{
					return PersistentPurchaseReplayStatus.Accepted;
				}

				return IsPersistentPurchasePrepared(journalEntry)
					? PersistentPurchaseReplayStatus.Prepared
					: PersistentPurchaseReplayStatus.Conflict;
			}

			FireSupportAuthorizationTransaction? existing =
				FindTransactionByRequestIdLocked(profile, requestId);
			if (existing == null)
			{
				return PersistentPurchaseReplayStatus.NotFound;
			}

			if (!IsMatchingPurchaseIdentity(existing, service, quantity))
			{
				return PersistentPurchaseReplayStatus.Conflict;
			}

			purchase = CreateAcceptedPurchaseFromTransaction(existing, requestId);
			return PersistentPurchaseReplayStatus.Accepted;
		}
	}

	public bool TryPreparePersistentPurchase(
		string profileId,
		ESupportType supportType,
		int quantity,
		int price,
		int preDebitBalance,
		string preDebitFingerprint,
		string expectedPostDebitFingerprint,
		int maxStored,
		string requestId,
		out Dictionary<string, int> credits,
		out FireSupportPersistentPurchaseRecord? purchase,
		out string reason)
	{
		credits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		purchase = null;
		reason = string.Empty;
		string service = ToLedgerKey(supportType);
		requestId = requestId?.Trim() ?? string.Empty;
		preDebitFingerprint = preDebitFingerprint?.Trim() ?? string.Empty;
		expectedPostDebitFingerprint = expectedPostDebitFingerprint?.Trim() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(profileId) ||
		    string.IsNullOrWhiteSpace(service) ||
		    quantity <= 0 ||
		    price < 0 ||
		    preDebitBalance < price ||
		    !IsValidRoubleInventoryFingerprint(preDebitFingerprint) ||
		    !IsValidRoubleInventoryFingerprint(expectedPostDebitFingerprint) ||
		    string.IsNullOrWhiteSpace(requestId) ||
		    requestId.Length > MaxPersistentPurchaseRequestIdLength)
		{
			reason = "InvalidAuthorizationRequest";
			return false;
		}

		lock (_gate)
		{
			FireSupportAuthorizationLedgerState snapshot = CloneState(_state);
			FireSupportPlayerAuthorizations profile = GetProfileLocked(profileId);
			credits = new Dictionary<string, int>(profile.Credits, StringComparer.OrdinalIgnoreCase);
			FireSupportPersistentPurchaseRecord? existingJournal =
				FindPersistentPurchaseLocked(profile, requestId, out _);
			if (existingJournal != null)
			{
				purchase = ClonePersistentPurchase(existingJournal);
				reason = !IsMatchingPersistentPurchase(existingJournal, service, quantity)
					? "PurchaseRequestConflict"
					: IsPersistentPurchaseAccepted(existingJournal)
						? "AlreadyAccepted"
						: IsPersistentPurchasePrepared(existingJournal)
							? "AlreadyPrepared"
							: "PurchaseRequestConflict";
				return false;
			}

			FireSupportAuthorizationTransaction? existingTransaction =
				FindTransactionByRequestIdLocked(profile, requestId);
			if (existingTransaction != null)
			{
				reason = IsMatchingPurchaseIdentity(existingTransaction, service, quantity)
					? "AlreadyAccepted"
					: "PurchaseRequestConflict";
				purchase = IsMatchingPurchaseIdentity(existingTransaction, service, quantity)
					? CreateAcceptedPurchaseFromTransaction(existingTransaction, requestId)
					: null;
				return false;
			}

			int current = GetCredit(profile, service);
			int preparedReservations = GetPreparedReservationCountLocked(profile, service);
			int pendingUses = GetPendingAuthorizationUseCountLocked(profile, service);
			int limit = Math.Max(1, maxStored);
			if ((long)current + pendingUses + preparedReservations + quantity > limit)
			{
				reason = "AuthorizationLimitReached";
				return false;
			}

			var prepared = new FireSupportPersistentPurchaseRecord
			{
				RequestId = requestId,
				RequestIdentity = PersistentPurchaseIdentity,
				Service = service,
				Quantity = quantity,
				Price = price,
				PreDebitBalance = preDebitBalance,
				ExpectedPostDebitBalance = preDebitBalance - price,
				PreDebitFingerprint = preDebitFingerprint,
				ExpectedPostDebitFingerprint = expectedPostDebitFingerprint,
				State = PersistentPurchasePreparedState,
				CreatedUtc = DateTimeOffset.UtcNow
			};
			profile.PersistentPurchases[requestId] = prepared;
			if (!TrySaveMutationLocked(snapshot, out reason))
			{
				credits = GetCreditsFromStateLocked(profileId);
				return false;
			}

			purchase = ClonePersistentPurchase(prepared);
			return true;
		}
	}

	public bool TryFinalizePersistentPurchase(
		string profileId,
		ESupportType supportType,
		int quantity,
		string requestId,
		out Dictionary<string, int> credits,
		out FireSupportPersistentPurchaseRecord? purchase,
		out string reason)
	{
		credits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		purchase = null;
		reason = string.Empty;
		string service = ToLedgerKey(supportType);
		requestId = requestId?.Trim() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(profileId) ||
		    string.IsNullOrWhiteSpace(service) ||
		    quantity <= 0 ||
		    string.IsNullOrWhiteSpace(requestId) ||
		    requestId.Length > MaxPersistentPurchaseRequestIdLength)
		{
			reason = "InvalidAuthorizationRequest";
			return false;
		}

		lock (_gate)
		{
			FireSupportAuthorizationLedgerState snapshot = CloneState(_state);
			FireSupportPlayerAuthorizations profile = GetProfileLocked(profileId);
			FireSupportPersistentPurchaseRecord? journalEntry =
				FindPersistentPurchaseLocked(profile, requestId, out _);
			if (journalEntry == null)
			{
				FireSupportAuthorizationTransaction? existing =
					FindTransactionByRequestIdLocked(profile, requestId);
				if (existing != null && IsMatchingPurchaseIdentity(existing, service, quantity))
				{
					credits = new Dictionary<string, int>(profile.Credits, StringComparer.OrdinalIgnoreCase);
					purchase = CreateAcceptedPurchaseFromTransaction(existing, requestId);
					reason = "AlreadyAccepted";
					return true;
				}

				reason = existing == null ? "PersistentPurchaseNotPrepared" : "PurchaseRequestConflict";
				return false;
			}

			credits = new Dictionary<string, int>(profile.Credits, StringComparer.OrdinalIgnoreCase);
			purchase = ClonePersistentPurchase(journalEntry);
			if (!IsMatchingPersistentPurchase(journalEntry, service, quantity))
			{
				reason = "PurchaseRequestConflict";
				return false;
			}

			if (IsPersistentPurchaseAccepted(journalEntry))
			{
				reason = "AlreadyAccepted";
				return true;
			}

			if (!IsPersistentPurchasePrepared(journalEntry))
			{
				reason = "PersistentPurchaseStateInvalid";
				return false;
			}

			FireSupportPersistentPurchaseRecord preparedSnapshot = ClonePersistentPurchase(journalEntry);
			profile.Credits[service] = GetCredit(profile, service) + quantity;
			journalEntry.State = PersistentPurchaseAcceptedState;
			journalEntry.AcceptedUtc = DateTimeOffset.UtcNow;
			AddTransactionLocked(
				profile,
				"Purchase",
				service,
				quantity,
				journalEntry.Price,
				requestId,
				reason: string.Empty);
			if (!TrySaveMutationLocked(snapshot, out reason))
			{
				credits = GetCreditsFromStateLocked(profileId);
				purchase = preparedSnapshot;
				return false;
			}

			credits = new Dictionary<string, int>(profile.Credits, StringComparer.OrdinalIgnoreCase);
			purchase = ClonePersistentPurchase(journalEntry);
			return true;
		}
	}

	public bool TryCancelPreparedPersistentPurchase(
		string profileId,
		ESupportType supportType,
		int quantity,
		string requestId,
		out string reason)
	{
		reason = string.Empty;
		string service = ToLedgerKey(supportType);
		requestId = requestId?.Trim() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(profileId) ||
		    string.IsNullOrWhiteSpace(service) ||
		    quantity <= 0 ||
		    string.IsNullOrWhiteSpace(requestId) ||
		    requestId.Length > MaxPersistentPurchaseRequestIdLength)
		{
			reason = "InvalidAuthorizationRequest";
			return false;
		}

		lock (_gate)
		{
			FireSupportAuthorizationLedgerState snapshot = CloneState(_state);
			FireSupportPlayerAuthorizations profile = GetProfileLocked(profileId);
			FireSupportPersistentPurchaseRecord? journalEntry =
				FindPersistentPurchaseLocked(profile, requestId, out string journalKey);
			if (journalEntry == null)
			{
				reason = "AlreadyCancelled";
				return true;
			}

			if (!IsMatchingPersistentPurchase(journalEntry, service, quantity))
			{
				reason = "PurchaseRequestConflict";
				return false;
			}

			if (IsPersistentPurchaseAccepted(journalEntry))
			{
				reason = "AlreadyAccepted";
				return false;
			}

			if (!IsPersistentPurchasePrepared(journalEntry))
			{
				reason = "PersistentPurchaseStateInvalid";
				return false;
			}

			profile.PersistentPurchases.Remove(journalKey);
			return TrySaveMutationLocked(snapshot, out reason);
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
			int current = GetCredit(profile, refundService);
			int preparedReservations = GetPreparedReservationCountLocked(profile, refundService);
			int effectiveLimit = Math.Max(current, limit - preparedReservations);
			profile.Credits[refundService] =
				Math.Min(effectiveLimit, current + Math.Max(1, pending.Quantity));
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

	private void NormalizeStateLocked()
	{
		var normalizedProfiles =
			new Dictionary<string, FireSupportPlayerAuthorizations>(StringComparer.OrdinalIgnoreCase);
		foreach (KeyValuePair<string, FireSupportPlayerAuthorizations> pair in
		         _state.Profiles ?? new Dictionary<string, FireSupportPlayerAuthorizations>())
		{
			if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null)
			{
				continue;
			}

			FireSupportPlayerAuthorizations profile = pair.Value;
			profile.Credits = profile.Credits == null
				? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
				: new Dictionary<string, int>(profile.Credits, StringComparer.OrdinalIgnoreCase);
			profile.Pending = profile.Pending == null
				? new Dictionary<string, FireSupportPendingAuthorizationUse>(StringComparer.OrdinalIgnoreCase)
				: new Dictionary<string, FireSupportPendingAuthorizationUse>(
					profile.Pending,
					StringComparer.OrdinalIgnoreCase);

			var normalizedPurchases =
				new Dictionary<string, FireSupportPersistentPurchaseRecord>(StringComparer.OrdinalIgnoreCase);
			foreach (KeyValuePair<string, FireSupportPersistentPurchaseRecord> purchasePair in
			         profile.PersistentPurchases ??
			         new Dictionary<string, FireSupportPersistentPurchaseRecord>())
			{
				FireSupportPersistentPurchaseRecord? purchase = purchasePair.Value;
				string requestId = string.IsNullOrWhiteSpace(purchase?.RequestId)
					? purchasePair.Key?.Trim() ?? string.Empty
					: purchase.RequestId.Trim();
				if (purchase == null || string.IsNullOrWhiteSpace(requestId))
				{
					continue;
				}

				purchase.RequestId = requestId;
				purchase.PreDebitFingerprint = purchase.PreDebitFingerprint?.Trim() ?? string.Empty;
				purchase.ExpectedPostDebitFingerprint =
					purchase.ExpectedPostDebitFingerprint?.Trim() ?? string.Empty;
				normalizedPurchases[requestId] = purchase;
			}

			profile.Transactions = profile.Transactions?
				.Where(transaction => transaction != null)
				.ToList() ?? new List<FireSupportAuthorizationTransaction>();
			foreach (FireSupportAuthorizationTransaction transaction in profile.Transactions)
			{
				string requestId = transaction.RequestId?.Trim() ?? string.Empty;
				if (!string.Equals(transaction.Type, "Purchase", StringComparison.OrdinalIgnoreCase) ||
				    string.IsNullOrWhiteSpace(requestId) ||
				    normalizedPurchases.ContainsKey(requestId))
				{
					continue;
				}

				normalizedPurchases[requestId] =
					CreateAcceptedPurchaseFromTransaction(transaction, requestId);
			}

			profile.PersistentPurchases = normalizedPurchases;
			if (profile.Transactions.Count > MaxTransactionsPerProfile)
			{
				profile.Transactions.RemoveRange(
					0,
					profile.Transactions.Count - MaxTransactionsPerProfile);
			}

			normalizedProfiles[pair.Key.Trim()] = profile;
		}

		_state.Profiles = normalizedProfiles;
		_state.SchemaVersion = CurrentSchemaVersion;
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

		profile.Credits ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		profile.Pending ??= new Dictionary<string, FireSupportPendingAuthorizationUse>(StringComparer.OrdinalIgnoreCase);
		profile.PersistentPurchases ??=
			new Dictionary<string, FireSupportPersistentPurchaseRecord>(StringComparer.OrdinalIgnoreCase);
		profile.Transactions ??= new List<FireSupportAuthorizationTransaction>();
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

	private static FireSupportAuthorizationTransaction? FindTransactionByRequestIdLocked(
		FireSupportPlayerAuthorizations profile,
		string requestId)
	{
		if (string.IsNullOrWhiteSpace(requestId))
		{
			return null;
		}

		return profile.Transactions.LastOrDefault(transaction =>
			string.Equals(transaction.RequestId, requestId, StringComparison.OrdinalIgnoreCase));
	}

	private static FireSupportPersistentPurchaseRecord? FindPersistentPurchaseLocked(
		FireSupportPlayerAuthorizations profile,
		string requestId,
		out string journalKey)
	{
		journalKey = string.Empty;
		if (string.IsNullOrWhiteSpace(requestId))
		{
			return null;
		}

		foreach (KeyValuePair<string, FireSupportPersistentPurchaseRecord> pair in profile.PersistentPurchases)
		{
			if (string.Equals(pair.Key, requestId, StringComparison.OrdinalIgnoreCase))
			{
				journalKey = pair.Key;
				return pair.Value;
			}
		}

		return null;
	}

	private static bool IsMatchingPurchaseIdentity(
		FireSupportAuthorizationTransaction transaction,
		string service,
		int quantity)
	{
		return string.Equals(transaction.Type, "Purchase", StringComparison.OrdinalIgnoreCase) &&
		       string.Equals(transaction.Service, service, StringComparison.OrdinalIgnoreCase) &&
		       transaction.Quantity == quantity;
	}

	private static bool IsMatchingPersistentPurchase(
		FireSupportPersistentPurchaseRecord purchase,
		string service,
		int quantity)
	{
		return string.Equals(
			       purchase.RequestIdentity,
			       PersistentPurchaseIdentity,
			       StringComparison.OrdinalIgnoreCase) &&
		       string.Equals(purchase.Service, service, StringComparison.OrdinalIgnoreCase) &&
		       purchase.Quantity == quantity;
	}

	private static bool IsPersistentPurchaseAccepted(FireSupportPersistentPurchaseRecord purchase)
	{
		return string.Equals(
			purchase.State,
			PersistentPurchaseAcceptedState,
			StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsPersistentPurchasePrepared(FireSupportPersistentPurchaseRecord purchase)
	{
		return string.Equals(
			purchase.State,
			PersistentPurchasePreparedState,
			StringComparison.OrdinalIgnoreCase);
	}

	private static int GetPreparedReservationCountLocked(
		FireSupportPlayerAuthorizations profile,
		string service)
	{
		long reserved = profile.PersistentPurchases.Values
			.Where(purchase =>
				purchase != null &&
				!IsPersistentPurchaseAccepted(purchase) &&
				string.Equals(purchase.Service, service, StringComparison.OrdinalIgnoreCase))
			.Sum(purchase => (long)Math.Max(0, purchase.Quantity));
		return reserved >= int.MaxValue ? int.MaxValue : (int)reserved;
	}

	private static int GetPendingAuthorizationUseCountLocked(
		FireSupportPlayerAuthorizations profile,
		string service)
	{
		long pending = profile.Pending.Values
			.Where(use =>
				use != null &&
				string.Equals(use.Service, service, StringComparison.OrdinalIgnoreCase))
			.Sum(use => (long)Math.Max(0, use.Quantity));
		return pending >= int.MaxValue ? int.MaxValue : (int)pending;
	}

	private static FireSupportPersistentPurchaseRecord CreateAcceptedPurchaseFromTransaction(
		FireSupportAuthorizationTransaction transaction,
		string requestId)
	{
		return new FireSupportPersistentPurchaseRecord
		{
			RequestId = requestId,
			RequestIdentity = PersistentPurchaseIdentity,
			Service = transaction.Service,
			Quantity = transaction.Quantity,
			Price = transaction.Price,
			State = PersistentPurchaseAcceptedState,
			CreatedUtc = transaction.CreatedUtc,
			AcceptedUtc = transaction.CreatedUtc
		};
	}

	private static FireSupportPersistentPurchaseRecord ClonePersistentPurchase(
		FireSupportPersistentPurchaseRecord purchase)
	{
		return new FireSupportPersistentPurchaseRecord
		{
			RequestId = purchase.RequestId,
			RequestIdentity = purchase.RequestIdentity,
			Service = purchase.Service,
			Quantity = purchase.Quantity,
			Price = purchase.Price,
			PreDebitBalance = purchase.PreDebitBalance,
			ExpectedPostDebitBalance = purchase.ExpectedPostDebitBalance,
			PreDebitFingerprint = purchase.PreDebitFingerprint,
			ExpectedPostDebitFingerprint = purchase.ExpectedPostDebitFingerprint,
			State = purchase.State,
			CreatedUtc = purchase.CreatedUtc,
			AcceptedUtc = purchase.AcceptedUtc
		};
	}

	private static bool IsValidRoubleInventoryFingerprint(string fingerprint)
	{
		return fingerprint.Length == 64 && fingerprint.All(Uri.IsHexDigit);
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
		if (profile.Transactions.Count > MaxTransactionsPerProfile)
		{
			profile.Transactions.RemoveRange(0, profile.Transactions.Count - MaxTransactionsPerProfile);
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

public enum PersistentPurchaseReplayStatus
{
	NotFound,
	Prepared,
	Accepted,
	Conflict
}

public sealed class FireSupportAuthorizationLedgerState
{
	public int SchemaVersion { get; set; } = 3;
	public Dictionary<string, FireSupportPlayerAuthorizations> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class FireSupportPlayerAuthorizations
{
	public Dictionary<string, int> Credits { get; set; } = new(StringComparer.OrdinalIgnoreCase);
	public Dictionary<string, FireSupportPendingAuthorizationUse> Pending { get; set; } = new(StringComparer.OrdinalIgnoreCase);
	public Dictionary<string, FireSupportPersistentPurchaseRecord> PersistentPurchases { get; set; } =
		new(StringComparer.OrdinalIgnoreCase);
	public List<FireSupportAuthorizationTransaction> Transactions { get; set; } = new();
}

public sealed class FireSupportPersistentPurchaseRecord
{
	public string RequestId { get; set; } = string.Empty;
	public string RequestIdentity { get; set; } = string.Empty;
	public string Service { get; set; } = string.Empty;
	public int Quantity { get; set; }
	public int Price { get; set; }
	public int PreDebitBalance { get; set; }
	public int ExpectedPostDebitBalance { get; set; }
	public string PreDebitFingerprint { get; set; } = string.Empty;
	public string ExpectedPostDebitFingerprint { get; set; } = string.Empty;
	public string State { get; set; } = string.Empty;
	public DateTimeOffset CreatedUtc { get; set; }
	public DateTimeOffset? AcceptedUtc { get; set; }
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
