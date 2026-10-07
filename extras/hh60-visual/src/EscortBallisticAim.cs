using System;
using System.Globalization;
using EFT.Ballistics;
using EFT.InventoryLogic;
using UnityEngine;

namespace TscHh60Visual
{
    internal readonly struct EscortTrajectoryPoint
    {
        internal readonly Vector3 Position;
        internal readonly float Time;
        internal EscortTrajectoryPoint(Vector3 position, float time) { Position = position; Time = time; }
    }

    internal readonly struct EscortAimSolution
    {
        internal readonly Vector3 Direction, PredictedTarget, ImpactPoint;
        internal readonly float FlightTime, MissDistance;
        internal readonly EscortTrajectoryPoint[] Path;
        internal EscortAimSolution(Vector3 direction, Vector3 target, Vector3 impact, float time,
            float miss, EscortTrajectoryPoint[] path)
        {
            Direction = direction; PredictedTarget = target; ImpactPoint = impact;
            FlightTime = time; MissDistance = miss; Path = path;
        }
    }

    // Main-thread prediction only. No Shot, damage callback, RNG, world registration
    // or ballistic item creation occurs here. Native history is returned each trial.
    internal sealed class EscortBallisticAim : IDisposable
    {
        internal const float MaximumRange = 200f;
        internal const float MaximumTargetSpeed = 15f;
        internal const float MaximumFlightTime = 1f;
        internal const float MaximumMissDistance = 0.20f;
        private const float SolveTolerance = 0.03f;
        private const int MaximumIterations = 6;
        private const int MaximumSteps = 100;
        private readonly TrajectoryCalculator _calculator = new TrajectoryCalculator();
        private readonly EscortTrajectoryPoint[] _points = new EscortTrajectoryPoint[MaximumSteps + 2];
        private int _pointCount;
        private bool _initialized, _disposed;

        internal bool TrySolve(Ammo ammo, Vector3 origin, Vector3 targetPosition, Vector3 targetVelocity,
            out EscortAimSolution solution, out string reason)
        {
            solution = default;
            if (!Validate(ammo, origin, targetPosition, targetVelocity, out reason)) return false;
            Vector3 aim = targetPosition - origin;
            for (int iteration = 0; iteration < MaximumIterations; iteration++)
            {
                Trial trial;
                if (!Evaluate(ammo, origin, aim.normalized, targetPosition, targetVelocity, out trial, out reason)) return false;
                if (trial.Miss <= SolveTolerance)
                {
                    solution = Capture(trial);
                    return true;
                }
                // The native trajectory determines both flight time and drop;
                // adjust only the direction by its measured intercept residual.
                aim += trial.Target - trial.Impact;
                if (!Finite(aim) || aim.sqrMagnitude < 1f)
                { reason = "native aim correction became invalid"; return false; }
            }
            reason = "native lead/drop solution did not converge";
            return false;
        }

        internal bool TryEvaluate(Ammo ammo, Vector3 origin, Vector3 direction, Vector3 targetPosition,
            Vector3 targetVelocity, out EscortAimSolution solution, out string reason)
        {
            solution = default;
            if (!Validate(ammo, origin, targetPosition, targetVelocity, out reason)) return false;
            Trial trial;
            if (!Evaluate(ammo, origin, direction, targetPosition, targetVelocity, out trial, out reason)) return false;
            if (trial.Miss > MaximumMissDistance)
            {
                reason = "actual barrel trajectory misses predicted torso by " + trial.Miss.ToString("F2", CultureInfo.InvariantCulture) + "m";
                return false;
            }
            solution = Capture(trial);
            return true;
        }

        // Predict the selected barrel's native path without acquiring or aiming
        // at an actor. World impact truncation and shot authorization belong to
        // the caller; reaching this range alone is never permission to fire.
        internal bool TryTrace(Ammo ammo, Vector3 origin, Vector3 direction, float maxRange,
            out EscortAimSolution solution, out string reason)
        {
            solution = default;
            reason = null;
            if (_disposed) { reason = "native aim solver disposed"; return false; }
            if (ammo == null || !Positive(ammo.InitialSpeed) || !Positive(ammo.BulletMassGram) ||
                !Positive(ammo.BulletDiameterMilimeters) || !Positive(ammo.BallisticCoeficient) || !Positive(ammo.AmmoLifeTimeSec))
            { reason = "native ammunition parameters unavailable"; return false; }
            if (!Finite(origin) || !Finite(direction) || !Finite(Physics.gravity))
            { reason = "non-finite barrel trace or native gravity"; return false; }
            if (!Positive(maxRange) || maxRange > MaximumRange)
            { reason = "native barrel trace range must be positive and at most 200m"; return false; }
            if (!Finite(direction.sqrMagnitude) || direction.sqrMagnitude < 0.000001f)
            { reason = "native barrel direction unavailable"; return false; }
            direction.Normalize();
            Trial trial;
            if (!SamplePlane(ammo, origin, direction, direction, maxRange,
                Vector3.zero, Vector3.zero, true, out trial, out reason)) return false;
            solution = Capture(trial);
            return true;
        }

        private bool Validate(Ammo ammo, Vector3 origin, Vector3 target, Vector3 velocity, out string reason)
        {
            reason = null;
            if (_disposed) { reason = "native aim solver disposed"; return false; }
            if (ammo == null || !Positive(ammo.InitialSpeed) || !Positive(ammo.BulletMassGram) ||
                !Positive(ammo.BulletDiameterMilimeters) || !Positive(ammo.BallisticCoeficient) || !Positive(ammo.AmmoLifeTimeSec))
            { reason = "native ammunition parameters unavailable"; return false; }
            if (!Finite(origin) || !Finite(target) || !Finite(velocity) || !Finite(Physics.gravity))
            { reason = "non-finite aim or native gravity"; return false; }
            float range = (target - origin).magnitude;
            if (!Finite(range) || range < 12f || range > MaximumRange)
            { reason = "native aim is limited to 12-200m"; return false; }
            if (velocity.magnitude > MaximumTargetSpeed)
            { reason = "target motion exceeds the verified prediction limit"; return false; }
            return true;
        }

        private bool Evaluate(Ammo ammo, Vector3 origin, Vector3 direction, Vector3 target, Vector3 targetVelocity,
            out Trial trial, out string reason)
        {
            trial = default;
            reason = null;
            _pointCount = 0;
            if (!Finite(direction) || direction.sqrMagnitude < 0.000001f)
            { reason = "native aim direction unavailable"; return false; }
            direction.Normalize();
            Vector3 initialOffset = target - origin;
            float range = initialOffset.magnitude;
            Vector3 line = initialOffset / range;
            if (Vector3.Dot(direction, line) < 0.9f)
            { reason = "barrel is outside the bounded lead/drop angle"; return false; }
            return SamplePlane(ammo, origin, direction, line, range, target, targetVelocity,
                false, out trial, out reason);
        }

        private bool SamplePlane(Ammo ammo, Vector3 origin, Vector3 direction, Vector3 line, float range,
            Vector3 target, Vector3 targetVelocity, bool targetless, out Trial trial, out string reason)
        {
            trial = default;
            reason = null;
            _pointCount = 0;
            float targetRadialSpeed = targetless ? 0f : Vector3.Dot(targetVelocity, line);
            float limit = Mathf.Min(MaximumFlightTime, ammo.AmmoLifeTimeSec);
            try
            {
                // This is the exact direct-CreateShot start velocity at speedFactor=1.
                // That API adds neither shooter/platform velocity nor weapon modifiers.
                _calculator.Initialize(origin, direction * ammo.InitialSpeed, ammo.BulletMassGram,
                    ammo.BulletDiameterMilimeters, ammo.BallisticCoeficient, false);
                _initialized = true;
                TrajectoryInfo previous = _calculator.Current;
                float previousGap = -range;
                _points[_pointCount++] = new EscortTrajectoryPoint(origin, 0f);
                int steps = Math.Min(MaximumSteps, _calculator.MaxAllowedLength - 1);
                for (int step = 0; step < steps; step++)
                {
                    TrajectoryInfo next = _calculator.Next();
                    if (!Finite(next.position) || !Finite(next.time) || next.time <= previous.time)
                    { reason = "invalid native trajectory sample"; return false; }
                    // A target uses its moving plane, including radial lead.
                    // A barrel-only trace uses a fixed plane along the barrel.
                    float gap = Vector3.Dot(next.position - origin, line) - range - targetRadialSpeed * next.time;
                    if (!Finite(gap) || gap <= previousGap)
                    { reason = "native trajectory cannot approach the target plane"; return false; }
                    if (gap >= 0f)
                    {
                        float fraction = -previousGap / (gap - previousGap);
                        float time = previous.time + (next.time - previous.time) * fraction;
                        if (!Positive(time) || time > limit)
                        { reason = "native intercept exceeds ammunition or prediction lifetime"; return false; }
                        // Recompute from the representable time exactly as Shot
                        // does, including a crossing that rounds to a sample edge.
                        float nativeFraction = (time - previous.time) / (next.time - previous.time);
                        Vector3 point = Vector3.Lerp(previous.position, next.position, nativeFraction);
                        Vector3 predicted = targetless ? point : target + targetVelocity * time;
                        float miss = (predicted - point).magnitude;
                        if (!Finite(point) || !Finite(predicted) || !Finite(miss))
                        { reason = "native intercept is non-finite"; return false; }
                        if (time > _points[_pointCount - 1].Time)
                            _points[_pointCount++] = new EscortTrajectoryPoint(point, time);
                        else
                            _points[_pointCount - 1] = new EscortTrajectoryPoint(point, time);
                        trial = new Trial { Direction = direction, Target = predicted, Impact = point, Time = time, Miss = miss };
                        return true;
                    }
                    if (next.time >= limit) break;
                    _points[_pointCount++] = new EscortTrajectoryPoint(next.position, next.time);
                    previous = next;
                    previousGap = gap;
                }
                reason = targetless ? "native trajectory did not reach the barrel range plane" :
                    "native trajectory did not reach the moving target plane";
                return false;
            }
            catch (Exception error)
            {
                reason = "native trajectory query failed: " + error.GetType().Name;
                return false;
            }
            finally { ReturnHistory(); }
        }

        private EscortAimSolution Capture(Trial trial)
        {
            var path = new EscortTrajectoryPoint[_pointCount];
            Array.Copy(_points, path, _pointCount);
            return new EscortAimSolution(trial.Direction, trial.Target, trial.Impact, trial.Time, trial.Miss, path);
        }

        private void ReturnHistory()
        {
            if (!_initialized) return;
            _initialized = false;
            _calculator.ClearClass();
        }

        public void Dispose() { if (_disposed) return; ReturnHistory(); _disposed = true; }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static bool Positive(float value) => Finite(value) && value > 0f;
        private struct Trial
        {
            internal Vector3 Direction, Target, Impact;
            internal float Time, Miss;
        }
    }
}
