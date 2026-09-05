using SamSWAT.FireSupport.ArysReloaded.Unity;

internal static class PhonePresentationTransitionTests
{
	[RegressionTest]
	private static void FovAndFramingShareASlowStartAndExactEndpoint()
	{
		var owner = new object();
		var transition = new PhonePresentationTransition();
		transition.Begin(owner, 10.08f, 0.75f);
		AssertEx.True(transition.TrySample(10f, owner, out float beforeRaise));
		AssertEx.Near(0f, beforeRaise, 0.00001f);
		AssertEx.True(transition.TrySample(10.08f + 0.75f * 0.25f, owner, out float early));
		AssertEx.True(early > 0f && early < 0.25f,
			"The first quarter of the raise should move less than a linear zoom, preventing an abrupt approach.");
		AssertEx.True(transition.TrySample(10.08f + 0.75f * 0.5f, owner, out float halfway));
		AssertEx.Near(60f, Interpolate(75f, 45f, halfway), 0.0001f);
		AssertEx.Near(0.045f, Interpolate(0f, 0.09f, halfway), 0.0001f);
		AssertEx.True(transition.TrySample(10.08f + 0.75f, owner, out float complete));
		AssertEx.Near(45f, Interpolate(75f, 45f, complete), 0.0001f);
		AssertEx.Near(0.09f, Interpolate(0f, 0.09f, complete), 0.0001f);
		AssertEx.True(transition.TrySample(30f, owner, out float held));
		AssertEx.Near(1f, held, 0.0001f);
	}

	[RegressionTest]
	private static void FrameRateAndRepeatedSamplingDoNotChangeTheZoomCurve()
	{
		var owner = new object();
		float[] milestones = { 0f, 0.1f, 0.25f, 0.5f, 0.74f, 0.75f, 1f };
		var expected = new PhonePresentationTransition();
		expected.Begin(owner, 100f, 0.75f);
		foreach (int framesPerSecond in new[] { 30, 60, 144 })
		{
			var sampled = new PhonePresentationTransition();
			sampled.Begin(owner, 100f, 0.75f);
			float previous = -1f;
			for (int frame = 0; frame <= framesPerSecond; frame++)
			{
				AssertEx.True(sampled.TrySample(100f + (float)frame / framesPerSecond, owner, out float blend));
				AssertEx.True(blend >= previous && blend <= 1f);
				previous = blend;
			}
			foreach (float milestone in milestones)
			{
				AssertEx.True(expected.TrySample(100f + milestone, owner, out float expectedBlend));
				AssertEx.True(sampled.TrySample(100f + milestone, owner, out float actualBlend));
				AssertEx.Near(expectedBlend, actualBlend, 0.000001f,
					$"Sampling at {framesPerSecond} FPS must not accelerate the elapsed-time transition.");
			}
		}
	}

	[RegressionTest]
	private static void AReplacedOwnerIsPermanentlyRetiredUntilExplicitlyRestarted()
	{
		var originalOwner = new object();
		var replacementOwner = new object();
		var transition = new PhonePresentationTransition();
		transition.Begin(originalOwner, 1f, 0.75f);
		AssertEx.True(transition.TrySample(1.2f, originalOwner, out _));
		AssertEx.False(transition.TrySample(1.3f, replacementOwner, out _));
		AssertEx.False(transition.IsActive);
		AssertEx.False(transition.TrySample(1.4f, originalOwner, out _),
			"An old owner must never resume its camera writes after a hand/controller replacement.");
		transition.Begin(replacementOwner, 1.4f, 0.75f);
		AssertEx.True(transition.TrySample(1.4f, replacementOwner, out float blend));
		AssertEx.Near(0f, blend, 0.00001f);
	}

	[RegressionTest]
	private static void CancellationStopsPendingWritesAndRestartCanBeginAtTheVisibleFov()
	{
		var owner = new object();
		var transition = new PhonePresentationTransition();
		transition.Begin(owner, 0f, 0.75f);
		AssertEx.True(transition.TrySample(0.2f, owner, out float interruptedBlend));
		float visibleFov = Interpolate(75f, 45f, interruptedBlend);
		transition.Cancel();
		transition.Cancel();
		AssertEx.False(transition.TrySample(0.4f, owner, out _));
		transition.Begin(owner, 0.4f, 0.75f);
		AssertEx.True(transition.TrySample(0.4f, owner, out float restartBlend));
		AssertEx.Near(visibleFov, Interpolate(visibleFov, 45f, restartBlend), 0.00001f,
			"Reopening an interrupted transition can reuse its visible start value without a camera jump.");
		transition.Cancel();
		AssertEx.False(transition.TrySample(1f, owner, out _));
	}

	[RegressionTest]
	private static void InvalidClockSamplesNeverEmitNanCameraValues()
	{
		var owner = new object();
		var transition = new PhonePresentationTransition();
		transition.Begin(owner, 1f, 0.75f);
		AssertEx.False(transition.TrySample(float.NaN, owner, out float blend));
		AssertEx.Near(0f, blend, 0.00001f);
		AssertEx.False(transition.TrySample(1.5f, owner, out _));
		transition.Begin(owner, float.PositiveInfinity, 0.75f);
		AssertEx.False(transition.TrySample(1.5f, owner, out _));
		transition.Begin(owner, 1f, float.NaN);
		AssertEx.True(transition.TrySample(1.1f, owner, out float immediate));
		AssertEx.Near(1f, immediate, 0.00001f);
	}

	private static float Interpolate(float from, float to, float blend)
	{
		return from + (to - from) * blend;
	}
}
