using EFT;
using HarmonyLib;
using JetBrains.Annotations;
using SamSWAT.FireSupport.ArysReloaded.Integration;
using SamSWAT.FireSupport.ArysReloaded.Unity;
using SPT.Reflection.Patching;
using System.Reflection;

namespace SamSWAT.FireSupport.ArysReloaded.Patches;

[UsedImplicitly]
public class GameWorldDisposePatch : ModulePatch
{
	protected override MethodBase GetTargetMethod()
	{
		return AccessTools.Method(typeof(GameWorld), nameof(GameWorld.Dispose));
	}

	[PatchPrefix]
	private static void PatchPrefix()
	{
		SeasonalModifiersBridge.ResetForRaidBoundary("raid disposed");
		UavDeviceHandsService.CancelAllPending("raid disposed");
		UavPhoneHotkeyController.ResetForRaidBoundary("raid disposed");
		UavDeviceActivationController.ResetForRaidBoundary("raid disposed");
		UavReconOverlay.Deactivate("raid disposed");
		UavAircraftLoiterController.ResetAll("raid disposed");
		FireSupportItemTransfer.ResetForRaidBoundary("raid disposed");
		FireSupportAuthorizations.Reset();
		FireSupportServerConfigClient.OnRaidEnded();

		bool hadController = FireSupportController.Instance != null;
		FireSupportController.DestroyCurrent("raid disposed");
		if (!hadController)
		{
			FireSupportRuntime.Dispose();
		}
	}
}
