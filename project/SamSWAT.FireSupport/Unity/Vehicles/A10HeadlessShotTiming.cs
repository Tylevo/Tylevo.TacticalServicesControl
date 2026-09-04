using System;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public static class A10HeadlessShotTiming
{
	public static bool TryAlignArrival(ref A10TracerSegment visual, ref A10TracerSegment damage)
	{
		if (!visual.IsValid || !damage.IsValid ||
		    !IsFiniteNonNegative(visual.DelaySeconds) ||
		    !IsFiniteNonNegative(damage.DelaySeconds) ||
		    !IsFiniteNonNegative(visual.FlightTimeSeconds) ||
		    !IsFiniteNonNegative(damage.FlightTimeSeconds))
		{
			visual.IsValid = false;
			damage.IsValid = false;
			return false;
		}

		float arrival = Math.Max(visual.ImpactDelaySeconds, damage.ImpactDelaySeconds);
		if (!IsFiniteNonNegative(arrival))
		{
			visual.IsValid = false;
			damage.IsValid = false;
			return false;
		}

		// The authority's nearby muzzle is an existing headless compatibility path.
		// Delay its launch so its native collision coincides with the distant replay.
		visual.DelaySeconds = Math.Max(visual.DelaySeconds, arrival - visual.FlightTimeSeconds);
		damage.DelaySeconds = Math.Max(damage.DelaySeconds, arrival - damage.FlightTimeSeconds);
		return true;
	}

	public static bool ReachesIntendedImpact(A10TracerSegment shot)
	{
		return shot.IsValid && (shot.TracerEnd - shot.IntendedImpact).sqrMagnitude <= 1f;
	}

	public static float GetSettleWaitSeconds(float now, float latestFiredImpactTime, float settleSeconds)
	{
		return Math.Max(0f, latestFiredImpactTime + settleSeconds - now);
	}

	private static bool IsFiniteNonNegative(float value)
	{
		return value >= 0f && !float.IsInfinity(value) && !float.IsNaN(value);
	}
}
