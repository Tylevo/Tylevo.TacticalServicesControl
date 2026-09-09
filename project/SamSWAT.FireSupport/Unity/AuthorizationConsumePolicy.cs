using System;
using System.Collections.Generic;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

internal static class AuthorizationConsumePolicy
{
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
		// The raid purchase path grants free authorizations locally before any
		// server request, even when the configured payment source is the stash.
		if (purchase.Cost == 0 ||
		    string.Equals(purchase.PaymentSource, nameof(PaymentSource.CarriedRoubles), StringComparison.Ordinal)) return true;
		// Successful stash responses retain the configured cash preference.
		// A carried purchase or fallback is explicitly labelled CarriedRoubles.
		serverBacked = purchase.PaymentSource is nameof(PaymentSource.StashRoubles)
			or nameof(PaymentSource.PreferCarriedThenStash)
			or nameof(PaymentSource.PreferStashThenCarried);
		return serverBacked;
	}
}
