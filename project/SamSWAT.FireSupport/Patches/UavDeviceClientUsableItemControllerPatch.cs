using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using SamSWAT.FireSupport.ArysReloaded.Unity;
using SPT.Reflection.Patching;
using System.Reflection;
using System.Threading.Tasks;

namespace SamSWAT.FireSupport.ArysReloaded.Patches;

internal sealed class UavDeviceClientUsableItemControllerPatch : ModulePatch
{
	protected override MethodBase GetTargetMethod()
	{
		return typeof(ClientUsableItemController).GetMethod(
			"smethod_11",
			BindingFlags.Public | BindingFlags.Static);
	}

	[PatchPrefix]
	private static bool Prefix(
		ref Task<ClientUsableItemController> __result,
		ClientPlayer player,
		string itemId)
	{
		if (string.IsNullOrEmpty(itemId))
		{
			return true;
		}

		UavDeviceItem item = player.InventoryController.FindItem<UavDeviceItem>(itemId);
		if (item == null)
		{
			Item diagnosticItem = player.InventoryController.FindItem<Item>(itemId);
			if (UavDeviceConstants.IsUavDeviceTemplate(diagnosticItem))
			{
				FireSupportPlugin.LogSource.LogWarning(
					$"TerraGroup TSC Uplink client usable controller not routed: itemId={itemId}, runtime item type is {diagnosticItem.GetType().FullName}, expected {typeof(UavDeviceItem).FullName}.");
			}

			return true;
		}

		TscDiagnostics.LogPhone(
			$"TerraGroup TSC Uplink client usable controller intercepted. itemId={itemId}, tpl={item.StringTemplateId}, type={item.GetType().FullName}.");

		__result = Player.UsableItemController.smethod_7<ClientUsableItemController>(player, item);
		return false;
	}
}
