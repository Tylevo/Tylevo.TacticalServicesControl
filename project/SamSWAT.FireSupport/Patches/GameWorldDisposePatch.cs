using EFT;
using HarmonyLib;
using JetBrains.Annotations;
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
		FireSupportAuthorizations.Reset();
		FireSupportServerConfigClient.OnRaidEnded();

		if (FireSupportController.Instance != null)
		{
			UnityEngine.Object.DestroyImmediate(FireSupportController.Instance);
			return;
		}

		FireSupportRuntime.Dispose();
	}
}
