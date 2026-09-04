using SamSWAT.FireSupport.ArysReloaded.Unity;

internal static class MainMenuSlotStepPolicyTests
{
	[RegressionTest]
	private static void AdjacentLowerRowsOverrideDoublePlayCharacterGap()
	{
		float step = MainMenuSlotStepPolicy.Resolve(
			tradeToHideout: -60f,
			hideoutToExit: -60f,
			cachedStep: -48f,
			playToCharacter: -120f,
			fallback: -60f);

		AssertEx.Equal(-60f, step);
	}

	[RegressionTest]
	private static void DoubleGapFallsBackWhenAdjacentRowsAreUnavailable()
	{
		float step = MainMenuSlotStepPolicy.Resolve(
			tradeToHideout: null,
			hideoutToExit: null,
			cachedStep: null,
			playToCharacter: -120f,
			fallback: -60f);

		AssertEx.Equal(-60f, step);
	}

	[RegressionTest]
	private static void ValidSingleRowFallbackRemainsSupported()
	{
		float step = MainMenuSlotStepPolicy.Resolve(
			tradeToHideout: null,
			hideoutToExit: null,
			cachedStep: null,
			playToCharacter: -54f,
			fallback: -60f);

		AssertEx.Equal(-54f, step);
	}

	[RegressionTest]
	private static void WideAdjacentRowsRemainSupported()
	{
		float step = MainMenuSlotStepPolicy.Resolve(
			tradeToHideout: -100f,
			hideoutToExit: null,
			cachedStep: null,
			playToCharacter: -120f,
			fallback: -60f);

		AssertEx.Equal(-100f, step);
	}

	[RegressionTest]
	private static void CachedNativeStepPrecedesAmbiguousPlayCharacterGap()
	{
		float step = MainMenuSlotStepPolicy.Resolve(
			tradeToHideout: null,
			hideoutToExit: null,
			cachedStep: -72f,
			playToCharacter: -54f,
			fallback: -60f);

		AssertEx.Equal(-72f, step);
	}
}
