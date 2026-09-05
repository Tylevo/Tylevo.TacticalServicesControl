using EFT.InventoryLogic;
using EFT.NextObservedPlayer;
using HarmonyLib;
using SamSWAT.FireSupport.ArysReloaded.Unity;
using SPT.Reflection.Patching;
using System.Reflection;

namespace SamSWAT.FireSupport.ArysReloaded.Patches;

internal sealed class UavDeviceUsableInterfaceDispatchPatch : ModulePatch
{
	protected override MethodBase GetTargetMethod()
	{
		return AccessTools.Method(
			typeof(ObservedPlayerUsableItemController),
			nameof(ObservedPlayerUsableItemController.GetObservedUsableItem),
			new[] { typeof(Item) });
	}

	[PatchPrefix]
	private static bool Prefix(ref IObservedUsableItem __result, Item item)
	{
		if (item is not UavDeviceItem)
		{
			if (UavDeviceConstants.IsUavDeviceTemplate(item))
			{
				FireSupportPlugin.LogSource.LogWarning(
					$"TerraGroup TSC Uplink usable interface not routed: runtime item type is {item.GetType().FullName}, expected {typeof(UavDeviceItem).FullName}.");
			}

			return true;
		}

		__result = new UavDeviceInterfaceClass();
		return false;
	}
}
