using System;
using System.Collections.Generic;
using System.Linq;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

/// <summary>Recent completed receipts for one authenticated PMC; omitted history is not an empty history.</summary>
public sealed class FireSupportPurchaseHistory
{
	public const int MaxEntries = 50;
	public string ProfileId { get; set; } = string.Empty;
	public bool HasMore { get; set; }
	public List<FireSupportPurchaseHistoryEntry> Entries { get; set; } = new();

	public bool IsValidFor(string profileId) => !string.IsNullOrWhiteSpace(profileId) &&
		string.Equals(ProfileId, profileId, StringComparison.Ordinal) && Entries != null && Entries.Count <= MaxEntries &&
		Entries.All(entry => entry != null && IsKnownService(entry.Service) && entry.Quantity > 0 && entry.Price >= 0 &&
			entry.PurchasedUtc != default && (entry.Currency is "RUB" or "USD" or "EUR"));

	public static bool IsKnownService(string service) =>
		service is "A10" or "DoublePass" or "Extraction" or "PriorityExfil" or "Uav" or "FocusedSweep";
}

public sealed class FireSupportPurchaseHistoryEntry
{
	public string Service { get; set; } = string.Empty;
	public int Quantity { get; set; }
	public int Price { get; set; }
	public string Currency { get; set; } = string.Empty;
	public DateTimeOffset PurchasedUtc { get; set; }
}
