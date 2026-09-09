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

	[RegressionTest]
	private static void SprintOutAndReturnUseTheOriginalRaiseDurationAndCurve()
	{
		var owner = new object();
		var originalRaise = new PhonePresentationTransition();
		var sprintTransition = new PhonePresentationTransition();
		originalRaise.Begin(owner, 0.08f, 0.75f);
		sprintTransition.Begin(owner, 0.08f, 0.75f);
		foreach (float edgeTime in new[] { 2f, 4f, 6f })
		{
			AssertEx.True(sprintTransition.TryRestart(edgeTime, owner));
			foreach (float elapsed in new[] { 0f, 0.1875f, 0.375f, 0.5625f, 0.75f })
			{
				AssertEx.True(originalRaise.TrySample(0.08f + elapsed, owner, out float originalBlend));
				AssertEx.True(sprintTransition.TrySample(edgeTime + elapsed, owner, out float sprintBlend));
				AssertEx.Near(originalBlend, sprintBlend, 0.000001f,
					"Sprint fade-out and return fade-in must keep the existing opening curve and duration.");
			}
			AssertEx.True(sprintTransition.TrySample(edgeTime + 0.75f, owner, out float complete));
			AssertEx.Near(75f, Interpolate(45f, 75f, complete), 0.00001f);
		}
	}

	[RegressionTest]
	private static void SprintReversalCanRebaseFovAndFramingAtTheirVisibleValues()
	{
		var owner = new object();
		var transition = new PhonePresentationTransition();
		transition.Begin(owner, 0f, 0.75f);
		AssertEx.True(transition.TrySample(0.25f, owner, out float openingBlend));
		float fovBeforeSprint = Interpolate(75f, 45f, openingBlend);
		float framingBeforeSprint = Interpolate(0f, 0.09f, openingBlend);
		AssertEx.True(transition.TryRestart(0.25f, owner));
		AssertEx.True(transition.TrySample(0.25f, owner, out float sprintStart));
		AssertEx.Near(fovBeforeSprint, Interpolate(fovBeforeSprint, 75f, sprintStart), 0.00001f);
		AssertEx.Near(framingBeforeSprint, Interpolate(framingBeforeSprint, 0f, sprintStart), 0.00001f);
		AssertEx.True(transition.TrySample(0.5f, owner, out float sprintBlend));
		float fovBeforeStop = Interpolate(fovBeforeSprint, 75f, sprintBlend);
		float framingBeforeStop = Interpolate(framingBeforeSprint, 0f, sprintBlend);
		AssertEx.True(fovBeforeStop > fovBeforeSprint);
		AssertEx.True(framingBeforeStop < framingBeforeSprint);
		AssertEx.True(transition.TryRestart(0.5f, owner));
		AssertEx.True(transition.TrySample(0.5f, owner, out float returnStart));
		AssertEx.Near(fovBeforeStop, Interpolate(fovBeforeStop, 45f, returnStart), 0.00001f);
		AssertEx.Near(framingBeforeStop, Interpolate(framingBeforeStop, 0.09f, returnStart), 0.00001f);
		AssertEx.True(transition.TrySample(1.25f, owner, out float complete));
		AssertEx.Near(45f, Interpolate(fovBeforeStop, 45f, complete), 0.00001f);
		AssertEx.Near(0.09f, Interpolate(framingBeforeStop, 0.09f, complete), 0.00001f);
	}

	[RegressionTest]
	private static void SprintEdgeCannotReviveAClosedOrReplacedPresentation()
	{
		var owner = new object();
		var replacement = new object();
		var transition = new PhonePresentationTransition();
		transition.Begin(owner, 0f, 0.75f);
		AssertEx.False(transition.TryRestart(0.25f, replacement));
		AssertEx.False(transition.TryRestart(0.5f, owner));
		transition.Begin(owner, 1f, 0.75f);
		transition.Cancel();
		AssertEx.False(transition.TryRestart(1.25f, owner));
		transition.Begin(owner, 2f, 0.75f);
		AssertEx.False(transition.TryRestart(float.NaN, owner));
		AssertEx.False(transition.TrySample(2.5f, owner, out _));
	}

	private static float Interpolate(float from, float to, float blend)
	{
		return from + (to - from) * blend;
	}
}
