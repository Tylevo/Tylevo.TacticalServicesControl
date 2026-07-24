using EFT;
using EFT.UI;
using HarmonyLib;
using JetBrains.Annotations;
using SamSWAT.FireSupport.ArysReloaded.Unity;
using SPT.Reflection.Patching;
using System.Reflection;

namespace SamSWAT.FireSupport.ArysReloaded.Patches;

/// <summary>
/// Adds the pre-raid purchase entry only after EFT has populated its native
/// main-menu button stack and supplied the authenticated profile.
/// </summary>
[UsedImplicitly]
internal sealed class MainMenuPurchasePatch : ModulePatch
{
	protected override MethodBase GetTargetMethod()
	{
		return AccessTools.Method(
			typeof(MenuScreen),
			nameof(MenuScreen.Show),
			[
				typeof(Profile),
				typeof(MatchmakerPlayerControllerClass),
				typeof(ESessionMode)
			]);
	}

	[PatchPostfix]
	private static void Postfix(MenuScreen __instance, Profile __0)
	{
		MainMenuPurchaseController.Attach(__instance, __0);
	}
}
