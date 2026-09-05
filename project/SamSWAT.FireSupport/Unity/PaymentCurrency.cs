using System;
using System.Globalization;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public enum PaymentCurrency
{
	RUB = 0,
	USD = 1,
	EUR = 2
}

public static class PaymentCurrencyInfo
{
	public const string RoubleTemplateId = "5449016a4bdc2d6f028b456f";
	public const string DollarTemplateId = "5696686a4bdc2da3298b456a";
	public const string EuroTemplateId = "569668774bdc2da2298b4568";

	public static PaymentCurrency Normalize(PaymentCurrency currency)
	{
		return currency is PaymentCurrency.RUB or PaymentCurrency.USD or PaymentCurrency.EUR
			? currency
			: PaymentCurrency.RUB;
	}

	public static PaymentCurrency Parse(
		string value,
		PaymentCurrency fallback = PaymentCurrency.RUB)
	{
		return TryParse(value, out PaymentCurrency parsed)
			? parsed
			: Normalize(fallback);
	}

	public static bool TryParse(string value, out PaymentCurrency currency)
	{
		if (Enum.TryParse(value, ignoreCase: true, out PaymentCurrency parsed) &&
		    Normalize(parsed) == parsed)
		{
			currency = parsed;
			return true;
		}

		currency = value?.Trim().ToLowerInvariant() switch
		{
			"rouble" or "roubles" or "ruble" or "rubles" => PaymentCurrency.RUB,
			"dollar" or "dollars" or "usdollars" or "us dollars" => PaymentCurrency.USD,
			"euro" or "euros" => PaymentCurrency.EUR,
			_ => (PaymentCurrency)(-1)
		};
		if (currency is PaymentCurrency.RUB or PaymentCurrency.USD or PaymentCurrency.EUR)
		{
			return true;
		}

		currency = PaymentCurrency.RUB;
		return false;
	}

	public static string GetTemplateId(PaymentCurrency currency)
	{
		return Normalize(currency) switch
		{
			PaymentCurrency.USD => DollarTemplateId,
			PaymentCurrency.EUR => EuroTemplateId,
			_ => RoubleTemplateId
		};
	}

	public static string GetCode(PaymentCurrency currency)
	{
		return Normalize(currency).ToString();
	}

	public static string GetSymbol(PaymentCurrency currency)
	{
		return Normalize(currency) switch
		{
			PaymentCurrency.USD => "$",
			PaymentCurrency.EUR => "\u20AC",
			_ => "\u20BD"
		};
	}

	public static string GetDisplayName(PaymentCurrency currency)
	{
		return Normalize(currency) switch
		{
			PaymentCurrency.USD => "US Dollars",
			PaymentCurrency.EUR => "Euros",
			_ => "Roubles"
		};
	}

	public static string Format(int amount, PaymentCurrency currency)
	{
		return $"{GetSymbol(currency)}{Math.Max(0, amount).ToString("N0", CultureInfo.InvariantCulture)}";
	}

	public static string FormatCode(int amount, PaymentCurrency currency)
	{
		return $"{Math.Max(0, amount).ToString("N0", CultureInfo.InvariantCulture)} {GetCode(currency)}";
	}
}
