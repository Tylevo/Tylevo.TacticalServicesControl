using EFT;
using EFT.Utilities;
using System;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

internal static class CargoTransferServiceData
{
	internal static GlobalConfiguration.ServiceData CopyWithIncludedHandling(
		GlobalConfiguration.ServiceData source)
	{
		if (source == null)
		{
			throw new ArgumentNullException(nameof(source));
		}

		return new GlobalConfiguration.ServiceData
		{
			TraderId = source.TraderId,
			ServiceType = source.ServiceType,
			SubServices = source.SubServices,
			TraderServiceRequirements = source.TraderServiceRequirements,
			UniqueItems = source.UniqueItems,
			// EFT keeps a zero-valued RUB requirement unresolved when no RUB
			// item exists. An empty dictionary permits a truly cash-free send.
			ServiceItemCost = new GlobalsDictionary<int>()
		};
	}
}
