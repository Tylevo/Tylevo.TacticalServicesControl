using System;
using System.Collections.Generic;
using UnityEngine;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public static class A10ShotPlanner
{
	private const int ShotCount = 50;
	private const float HorizontalSpreadMin = -0.007f;
	private const float HorizontalSpreadMax = 0.007f;
	private const float VerticalWalkPerShot = 0.00037f;
	private const float OriginalAircraftDistance = 2650f;
	private const float OriginalAircraftAltitude = 320f;
	private const float Gau8ForwardOffset = 515f;
	private const float OriginalStrafeSpeed = 150f;
	private const float OriginalGunFireDelaySeconds = 8f;
	private const float AnchoredReplayLongitudinalSpacing = 0.9f;
	private const float AnchoredReplayLateralSpread = 7.5f;
	private const float AnchoredReplayGroundProbeHeight = 140f;
	private const float AnchoredReplayGroundProbeDistance = 320f;
	private const float DamageOnlyMinimumGroundClearance = 20f;
	private const float DamageOnlyGroundProbeHeight = 500f;
	private const float DamageOnlyGroundProbeDistance = 900f;

	public static List<A10TracerSegment> Build(
		Vector3 projectileOrigin,
		Vector3 targetPosition,
		int seed,
		float timeBetweenShots)
	{
		var random = new System.Random(seed != 0 ? seed : Environment.TickCount);
		Vector3 projectileDirection = Vector3.Normalize(targetPosition - projectileOrigin);
		if (projectileDirection.sqrMagnitude <= 0.0001f)
		{
			projectileDirection = Vector3.down;
		}

		Vector3 leftDirection = Vector3.Cross(projectileDirection, Vector3.up).normalized;
		if (leftDirection.sqrMagnitude <= 0.0001f)
		{
			leftDirection = Vector3.right;
		}

		float shotDelay = 0f;
		float safeTimeBetweenShots = Mathf.Max(0.001f, timeBetweenShots);
		var plan = new List<A10TracerSegment>(ShotCount);
		for (int index = 0; index < ShotCount; index++)
		{
			Vector3 leftRightSpread = leftDirection * NextSpread(random, HorizontalSpreadMin, HorizontalSpreadMax);
			projectileDirection = Vector3.Normalize(projectileDirection + new Vector3(0, VerticalWalkPerShot, 0));
			Vector3 shotDirection = Vector3.Normalize(projectileDirection + leftRightSpread);
			plan.Add(A10Behaviour.BuildVisualTracerSegment(projectileOrigin, shotDirection, shotDelay));
			shotDelay += safeTimeBetweenShots;
		}

		return plan;
	}

	public static List<A10TracerSegment> BuildImpactAnchoredReplay(
		Vector3 projectileOrigin,
		Vector3 targetPosition,
		Vector3 strafeDirection,
		int seed,
		float timeBetweenShots)
	{
		var random = new System.Random(seed != 0 ? seed : Environment.TickCount);
		Vector3 aircraftForward = -GetSafeStrafeDirection(strafeDirection);
		aircraftForward.y = 0f;
		if (aircraftForward.sqrMagnitude <= 0.0001f)
		{
			aircraftForward = Vector3.forward;
		}

		aircraftForward.Normalize();
		Vector3 right = Vector3.Cross(Vector3.up, aircraftForward).normalized;
		if (right.sqrMagnitude <= 0.0001f)
		{
			right = Vector3.right;
		}

		float shotDelay = 0f;
		float safeTimeBetweenShots = Mathf.Max(0.001f, timeBetweenShots);
		var plan = new List<A10TracerSegment>(ShotCount);
		float centerIndex = (ShotCount - 1) * 0.5f;
		for (int index = 0; index < ShotCount; index++)
		{
			float longitudinalOffset = (index - centerIndex) * AnchoredReplayLongitudinalSpacing;
			float lateralOffset = NextSpread(random, -AnchoredReplayLateralSpread, AnchoredReplayLateralSpread);
			Vector3 intendedImpact = targetPosition +
			                         aircraftForward * longitudinalOffset +
			                         right * lateralOffset;
			Vector3 impactPoint = ResolveImpactNearTarget(intendedImpact);
			Vector3 direction = Vector3.Normalize(impactPoint - projectileOrigin);
			if (direction.sqrMagnitude <= 0.0001f)
			{
				direction = Vector3.down;
			}

			float distance = Vector3.Distance(projectileOrigin, impactPoint);
			if (distance <= 1f)
			{
				plan.Add(A10TracerSegment.Invalid(projectileOrigin, direction, shotDelay));
			}
			else
			{
				float segmentLength = Mathf.Min(42f, distance);
				Vector3 tracerStart = impactPoint - direction * segmentLength;
				plan.Add(new A10TracerSegment(projectileOrigin, direction, tracerStart, impactPoint, shotDelay));
			}

			shotDelay += safeTimeBetweenShots;
		}

		return plan;
	}

	public static Vector3 GetOriginalAircraftOrigin(Vector3 targetPosition, Vector3 strafeDirection)
	{
		Vector3 safeDirection = GetSafeStrafeDirection(strafeDirection);
		return targetPosition + safeDirection * OriginalAircraftDistance + Vector3.up * OriginalAircraftAltitude;
	}

	public static Vector3 GetOriginalGau8VisualOrigin(Vector3 targetPosition, Vector3 strafeDirection)
	{
		Vector3 aircraftStart = GetOriginalAircraftOrigin(targetPosition, strafeDirection);
		Vector3 horizontalHeading = targetPosition - aircraftStart;
		horizontalHeading.y = 0f;
		if (horizontalHeading.sqrMagnitude <= 0.0001f)
		{
			horizontalHeading = -GetSafeStrafeDirection(strafeDirection);
		}

		Vector3 forward = horizontalHeading.normalized;
		if (forward.sqrMagnitude <= 0.0001f)
		{
			forward = Vector3.forward;
		}

		float fireTravelDistance = OriginalStrafeSpeed * OriginalGunFireDelaySeconds;
		return aircraftStart + forward * (fireTravelDistance + Gau8ForwardOffset);
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
		if (Physics.Raycast(probeStart, Vector3.down, out RaycastHit hit, probeDistance, ~0, QueryTriggerInteraction.Ignore))
		{
			return hit.point;
		}

		return intendedImpact;
	}

	private static float NextSpread(System.Random random, float min, float max)
	{
		return min + (float)random.NextDouble() * (max - min);
	}
}
