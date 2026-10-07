using System;
using BepInEx.Configuration;
using UnityEngine;

namespace TscHh60Visual
{
    public sealed partial class Plugin
    {
        private ConfigEntry<bool> _benchAutomaticBursts;
        private ConfigEntry<KeyboardShortcut> _benchHoldFireKey;
        private EscortBenchTargeting _benchRightTargeting;
        private readonly EscortBurstController[] _benchBursts = new EscortBurstController[2];
        private readonly int[] _benchAimGeneration = new int[2];
        private System.Random _benchAimRandom;
        private bool _benchHoldFire;

        private void BindAutomaticBench(string section)
        {
            _benchAutomaticBursts = Config.Bind(section, "Permit automatic bursts", false,
                "Allow both door guns to fire independently in PatrolAutomaticBursts. Three-round bursts, 0.12 seconds between rounds, 2.5-second cooldown, and 0.6-second aim settling. Each gun has a 60-round test budget. Every round rechecks the target and actual trajectory. The caller owns these real shots, with normal kill, quest, XP and faction consequences.");
            _benchHoldFireKey = Config.Bind(section, "Hold or resume automatic fire", new KeyboardShortcut(KeyCode.Delete, KeyCode.RightControl),
                "Pause or resume both automatic door guns for this session. Resuming requires fresh aim settling. Recalling always stops fire.");
        }

        private void PrepareAutomaticBench()
        {
            _benchTargeting = new EscortBenchTargeting(_benchAircraft, _benchCaller, _benchBallistics, true, 0);
            _benchRightTargeting = new EscortBenchTargeting(_benchAircraft, _benchCaller, _benchBallistics, true, 1);
            _benchAimRandom = new System.Random(Guid.NewGuid().GetHashCode());
            for (int i = 0; i < 2; i++)
            {
                _benchBursts[i] = new EscortBurstController();
                _benchAimGeneration[i] = 0;
            }
        }

        private void TickBenchTargeting(float dt)
        {
            bool moving = _activeBenchMode == EscortTestMode.PatrolManualShots || EscortBenchModePolicy.IsAutomatic(_activeBenchMode);
            if (moving && (_benchFlight == null || !_benchFlight.CanTrackTargets))
            {
                _benchTargeting?.Suspend("tracking suspended while flight is not in an active local patrol");
                _benchRightTargeting?.Suspend("tracking suspended while flight is not in an active local patrol");
                return;
            }
            _benchTargeting?.Tick(dt);
            _benchRightTargeting?.Tick(dt);
        }

        private string AutomaticBenchSummary => "Bursts | Left " + BurstStatus(0) + " | Right " + BurstStatus(1);
        private string BurstStatus(int gun) => _benchBursts[gun] == null ? "preparing" :
            _benchBursts[gun].AcceptedRounds + "/60 rounds, " + _benchBursts[gun].Status;

        private void TickAutomaticBench(bool inputAvailable, float dt)
        {
            if (!EscortBenchModePolicy.IsAutomatic(_activeBenchMode)) return;
            // The manual trigger takes priority if both actions share a binding.
            // Do not silently toggle persistent auto hold while manually firing.
            if (_benchManualHeld) { SuspendAutomaticBursts(); return; }
            if (inputAvailable && _benchHoldFireKey.Value.IsDown())
            {
                _benchHoldFire = !_benchHoldFire;
                SetBenchMessage(_benchHoldFire ? "Both door guns holding fire." : "Automatic fire resumed; acquiring and settling aim.");
            }
            bool activeFrame = inputAvailable && EscortBenchSafetyPolicy.Finite(dt) && dt > 0f && dt <= 0.25f;
            string reason;
            if (!EscortBenchModePolicy.CanAutomaticFire(_activeBenchMode, _benchAutomaticBursts.Value, _benchHoldFire,
                activeFrame, _benchBallistics != null, _benchTargeting != null && _benchRightTargeting != null,
                _benchFlight != null && _benchFlight.CanTrackTargets, out reason))
            {
                SuspendAutomaticBursts();
                return;
            }
            // Fixed gun instances own separate mounts, tracking, and cadence.
            // The shared emitter allocates the exact next round separately for
            // each successful call, including when both guns fire in one frame.
            TickAutomaticGun(0, _benchTargeting);
            TickAutomaticGun(1, _benchRightTargeting);
        }

        private void TickAutomaticGun(int index, EscortBenchTargeting targeting)
        {
            var burst = _benchBursts[index];
            if (burst == null) return;
            string expectedTarget;
            bool request = burst.Tick(Time.time, targeting.EligibleTargetId, out expectedTarget);
            if (_benchAimGeneration[index] != burst.AimGeneration)
            {
                _benchAimGeneration[index] = burst.AimGeneration;
                targeting.ClearBurstAim();
                Vector3? muzzle = targeting.MuzzlePosition;
                Vector3? aim = targeting.AimPoint;
                string offsetReason = "gun or target position unavailable";
                if (string.IsNullOrEmpty(burst.TargetId) || !muzzle.HasValue || !aim.HasValue ||
                    !targeting.SetBurstAim(burst.TargetId, NextBurstOffset(aim.Value - muzzle.Value), out offsetReason))
                {
                    burst.Suspend(Time.time);
                    targeting.ClearBurstAim();
                    Log.LogInfo("[EscortBench] Automatic aim withheld gun=" + index + " reason=" + offsetReason);
                    return;
                }
                // A new aim offset always gets its complete settling interval.
                return;
            }
            if (!request) return;
            if (!TryGetBenchRaid(out var world, out var caller, out var reason) || world != _benchWorld || caller != _benchCaller)
            {
                EndBench(reason ?? "Raid changed before automatic firing.");
                return;
            }
            bool accepted = false;
            if (targeting.TryGetShot(expectedTarget, out var origin, out var direction, out var target, out var ammo, out reason))
            {
                accepted = _benchBallistics.Fire(origin, direction, target, ammo);
                if (!accepted) reason = "native projectile submission refused";
            }
            burst.RecordResult(Time.time, expectedTarget, accepted);
            if (accepted)
            {
                _benchPresentation.PlayMuzzleFlash(targeting.SelectedBarrel, origin, direction);
                _benchPresentation.PlayGunshot(origin);
            }
            Log.LogInfo("[EscortBench] automatic-round gun=" + (index == 0 ? "Left" : "Right") +
                " accepted=" + accepted + " total=" + burst.AcceptedRounds + "/60" + (accepted ? "" : " reason=" + reason));
        }

        private Vector3 NextBurstOffset(Vector3 sightline)
        {
            // Move the visible aim before settling; never perturb an authorized
            // emitted direction. Separate RNG leaves native projectile RNG alone.
            Vector3 forward = sightline.normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            if (right.sqrMagnitude < 0.5f) right = Vector3.Cross(Vector3.forward, forward).normalized;
            Vector3 up = Vector3.Cross(forward, right).normalized;
            double angle = _benchAimRandom.NextDouble() * Math.PI * 2;
            double radius = Math.Sqrt(_benchAimRandom.NextDouble()) * 0.55;
            return right * (float)(Math.Cos(angle) * radius) + up * (float)(Math.Sin(angle) * radius);
        }

        private void SuspendAutomaticBursts()
        {
            for (int i = 0; i < 2; i++) _benchBursts[i]?.Suspend(Time.time);
            _benchTargeting?.ClearBurstAim();
            _benchRightTargeting?.ClearBurstAim();
        }

        private void DisposeAutomaticBench()
        {
            _benchRightTargeting?.Dispose();
            _benchRightTargeting = null;
            for (int i = 0; i < 2; i++) { _benchBursts[i] = null; _benchAimGeneration[i] = -1; }
            _benchAimRandom = null;
            _benchHoldFire = false;
        }
    }
}
