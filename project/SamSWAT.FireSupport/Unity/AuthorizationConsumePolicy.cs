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

	public static bool HasRequestBudget(PaymentMode mode, int baseRequests,
		bool hasAuthorization, bool hasPrepaidAuthorization) => mode switch
	{
		PaymentMode.PhoneAuthorizations => hasPrepaidAuthorization || baseRequests > 0 && hasAuthorization,
		PaymentMode.Hybrid => baseRequests > 0 || hasPrepaidAuthorization,
		_ => baseRequests > 0
	};

	public static void RefundLocal(
		IDictionary<ESupportType, int> localCredits,
		IDictionary<ESupportType, int> baseRequestRefunds,
		ESupportType type,
		bool purchasedForBaseRequest)
	{
		IDictionary<ESupportType, int> destination = purchasedForBaseRequest ? baseRequestRefunds : localCredits;
		destination.TryGetValue(type, out int count);
		destination[type] = count + 1;
	}

	public static bool TryConsumeForDeployment(
		IDictionary<ESupportType, int> localCredits,
		IDictionary<ESupportType, int> serverCredits,
		IDictionary<ESupportType, int> baseRequestRefunds,
		ESupportType type,
		bool? requiredServerBacked,
		out bool serverBacked,
		out bool purchasedForBaseRequest)
	{
		purchasedForBaseRequest = false;
		// Real prepaid credits retain their budget bypass. A refunded cash use
		// remains tied to the base request even on its second or later attempt.
		if (TryConsume(localCredits, serverCredits, type, requiredServerBacked, out serverBacked)) return true;
		if (requiredServerBacked != true && TryConsumeOne(baseRequestRefunds, type))
		{
			purchasedForBaseRequest = true;
			return true;
		}
		return false;
	}

	private static bool TryConsumeOne(IDictionary<ESupportType, int> credits, ESupportType type)
	{
		if (!credits.TryGetValue(type, out int count) || count <= 0) return false;
		credits[type] = count - 1;
		return true;
	}

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
