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

		// Same guard as the quick-use patch: only claim items that are actually
		// in the player's inventory, so ground-pickup flows stay vanilla.
		if (__instance.InventoryController.FindItem<UavDeviceItem>(item.Id) == null)
		{
			TscDiagnostics.LogPhone(
				$"TSC Uplink usable item equip not intercepted: item is not in the player's inventory. item={item.Id}.");
			return true;
		}

		TscDiagnostics.LogPhone(
			$"TerraGroup TSC Uplink usable item equip intercepted. item={item.Id}, tpl={item.StringTemplateId}, type={item.GetType().FullName}.");

		try
		{
			__instance.Proceed<UavDeviceController>(item, callback, true);
		}
		catch (System.Exception ex)
		{
			// EFT's caller waits on this callback; leaving it uninvoked freezes
			// the player in the interaction state.
			FireSupportPlugin.LogSource.LogWarning($"TSC Uplink usable item equip failed. {ex}");
			callback?.Invoke(new Result<GInterface202>(null, ex.Message, 0));
		}

		return false;
	}
}
