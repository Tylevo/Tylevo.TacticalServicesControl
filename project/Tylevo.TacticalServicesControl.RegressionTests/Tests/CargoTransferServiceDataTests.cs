using EFT;
using SamSWAT.FireSupport.ArysReloaded.Unity;

internal static class CargoTransferServiceDataTests
{
	[RegressionTest]
	private static void IncludedHandlingCopyPreservesNativeRequirementsAndOriginalCosts()
	{
		var source = new GlobalConfiguration.ServiceData
		{
			TraderId = "native-trader",
			ServiceType = ETraderServiceType.TransitItemsDelivery,
			SubServices = [new SubService()],
			TraderServiceRequirements = new ServiceRequirements(),
			UniqueItems = ["native-service-item"],
			ServiceItemCost = new() { ["roubles"] = 12345, ["other-native-cost"] = 2 }
		};

		GlobalConfiguration.ServiceData copy = CargoTransferServiceData.CopyWithIncludedHandling(source);

		AssertEx.False(ReferenceEquals(source, copy));
		AssertEx.Equal(source.TraderId, copy.TraderId);
		AssertEx.Equal(source.ServiceType, copy.ServiceType);
		AssertEx.True(ReferenceEquals(source.SubServices, copy.SubServices));
		AssertEx.True(ReferenceEquals(source.TraderServiceRequirements, copy.TraderServiceRequirements));
		AssertEx.True(ReferenceEquals(source.UniqueItems, copy.UniqueItems));
		AssertEx.False(ReferenceEquals(source.ServiceItemCost, copy.ServiceItemCost));
		AssertEx.Equal(0, copy.ServiceItemCost.Count,
			"No payment requirement may remain: native EFT rejects even RUB=0 when no carried RUB item exists.");
		AssertEx.Equal(2, source.ServiceItemCost.Count);
		AssertEx.Equal(12345, source.ServiceItemCost["roubles"]);
		AssertEx.Equal(2, source.ServiceItemCost["other-native-cost"]);
	}

	[RegressionTest]
	private static void PendingCargoCopySurvivesLaterNativeQuotesAndOtherPurchases()
	{
		var source = new GlobalConfiguration.ServiceData
		{
			ServiceType = ETraderServiceType.BtrItemsDelivery,
			ServiceItemCost = new() { ["roubles"] = 50000 }
		};
		GlobalConfiguration.ServiceData pending = CargoTransferServiceData.CopyWithIncludedHandling(source);
		GlobalConfiguration.ServiceData next = CargoTransferServiceData.CopyWithIncludedHandling(source);

		// EFT can retain the first copy while a different screen recalculates
		// its global quote. Neither later quote nor purchase shares its costs.
		source.ServiceItemCost["roubles"] = 75000;
		next.ServiceItemCost["roubles"] = 1;
		AssertEx.Equal(0, pending.ServiceItemCost.Count);
		AssertEx.Equal(75000, source.ServiceItemCost["roubles"]);
		AssertEx.Equal(1, next.ServiceItemCost["roubles"]);
	}
}
