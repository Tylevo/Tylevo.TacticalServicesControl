using UnityEngine;

namespace TscHh60Visual
{
    public sealed partial class Plugin
    {
        private readonly EscortHeldTrigger _benchHeldCadence = new EscortHeldTrigger();
        private static readonly System.Func<KeyCode, bool> BenchKeyHeld = Input.GetKey;
        private bool _benchManualHeld, _benchManualRawHeld, _benchManualAccepted;
        private int _benchManualGun;
        private string _benchManualBlockReason;
        private EscortBenchTargeting ManualBenchTargeting => EscortBenchModePolicy.IsAutomatic(_activeBenchMode) && _benchManualGun == 1
            ? _benchRightTargeting : _benchTargeting;
        private string ManualBenchGunName => (EscortBenchModePolicy.IsAutomatic(_activeBenchMode)
            ? _benchManualGun : (_benchTargeting?.SelectedGunIndex ?? 0)) == 0 ? "Left" : "Right";

        private void TickHeldBenchTrigger(bool pressed, float dt)
        {
            if (!_benchManualHeld) { _benchManualAccepted = false; _benchManualBlockReason = null; }
            var targeting = ManualBenchTargeting;
            bool permitted = EscortBenchModePolicy.CanFire(_activeBenchMode, _benchLiveShots.Value,
                _benchBallistics != null, targeting != null, _benchFlight != null,
                _benchFlight != null && _benchFlight.CanTrackTargets, out string reason);
            permitted &= EscortBenchSafetyPolicy.Finite(dt) && dt > 0f && dt <= 0.25f;
            if (!permitted && _benchManualHeld)
            {
                reason = reason ?? "Manual trigger paused during this frame.";
                if (pressed || reason != _benchManualBlockReason) ReportBenchShot(reason);
                _benchManualBlockReason = reason;
            }
            if (!_benchHeldCadence.Tick(Time.time, _benchManualHeld, permitted)) return;
            if (!TryGetBenchRaid(out var world, out var caller, out reason) || world != _benchWorld || caller != _benchCaller)
            {
                EndBench(reason ?? "Raid changed before manual firing.");
                return;
            }
            if (!targeting.TryGetFreeShot(out var origin, out var direction, out var ammo, out reason))
            {
                if (pressed || reason != _benchManualBlockReason) ReportBenchShot("Manual shot blocked: " + reason);
                _benchManualBlockReason = reason;
                return;
            }
            if (!_benchBallistics.Fire(origin, direction, null, ammo))
            {
                if (_benchManualBlockReason != "submission") ReportBenchShot("Manual shot submission refused; see LogOutput.log.");
                _benchManualBlockReason = "submission";
                return;
            }
            if (!_benchManualAccepted || _benchManualBlockReason != null)
                ReportBenchShot("Manual trigger firing " + ManualBenchGunName + " barrel. Release the trigger to stop.", false);
            _benchManualAccepted = true;
            _benchManualBlockReason = null;
            Log.LogInfo("[EscortBench] held-trigger-round gun=" + ManualBenchGunName + " accepted=True targetRequired=False");
            _benchPresentation.PlayMuzzleFlash(targeting.SelectedBarrel, origin, direction);
            _benchPresentation.PlayGunshot(origin);
        }

        private void ResetHeldBenchTrigger()
        {
            _benchHeldCadence.Reset();
            _benchManualHeld = _benchManualRawHeld = _benchManualAccepted = false;
            _benchManualGun = 0;
            _benchManualBlockReason = null;
        }
    }
}
