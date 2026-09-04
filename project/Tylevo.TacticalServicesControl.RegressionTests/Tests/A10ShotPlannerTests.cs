using SamSWAT.FireSupport.ArysReloaded.Unity;
using UnityEngine;

internal static class A10ShotPlannerTests
{
	private const float Tolerance = 0.0001f;

	[RegressionTest]
	private static void ImpactPlanIsFiftyRoundsAndDeterministicForASeed()
	{
		var target = new Vector3(100f, 12f, -40f);
		var aircraftForward = new Vector3(10f, 0f, 4f);

		IReadOnlyList<Vector3> first = A10ShotPlanner.BuildImpactPlan(
			target,
			aircraftForward,
			seed: 8675309);
		IReadOnlyList<Vector3> replay = A10ShotPlanner.BuildImpactPlan(
			target,
			aircraftForward,
			seed: 8675309);
		IReadOnlyList<Vector3> otherSeed = A10ShotPlanner.BuildImpactPlan(
			target,
			aircraftForward,
			seed: 8675310);

		AssertEx.Equal(A10ShotPlanner.ShotCount, first.Count);
		AssertEx.Equal(50, first.Count);
		AssertEx.SequenceEqual(first, replay);
		AssertEx.True(
			first.Where((impact, index) => !impact.Equals(otherSeed[index])).Any(),
			"A different visual seed should produce a different lateral impact pattern.");
	}

	[RegressionTest]
	private static void MovingMuzzlePlanAdvancesEveryShotAtNormalizedStrafeSpeed()
	{
		var firstMuzzleOrigin = new Vector3(20f, 320f, -500f);
		var nonUnitForward = new Vector3(0f, 0f, 10f);
		const float timeBetweenShots = 0.05f;
		IReadOnlyList<Vector3> impacts = Enumerable.Range(0, A10ShotPlanner.ShotCount)
			.Select(index => new Vector3(index * 0.2f, 0f, 1000f + index))
			.ToArray();

		IReadOnlyList<A10TracerSegment> plan = A10ShotPlanner.BuildMovingMuzzlePlan(
			firstMuzzleOrigin,
			nonUnitForward,
			impacts,
			timeBetweenShots);

		AssertEx.Equal(50, plan.Count);
		for (int index = 0; index < plan.Count; index++)
		{
			float expectedDelay = index * timeBetweenShots;
			Vector3 expectedOrigin = firstMuzzleOrigin +
			                         Vector3.forward * A10ShotPlanner.StrafeSpeed * expectedDelay;
			A10TracerSegment shot = plan[index];

			AssertVectorNear(expectedOrigin, shot.ProjectileOrigin);
			AssertEx.Near(expectedDelay, shot.DelaySeconds, Tolerance);
			AssertEx.Near(1f, shot.ProjectileDirection.magnitude, Tolerance);
			AssertVectorNear(impacts[index], shot.TracerEnd);
			AssertEx.True(shot.IsValid);
		}

		float expectedBurstTravel =
			(A10ShotPlanner.ShotCount - 1) * timeBetweenShots * A10ShotPlanner.StrafeSpeed;
		AssertEx.Near(
			expectedBurstTravel,
			Vector3.Distance(plan[0].ProjectileOrigin, plan[^1].ProjectileOrigin),
			Tolerance);
	}

	[RegressionTest]
	private static void VisualAndDamageOriginsProjectTheSameImpactIntent()
	{
		var target = new Vector3(50f, 2f, 75f);
		var aircraftForward = new Vector3(-4f, 0f, 9f);
		IReadOnlyList<Vector3> impacts = A10ShotPlanner.BuildImpactPlan(
			target,
			aircraftForward,
			seed: 12345);
		IReadOnlyList<A10TracerSegment> visualPlan = A10ShotPlanner.BuildMovingMuzzlePlan(
			new Vector3(500f, 320f, -800f),
			aircraftForward,
			impacts,
			0.04f);
		IReadOnlyList<A10TracerSegment> damagePlan = A10ShotPlanner.BuildMovingMuzzlePlan(
			new Vector3(240f, 150f, -300f),
			aircraftForward,
			impacts,
			0.04f);

		AssertEx.Equal(impacts.Count, visualPlan.Count);
		AssertEx.Equal(impacts.Count, damagePlan.Count);
		for (int index = 0; index < impacts.Count; index++)
		{
			AssertVectorNear(impacts[index], visualPlan[index].TracerEnd);
			AssertVectorNear(impacts[index], damagePlan[index].TracerEnd);
			AssertEx.Near(1f, visualPlan[index].ProjectileDirection.magnitude, Tolerance);
			AssertEx.Near(1f, damagePlan[index].ProjectileDirection.magnitude, Tolerance);
		}
	}

	private static void AssertVectorNear(Vector3 expected, Vector3 actual)
	{
		AssertEx.Near(expected.x, actual.x, Tolerance);
		AssertEx.Near(expected.y, actual.y, Tolerance);
		AssertEx.Near(expected.z, actual.z, Tolerance);
	}
}
