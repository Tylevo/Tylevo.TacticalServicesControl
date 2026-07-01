using EFT;
using HarmonyLib;
using SamSWAT.FireSupport.ArysReloaded.Unity;
using SPT.Reflection.Patching;
using System.Reflection;

namespace SamSWAT.FireSupport.ArysReloaded.Patches;

internal sealed class UavDeviceHandsAnimationTypePatch : ModulePatch
{
	private static bool s_logged;

	protected override MethodBase GetTargetMethod()
	{
		return typeof(HandsControllerClass).GetMethod(
			"method_49",
			BindingFlags.Public | BindingFlags.Instance);
	}

	[PatchPrefix]
	private static bool Prefix(
		ref PlayerAnimator.EWeaponAnimationType __result,
		HandsControllerClass __instance)
	{
		if (__instance.ItemInHands is not UavDeviceItem)
		{
			if (UavDeviceConstants.IsUavDeviceTemplate(__instance.ItemInHands))
			{
				FireSupportPlugin.LogSource.LogWarning(
					$"TerraGroup TSC Uplink hands animation not forced: runtime item type is {__instance.ItemInHands.GetType().FullName}, expected {typeof(UavDeviceItem).FullName}.");
			}

			return true;
		}

		__result = PlayerAnimator.EWeaponAnimationType.Pistol;
		if (!s_logged)
		{
			s_logged = true;
			TscDiagnostics.LogPhone("TerraGroup TSC Uplink hands animation profile forced to Pistol.");
		}

		return false;
	}
}
