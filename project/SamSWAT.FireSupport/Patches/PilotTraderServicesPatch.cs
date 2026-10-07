using EFT;
using EFT.InputSystem;
using EFT.InventoryLogic;
using EFT.Trading;
using EFT.UI;
using HarmonyLib;
using JetBrains.Annotations;
using SamSWAT.FireSupport.ArysReloaded.Unity;
using SPT.Reflection.Patching;
using System.Reflection;

namespace SamSWAT.FireSupport.ArysReloaded.Patches;

[UsedImplicitly]
internal sealed class PilotServicesAvailabilityPatch : ModulePatch
{
	protected override MethodBase GetTargetMethod() =>
		AccessTools.Method(typeof(ServicesScreen), nameof(ServicesScreen.CheckAvailableServices));

	[PatchPostfix]
	private static void Postfix(Trader __0, ref bool __result)
	{
		if (PilotServicesView.IsPilot(__0)) __result = MainMenuPurchaseController.ServicesEnabled;
	}
}

[UsedImplicitly]
internal sealed class PilotServicesShowPatch : ModulePatch
{
	protected override MethodBase GetTargetMethod() =>
		AccessTools.Method(typeof(ServicesScreen), nameof(ServicesScreen.Show));

	[PatchPrefix]
	private static bool Prefix(ServicesScreen __instance, Trader __0, Profile __1,
		InventoryController __2, IEftSession __7,
		ref ServiceView ____currentServiceView)
	{
		if (!PilotServicesView.IsPilot(__0)) return true;
		PilotServicesView view = PilotServicesView.GetOrCreate(__instance);
		// Native Close calls _currentServiceView.Close() unconditionally. Give it
		// a real ServiceView so normal tab switching and disposal stay intact.
		____currentServiceView = view;
		__instance.ShowGameObject();
		view.Open(__1, __2, __7);
		return false;
	}
}

[UsedImplicitly]
internal sealed class PilotServicesEscapePatch : ModulePatch
{
	protected override MethodBase GetTargetMethod() =>
		AccessTools.Method(typeof(TraderScreensGroup), nameof(TraderScreensGroup.TranslateCommand));

	[PatchPrefix]
	private static bool Prefix(TraderScreensGroup __instance, ECommand __0,
		ref InputNode.ETranslateResult __result)
	{
		if (__0 != ECommand.Escape || !PilotServicesView.IsPilot(__instance.Trader)) return true;
		PilotServicesView view = __instance._servicesScreen?.GetComponentInChildren<PilotServicesView>();
		if (view == null || !view.isActiveAndEnabled ||
			!MainMenuPurchaseController.DismissConfirmation(view.RectTransform)) return true;
		__result = InputNode.ETranslateResult.BlockAll;
		return false;
	}
}
