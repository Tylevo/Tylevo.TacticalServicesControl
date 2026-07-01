using EFT.InventoryLogic;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

internal static class UavDeviceConstants
{
	public const string UavDeviceParentTpl = "66f51f3a0000000000000a00";
	public const string UavDeviceTpl = "66f51f3a0000000000000a01";
	public const string UavDeviceLootBundlePath = "raidops/uav_uplink_loot.bundle";
	public const string UavDeviceContainerBundlePath = "raidops/uav_uplink_container.bundle";

	public static bool IsUavDevice(Item item)
	{
		return item is UavDeviceItem;
	}

	public static bool IsUavDeviceTemplate(Item item)
	{
		return item?.StringTemplateId == UavDeviceTpl ||
		       item?.TemplateId == UavDeviceTpl;
	}
}
