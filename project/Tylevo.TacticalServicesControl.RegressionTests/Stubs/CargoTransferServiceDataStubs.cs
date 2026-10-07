// Only the DTO surface used by the source-linked copy seam. Native EFT's
// purchase algorithm is separately inspected against the supported game DLL.
namespace EFT
{
	internal enum ETraderServiceType
	{
		BtrItemsDelivery = 3,
		TransitItemsDelivery = 6
	}

	internal sealed class ServiceRequirements;
	internal sealed class SubService;

	internal sealed class GlobalConfiguration
	{
		internal sealed class ServiceData
		{
			public string TraderId { get; set; } = string.Empty;
			public ETraderServiceType ServiceType { get; set; }
			public SubService[] SubServices { get; set; } = [];
			public ServiceRequirements TraderServiceRequirements { get; set; } = new();
			public string[] UniqueItems { get; set; } = [];
			public Utilities.GlobalsDictionary<int> ServiceItemCost { get; set; } = new();
		}
	}
}

namespace EFT.Utilities
{
	internal sealed class GlobalsDictionary<T> : Dictionary<string, T>;
}
