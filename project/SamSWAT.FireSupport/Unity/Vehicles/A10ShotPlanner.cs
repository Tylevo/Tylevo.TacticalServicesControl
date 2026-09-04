using System;
using System.Collections.Generic;
using UnityEngine;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public static class A10ShotPlanner
{
	public const int ShotCount = 50;
	public const float AircraftDistance = 2650f;
	public const float AircraftAltitude = 320f;
	public const float StrafeSpeed = 150f;
	public const float GunFireDelaySeconds = 8f;
	public const float MaximumTracerDistance = 2200f;
	public const float TracerSegmentLength = 42f;
	private const float AnchoredReplayLongitudinalSpacing = 0.9f;
	private const float AnchoredReplayLateralSpread = 7.5f;
	// Search near the designated elevation, including stacked surfaces, rather
	// than snapping every round to the first roof below a probe 140 m overhead.
	private const float AnchoredReplayGroundProbeHeight = 24f;
	private const float AnchoredReplayGroundProbeDistance = 24f;
	private const float DamageOnlyMinimumGroundClearance = 20f;
	private const float DamageOnlyGroundProbeHeight = 500f;
	private const float DamageOnlyGroundProbeDistance = 900f;

	public static IReadOnlyList<Vector3> BuildImpactPlan(
		Vector3 targetPosition,
		Vector3 aircraftForward,
		int seed)
	{
		var random = new System.Random(seed);
		Vector3 safeAircraftForward = NormalizeAircraftForward(aircraftForward);
		Vector3 right = Vector3.Cross(Vector3.up, safeAircraftForward).normalized;
		if (right.sqrMagnitude <= 0.0001f)
		{
			right = Vector3.right;
		}

		var plan = new List<Vector3>(ShotCount);
		float centerIndex = (ShotCount - 1) * 0.5f;
		for (int index = 0; index < ShotCount; index++)
		{
			float longitudinalOffset = (index - centerIndex) * AnchoredReplayLongitudinalSpacing;
			float lateralOffset = NextSpread(random, -AnchoredReplayLateralSpread, AnchoredReplayLateralSpread);
			Vector3 intendedImpact = targetPosition +
			                         safeAircraftForward * longitudinalOffset +
			                         right * lateralOffset;
			plan.Add(ResolveImpactNearTarget(intendedImpact));
		}

		return plan;
	}

	public static List<A10TracerSegment> BuildMovingMuzzlePlan(
		Vector3 firstMuzzleOrigin,
		Vector3 aircraftForward,
		IReadOnlyList<Vector3> impactPlan,
		float timeBetweenShots,
		IA10TrajectoryEvaluator trajectoryEvaluator)
	{
		var plan = new List<A10TracerSegment>(impactPlan?.Count ?? 0);
		if (impactPlan == null || impactPlan.Count == 0)
		{
			return plan;
		}

		Vector3 safeAircraftForward = NormalizeAircraftForward(aircraftForward);
		float safeTimeBetweenShots = Mathf.Max(0.001f, timeBetweenShots);
		for (int index = 0; index < impactPlan.Count; index++)
		{
			float shotDelay = index * safeTimeBetweenShots;
			Vector3 projectileOrigin = firstMuzzleOrigin +
			                           safeAircraftForward * StrafeSpeed * shotDelay;
			plan.Add(BuildBallisticShot(projectileOrigin, impactPlan[index], shotDelay, trajectoryEvaluator));
		}

		return plan;
	}

	public static A10TracerSegment BuildBallisticShot(
		Vector3 projectileOrigin,
		Vector3 intendedImpact,
		float shotDelay,
		IA10TrajectoryEvaluator trajectoryEvaluator)
	{
		if (!A10BallisticSolver.TrySolve(projectileOrigin, intendedImpact, trajectoryEvaluator, out A10BallisticSolution solution))
		{
			return A10TracerSegment.Invalid(projectileOrigin, Vector3.zero, shotDelay);
		}

		IReadOnlyList<A10TrajectoryPoint> path = solution.Path;
		Vector3 tracerEnd = path[path.Count - 1].Position;
		float flightTime = solution.FlightTimeSeconds;
		int terminalIndex = path.Count - 1;
		for (int index = 1; index < path.Count; index++)
		{
			Vector3 chord = path[index].Position - path[index - 1].Position;
			float length = chord.magnitude;
			if (length <= 0.0001f) continue;
			// Include the intended surface if the final integration point lands
			// fractionally above it. Earlier chords have no padding.
			float padding = index == path.Count - 1 ? 0.5f : 0f;
			if (!Physics.Raycast(path[index - 1].Position, chord / length,
				out RaycastHit hit, length + padding, EFT.Ballistics.BallisticsCalculatorConstants.HitMask,
				QueryTriggerInteraction.UseGlobal)) continue;
			tracerEnd = hit.point;
			flightTime = path[index - 1].TimeSeconds +
			             (path[index].TimeSeconds - path[index - 1].TimeSeconds) * hit.distance / length;
			terminalIndex = index;
			break;
		}

		if (Vector3.Distance(projectileOrigin, tracerEnd) <= 1f)
		{
			return A10TracerSegment.Invalid(projectileOrigin, solution.Direction, shotDelay);
		}

		// Walk back along the solved flight for the short visible terminal tracer.
		Vector3 tracerStart = tracerEnd;
		float remaining = TracerSegmentLength;
		for (int index = terminalIndex - 1; index >= 0; index--)
		{
			Vector3 delta = path[index].Position - tracerStart;
			float length = delta.magnitude;
			if (length >= remaining && length > 0f)
			{
				tracerStart += delta * (remaining / length);
				break;
			}
			tracerStart = path[index].Position;
			remaining -= length;
		}

		return new A10TracerSegment(projectileOrigin, solution.Direction, tracerStart, tracerEnd, shotDelay)
		{
			IntendedImpact = intendedImpact,
			FlightTimeSeconds = flightTime
		};
	}

	public static A10TracerSegment BuildRaycastTracerSegment(
		Vector3 origin,
		Vector3 direction,
		float delaySeconds)
	{
		direction = direction.normalized;
		float tracerDistance = MaximumTracerDistance;
		if (Physics.Raycast(origin, direction, out RaycastHit hitInfo, tracerDistance, ~0, QueryTriggerInteraction.Ignore))
		{
			tracerDistance = hitInfo.distance;
		}

		if (tracerDistance <= 1f)
		{
			return A10TracerSegment.Invalid(origin, direction, delaySeconds);
		}

		float segmentLength = Mathf.Min(TracerSegmentLength, tracerDistance);
		Vector3 tracerEnd = origin + direction * tracerDistance;
		Vector3 tracerStart = tracerEnd - direction * segmentLength;
		return new A10TracerSegment(origin, direction, tracerStart, tracerEnd, delaySeconds);
	}

	public static Vector3 GetOriginalAircraftOrigin(Vector3 targetPosition, Vector3 strafeDirection)
	{
		Vector3 safeDirection = GetSafeStrafeDirection(strafeDirection);
		return targetPosition + safeDirection * AircraftDistance + Vector3.up * AircraftAltitude;
	}

	public static Vector3 GetAircraftPositionAtFire(Vector3 targetPosition, Vector3 strafeDirection)
	{
		Vector3 aircraftStart = GetOriginalAircraftOrigin(targetPosition, strafeDirection);
		Vector3 forward = GetAircraftForward(strafeDirection);
		float fireTravelDistance = StrafeSpeed * GunFireDelaySeconds;
		return aircraftStart + forward * fireTravelDistance;
	}

	public static Vector3 GetAircraftForward(Vector3 strafeDirection)
	{
		return NormalizeAircraftForward(-GetSafeStrafeDirection(strafeDirection));
	}

	public static Vector3 NormalizeAircraftForward(Vector3 aircraftForward)
	{
		aircraftForward.y = 0f;
		return aircraftForward.sqrMagnitude > 0.0001f
			? aircraftForward.normalized
			: Vector3.forward;
	}

	public static Vector3 GetHeadlessDamageOrigin(Vector3 targetPosition, Vector3 strafeDirection)
	{
		Vector3 safeDirection = GetSafeStrafeDirection(strafeDirection);
		Vector3 origin = targetPosition +
		                 safeDirection * FireSupportTuningSettings.GetA10HeadlessDamageOriginDistance() +
		                 Vector3.up * FireSupportTuningSettings.GetA10HeadlessDamageOriginAltitude();
		return AdjustAboveTerrain(origin);
	}

	private static Vector3 GetSafeStrafeDirection(Vector3 strafeDirection)
	{
		return strafeDirection.sqrMagnitude > 0.0001f
			? strafeDirection.normalized
			: Vector3.forward;
	}

	private static Vector3 AdjustAboveTerrain(Vector3 origin)
	{
		Vector3 probeStart = origin + Vector3.up * DamageOnlyGroundProbeHeight;
		float probeDistance = DamageOnlyGroundProbeHeight + DamageOnlyGroundProbeDistance;
		if (!Physics.Raycast(probeStart, Vector3.down, out RaycastHit hit, probeDistance, ~0, QueryTriggerInteraction.Ignore))
		{
			return origin;
		}

		float minimumY = hit.point.y + DamageOnlyMinimumGroundClearance;
		if (origin.y < minimumY)
		{
			origin.y = minimumY;
		}

		return origin;
	}

	private static Vector3 ResolveImpactNearTarget(Vector3 intendedImpact)
	{
		Vector3 probeStart = intendedImpact + Vector3.up * AnchoredReplayGroundProbeHeight;
		float probeDistance = AnchoredReplayGroundProbeHeight + AnchoredReplayGroundProbeDistance;
		RaycastHit[] hits = Physics.RaycastAll(probeStart, Vector3.down, probeDistance,
			LayersMaskController.TerrainLowPoly, QueryTriggerInteraction.Ignore);
		Vector3 closest = intendedImpact;
		float closestHeight = float.PositiveInfinity;
		foreach (RaycastHit hit in hits)
		{
			float height = Math.Abs(hit.point.y - intendedImpact.y);
			if (height < closestHeight)
			{
				closest = hit.point;
				closestHeight = height;
			}
		}
		return closest;
	}

	private static float NextSpread(System.Random random, float min, float max)
	{
		return min + (float)random.NextDouble() * (max - min);
	}
}
