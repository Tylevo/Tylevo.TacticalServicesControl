using System;
using System.Collections.Generic;
#nullable enable

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

/// <summary>Shared selection rules for service quotes and authoritative payment.</summary>
public static class ServicePaymentPolicy
{
	public const string Inherit = "Inherit";

	public static string GetServiceKey(ESupportType supportType) => supportType switch
	{
		ESupportType.Strafe => "A10",
		ESupportType.DoubleStrafe => "DoublePass",
		ESupportType.Uav => "Uav",
		ESupportType.FocusedSweep => "FocusedSweep",
		ESupportType.Extract => "Extraction",
		ESupportType.PriorityExfil => "PriorityExfil",
		_ => string.Empty
	};

	public static string GetCurrencyCode(string? globalCurrency,
		IReadOnlyDictionary<string, string>? overrides, ESupportType supportType)
	{
		string key = GetServiceKey(supportType);
		if (key.Length == 0) return string.Empty;
		string? selected = null;
		bool found = false;
		if (overrides != null)
		{
			foreach (KeyValuePair<string, string> entry in overrides)
			{
				if (!string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase)) continue;
				// Ambiguous administrator input must never select a payment asset.
				if (found) return string.Empty;
				selected = entry.Value;
				found = true;
			}
		}
		if (!found || string.Equals(selected?.Trim(), Inherit, StringComparison.OrdinalIgnoreCase))
			return globalCurrency ?? string.Empty;
		// An explicitly empty or invalid override is not an inherited price.
		return selected ?? string.Empty;
	}

	public static bool TryResolveCurrency(RaidOpsFireSupportServerConfig? config,
		ESupportType supportType, out PaymentCurrency currency)
	{
		currency = PaymentCurrency.RUB;
		return config != null && PaymentCurrencyInfo.TryParse(
			GetCurrencyCode(config.PaymentCurrency, config.ServiceCurrencies, supportType), out currency);
	}

	public static PaymentSource GetPaymentSource(PaymentSource source, PaymentCurrency currency) =>
		PaymentCurrencyInfo.IsStashOnly(currency) ? PaymentSource.StashRoubles : source;
}
