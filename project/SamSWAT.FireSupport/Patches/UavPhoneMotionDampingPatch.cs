using EFT.Animations;
using HarmonyLib;
using SamSWAT.FireSupport.ArysReloaded.Unity;
using SPT.Reflection.Patching;
using System;
using System.Reflection;
using UnityEngine;

namespace SamSWAT.FireSupport.ArysReloaded.Patches;

/// <summary>
/// Reduces procedural walking and turning motion for the held upright phone.
/// The native hands still animate, and the phone continues to follow the palm.
/// Values are borrowed only for this call so they cannot remain on a later item.
/// </summary>
internal sealed class UavPhoneMotionDampingPatch : ModulePatch
{
	private const float RetainedMotion = 0.25f;

	protected override MethodBase GetTargetMethod()
	{
		return AccessTools.DeclaredMethod(
			typeof(ProceduralWeaponAnimation),
			nameof(ProceduralWeaponAnimation.ProcessEffectors),
			new[] { typeof(float), typeof(int), typeof(Vector3), typeof(Vector3) });
	}

	[PatchPrefix]
	private static void Prefix(ProceduralWeaponAnimation __instance, out MotionState __state)
	{
		__state = default;
		if (!UavDeviceController.ShouldDampenUprightPhoneMotion(__instance))
		{
			return;
		}

		__state = new MotionState(__instance.Walk, __instance.MotionReact);
		__state.Apply();
	}

	[PatchPostfix]
	private static void Postfix(ref MotionState __state)
	{
		__state.Restore();
	}

	[PatchFinalizer]
	private static Exception Finalizer(Exception __exception, ref MotionState __state)
	{
		// Also restore when native processing or another patch throws. Returning
		// the same exception preserves the game's existing error behavior.
		__state.Restore();
		return __exception;
	}

	private struct MotionState
	{
		private readonly WalkEffector _walk;
		private readonly MotionEffector _motion;
		private readonly float _walkIntensity;
		private readonly float _walkOverweight;
		private readonly float _motionIntensity;
		private readonly Vector3 _swayFactors;
		private bool _applied;

		public MotionState(WalkEffector walk, MotionEffector motion)
		{
			_walk = walk;
			_motion = motion;
			_walkIntensity = walk?.Intensity ?? 0f;
			_walkOverweight = walk?.Overweight ?? 0f;
			_motionIntensity = motion?.Intensity ?? 0f;
			_swayFactors = motion?.SwayFactors ?? Vector3.zero;
			_applied = false;
		}

		public void Apply()
		{
			_applied = true;
			if (_walk != null)
			{
				_walk.Intensity = _walkIntensity * RetainedMotion;
				// Native overweight footsteps have their own amplitude and do not
				// pass through Walk.Intensity.
				_walk.Overweight = _walkOverweight * RetainedMotion;
			}

			if (_motion != null)
			{
				_motion.Intensity = _motionIntensity * RetainedMotion;
				// Mouse turning uses SwayFactors independently of Intensity. Keep
				// native tracking running to avoid stale input when the phone closes.
				_motion.SwayFactors = _swayFactors * RetainedMotion;
			}
		}

		public void Restore()
		{
			if (!_applied)
			{
				return;
			}

			_applied = false;
			if (_walk != null)
			{
				_walk.Intensity = _walkIntensity;
				_walk.Overweight = _walkOverweight;
			}

			if (_motion != null)
			{
				_motion.Intensity = _motionIntensity;
				_motion.SwayFactors = _swayFactors;
			}
		}
	}
}
