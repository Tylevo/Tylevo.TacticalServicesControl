using SamSWAT.FireSupport.ArysReloaded.Unity;

internal static class A10AudioTimingTests
{
	[RegressionTest]
	private static void NearbyListenerStillWaitsForProjectileArrival()
	{
		AssertEx.True(A10AudioTiming.TryGetImpactPlaybackDelay([Shot(0f, 3.8f)], 0f, out float delay));
		AssertEx.Near(3.8f, delay, 0.0001f);
	}

	[RegressionTest]
	private static void DistantListenerWaitsForBallisticsAndSoundTravel()
	{
		AssertEx.True(A10AudioTiming.TryGetImpactPlaybackDelay([Shot(0.3f, 3.8f)], 686f, out float delay));
		AssertEx.Near(6.1f, delay, 0.0001f);
	}

	[RegressionTest]
	private static void EarliestArrivalCanBelongToALaterLaunchedRound()
	{
		AssertEx.True(A10AudioTiming.TryGetImpactPlaybackDelay(
			[Shot(0f, 4f), Shot(0.5f, 2f), Shot(1f, 3f)], 343f, out float delay));
		AssertEx.Near(3.5f, delay, 0.0001f);
	}

	[RegressionTest]
	private static void InvalidShotsCannotStartAnEarlyImpactRecording()
	{
		A10TracerSegment invalid = Shot(0f, 0f);
		invalid.IsValid = false;
		AssertEx.True(A10AudioTiming.TryGetImpactPlaybackDelay(
			[invalid, Shot(0f, float.NaN), Shot(-1f, 1f), Shot(0f, -1f), Shot(0f, float.PositiveInfinity), Shot(1f, 2f)],
			0f, out float delay));
		AssertEx.Near(3f, delay, 0.0001f);
		AssertEx.False(A10AudioTiming.TryGetImpactPlaybackDelay([invalid], 0f, out _));
		AssertEx.False(A10AudioTiming.TryGetImpactPlaybackDelay([], 0f, out _));
	}

	[RegressionTest]
	private static void InvalidListenerDistanceDoesNotScheduleAudio()
	{
		foreach (float distance in new[] { -1f, float.NaN, float.PositiveInfinity })
			AssertEx.False(A10AudioTiming.TryGetImpactPlaybackDelay([Shot(0f, 1f)], distance, out _));
	}

	[RegressionTest]
	private static void SeparatePassesUseTheirOwnBallisticArrival()
	{
		AssertEx.True(A10AudioTiming.TryGetImpactPlaybackDelay([Shot(0f, 4f)], 343f, out float first));
		AssertEx.True(A10AudioTiming.TryGetImpactPlaybackDelay([Shot(0f, 2f)], 343f, out float second));
		AssertEx.Near(5f, first, 0.0001f);
		AssertEx.Near(3f, second, 0.0001f);
		AssertEx.Near(5f, A10AudioTiming.ImpactToCannonDelaySeconds, 0.0001f);
	}

	private static A10TracerSegment Shot(float launch, float flight)
	{
		return new A10TracerSegment { IsValid = true, DelaySeconds = launch, FlightTimeSeconds = flight };
	}

	[RegressionTest]
	private static void ImpactWindowUsesTheFirstAndLastArrival()
	{
		AssertEx.True(A10AudioTiming.TryGetImpactPlaybackWindow(
			[Shot(0f, 4f), Shot(0.5f, 2f), Shot(1f, 3.5f)], 686f,
			out float delay, out float duration));
		AssertEx.Near(4.5f, delay, 0.0001f);
		AssertEx.Near(2f, duration, 0.0001f);
		// Sound travel shifts the complete burst; it must not stretch its length.
		AssertEx.True(A10AudioTiming.TryGetImpactPlaybackWindow(
			[Shot(0f, 4f), Shot(0.5f, 2f), Shot(1f, 3.5f)], 0f, out _, out float nearDuration));
		AssertEx.Near(duration, nearDuration, 0.0001f);
	}

}
