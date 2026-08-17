using EFT.InventoryLogic;
using WTTClientCommonLib.Attributes;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

// The SpecItem class/template pair puts the phone in the special-equipment
// family so the icon shows the orange SPEC tag like the R1500. Deliberately
// NOT the rangefinder classes: the phone must never match rangefinder checks
// (the spotter flow requires the real rangefinder).
[CustomParent(UavDeviceConstants.UavDeviceParentTpl, typeof(UavDeviceItem), typeof(SpecItemTemplate))]
public sealed class UavDeviceItem : SpecItem
{
	public UavDeviceItem(string id, SpecItemTemplate template) : base(id, template)
	{
	}
}
