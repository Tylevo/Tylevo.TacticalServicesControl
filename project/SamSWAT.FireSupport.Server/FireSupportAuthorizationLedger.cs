using SamSWAT.FireSupport.ArysReloaded.Unity;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Utils;
using System.Text.Json;

namespace SamSWAT.FireSupport.ArysReloaded;

[Injectable(InjectionType.Singleton)]
public sealed partial class FireSupportAuthorizationLedger(
	ISptLogger<FireSupportAuthorizationLedger> logger)
{
	public const int MaxPersistentPurchaseRequestIdLength = 128;
	private const int CurrentSchemaVersion = 5;
	private const int MaxTransactionsPerProfile = 512;
	private const string DefaultCurrency = "RUB";
	private const string PersistentPurchaseIdentity = "BuyPersistentAuthorization";
	private const string PersistentPurchasePreparedState = "Prepared";
	private const string PersistentPurchaseAcceptedState = "Accepted";
	private const string PersistentPurchaseInvalidCurrencyState = "InvalidCurrency";
	private const string AuthorizationUsePendingState = "Pending";
	private const string AuthorizationUseCommittedState = "Committed";
	private const string AuthorizationUseRefundedState = "Refunded";
	private const string AuthorizationUseExpiredRefundedState = "ExpiredRefunded";

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

	public Dictionary<string, int> GetCredits(
		string profileId,
		int pendingTimeoutSeconds,
		int maxStored)
	{
		lock (_gate)
		{
			FireSupportAuthorizationLedgerState snapshot = CloneState(_state);
			if (PruneExpiredPendingLocked(
				    TimeSpan.FromSeconds(Math.Max(1, pendingTimeoutSeconds)),
				    maxStored) &&
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

	public Dictionary<string, FireSupportPreparedPurchaseQuote>
		GetPreparedPersistentPurchaseDetails(string profileId)
	{
		var preparedByService =
			new Dictionary<string, FireSupportPreparedPurchaseQuote>(
				StringComparer.OrdinalIgnoreCase);
		if (string.IsNullOrWhiteSpace(profileId))
		{
			return preparedByService;
		}

		lock (_gate)
		{
			FireSupportPlayerAuthorizations profile = GetProfileLocked(profileId);
			foreach (FireSupportPersistentPurchaseRecord purchase in
			         profile.PersistentPurchases.Values
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
				preparedByService.TryAdd(
					purchase.Service,
					new FireSupportPreparedPurchaseQuote
					{
						RequestId = purchase.RequestId,
						Price = Math.Max(0, purchase.Price),
						Currency = NormalizePersistedCurrency(purchase.Currency)
					});
			}
		}

		return preparedByService;
	}

	public bool TryGrant(
		string profileId,
		ESupportType supportType,
		int quantity,
		int price,
		string currency,
		int maxStored,
		int pendingTimeoutSeconds,
		out Dictionary<string, int> credits,
		out string reason)
	{
		credits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		reason = string.Empty;
		string service = ToLedgerKey(supportType);
		bool validCurrency = TryNormalizeCurrency(currency, out string canonicalCurrency);
		if (string.IsNullOrWhiteSpace(profileId) ||
		    string.IsNullOrWhiteSpace(service) ||
		    !validCurrency ||
		    quantity <= 0)
		{
			reason = "InvalidAuthorizationRequest";
			return false;
		}

		lock (_gate)
		{
			if (!TryPruneExpiredPendingAndPersistLocked(
				    TimeSpan.FromSeconds(Math.Max(1, pendingTimeoutSeconds)),
				    maxStored,
				    out reason))
			{
				credits = GetCreditsFromStateLocked(profileId);
				return false;
			}

			FireSupportAuthorizationLedgerState snapshot = CloneState(_state);
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
			AddTransactionLocked(
				profile,
				"Purchase",
				service,
				quantity,
				price,
				canonicalCurrency,
				requestId: string.Empty,
				reason: string.Empty);
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
		string currency,
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
		bool validCurrency = TryNormalizeCurrency(currency, out string canonicalCurrency);
		requestId = requestId?.Trim() ?? string.Empty;
		preDebitFingerprint = preDebitFingerprint?.Trim() ?? string.Empty;
		expectedPostDebitFingerprint = expectedPostDebitFingerprint?.Trim() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(profileId) ||
		    string.IsNullOrWhiteSpace(service) ||
		    !validCurrency ||
		    quantity <= 0 ||
		    price < 0 ||
		    preDebitBalance < price ||
		    !IsValidInventoryFingerprint(preDebitFingerprint) ||
		    !IsValidInventoryFingerprint(expectedPostDebitFingerprint) ||
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
				Currency = canonicalCurrency,
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
				journalEntry.Currency,
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
		int maxStored,
		int pendingTimeoutSeconds,
		out Dictionary<string, int> credits,
		out string reason)
	{
		credits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		reason = string.Empty;
		string service = ToLedgerKey(supportType);
		requestId = requestId?.Trim() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(profileId) ||
		    string.IsNullOrWhiteSpace(service) ||
		    string.IsNullOrWhiteSpace(requestId) ||
		    requestId.Length > MaxPersistentPurchaseRequestIdLength)
		{
			reason = "InvalidAuthorizationRequest";
			return false;
		}

		lock (_gate)
		{
			if (!TryPruneExpiredPendingAndPersistLocked(
				    TimeSpan.FromSeconds(Math.Max(1, pendingTimeoutSeconds)),
				    maxStored,
				    out reason))
			{
				credits = GetCreditsFromStateLocked(profileId);
				return false;
			}

			FireSupportPlayerAuthorizations profile = GetProfileLocked(profileId);
			credits = new Dictionary<string, int>(profile.Credits, StringComparer.OrdinalIgnoreCase);
			if (profile.AuthorizationUses.TryGetValue(
				    requestId,
				    out FireSupportAuthorizationUseRecord? existingUse))
			{
				if (!IsMatchingAuthorizationUse(existingUse, service, quantity: 1))
				{
					reason = "AuthorizationRequestConflict";
					return false;
				}

				if (IsAuthorizationUsePending(existingUse))
				{
					reason = "AlreadyConsumed";
					return true;
				}

				if (IsAuthorizationUseCommitted(existingUse))
				{
					reason = "AlreadyCommitted";
					// A committed use is terminal. Treating a replayed Consume as
					// success would authorize another gameplay dispatch without
					// reserving another credit.
					return false;
				}

				if (IsAuthorizationUseRefunded(existingUse))
				{
					reason = "AlreadyRefunded";
					return false;
				}

				if (IsAuthorizationUseExpiredRefunded(existingUse))
				{
					reason = "AuthorizationUseExpired";
					return false;
				}

				reason = "AuthorizationUseStateInvalid";
				return false;
			}

			int current = GetCredit(profile, service);
			if (current <= 0)
			{
				credits = new Dictionary<string, int>(profile.Credits, StringComparer.OrdinalIgnoreCase);
				reason = "AuthorizationRequired";
				return false;
			}

			FireSupportAuthorizationLedgerState snapshot = CloneState(_state);
			profile.Credits[service] = current - 1;
			profile.AuthorizationUses[requestId] = new FireSupportAuthorizationUseRecord
			{
				RequestId = requestId,
				Service = service,
				Quantity = 1,
				State = AuthorizationUsePendingState,
				CreatedUtc = DateTimeOffset.UtcNow,
				Reason = string.Empty
			};
			AddTransactionLocked(
				profile,
				"Consume",
				service,
				1,
				0,
				DefaultCurrency,
				requestId,
				reason: string.Empty);
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
		requestId = requestId?.Trim() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(profileId) ||
		    string.IsNullOrWhiteSpace(service) ||
		    string.IsNullOrWhiteSpace(requestId) ||
		    requestId.Length > MaxPersistentPurchaseRequestIdLength)
		{
			reason = "InvalidAuthorizationRequest";
			return false;
		}

		lock (_gate)
		{
			if (!TryPruneExpiredPendingAndPersistLocked(
				    TimeSpan.FromSeconds(Math.Max(1, pendingTimeoutSeconds)),
				    maxStored,
				    out reason))
			{
				credits = GetCreditsFromStateLocked(profileId);
				return false;
			}

			FireSupportPlayerAuthorizations profile = GetProfileLocked(profileId);
			string trimmedRequestId = requestId;
			credits = new Dictionary<string, int>(profile.Credits, StringComparer.OrdinalIgnoreCase);
			if (!profile.AuthorizationUses.TryGetValue(
				    trimmedRequestId,
				    out FireSupportAuthorizationUseRecord? authorizationUse))
			{
				reason = "ConsumedAuthorizationNotFound";
				return false;
			}

			if (!IsMatchingAuthorizationUse(authorizationUse, service, quantity: 1))
			{
				reason = "AuthorizationRequestConflict";
				return false;
			}

			if (IsAuthorizationUseRefunded(authorizationUse) ||
			    IsAuthorizationUseExpiredRefunded(authorizationUse))
			{
				reason = "AlreadyRefunded";
				return true;
			}

			if (IsAuthorizationUseCommitted(authorizationUse))
			{
				reason = "AlreadyCommitted";
				return false;
			}

			if (!IsAuthorizationUsePending(authorizationUse))
			{
				reason = "AuthorizationUseStateInvalid";
				return false;
			}

			FireSupportAuthorizationLedgerState snapshot = CloneState(_state);
			int quantity = Math.Max(1, authorizationUse.Quantity);
			RestoreAuthorizationCreditLocked(profile, authorizationUse.Service, quantity, maxStored);
			authorizationUse.State = AuthorizationUseRefundedState;
			authorizationUse.CompletedUtc = DateTimeOffset.UtcNow;
			authorizationUse.Reason = refundReason ?? string.Empty;
			AddTransactionLocked(
				profile,
				"Refund",
				authorizationUse.Service,
				quantity,
				0,
				DefaultCurrency,
				trimmedRequestId,
				authorizationUse.Reason);
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
		int maxStored,
		int pendingTimeoutSeconds,
		out Dictionary<string, int> credits,
		out string reason)
	{
		credits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		reason = string.Empty;
		string service = ToLedgerKey(supportType);
		requestId = requestId?.Trim() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(profileId) ||
		    string.IsNullOrWhiteSpace(service) ||
		    string.IsNullOrWhiteSpace(requestId) ||
		    requestId.Length > MaxPersistentPurchaseRequestIdLength)
		{
			reason = "InvalidAuthorizationRequest";
			return false;
		}

		lock (_gate)
		{
			if (!TryPruneExpiredPendingAndPersistLocked(
				    TimeSpan.FromSeconds(Math.Max(1, pendingTimeoutSeconds)),
				    maxStored,
				    out reason))
			{
				credits = GetCreditsFromStateLocked(profileId);
				return false;
			}

			FireSupportPlayerAuthorizations profile = GetProfileLocked(profileId);
			credits = new Dictionary<string, int>(profile.Credits, StringComparer.OrdinalIgnoreCase);
			if (!profile.AuthorizationUses.TryGetValue(
				    requestId,
				    out FireSupportAuthorizationUseRecord? authorizationUse))
			{
				reason = "ConsumedAuthorizationNotFound";
				return false;
			}

			if (!IsMatchingAuthorizationUse(authorizationUse, service, quantity: 1))
			{
				reason = "AuthorizationRequestConflict";
				return false;
			}

			if (IsAuthorizationUseCommitted(authorizationUse))
			{
				reason = "AlreadyCommitted";
				return true;
			}

			if (IsAuthorizationUseRefunded(authorizationUse))
			{
				reason = "AlreadyRefunded";
				return false;
			}

			if (IsAuthorizationUseExpiredRefunded(authorizationUse))
			{
				reason = "AuthorizationUseExpired";
				return false;
			}

			if (!IsAuthorizationUsePending(authorizationUse))
			{
				reason = "AuthorizationUseStateInvalid";
				return false;
			}

			FireSupportAuthorizationLedgerState snapshot = CloneState(_state);
			authorizationUse.State = AuthorizationUseCommittedState;
			authorizationUse.CompletedUtc = DateTimeOffset.UtcNow;
			authorizationUse.Reason = "DispatchAccepted";
			AddTransactionLocked(
				profile,
				"Commit",
				authorizationUse.Service,
				Math.Max(1, authorizationUse.Quantity),
				0,
				DefaultCurrency,
				requestId,
				authorizationUse.Reason);
			if (!TrySaveMutationLocked(snapshot, out reason))
			{
				credits = GetCreditsFromStateLocked(profileId);
				return false;
			}

			credits = new Dictionary<string, int>(profile.Credits, StringComparer.OrdinalIgnoreCase);
			return true;
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
			// JSON cloning does not preserve dictionary comparers. Re-normalize
			// after rollback so request IDs remain case-insensitive.
			NormalizeStateLocked();
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
		bool migrateMissingLegacyCurrency = _state.SchemaVersion < 5;
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
			Dictionary<string, FireSupportPendingAuthorizationUse> legacyPending = profile.Pending == null
				? new Dictionary<string, FireSupportPendingAuthorizationUse>(StringComparer.OrdinalIgnoreCase)
				: new Dictionary<string, FireSupportPendingAuthorizationUse>(
					profile.Pending,
					StringComparer.OrdinalIgnoreCase);

			profile.Transactions = profile.Transactions?
				.Where(transaction => transaction != null)
				.ToList() ?? new List<FireSupportAuthorizationTransaction>();
			foreach (FireSupportAuthorizationTransaction transaction in profile.Transactions)
			{
				transaction.Currency = NormalizePersistedCurrency(
					transaction.Currency,
					migrateMissingLegacyCurrency);
			}

			var normalizedAuthorizationUses =
				new Dictionary<string, FireSupportAuthorizationUseRecord>(StringComparer.OrdinalIgnoreCase);
			foreach (KeyValuePair<string, FireSupportAuthorizationUseRecord> authorizationUsePair in
			         profile.AuthorizationUses ??
			         new Dictionary<string, FireSupportAuthorizationUseRecord>())
			{
				FireSupportAuthorizationUseRecord? authorizationUse = authorizationUsePair.Value;
				string requestId = string.IsNullOrWhiteSpace(authorizationUse?.RequestId)
					? authorizationUsePair.Key?.Trim() ?? string.Empty
					: authorizationUse.RequestId.Trim();
				if (authorizationUse == null ||
				    string.IsNullOrWhiteSpace(requestId) ||
				    string.IsNullOrWhiteSpace(authorizationUse.Service))
				{
					continue;
				}

				authorizationUse.RequestId = requestId;
				authorizationUse.Service = authorizationUse.Service.Trim();
				authorizationUse.Quantity = Math.Max(1, authorizationUse.Quantity);
				authorizationUse.State = NormalizeAuthorizationUseState(authorizationUse.State);
				authorizationUse.CreatedUtc = authorizationUse.CreatedUtc == default
					? DateTimeOffset.UtcNow
					: authorizationUse.CreatedUtc;
				if (string.IsNullOrWhiteSpace(authorizationUse.State))
				{
					// An unknown schema-4 state must never become refundable or
					// consumable by guessing. Preserve the debit as committed.
					authorizationUse.State = AuthorizationUseCommittedState;
					authorizationUse.CompletedUtc ??= DateTimeOffset.UtcNow;
					authorizationUse.Reason = "InvalidPersistedStatePreservedAsCommitted";
				}
				else if (IsAuthorizationUsePending(authorizationUse))
				{
					authorizationUse.CompletedUtc = null;
				}
				else
				{
					authorizationUse.CompletedUtc ??= authorizationUse.CreatedUtc;
				}

				authorizationUse.Reason ??= string.Empty;
				normalizedAuthorizationUses[requestId] = authorizationUse;
			}

			IEnumerable<string> legacyAuthorizationRequestIds = legacyPending.Keys
				.Concat(profile.Transactions
					.Where(IsAuthorizationUseTransaction)
					.Select(transaction => transaction.RequestId?.Trim() ?? string.Empty))
				.Where(requestId => !string.IsNullOrWhiteSpace(requestId))
				.Distinct(StringComparer.OrdinalIgnoreCase);
			foreach (string requestId in legacyAuthorizationRequestIds)
			{
				if (normalizedAuthorizationUses.ContainsKey(requestId))
				{
					continue;
				}

				legacyPending.TryGetValue(
					requestId,
					out FireSupportPendingAuthorizationUse? pending);
				List<FireSupportAuthorizationTransaction> lifecycleTransactions = profile.Transactions
					.Where(transaction =>
						IsAuthorizationUseTransaction(transaction) &&
						string.Equals(
							transaction.RequestId,
							requestId,
							StringComparison.OrdinalIgnoreCase))
					.ToList();
				FireSupportAuthorizationTransaction? consume = lifecycleTransactions.LastOrDefault(
					transaction => string.Equals(
						transaction.Type,
						"Consume",
						StringComparison.OrdinalIgnoreCase));
				FireSupportAuthorizationTransaction? refund = lifecycleTransactions.LastOrDefault(
					transaction => string.Equals(
						transaction.Type,
						"Refund",
						StringComparison.OrdinalIgnoreCase));
				FireSupportAuthorizationTransaction? expiredRefund = lifecycleTransactions.LastOrDefault(
					transaction => string.Equals(
						transaction.Type,
						"RefundExpiredPending",
						StringComparison.OrdinalIgnoreCase));
				FireSupportAuthorizationTransaction? commit = lifecycleTransactions.LastOrDefault(
					transaction =>
						string.Equals(transaction.Type, "Commit", StringComparison.OrdinalIgnoreCase) ||
						string.Equals(
							transaction.Type,
							"CommitExpiredPending",
							StringComparison.OrdinalIgnoreCase));

				// A retained refund is terminal even if an older build later wrote
				// a legacy commit fallback for the same request.
				FireSupportAuthorizationTransaction? identityTransaction =
					refund ?? expiredRefund ?? commit ?? consume;
				string service = !string.IsNullOrWhiteSpace(identityTransaction?.Service)
					? identityTransaction.Service.Trim()
					: pending?.Service?.Trim() ?? string.Empty;
				if (string.IsNullOrWhiteSpace(service))
				{
					continue;
				}

				int quantity = Math.Max(
					1,
					identityTransaction?.Quantity ?? pending?.Quantity ?? 1);
				DateTimeOffset createdUtc =
					pending?.CreatedUtc ??
					consume?.CreatedUtc ??
					identityTransaction?.CreatedUtc ??
					DateTimeOffset.UtcNow;
				var migrated = new FireSupportAuthorizationUseRecord
				{
					RequestId = requestId,
					Service = service,
					Quantity = quantity,
					CreatedUtc = createdUtc
				};
				if (refund != null)
				{
					migrated.State = AuthorizationUseRefundedState;
					migrated.CompletedUtc = refund.CreatedUtc;
					migrated.Reason = string.IsNullOrWhiteSpace(refund.Reason)
						? "MigratedRefund"
						: refund.Reason;
				}
				else if (expiredRefund != null)
				{
					migrated.State = AuthorizationUseExpiredRefundedState;
					migrated.CompletedUtc = expiredRefund.CreatedUtc;
					migrated.Reason = string.IsNullOrWhiteSpace(expiredRefund.Reason)
						? "PendingUseTimeout"
						: expiredRefund.Reason;
				}
				else if (commit != null)
				{
					migrated.State = AuthorizationUseCommittedState;
					migrated.CompletedUtc = commit.CreatedUtc;
					migrated.Reason = string.IsNullOrWhiteSpace(commit.Reason)
						? "MigratedCommit"
						: commit.Reason;
				}
				else if (pending != null)
				{
					migrated.State = AuthorizationUsePendingState;
					migrated.Reason = string.Empty;
				}
				else
				{
					// A legacy Consume without its Pending entry was already
					// charged. Conservatively preserve it as terminal rather than
					// inventing a refund or allowing another consume.
					migrated.State = AuthorizationUseCommittedState;
					migrated.CompletedUtc = consume?.CreatedUtc ?? DateTimeOffset.UtcNow;
					migrated.Reason = "LegacyConsumeWithoutPendingPreservedAsCommitted";
				}

				normalizedAuthorizationUses[requestId] = migrated;
			}

			profile.AuthorizationUses = normalizedAuthorizationUses;
			// Retained only as a schema-3 migration input. Schema 4 writes all
			// lifecycle state through AuthorizationUses.
			profile.Pending =
				new Dictionary<string, FireSupportPendingAuthorizationUse>(StringComparer.OrdinalIgnoreCase);

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
				purchase.Currency = NormalizePersistedCurrency(
					purchase.Currency,
					migrateMissingLegacyCurrency);
				if (!TryNormalizeCurrency(purchase.Currency, out _))
				{
					// Keep the request ID as a non-replayable tombstone. Dropping
					// it could permit a duplicate purchase; coercing it to RUB
					// could debit or recover the wrong inventory.
					purchase.State = PersistentPurchaseInvalidCurrencyState;
				}
				purchase.PreDebitFingerprint = purchase.PreDebitFingerprint?.Trim() ?? string.Empty;
				purchase.ExpectedPostDebitFingerprint =
					purchase.ExpectedPostDebitFingerprint?.Trim() ?? string.Empty;
				normalizedPurchases[requestId] = purchase;
			}

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
		profile.AuthorizationUses ??=
			new Dictionary<string, FireSupportAuthorizationUseRecord>(StringComparer.OrdinalIgnoreCase);
		profile.PersistentPurchases ??=
			new Dictionary<string, FireSupportPersistentPurchaseRecord>(StringComparer.OrdinalIgnoreCase);
		profile.Transactions ??= new List<FireSupportAuthorizationTransaction>();
		return profile;
	}

	private bool TryPruneExpiredPendingAndPersistLocked(
		TimeSpan timeout,
		int maxStored,
		out string reason)
	{
		FireSupportAuthorizationLedgerState snapshot = CloneState(_state);
		if (!PruneExpiredPendingLocked(timeout, maxStored))
		{
			reason = string.Empty;
			return true;
		}

		return TrySaveMutationLocked(snapshot, out reason);
	}

	private bool PruneExpiredPendingLocked(TimeSpan timeout, int maxStored)
	{
		bool changed = false;
		DateTimeOffset cutoff = DateTimeOffset.UtcNow - timeout;
		DateTimeOffset completedUtc = DateTimeOffset.UtcNow;
		foreach (FireSupportPlayerAuthorizations profile in _state.Profiles.Values)
		{
			profile.AuthorizationUses ??=
				new Dictionary<string, FireSupportAuthorizationUseRecord>(StringComparer.OrdinalIgnoreCase);
			foreach (string requestId in profile.AuthorizationUses
				         .Where(pair =>
					         pair.Value != null &&
					         IsAuthorizationUsePending(pair.Value) &&
					         pair.Value.CreatedUtc < cutoff)
				         .Select(pair => pair.Key)
				         .ToList())
			{
				FireSupportAuthorizationUseRecord pending = profile.AuthorizationUses[requestId];
				changed = true;
				if (!string.IsNullOrWhiteSpace(pending.Service))
				{
					int quantity = Math.Max(1, pending.Quantity);
					RestoreAuthorizationCreditLocked(profile, pending.Service, quantity, maxStored);
					AddTransactionLocked(
						profile,
						"RefundExpiredPending",
						pending.Service,
						quantity,
						0,
						DefaultCurrency,
						requestId,
						"PendingUseTimeout");
				}

				pending.State = AuthorizationUseExpiredRefundedState;
				pending.CompletedUtc = completedUtc;
				pending.Reason = "PendingUseTimeout";
			}
		}

		return changed;
	}

	private static int GetCredit(FireSupportPlayerAuthorizations profile, string service)
	{
		return profile.Credits.TryGetValue(service, out int count) ? Math.Max(0, count) : 0;
	}

	private static void RestoreAuthorizationCreditLocked(
		FireSupportPlayerAuthorizations profile,
		string service,
		int quantity,
		int maxStored)
	{
		int current = GetCredit(profile, service);
		int restoredQuantity = Math.Max(1, quantity);

		// A refund returns a credit the player already owned. If the configured
		// storage limit was lowered while this use was Pending, applying the new
		// cap here would report a successful refund while silently restoring
		// nothing. Allow the balance to sit above the new limit; grant/purchase
		// paths still enforce that limit until normal use brings it back under.
		profile.Credits[service] =
			(int)Math.Min(int.MaxValue, (long)current + restoredQuantity);
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

	private static string NormalizeAuthorizationUseState(string? state)
	{
		if (string.Equals(state, AuthorizationUsePendingState, StringComparison.OrdinalIgnoreCase))
		{
			return AuthorizationUsePendingState;
		}

		if (string.Equals(state, AuthorizationUseCommittedState, StringComparison.OrdinalIgnoreCase))
		{
			return AuthorizationUseCommittedState;
		}

		if (string.Equals(state, AuthorizationUseRefundedState, StringComparison.OrdinalIgnoreCase))
		{
			return AuthorizationUseRefundedState;
		}

		return string.Equals(
			state,
			AuthorizationUseExpiredRefundedState,
			StringComparison.OrdinalIgnoreCase)
			? AuthorizationUseExpiredRefundedState
			: string.Empty;
	}

	private static bool IsAuthorizationUseTransaction(FireSupportAuthorizationTransaction transaction)
	{
		return transaction != null &&
		       (string.Equals(transaction.Type, "Consume", StringComparison.OrdinalIgnoreCase) ||
		        string.Equals(transaction.Type, "Commit", StringComparison.OrdinalIgnoreCase) ||
		        string.Equals(transaction.Type, "Refund", StringComparison.OrdinalIgnoreCase) ||
		        string.Equals(
			        transaction.Type,
			        "CommitExpiredPending",
			        StringComparison.OrdinalIgnoreCase) ||
		        string.Equals(
			        transaction.Type,
			        "RefundExpiredPending",
			        StringComparison.OrdinalIgnoreCase));
	}

	private static bool IsMatchingAuthorizationUse(
		FireSupportAuthorizationUseRecord authorizationUse,
		string service,
		int quantity)
	{
		return string.Equals(
			       authorizationUse.Service,
			       service,
			       StringComparison.OrdinalIgnoreCase) &&
		       authorizationUse.Quantity == quantity;
	}

	private static bool IsAuthorizationUsePending(FireSupportAuthorizationUseRecord authorizationUse)
	{
		return string.Equals(
			authorizationUse.State,
			AuthorizationUsePendingState,
			StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsAuthorizationUseCommitted(FireSupportAuthorizationUseRecord authorizationUse)
	{
		return string.Equals(
			authorizationUse.State,
			AuthorizationUseCommittedState,
			StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsAuthorizationUseRefunded(FireSupportAuthorizationUseRecord authorizationUse)
	{
		return string.Equals(
			authorizationUse.State,
			AuthorizationUseRefundedState,
			StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsAuthorizationUseExpiredRefunded(FireSupportAuthorizationUseRecord authorizationUse)
	{
		return string.Equals(
			authorizationUse.State,
			AuthorizationUseExpiredRefundedState,
			StringComparison.OrdinalIgnoreCase);
	}

	private static int GetPreparedReservationCountLocked(
		FireSupportPlayerAuthorizations profile,
		string service)
	{
		long reserved = profile.PersistentPurchases.Values
			.Where(purchase =>
				purchase != null &&
				IsPersistentPurchasePrepared(purchase) &&
				string.Equals(purchase.Service, service, StringComparison.OrdinalIgnoreCase))
			.Sum(purchase => (long)Math.Max(0, purchase.Quantity));
		return reserved >= int.MaxValue ? int.MaxValue : (int)reserved;
	}

	private static int GetPendingAuthorizationUseCountLocked(
		FireSupportPlayerAuthorizations profile,
		string service)
	{
		long pending = profile.AuthorizationUses.Values
			.Where(use =>
				use != null &&
				IsAuthorizationUsePending(use) &&
				string.Equals(use.Service, service, StringComparison.OrdinalIgnoreCase))
			.Sum(use => (long)Math.Max(0, use.Quantity));
		return pending >= int.MaxValue ? int.MaxValue : (int)pending;
	}

	private static FireSupportPersistentPurchaseRecord CreateAcceptedPurchaseFromTransaction(
		FireSupportAuthorizationTransaction transaction,
		string requestId)
	{
		bool validCurrency =
			TryNormalizeCurrency(
				transaction.Currency,
				out string canonicalCurrency);
		return new FireSupportPersistentPurchaseRecord
		{
			RequestId = requestId,
			RequestIdentity = PersistentPurchaseIdentity,
			Service = transaction.Service,
			Quantity = transaction.Quantity,
			Price = transaction.Price,
			Currency = validCurrency
				? canonicalCurrency
				: NormalizePersistedCurrency(transaction.Currency),
			State = validCurrency
				? PersistentPurchaseAcceptedState
				: PersistentPurchaseInvalidCurrencyState,
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
			Currency = NormalizePersistedCurrency(purchase.Currency),
			PreDebitBalance = purchase.PreDebitBalance,
			ExpectedPostDebitBalance = purchase.ExpectedPostDebitBalance,
			PreDebitFingerprint = purchase.PreDebitFingerprint,
			ExpectedPostDebitFingerprint = purchase.ExpectedPostDebitFingerprint,
			State = purchase.State,
			CreatedUtc = purchase.CreatedUtc,
			AcceptedUtc = purchase.AcceptedUtc
		};
	}

	private static bool IsValidInventoryFingerprint(string fingerprint)
	{
		return fingerprint.Length == 64 && fingerprint.All(Uri.IsHexDigit);
	}

	private static void AddTransactionLocked(
		FireSupportPlayerAuthorizations profile,
		string type,
		string service,
		int quantity,
		int price,
		string currency,
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
			Currency = NormalizePersistedCurrency(currency),
			RequestId = requestId,
			Reason = reason,
			CreatedUtc = DateTimeOffset.UtcNow
		});
		if (profile.Transactions.Count > MaxTransactionsPerProfile)
		{
			profile.Transactions.RemoveRange(0, profile.Transactions.Count - MaxTransactionsPerProfile);
		}
	}

	private static bool TryNormalizeCurrency(string? currency, out string canonicalCurrency)
	{
		canonicalCurrency = currency?.Trim().ToUpperInvariant() ?? string.Empty;
		if (canonicalCurrency is "RUB" or "USD" or "EUR")
		{
			return true;
		}

		canonicalCurrency = string.Empty;
		return false;
	}

	private static string NormalizePersistedCurrency(
		string? currency,
		bool migrateMissingLegacyCurrency = false)
	{
		if (TryNormalizeCurrency(currency, out string canonicalCurrency))
		{
			return canonicalCurrency;
		}

		if (migrateMissingLegacyCurrency &&
		    string.IsNullOrWhiteSpace(currency))
		{
			return DefaultCurrency;
		}

		return currency?.Trim().ToUpperInvariant() ?? string.Empty;
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
	public int SchemaVersion { get; set; } = 5;
	public Dictionary<string, FireSupportPlayerAuthorizations> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class FireSupportPlayerAuthorizations
{
	public Dictionary<string, int> Credits { get; set; } = new(StringComparer.OrdinalIgnoreCase);
	// Retained only so schema-3 ledger files can migrate their pending entries.
	public Dictionary<string, FireSupportPendingAuthorizationUse> Pending { get; set; } = new(StringComparer.OrdinalIgnoreCase);
	public Dictionary<string, FireSupportAuthorizationUseRecord> AuthorizationUses { get; set; } =
		new(StringComparer.OrdinalIgnoreCase);
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
	public string Currency { get; set; } = string.Empty;
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

public sealed class FireSupportAuthorizationUseRecord
{
	public string RequestId { get; set; } = string.Empty;
	public string Service { get; set; } = string.Empty;
	public int Quantity { get; set; }
	public string State { get; set; } = string.Empty;
	public DateTimeOffset CreatedUtc { get; set; }
	public DateTimeOffset? CompletedUtc { get; set; }
	public string Reason { get; set; } = string.Empty;
}

public sealed class FireSupportAuthorizationTransaction
{
	public string Id { get; set; } = string.Empty;
	public string Type { get; set; } = string.Empty;
	public string Service { get; set; } = string.Empty;
	public int Quantity { get; set; }
	public int Price { get; set; }
	public string Currency { get; set; } = string.Empty;
	public string RequestId { get; set; } = string.Empty;
	public string Reason { get; set; } = string.Empty;
	public DateTimeOffset CreatedUtc { get; set; }
}
