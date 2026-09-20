using System;
using System.Collections.Generic;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

internal static class AuthorizationConsumePolicy
{
	public static bool ShouldConsumeBeforeCash(PaymentMode mode, bool persistenceEnabled,
		bool spendCreditsBeforeCash, bool consumePurchasedAuthorization, bool requirePrepaidAuthorization) =>
		consumePurchasedAuthorization || requirePrepaidAuthorization || mode == PaymentMode.PhoneAuthorizations ||
		mode == PaymentMode.Hybrid && (!persistenceEnabled || spendCreditsBeforeCash) ||
		mode == PaymentMode.DirectRadial && !persistenceEnabled;

	public static bool PurchasedForBaseRequest(PaymentMode mode, bool persistenceEnabled,
		bool consumePurchasedAuthorization, bool requirePrepaidAuthorization) =>
		!persistenceEnabled && !requirePrepaidAuthorization &&
		(consumePurchasedAuthorization || mode == PaymentMode.DirectRadial);

	public static bool TryConsume(
		IDictionary<ESupportType, int> localCredits,
		IDictionary<ESupportType, int> serverCredits,
		ESupportType type,
		bool? requiredServerBacked,
		out bool serverBacked)
	{
		serverBacked = false;
		if (requiredServerBacked != true &&
		    localCredits.TryGetValue(type, out int localCount) && localCount > 0)
		{
			localCredits[type] = localCount - 1;
			return true;
		}
		if (requiredServerBacked == false ||
		    !serverCredits.TryGetValue(type, out int serverCount) || serverCount <= 0)
		{
			return false;
		}
		serverCredits[type] = serverCount - 1;
		serverBacked = true;
		return true;
	}

	public static bool TryGetPurchasedSource(FireSupportPurchaseResponse purchase, out bool serverBacked)
	{
		serverBacked = false;
		if (purchase?.Ok != true || !purchase.AuthorizationGranted || purchase.Cost < 0) return false;
		if (purchase.PurchasedAuthorizationServerBacked.HasValue)
		{
			serverBacked = purchase.PurchasedAuthorizationServerBacked.Value;
			return true;
		}
		// Compatibility for receipts issued by older versions. These names only
		// identify ownership of an already-paid credit; they cannot select a wallet.
		if (purchase.Cost == 0 ||
		    string.Equals(purchase.PaymentSource, "CarriedRoubles", StringComparison.Ordinal)) return true;
		serverBacked = purchase.PaymentSource is nameof(PaymentSource.StashRoubles)
			or "PreferCarriedThenStash"
			or "PreferStashThenCarried";
		return serverBacked;
	}
}
