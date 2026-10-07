using SamSWAT.FireSupport.ArysReloaded.Unity;

internal static class PhoneZoomPolicyTests
{
	[RegressionTest]
	private static void DeploymentZoomWorksWithPurchaseZoomDisabledAndHasItsOwnFov()
	{
		AssertEx.True(PhoneZoomPolicy.IsPresentationEnabled(UavPhoneLaunchMode.DeployMenu, false, true));
		AssertEx.False(PhoneZoomPolicy.IsPresentationEnabled(UavPhoneLaunchMode.ManualAuthorization, false, true));
		AssertEx.Near(35f, PhoneZoomPolicy.GetRaisedFov(UavPhoneLaunchMode.DeployMenu, 75f, 50f, 35f), 0.001f);
		AssertEx.Near(50f, PhoneZoomPolicy.GetRaisedFov(UavPhoneLaunchMode.ManualAuthorization, 75f, 50f, 35f), 0.001f);
		AssertEx.False(PhoneZoomPolicy.IsPresentationEnabled(UavPhoneLaunchMode.DeployMenu, true, false));
	}

	[RegressionTest]
	private static void RadarAndIncomingCallsRetainRaidFovRegardlessOfZoomSettings()
	{
		foreach (var mode in new[] { UavPhoneLaunchMode.UavRadarMonitor, UavPhoneLaunchMode.DangerCloseIncomingCall })
		{
			AssertEx.True(PhoneZoomPolicy.PreservesRaidFov(mode));
			AssertEx.Near(65f, PhoneZoomPolicy.GetRaisedFov(mode, 65f, 20f, 20f), 0.001f);
			AssertEx.False(PhoneZoomPolicy.SupportsSprintZoom(mode));
		}
		AssertEx.True(PhoneZoomPolicy.SupportsSprintZoom(UavPhoneLaunchMode.DeployMenu));
		AssertEx.True(PhoneZoomPolicy.SupportsSprintZoom(UavPhoneLaunchMode.ManualAuthorization));
	}

	[RegressionTest]
	private static void DeploymentZoomNeverWidensANarrowerViewOrEmitsInvalidConfiguredFov()
	{
		AssertEx.Near(30f, PhoneZoomPolicy.GetRaisedFov(UavPhoneLaunchMode.DeployMenu, 30f, 45f, 50f), 0.001f);
		AssertEx.Near(20f, PhoneZoomPolicy.GetRaisedFov(UavPhoneLaunchMode.DeployMenu, 75f, 45f, -5f), 0.001f);
		AssertEx.Near(75f, PhoneZoomPolicy.GetRaisedFov(UavPhoneLaunchMode.DeployMenu, 90f, 45f, 100f), 0.001f);
		foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
		{
			AssertEx.Near(45f, PhoneZoomPolicy.GetRaisedFov(UavPhoneLaunchMode.DeployMenu, 75f, 45f, invalid), 0.001f);
		}
	}
}
