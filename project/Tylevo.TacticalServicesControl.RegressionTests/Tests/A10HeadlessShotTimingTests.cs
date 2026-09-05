using SamSWAT.FireSupport.ArysReloaded.Unity;
using UnityEngine;

internal static class A10HeadlessShotTimingTests
{
	[RegressionTest]
	private static void NearbyDamageLaunchWaitsForTheDistantVisualArrival()
	{
		A10TracerSegment visual = MakeShot(delay: 0.3f, flight: 3.8f);
		A10TracerSegment damage = MakeShot(delay: 0.3f, flight: 0.4f);
		Vector3 damageOrigin = damage.ProjectileOrigin;

		AssertEx.True(A10HeadlessShotTiming.TryAlignArrival(ref visual, ref damage));
		AssertEx.Near(0.3f, visual.DelaySeconds, 0.0001f);
		AssertEx.Near(3.7f, damage.DelaySeconds, 0.0001f);
		AssertEx.Near(visual.ImpactDelaySeconds, damage.ImpactDelaySeconds, 0.0001f);
		AssertEx.Equal(damageOrigin, damage.ProjectileOrigin);
	}

	[RegressionTest]
	private static void AlignmentNeverSchedulesAnEarlierLaunchWhenDamageFlightIsLonger()
	{
		A10TracerSegment visual = MakeShot(delay: 0.6f, flight: 0.4f);
		A10TracerSegment damage = MakeShot(delay: 0.6f, flight: 1.7f);

		AssertEx.True(A10HeadlessShotTiming.TryAlignArrival(ref visual, ref damage));
		AssertEx.Near(0.6f, damage.DelaySeconds, 0.0001f);
		AssertEx.Near(1.9f, visual.DelaySeconds, 0.0001f);
		AssertEx.Near(visual.ImpactDelaySeconds, damage.ImpactDelaySeconds, 0.0001f);
	}

	[RegressionTest]
	private static void UnreachableVisualShotAlsoInvalidatesItsDamagePair()
	{
		A10TracerSegment visual = MakeShot(delay: 0f, flight: 2f);
		A10TracerSegment damage = MakeShot(delay: 0f, flight: 0.5f);
		visual.IsValid = false;

		AssertEx.False(A10HeadlessShotTiming.TryAlignArrival(ref visual, ref damage));
		AssertEx.False(visual.IsValid);
		AssertEx.False(damage.IsValid);
	}

	[RegressionTest]
	private static void InvalidFlightTimeDoesNotPublishOrLaunchEitherPair()
	{
		foreach (float flight in new[] { float.NaN, float.PositiveInfinity, -0.1f })
		{
			A10TracerSegment visual = MakeShot(delay: 0f, flight: 2f);
			A10TracerSegment damage = MakeShot(delay: 0f, flight: flight);
			AssertEx.False(A10HeadlessShotTiming.TryAlignArrival(ref visual, ref damage));
			AssertEx.False(visual.IsValid);
			AssertEx.False(damage.IsValid);
		}
	}

	[RegressionTest]
	private static void FallbackWaitIncludesRemainingFlightAndNativeDamageSettleTime()
	{
		AssertEx.Near(2.75f,
			A10HeadlessShotTiming.GetSettleWaitSeconds(now: 10f, latestFiredImpactTime: 12.4f, settleSeconds: 0.35f),
			0.0001f);
		AssertEx.Near(0f,
			A10HeadlessShotTiming.GetSettleWaitSeconds(now: 13f, latestFiredImpactTime: 12.4f, settleSeconds: 0.35f),
			0.0001f);
	}

	[RegressionTest]
	private static void ObstructedPredictedImpactCannotAuthorizeFallbackAtIntendedTarget()
	{
		A10TracerSegment shot = MakeShot(delay: 0f, flight: 2f);
		AssertEx.True(A10HeadlessShotTiming.ReachesIntendedImpact(shot));
		shot.TracerEnd -= new Vector3(0f, 0f, 40f);
		AssertEx.False(A10HeadlessShotTiming.ReachesIntendedImpact(shot));
		shot.TracerEnd = shot.IntendedImpact;
		shot.IsValid = false;
		AssertEx.False(A10HeadlessShotTiming.ReachesIntendedImpact(shot));
	}

	private static A10TracerSegment MakeShot(float delay, float flight)
	{
		var impact = new Vector3(10f, 2f, 30f);
		return new A10TracerSegment(
			new Vector3(200f, 150f, -300f), Vector3.forward,
			new Vector3(20f, 30f, 0f), impact, delay)
		{
			IntendedImpact = impact,
			FlightTimeSeconds = flight
		};
	}
}
