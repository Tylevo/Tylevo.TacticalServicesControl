using Cysharp.Threading.Tasks;
using EFT;
using EFT.UI;
using HarmonyLib;
using JetBrains.Annotations;
using SamSWAT.FireSupport.ArysReloaded.Unity;
using SPT.Reflection.Patching;
using System;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine.UI;

namespace SamSWAT.FireSupport.ArysReloaded.Patches;

/// <summary>Frames only the Pilot after the native asynchronous avatar assignment completes.</summary>
[UsedImplicitly]
internal sealed class PilotTraderPortraitPatch : ModulePatch
{
	protected override MethodBase GetTargetMethod() =>
		AccessTools.Method(typeof(TraderAvatar), nameof(TraderAvatar.SetAvatar));

	[PatchPrefix]
	private static void Prefix(TraderAvatar __instance, Profile.TraderInfo ____traderInfo,
		Image ____avatar, out PortraitLoad __state)
	{
		__state = null;
		if (____avatar == null) return;
		PilotTraderPortrait owner = ____avatar.GetComponent<PilotTraderPortrait>();
		bool pilot = ____traderInfo?.Id == PilotServicesView.PilotTraderId;
		if (!pilot && owner == null) return;
		if (owner == null) owner = ____avatar.gameObject.AddComponent<PilotTraderPortrait>();
		int generation = owner.BeginLoad(____avatar);
		if (pilot) __state = new PortraitLoad(__instance, owner, generation);
	}

	[PatchPostfix]
	private static void Postfix(ref Task __result, PortraitLoad __state)
	{
		if (__state != null && __result != null) __result = FrameWhenReadyAsync(__result, __state);
	}

	private static async Task FrameWhenReadyAsync(Task nativeLoad, PortraitLoad load)
	{
		// Preserve native loading/cancellation failures for its existing error handler.
		await nativeLoad;
		await UniTask.SwitchToMainThread();
		if (load.Avatar == null || load.Owner == null || !load.Owner.IsCurrent(load.Generation)) return;
		try
		{
			load.Owner.Apply(load.Generation);
		}
		catch (Exception exception)
		{
			FireSupportPlugin.LogSource?.LogWarning($"TSC Pilot portrait framing unavailable: {exception.Message}");
		}
	}

	private sealed class PortraitLoad(TraderAvatar avatar, PilotTraderPortrait owner, int generation)
	{
		internal readonly TraderAvatar Avatar = avatar;
		internal readonly PilotTraderPortrait Owner = owner;
		internal readonly int Generation = generation;
	}
}
