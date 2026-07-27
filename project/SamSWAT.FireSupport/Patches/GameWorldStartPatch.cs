using EFT;
using HarmonyLib;
using JetBrains.Annotations;
using SamSWAT.FireSupport.ArysReloaded.Unity;
using SPT.Reflection.Patching;
using System.Reflection;

namespace SamSWAT.FireSupport.ArysReloaded.Patches;

[UsedImplicitly]
public class GameWorldStartPatch : ModulePatch
{
	protected override MethodBase GetTargetMethod()
	{
		return AccessTools.Method(typeof(GameWorld), nameof(GameWorld.OnGameStarted));
	}

	[PatchPostfix]
	private static void PostfixPatch()
	{
		UavDeviceHandsService.CancelAllPending("raid started");
		UavPhoneHotkeyController.ResetForRaidBoundary("raid started");
		UavDeviceActivationController.ResetForRaidBoundary("raid started");
		UavReconOverlay.Deactivate("raid started");
		UavAircraftLoiterController.ResetAll("raid started");
		MainMenuPurchaseController.CloseForRaidStart();
		FireSupportAuthorizations.Reset();
		FireSupportServerConfigClient.OnRaidStarted();

		if (FireSupportAudio.Instance != null)
		{
			FireSupportAudio.Instance.PlayVoiceover(EVoiceoverType.StationReminder);
		}
	}
}
