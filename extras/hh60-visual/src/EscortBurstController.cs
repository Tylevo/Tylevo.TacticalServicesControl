using System;

namespace TscHh60Visual
{
    // One instance belongs to one gun for one summoned aircraft. This requests
    // rounds only; the caller must repeat all live safety checks before firing.
    internal sealed class EscortBurstController
    {
        internal const float SettleSeconds = 0.6f;
        internal const float RoundIntervalSeconds = 0.12f;
        internal const float CooldownSeconds = 2.5f;
        internal const int RoundsPerBurst = 3;
        internal const int SessionRoundLimit = 60;

        private string _target, _pendingTarget, _fault;
        private float _lastTime, _targetSince, _nextRoundAt, _cooldownUntil;
        private bool _hasTime, _settling;
        private int _burstAccepted;

        internal int AcceptedRounds { get; private set; }
        internal int AimGeneration { get; private set; }
        internal string TargetId => _target;
        internal bool LimitReached => AcceptedRounds >= SessionRoundLimit;
        internal string Status { get; private set; } = "Waiting for an eligible target";

        // A request must receive its matching RecordResult immediately, before
        // another Tick/Suspend. No accumulated interval produces catch-up fire.
        internal bool Tick(float now, string eligibleTargetId, out string expectedTargetId)
        {
            expectedTargetId = null;
            if (!ObserveTime(now)) return false;
            if (LimitReached)
            {
                Status = "Session round limit reached";
                return false;
            }
            if (string.IsNullOrWhiteSpace(eligibleTargetId))
            {
                ResetEligibility(now);
                Status = "Waiting for an eligible target";
                return false;
            }
            if (!string.Equals(_target, eligibleTargetId, StringComparison.Ordinal))
            {
                ResetEligibility(now);
                _target = eligibleTargetId;
            }
            if (_pendingTarget != null)
            {
                Status = "Waiting for the requested round result";
                return false;
            }
            if (now < _cooldownUntil)
            {
                Status = "Burst cooldown";
                return false;
            }
            if (!_settling)
            {
                if (AimGeneration == int.MaxValue)
                {
                    Fail("Disabled after aim generation overflow");
                    return false;
                }
                AimGeneration++;
                _targetSince = now;
                _settling = true;
                // The caller observes the new generation and applies its aim
                // offset now, before the entire continuous settle period.
                Status = "Settling on a new burst aim";
                return false;
            }
            if (now - _targetSince < SettleSeconds)
            {
                Status = "Settling on the same eligible target";
                return false;
            }
            if (_burstAccepted > 0 && now < _nextRoundAt)
            {
                Status = "Spacing burst rounds";
                return false;
            }
            _pendingTarget = _target;
            expectedTargetId = _pendingTarget;
            Status = "Round requested; live safety check required";
            return true;
        }

        internal void RecordResult(float now, string expectedTargetId, bool accepted)
        {
            // The accepted count is never reduced by target loss, suspension or
            // a malformed result. Mismatched results disable further requests.
            bool pending = _pendingTarget != null;
            bool matches = pending && string.Equals(_pendingTarget, expectedTargetId, StringComparison.Ordinal);
            if (pending && accepted) AcceptedRounds++;
            if (!ObserveTime(now)) return;
            if (!matches)
            {
                Fail("Disabled after an unmatched round result");
                return;
            }
            _pendingTarget = null;
            if (!accepted)
            {
                _cooldownUntil = Math.Max(_cooldownUntil, now + CooldownSeconds);
                ResetEligibility(now);
                Status = "Shot refused; burst aborted and cooling down";
                return;
            }
            _burstAccepted++;
            if (LimitReached)
            {
                ResetEligibility(now);
                Status = "Session round limit reached";
            }
            else if (_burstAccepted >= RoundsPerBurst)
            {
                _burstAccepted = 0;
                _cooldownUntil = now + CooldownSeconds;
                _settling = false;
                Status = "Burst cooldown";
            }
            else
            {
                // Schedule from the accepted attempt, not an overdue ideal
                // cadence: a delayed frame can never flush several old rounds.
                _nextRoundAt = now + RoundIntervalSeconds;
                Status = "Spacing burst rounds";
            }
        }

        // Used for disabled permission, UI/pause, unsafe flight, target loss,
        // and recall. Returning eligibility always needs a new settle period.
        internal void Suspend(float now)
        {
            if (!ObserveTime(now)) return;
            ResetEligibility(now);
            Status = LimitReached ? "Session round limit reached" : "Burst scheduling suspended";
        }

        private void ResetEligibility(float now)
        {
            if (_burstAccepted > 0 || _pendingTarget != null)
                _cooldownUntil = Math.Max(_cooldownUntil, now + CooldownSeconds);
            _target = null;
            _pendingTarget = null;
            _burstAccepted = 0;
            _settling = false;
            _targetSince = now;
            _nextRoundAt = now;
        }

        private bool ObserveTime(float now)
        {
            if (_fault != null) { Status = _fault; return false; }
            if (float.IsNaN(now) || float.IsInfinity(now) || now < 0f)
            {
                Fail("Disabled after invalid scaled time");
                return false;
            }
            if (_hasTime && now < _lastTime)
            {
                float remaining = Math.Max(0f, _cooldownUntil - _lastTime);
                if (_burstAccepted > 0 || _pendingTarget != null)
                    remaining = Math.Max(remaining, CooldownSeconds);
                ResetEligibility(now);
                _cooldownUntil = now + remaining;
                _lastTime = now;
                Status = "Scaled clock reset; fresh target settling required";
                return false;
            }
            _hasTime = true;
            _lastTime = now;
            return true;
        }

        private void Fail(string reason)
        {
            _fault = reason;
            _target = null;
            _pendingTarget = null;
            _burstAccepted = 0;
            _settling = false;
            Status = reason;
        }
    }
}
