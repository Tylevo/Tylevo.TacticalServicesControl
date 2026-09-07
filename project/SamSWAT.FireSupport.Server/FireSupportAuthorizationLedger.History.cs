using SamSWAT.FireSupport.ArysReloaded.Unity;

namespace SamSWAT.FireSupport.ArysReloaded;

public sealed partial class FireSupportAuthorizationLedger
{
	/// <summary>Returns detached completed receipts without creating or changing ledger state.</summary>
	public FireSupportPurchaseHistory GetPurchaseHistory(string profileId)
	{
		var history = new FireSupportPurchaseHistory { ProfileId = profileId ?? string.Empty };
		if (string.IsNullOrWhiteSpace(profileId)) return history;
		lock (_gate)
		{
			if (!_state.Profiles.TryGetValue(profileId, out FireSupportPlayerAuthorizations? profile))
				return history;
			var entries = new List<(FireSupportPurchaseHistoryEntry Entry, string Key)>();
			var journalRequests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (FireSupportPersistentPurchaseRecord purchase in profile.PersistentPurchases.Values)
			{
				if (purchase == null || string.IsNullOrWhiteSpace(purchase.RequestId) ||
				    !journalRequests.Add(purchase.RequestId)) continue;
				// An unsettled or invalid journal record must not reappear via a
				// duplicate transaction, even if that transaction claims completion.
				if (!IsPersistentPurchaseAccepted(purchase) || !purchase.AcceptedUtc.HasValue ||
				    !string.Equals(purchase.RequestIdentity, PersistentPurchaseIdentity, StringComparison.OrdinalIgnoreCase)) continue;
				Add(purchase.Service, purchase.Quantity, purchase.Price, purchase.Currency,
					purchase.AcceptedUtc.Value, "purchase:" + purchase.RequestId);
			}
			var seenTransactions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (FireSupportAuthorizationTransaction transaction in profile.Transactions)
			{
				if (transaction == null || string.IsNullOrWhiteSpace(transaction.Id) ||
				    !seenTransactions.Add(transaction.Id) ||
				    !string.Equals(transaction.Type, "Purchase", StringComparison.OrdinalIgnoreCase)) continue;
				if (!string.IsNullOrWhiteSpace(transaction.RequestId) &&
				    !journalRequests.Add(transaction.RequestId)) continue;
				// Older in-raid purchases have no durable request ID. Their retained
				// committed Purchase transaction is the available receipt.
				Add(transaction.Service, transaction.Quantity, transaction.Price, transaction.Currency,
					transaction.CreatedUtc, "transaction:" + transaction.Id);
			}
			history.HasMore = entries.Count > FireSupportPurchaseHistory.MaxEntries;
			history.Entries = entries.OrderByDescending(entry => entry.Entry.PurchasedUtc)
				.ThenBy(entry => entry.Key, StringComparer.Ordinal)
				.Take(FireSupportPurchaseHistory.MaxEntries).Select(entry => entry.Entry).ToList();
			return history;

			void Add(string service, int quantity, int price, string currency, DateTimeOffset purchasedUtc, string key)
			{
				if (!FireSupportPurchaseHistory.IsKnownService(service) || quantity <= 0 || price < 0 ||
				    purchasedUtc == default || !TryNormalizeCurrency(currency, out string canonicalCurrency)) return;
				entries.Add((new FireSupportPurchaseHistoryEntry
				{
					Service = service, Quantity = quantity, Price = price, Currency = canonicalCurrency,
					PurchasedUtc = purchasedUtc.ToUniversalTime()
				}, key));
			}
		}
	}
}
