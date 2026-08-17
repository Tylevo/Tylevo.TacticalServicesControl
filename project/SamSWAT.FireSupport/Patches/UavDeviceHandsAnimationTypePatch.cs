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
		return AccessTools.DeclaredMethod(
			typeof(Player),
			nameof(Player.GetWeaponAnimationType),
			new[] { typeof(Player.AbstractHandsController) });
	}

	[PatchPrefix]
	private static bool Prefix(
		ref PlayerAnimator.EWeaponAnimationType __result,
		Player.AbstractHandsController __0)
	{
		if (__0?.Item is not UavDeviceItem)
		{
			if (UavDeviceConstants.IsUavDeviceTemplate(__0?.Item))
			{
				FireSupportPlugin.LogSource.LogWarning(
					$"TerraGroup TSC Uplink hands animation not forced: runtime item type is {__0.Item.GetType().FullName}, expected {typeof(UavDeviceItem).FullName}.");
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
