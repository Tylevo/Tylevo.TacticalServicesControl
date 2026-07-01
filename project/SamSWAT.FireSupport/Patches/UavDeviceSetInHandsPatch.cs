using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using SamSWAT.FireSupport.ArysReloaded.Unity;
using SPT.Reflection.Patching;
using System.Reflection;

namespace SamSWAT.FireSupport.ArysReloaded.Patches;

internal sealed class UavDeviceSetInHandsPatch : ModulePatch
{
	protected override MethodBase GetTargetMethod()
	{
		return typeof(Player).GetMethod(
			nameof(Player.SetInHandsUsableItem),
			BindingFlags.Public | BindingFlags.Instance);
	}

	[PatchPrefix]
	private static bool Prefix(Player __instance, Item __0, Callback<GInterface202> __1)
	{
		Item item = __0;
		Callback<GInterface202> callback = __1;

		if (item is not UavDeviceItem)
		{
			if (UavDeviceConstants.IsUavDeviceTemplate(item))
			{
				FireSupportPlugin.LogSource.LogWarning(
					$"TerraGroup TSC Uplink usable item equip not routed: runtime item type is {item.GetType().FullName}, expected {typeof(UavDeviceItem).FullName}.");
			}

			return true;
		}

		TscDiagnostics.LogPhone(
			$"TerraGroup TSC Uplink usable item equip intercepted. item={item.Id}, tpl={item.StringTemplateId}, type={item.GetType().FullName}.");

		__instance.Proceed<UavDeviceController>(item, callback, true);
		return false;
	}
}
