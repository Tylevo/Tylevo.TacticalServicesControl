using System;

namespace TscHh60Visual
{
    // Pure policy shared by the live observation bench and its isolated tests.
    internal static class EscortBenchSafetyPolicy
    {
        internal const float MinimumDistance = 12f;
        internal const float MaximumDistance = 200f;
        internal const float HorizontalHalfAngle = 55f;
        internal const float DownwardAngle = 65f;
        internal const float UpwardAngle = 10f;
        internal const float MaximumAimError = 0.15f;

        internal static bool ConfirmedHostile(bool alive, bool isAi, bool activeBot,
            bool groupAvailable, bool recordedEnemy, bool explicitAlly, bool sameGroup)
        {
            return alive && isAi && activeBot && groupAvailable && recordedEnemy && !explicitAlly && !sameGroup;
        }

        internal static bool WithinLimits(float distance, float yaw, float pitch, out string reason)
        {
            if (!Finite(distance) || !Finite(yaw) || !Finite(pitch)) { reason = "invalid aim geometry"; return false; }
            if (distance < MinimumDistance) { reason = "target too close (<12m)"; return false; }
            if (distance > MaximumDistance) { reason = "target beyond 200m bench range"; return false; }
            if (Math.Abs(yaw) > HorizontalHalfAngle) { reason = "outside side-gun yaw arc"; return false; }
            if (pitch < -DownwardAngle || pitch > UpwardAngle) { reason = "outside side-gun elevation arc"; return false; }
            reason = null;
            return true;
        }

        internal static bool Aligned(float errorDegrees)
        {
            return Finite(errorDegrees) && errorDegrees >= 0f && errorDegrees <= MaximumAimError;
        }

        internal static bool RelevantCollision(bool isTrigger, bool registeredPlayer, bool ballisticSurface)
        {
            return !isTrigger || registeredPlayer || ballisticSurface;
        }

        internal static float WorldClearance(float distance)
        {
            if (!Finite(distance) || distance < 0f || distance > MaximumDistance)
                throw new ArgumentOutOfRangeException(nameof(distance));
            // M33/M21 start at 887/867m/s. At <=200m this uses a lower 600m/s
            // average-speed envelope plus 10cm clearance, rather than pretending
            // the torso ray is an exact simulation of native projectile flight.
            float seconds = distance / 600f;
            return 0.1f + 0.5f * 9.81f * seconds * seconds;
        }

        internal static bool IntersectsProtectedCorridor(float forwardDistance, float perpendicularDistance, float bodyRadius)
        {
            if (!Finite(forwardDistance) || !Finite(perpendicularDistance) || !Finite(bodyRadius)
                || perpendicularDistance < 0f || bodyRadius < 0f) return true;
            if (forwardDistance < -bodyRadius - 0.35f) return false;
            double distance = Math.Max(0.0, forwardDistance);
            // Check every other living player's full body bounds, including those
            // beyond the chosen target. No cutoff at target distance or 200m.
            // The broad envelope reserves half a degree plus slow-flight drop;
            // shots themselves have no added dispersion or aim compensation.
            double seconds = distance / 300.0;
            double radius = bodyRadius + 0.35 + distance * Math.Tan(Math.PI / 360.0)
                + 0.5 * 9.81 * seconds * seconds;
            return perpendicularDistance <= radius;
        }

        internal static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
