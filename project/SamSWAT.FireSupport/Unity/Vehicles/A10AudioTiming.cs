using System;
using System.Collections.Generic;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public static class A10AudioTiming
{
	public const float SoundSpeedMetresPerSecond = 343f;
	// SamSWAT's original impact recording precedes the distant cannon report by five seconds.
	public const float ImpactToCannonDelaySeconds = 5f;

	public static bool TryGetImpactPlaybackDelay(
		IReadOnlyList<A10TracerSegment> shots,
		float listenerDistance,
		out float delaySeconds)
	{
		return TryGetImpactPlaybackWindow(shots, listenerDistance, out delaySeconds, out _);
	}

	public static bool TryGetImpactPlaybackWindow(
		IReadOnlyList<A10TracerSegment> shots,
		float listenerDistance,
		out float delaySeconds,
		out float durationSeconds)
	{
		delaySeconds = 0f;
		durationSeconds = 0f;
		if (shots == null || !IsFiniteNonNegative(listenerDistance)) return false;

		float earliestImpact = float.PositiveInfinity;
		float latestImpact = 0f;
		foreach (A10TracerSegment shot in shots)
		{
			if (!shot.IsValid || !IsFiniteNonNegative(shot.DelaySeconds) ||
			    !IsFiniteNonNegative(shot.FlightTimeSeconds) ||
			    !IsFiniteNonNegative(shot.ImpactDelaySeconds)) continue;
			earliestImpact = Math.Min(earliestImpact, shot.ImpactDelaySeconds);
			latestImpact = Math.Max(latestImpact, shot.ImpactDelaySeconds);
		}

		if (float.IsPositiveInfinity(earliestImpact)) return false;
		float arrival = earliestImpact + listenerDistance / SoundSpeedMetresPerSecond;
		if (!IsFiniteNonNegative(arrival)) return false;
		delaySeconds = arrival;
		durationSeconds = latestImpact - earliestImpact;
		return true;
	}

	private static bool IsFiniteNonNegative(float value)
	{
		return !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;
	}
}
