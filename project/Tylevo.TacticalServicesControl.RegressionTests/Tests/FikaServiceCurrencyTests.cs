using Fika.Core.Networking.LiteNetLib.Utils;
using SamSWAT.FireSupport.ArysReloaded.Fika;
using SamSWAT.FireSupport.ArysReloaded.Unity;

internal static class FikaServiceCurrencyTests
{
	[RegressionTest]
	private static void MixedCurrenciesRoundTripWithoutChangingOtherServices()
	{
		var expected = new FireSupportSettingsPacket
		{
			PaymentCurrency = PaymentCurrency.USD,
			ServiceCurrencies = new() { ["A10"] = "GP", ["Uav"] = "BTC", ["Extraction"] = "RUB" }
		};
		var writer = new NetDataWriter(); expected.Serialize(writer);
		var actual = new FireSupportSettingsPacket(); var reader = new NetDataReader(writer.ToArray()); actual.Deserialize(reader);
		AssertEx.Equal(0, reader.AvailableBytes);
		AssertEx.Equal("GP", actual.ServiceCurrencies["A10"]);
		AssertEx.Equal("BTC", actual.ServiceCurrencies["Uav"]);
		AssertEx.Equal("RUB", actual.ServiceCurrencies["Extraction"]);
		AssertEx.Equal("USD", actual.ServiceCurrencies["DoublePass"]);
		AssertEx.True(FireSupportServiceSemantics.SupportsServiceCurrencies(actual.ServiceSemanticsVersion));
	}

	[RegressionTest]
	private static void LegacyReaderSeesLockedSemanticsBeforeCurrencyExtension()
	{
		var packet = new FireSupportSettingsPacket { PaymentCurrency = PaymentCurrency.GP };
		var writer = new NetDataWriter(); packet.Serialize(writer);
		byte[] bytes = writer.ToArray(); Array.Resize(ref bytes, bytes.Length - FireSupportSettingsPacket.ServiceCurrencyTailBytes);
		var legacy = new FireSupportSettingsPacket(); legacy.Deserialize(new NetDataReader(bytes));
		AssertEx.False(FireSupportServiceSemantics.SupportsProgression(legacy.ServiceSemanticsVersion));
		AssertEx.False(FireSupportServiceSemantics.SupportsServiceCurrencies(FireSupportServiceSemantics.ProgressionVersion));
	}

	[RegressionTest]
	private static void InvalidServiceCurrencyCannotEnableManualPayment()
	{
		var packet = new FireSupportSettingsPacket { ServiceCurrencies = new() { ["Uav"] = "TYPO" } };
		var writer = new NetDataWriter(); packet.Serialize(writer);
		var actual = new FireSupportSettingsPacket(); actual.Deserialize(new NetDataReader(writer.ToArray()));
		AssertEx.False(FireSupportServiceSemantics.SupportsServiceCurrencies(actual.ServiceSemanticsVersion));
		AssertEx.False(PaymentCurrencyInfo.TryParse(actual.ServiceCurrencies["Uav"], out _));
	}
}
