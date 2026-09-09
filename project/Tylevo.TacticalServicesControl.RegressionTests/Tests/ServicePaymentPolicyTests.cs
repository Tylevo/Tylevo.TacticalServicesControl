using SamSWAT.FireSupport.ArysReloaded.Unity;

internal static class ServicePaymentPolicyTests
{
	[RegressionTest]
	private static void MixedServiceAssetsPreserveTheGlobalChoiceAndPrices()
	{
		var config = new RaidOpsFireSupportServerConfig
		{
			PaymentCurrency = "USD",
			Prices = new() { ["A10"] = 1, ["Extraction"] = 1, ["Uav"] = 10000 },
			ServiceCurrencies = new()
			{
				["A10"] = "GP", ["Extraction"] = "BTC", ["Uav"] = "RUB", ["FocusedSweep"] = "Inherit"
			}
		};
		foreach ((ESupportType service, PaymentCurrency expected) in new[]
		{
			(ESupportType.Strafe, PaymentCurrency.GP), (ESupportType.Extract, PaymentCurrency.BTC),
			(ESupportType.Uav, PaymentCurrency.RUB), (ESupportType.FocusedSweep, PaymentCurrency.USD),
			(ESupportType.DoubleStrafe, PaymentCurrency.USD), (ESupportType.PriorityExfil, PaymentCurrency.USD)
		})
		{
			AssertEx.True(ServicePaymentPolicy.TryResolveCurrency(config, service, out PaymentCurrency actual));
			AssertEx.Equal(expected, actual);
		}
		AssertEx.Equal("USD", config.PaymentCurrency);
		AssertEx.Equal(1, config.Prices["A10"]);
		AssertEx.Equal(1, config.Prices["Extraction"]);
		AssertEx.Equal(10000, config.Prices["Uav"]);
	}

	[RegressionTest]
	private static void InvalidOverridesNeverBecomeInheritedCashAndAmbiguousKeysAreRejected()
	{
		foreach (string value in new[] { "", " ", "not-currency", "DOGE" })
		{
			var config = new RaidOpsFireSupportServerConfig
			{
				PaymentCurrency = "RUB", ServiceCurrencies = new() { ["A10"] = value }
			};
			AssertEx.False(ServicePaymentPolicy.TryResolveCurrency(config, ESupportType.Strafe, out _));
			AssertEx.True(ServicePaymentPolicy.TryResolveCurrency(config, ESupportType.Uav, out _));
		}
		var ambiguous = new RaidOpsFireSupportServerConfig
		{
			PaymentCurrency = "RUB", ServiceCurrencies = new() { ["A10"] = "GP", ["a10"] = "BTC" }
		};
		AssertEx.False(ServicePaymentPolicy.TryResolveCurrency(ambiguous, ESupportType.Strafe, out _));
		AssertEx.False(ServicePaymentPolicy.TryResolveCurrency(ambiguous, ESupportType.None, out _));
	}

	[RegressionTest]
	private static void CoinPaymentsAlwaysUseStashAndCashRetainsItsConfiguredWallet()
	{
		foreach (PaymentSource source in Enum.GetValues<PaymentSource>())
		foreach (PaymentCurrency currency in Enum.GetValues<PaymentCurrency>())
		{
			PaymentSource expected = currency is PaymentCurrency.GP or PaymentCurrency.BTC ? PaymentSource.StashRoubles : source;
			AssertEx.Equal(expected, ServicePaymentPolicy.GetPaymentSource(source, currency));
		}
		AssertEx.Equal("1 GP", PaymentCurrencyInfo.Format(1, PaymentCurrency.GP));
		AssertEx.Equal("2 BTC", PaymentCurrencyInfo.Format(2, PaymentCurrency.BTC));
		AssertEx.Equal("$1,000", PaymentCurrencyInfo.Format(1000, PaymentCurrency.USD));
		AssertEx.Equal("5d235b4d86f7742e017bc88a", PaymentCurrencyInfo.GetTemplateId(PaymentCurrency.GP));
		AssertEx.Equal("59faff1d86f7746c51718c9c", PaymentCurrencyInfo.GetTemplateId(PaymentCurrency.BTC));
		AssertEx.False(PaymentCurrencyInfo.IsSupportedTemplateId("59faff1d86f7746c51718c9d"));
	}

	[RegressionTest]
	private static void CoinHistoryRemainsBoundToItsProfileAndOriginalAsset()
	{
		var history = new FireSupportPurchaseHistory
		{
			ProfileId = "test-profile",
			Entries = new()
			{
				new() { Service = "A10", Quantity = 1, Price = 1, Currency = "GP", PurchasedUtc = DateTimeOffset.UtcNow },
				new() { Service = "Extraction", Quantity = 1, Price = 1, Currency = "BTC", PurchasedUtc = DateTimeOffset.UtcNow }
			}
		};
		AssertEx.True(history.IsValidFor("test-profile"));
		AssertEx.False(history.IsValidFor("other-profile"));
		history.Entries[0].Currency = "DOGE";
		AssertEx.False(history.IsValidFor("test-profile"));
	}
}
