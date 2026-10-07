using System;

namespace TscHh60Visual
{
    // Unity-free steering limits, shared with the deterministic regression tests.
    internal readonly struct FlightVector
    {
        internal readonly float X, Y, Z;
        internal FlightVector(float x, float y, float z) { X = x; Y = y; Z = z; }
        internal static readonly FlightVector Zero = new FlightVector(0, 0, 0);
    }

    internal static class EscortFlightMath
    {
        internal const float MaxStepSeconds = 0.1f;

        internal static bool IsFinite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        internal static bool IsFinite(FlightVector value) { return IsFinite(value.X) && IsFinite(value.Y) && IsFinite(value.Z); }

        internal static float LimitDeltaTime(float dt)
        {
            return !IsFinite(dt) || dt <= 0 ? 0 : Math.Min(dt, MaxStepSeconds);
        }

        internal static FlightVector MoveCenter(FlightVector current, FlightVector target, float deadband, float maxSpeed, float dt)
        {
            if (!IsFinite(current)) current = FlightVector.Zero;
            dt = LimitDeltaTime(dt);
            if (!IsFinite(target) || !IsFinite(deadband) || !IsFinite(maxSpeed) || deadband < 0 || maxSpeed <= 0 || dt == 0)
                return current;
            double x = (double)target.X - current.X, y = (double)target.Y - current.Y, z = (double)target.Z - current.Z;
            double distance = Math.Sqrt(x * x + y * y + z * z);
            if (distance <= deadband || distance < 0.0000001) return current;
            double step = Math.Min(distance - deadband, (double)maxSpeed * dt) / distance;
            var result = new FlightVector((float)(current.X + x * step), (float)(current.Y + y * step), (float)(current.Z + z * step));
            return IsFinite(result) ? result : current;
        }

        internal static FlightVector MoveVelocity(FlightVector current, FlightVector desired, float maxAcceleration, float dt)
        {
            return MoveCenter(current, desired, 0, maxAcceleration, dt);
        }

        internal static float WrapRadians(float radians)
        {
            if (!IsFinite(radians)) return 0;
            return (float)Math.IEEERemainder(radians, Math.PI * 2);
        }

        internal static float TurnHeading(float currentRadians, float targetRadians, float maxRadiansPerSecond, float dt)
        {
            float current = WrapRadians(currentRadians);
            dt = LimitDeltaTime(dt);
            if (!IsFinite(targetRadians) || !IsFinite(maxRadiansPerSecond) || maxRadiansPerSecond <= 0 || dt == 0)
                return current;
            double delta = Math.IEEERemainder((double)WrapRadians(targetRadians) - current, Math.PI * 2);
            double maximum = (double)maxRadiansPerSecond * dt;
            delta = Math.Max(-maximum, Math.Min(maximum, delta));
            return WrapRadians((float)(current + delta));
        }
    }
}
