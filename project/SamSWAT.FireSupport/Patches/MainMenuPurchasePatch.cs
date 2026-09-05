using EFT;
using EFT.UI;
using EFT.UI.Matchmaker;
using HarmonyLib;
using JetBrains.Annotations;
using SamSWAT.FireSupport.ArysReloaded.Unity;
using SPT.Reflection.Patching;
using System.Reflection;

namespace SamSWAT.FireSupport.ArysReloaded.Patches;

/// <summary>
/// Binds the pre-raid footer entry after EFT supplies the authenticated
/// main-menu profile. The controller waits for PreloaderUI's native taskbar.
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
				typeof(MatchmakerPlayersController),
				typeof(ESessionMode)
			]);
	}

	[PatchPostfix]
	private static void Postfix(MenuScreen __instance, Profile __0)
	{
		MainMenuPurchaseController.Attach(__instance, __0);
	}
}
