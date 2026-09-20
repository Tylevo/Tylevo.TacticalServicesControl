using System;
using System.Globalization;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public enum PaymentCurrency
{
	RUB = 0,
	USD = 1,
	EUR = 2,
	GP = 3,
	BTC = 4
}

public static class PaymentCurrencyInfo
{
	public const string RoubleTemplateId = "5449016a4bdc2d6f028b456f";
	public const string DollarTemplateId = "5696686a4bdc2da3298b456a";
	public const string EuroTemplateId = "569668774bdc2da2298b4568";
	public const string GpCoinTemplateId = "5d235b4d86f7742e017bc88a";
	public const string BitcoinTemplateId = "59faff1d86f7746c51718c9c";

	public static PaymentCurrency Normalize(PaymentCurrency currency)
	{
		return currency is PaymentCurrency.RUB or PaymentCurrency.USD or PaymentCurrency.EUR or PaymentCurrency.GP or PaymentCurrency.BTC
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
			"gp coin" or "gp coins" => PaymentCurrency.GP,
			"bitcoin" or "bitcoins" or "physical bitcoin" => PaymentCurrency.BTC,
			_ => (PaymentCurrency)(-1)
		};
		if (currency is PaymentCurrency.RUB or PaymentCurrency.USD or PaymentCurrency.EUR or PaymentCurrency.GP or PaymentCurrency.BTC)
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
			PaymentCurrency.GP => GpCoinTemplateId,
			PaymentCurrency.BTC => BitcoinTemplateId,
			_ => RoubleTemplateId
		};
	}

	public static string GetCode(PaymentCurrency currency)
	{
		return Normalize(currency).ToString();
	}

	public static bool IsBarter(PaymentCurrency currency) => currency is PaymentCurrency.GP or PaymentCurrency.BTC;

	public static bool IsSupportedTemplateId(string templateId) =>
		string.Equals(templateId, RoubleTemplateId, StringComparison.OrdinalIgnoreCase) ||
		string.Equals(templateId, DollarTemplateId, StringComparison.OrdinalIgnoreCase) ||
		string.Equals(templateId, EuroTemplateId, StringComparison.OrdinalIgnoreCase) ||
		string.Equals(templateId, GpCoinTemplateId, StringComparison.OrdinalIgnoreCase) ||
		string.Equals(templateId, BitcoinTemplateId, StringComparison.OrdinalIgnoreCase);

	public static string GetSymbol(PaymentCurrency currency)
	{
		return Normalize(currency) switch
		{
			PaymentCurrency.USD => "$",
			PaymentCurrency.EUR => "\u20AC",
			PaymentCurrency.GP => "GP",
			PaymentCurrency.BTC => "BTC",
			_ => "\u20BD"
		};
	}

	public static string GetDisplayName(PaymentCurrency currency)
	{
		return Normalize(currency) switch
		{
			PaymentCurrency.USD => "US Dollars",
			PaymentCurrency.EUR => "Euros",
			PaymentCurrency.GP => "GP coins",
			PaymentCurrency.BTC => "Bitcoin",
			_ => "Roubles"
		};
	}

	public static string Format(int amount, PaymentCurrency currency)
	{
		return IsBarter(currency)
			? FormatCode(amount, currency)
			: $"{GetSymbol(currency)}{Math.Max(0, amount).ToString("N0", CultureInfo.InvariantCulture)}";
	}

	public static string FormatCode(int amount, PaymentCurrency currency)
	{
		return $"{Math.Max(0, amount).ToString("N0", CultureInfo.InvariantCulture)} {GetCode(currency)}";
	}
}
