using EFT.InventoryLogic;
using WTTClientCommonLib.Attributes;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

[CustomParent(UavDeviceConstants.UavDeviceParentTpl, typeof(UavDeviceItem), typeof(ItemTemplate))]
public sealed class UavDeviceItem : Item
{
	public UavDeviceItem(string id, ItemTemplate template) : base(id, template)
	{
	}
}
