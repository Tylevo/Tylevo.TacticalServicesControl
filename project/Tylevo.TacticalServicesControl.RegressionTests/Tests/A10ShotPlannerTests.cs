using EFT.Ballistics;
using SamSWAT.FireSupport.ArysReloaded.Unity;
using UnityEngine;

internal static class A10ShotPlannerTests
{
	private const float Tolerance = 0.0001f;
	private const float BallisticTolerance = A10BallisticSolver.VerticalToleranceMeters * 2f;

	[RegressionTest]
	private static void ImpactPlanIsFiftyRoundsAndDeterministicForASeed()
	{
		using var physics = new PhysicsScope();
		var target = new Vector3(100f, 12f, -40f);
		var aircraftForward = new Vector3(10f, 0f, 4f);
		IReadOnlyList<Vector3> first = A10ShotPlanner.BuildImpactPlan(target, aircraftForward, 8675309);
		IReadOnlyList<Vector3> replay = A10ShotPlanner.BuildImpactPlan(target, aircraftForward, 8675309);
		IReadOnlyList<Vector3> otherSeed = A10ShotPlanner.BuildImpactPlan(target, aircraftForward, 8675310);
		AssertEx.Equal(50, first.Count);
		AssertEx.SequenceEqual(first, replay);
		AssertEx.True(first.Where((impact, index) => !impact.Equals(otherSeed[index])).Any());
		Vector3 forward = aircraftForward.normalized;
		Vector3 right = Vector3.Cross(Vector3.up, forward);
		AssertEx.Near(-22.05f, Vector3.Dot(first[0] - target, forward), Tolerance);
		AssertEx.Near(22.05f, Vector3.Dot(first[^1] - target, forward), Tolerance);
		AssertEx.True(first.All(impact => Math.Abs(Vector3.Dot(impact - target, right)) <= 7.5f));
	}

	[RegressionTest]
	private static void SurfaceProbeStaysNearDesignationAndDoesNotReachHighRoof()
	{
		using var physics = new PhysicsScope();
		int probes = 0;
		Physics.RaycastAllHandler = query =>
		{
			probes++;
			AssertSurfaceQuery(query);
			AssertEx.True(query.Origin.y < 80f, "The probe must begin below the unrelated high roof.");
			AssertEx.True(query.Origin.y - query.MaximumDistance < 10f);
			return [new RaycastHit { point = new Vector3(query.Origin.x, 10f, query.Origin.z) }];
		};
		IReadOnlyList<Vector3> impacts = A10ShotPlanner.BuildImpactPlan(new Vector3(0f, 10f, 0f), Vector3.forward, 12);
		AssertEx.Equal(50, probes);
		AssertEx.True(impacts.All(point => point.y == 10f));
	}

	[RegressionTest]
	private static void SurfaceProbeChoosesClosestLayeredSurfaceInsteadOfTopmostHit()
	{
		using var physics = new PhysicsScope();
		Physics.RaycastAllHandler = query =>
		{
			AssertSurfaceQuery(query);
			return
			[
				new RaycastHit { point = new Vector3(query.Origin.x, 22f, query.Origin.z) },
				new RaycastHit { point = new Vector3(query.Origin.x, 9f, query.Origin.z) },
				new RaycastHit { point = new Vector3(query.Origin.x, -6f, query.Origin.z) }
			];
		};
		IReadOnlyList<Vector3> impacts = A10ShotPlanner.BuildImpactPlan(new Vector3(5f, 10f, 8f), Vector3.forward, 98);
		AssertEx.True(impacts.All(point => point.y == 9f), "Ground near the locked elevation must win over roof and basement.");
	}

	[RegressionTest]
	private static void SurfaceProbeRetainsSlopeAcrossTheStrikeCorridor()
	{
		using var physics = new PhysicsScope();
		Physics.RaycastAllHandler = query =>
		{
			float height = 10f + query.Origin.z * 0.2f + query.Origin.x * 0.1f;
			return [new RaycastHit { point = new Vector3(query.Origin.x, height, query.Origin.z) }];
		};
		IReadOnlyList<Vector3> impacts = A10ShotPlanner.BuildImpactPlan(new Vector3(0f, 10f, 0f), Vector3.forward, 2);
		foreach (Vector3 impact in impacts)
			AssertEx.Near(10f + impact.z * 0.2f + impact.x * 0.1f, impact.y, Tolerance);
		AssertEx.True(impacts[^1].y - impacts[0].y > 7f);
	}

	[RegressionTest]
	private static void MovingMuzzlePlanAdvancesEveryShotAndSeparatesLaunchFromArrival()
	{
		using var physics = new PhysicsScope();
		using A10EftTrajectoryEvaluator evaluator = CreateEvaluator();
		var firstMuzzleOrigin = new Vector3(20f, 320f, -500f);
		const float timeBetweenShots = 0.05f;
		IReadOnlyList<Vector3> impacts = Enumerable.Range(0, A10ShotPlanner.ShotCount)
			.Select(index => new Vector3(index * 0.2f, 0f, 1000f + index)).ToArray();
		IReadOnlyList<A10TracerSegment> plan = A10ShotPlanner.BuildMovingMuzzlePlan(
			firstMuzzleOrigin, new Vector3(0f, 0f, 10f), impacts, timeBetweenShots, evaluator);
		AssertEx.Equal(50, plan.Count);
		for (int index = 0; index < plan.Count; index++)
		{
			float expectedDelay = index * timeBetweenShots;
			Vector3 expectedOrigin = firstMuzzleOrigin + Vector3.forward * A10ShotPlanner.StrafeSpeed * expectedDelay;
			A10TracerSegment shot = plan[index];
			AssertEx.True(shot.IsValid, $"Shot {index} must have a valid compensated solution.");
			AssertVectorNear(expectedOrigin, shot.ProjectileOrigin);
			AssertEx.Near(expectedDelay, shot.DelaySeconds, Tolerance);
			AssertVectorNear(impacts[index], shot.IntendedImpact);
			AssertVectorNear(impacts[index], shot.TracerEnd, BallisticTolerance);
			AssertEx.Near(1f, shot.ProjectileDirection.magnitude, Tolerance);
			AssertEx.True(shot.ProjectileDirection.y > (impacts[index] - expectedOrigin).normalized.y);
			AssertEx.True(shot.FlightTimeSeconds > 0f);
			AssertEx.Near(expectedDelay + shot.FlightTimeSeconds, shot.ImpactDelaySeconds, Tolerance);
		}
		AssertEx.Near(49f * timeBetweenShots * A10ShotPlanner.StrafeSpeed,
			Vector3.Distance(plan[0].ProjectileOrigin, plan[^1].ProjectileOrigin), Tolerance);
	}

	[RegressionTest]
	private static void VisualAndDamageOriginsSolveToTheSameImpactIntent()
	{
		using var physics = new PhysicsScope();
		using A10EftTrajectoryEvaluator evaluator = CreateEvaluator();
		IReadOnlyList<Vector3> impacts = A10ShotPlanner.BuildImpactPlan(new Vector3(50f, 2f, 75f), new Vector3(-4f, 0f, 9f), 12345);
		IReadOnlyList<A10TracerSegment> visualPlan = A10ShotPlanner.BuildMovingMuzzlePlan(
			new Vector3(500f, 320f, -800f), new Vector3(-4f, 0f, 9f), impacts, 0.04f, evaluator);
		IReadOnlyList<A10TracerSegment> damagePlan = A10ShotPlanner.BuildMovingMuzzlePlan(
			new Vector3(240f, 150f, -300f), new Vector3(-4f, 0f, 9f), impacts, 0.04f, evaluator);
		for (int index = 0; index < impacts.Count; index++)
		{
			AssertEx.True(visualPlan[index].IsValid && damagePlan[index].IsValid);
			AssertVectorNear(impacts[index], visualPlan[index].TracerEnd, BallisticTolerance);
			AssertVectorNear(impacts[index], damagePlan[index].TracerEnd, BallisticTolerance);
			AssertVectorNear(impacts[index], visualPlan[index].IntendedImpact);
			AssertVectorNear(impacts[index], damagePlan[index].IntendedImpact);
		}
	}

	[RegressionTest]
	private static void CurvedPathObstacleTruncatesImpactAndTimeWithoutChangingLaunchAim()
	{
		using var physics = new PhysicsScope();
		using A10EftTrajectoryEvaluator evaluator = CreateEvaluator();
		var origin = new Vector3(0f, 320f, 0f);
		var target = new Vector3(0f, 0f, 1450f);
		const float obstacleRange = 600f;
		A10TracerSegment clear = A10ShotPlanner.BuildBallisticShot(origin, target, 0.7f, evaluator);
		AssertEx.True(clear.IsValid);
		AssertEx.True(evaluator.TryEvaluate(origin, clear.ProjectileDirection, obstacleRange, true, out A10TrajectoryEvaluation atWall));
		int collisions = 0;
		Physics.RaycastHandler = query =>
		{
			AssertEx.Equal(BallisticsCalculatorConstants.HitMask, query.LayerMask);
			AssertEx.Equal(QueryTriggerInteraction.UseGlobal, query.TriggerInteraction);
			if (query.Direction.z <= 0f) return null;
			float distance = (obstacleRange - query.Origin.z) / query.Direction.z;
			if (distance < 0f || distance > query.MaximumDistance) return null;
			collisions++;
			return new RaycastHit { point = query.Origin + query.Direction * distance, distance = distance };
		};
		A10TracerSegment blocked = A10ShotPlanner.BuildBallisticShot(origin, target, 0.7f, evaluator);
		AssertEx.True(blocked.IsValid);
		AssertEx.Equal(1, collisions);
		AssertVectorNear(clear.ProjectileDirection, blocked.ProjectileDirection);
		AssertVectorNear(target, blocked.IntendedImpact);
		AssertVectorNear(atWall.Position, blocked.TracerEnd, 0.002f);
		AssertEx.Near(atWall.FlightTimeSeconds, blocked.FlightTimeSeconds, 0.0001f);
		AssertEx.True(blocked.FlightTimeSeconds < clear.FlightTimeSeconds);
		AssertEx.Near(0.7f + blocked.FlightTimeSeconds, blocked.ImpactDelaySeconds, Tolerance);
		float directRayHeight = origin.y + (target.y - origin.y) * obstacleRange / target.z;
		AssertEx.True(blocked.TracerEnd.y > directRayHeight + 1f, "Obstacle contact must follow the compensated curve, not a direct aim ray.");
	}

	[RegressionTest]
	private static void TerminalTracerFollowsDescendingTrajectoryRatherThanLaunchDirection()
	{
		using var physics = new PhysicsScope();
		using A10EftTrajectoryEvaluator evaluator = CreateEvaluator();
		var origin = new Vector3(0f, 320f, 0f);
		var target = new Vector3(0f, 0f, 1450f);
		A10TracerSegment shot = A10ShotPlanner.BuildBallisticShot(origin, target, 0f, evaluator);
		AssertEx.True(shot.IsValid);
		AssertEx.True(evaluator.TryEvaluate(origin, shot.ProjectileDirection, target.z, true, out A10TrajectoryEvaluation trajectory));
		Vector3 terminalDirection = (shot.TracerEnd - shot.TracerStart).normalized;
		Vector3 lastChord = (trajectory.Path[^1].Position - trajectory.Path[^2].Position).normalized;
		AssertEx.True(terminalDirection.y < shot.ProjectileDirection.y - 0.001f);
		AssertEx.True(Vector3.Dot(terminalDirection, lastChord) > 0.999f);
		AssertEx.Near(A10ShotPlanner.TracerSegmentLength, Vector3.Distance(shot.TracerStart, shot.TracerEnd), 0.02f);
	}

	[RegressionTest]
	private static void InvalidTrajectoryEvaluatorCannotCreateValidDamageOrTracerPlan()
	{
		using var physics = new PhysicsScope();
		using var invalidEvaluator = new A10EftTrajectoryEvaluator(0f, 280f, 30f, 0.316f, 40f);
		int raycasts = 0;
		Physics.RaycastHandler = _ => { raycasts++; return null; };
		IReadOnlyList<A10TracerSegment> plan = A10ShotPlanner.BuildMovingMuzzlePlan(
			new Vector3(0f, 320f, 0f), Vector3.forward,
			[new Vector3(0f, 0f, 1000f), new Vector3(0f, 0f, 1001f)], 0.05f, invalidEvaluator);
		AssertEx.Equal(2, plan.Count);
		AssertEx.True(plan.All(shot => !shot.IsValid));
		AssertEx.Equal(0, raycasts);
	}

	private static A10EftTrajectoryEvaluator CreateEvaluator() => new(1070f, 280f, 30f, 0.316f, 40f);
	private static void AssertSurfaceQuery(Physics.RaycastQuery query)
	{
		AssertEx.Equal(LayersMaskController.TerrainLowPoly, query.LayerMask);
		AssertEx.Equal(QueryTriggerInteraction.Ignore, query.TriggerInteraction);
		AssertVectorNear(Vector3.down, query.Direction);
	}
	private static void AssertVectorNear(Vector3 expected, Vector3 actual, float tolerance = Tolerance)
	{
		AssertEx.Near(expected.x, actual.x, tolerance);
		AssertEx.Near(expected.y, actual.y, tolerance);
		AssertEx.Near(expected.z, actual.z, tolerance);
	}
	private sealed class PhysicsScope : IDisposable
	{
		public PhysicsScope() => Physics.Reset();
		public void Dispose() => Physics.Reset();
	}
}
