using SamSWAT.FireSupport.ArysReloaded.Unity;

internal static class PhoneDeploymentHandoffTests
{
	[RegressionTest]
	private static void LongNativeZoomRestoreFinishesBeforeDesignationIsDispatched()
	{
		var handoff = new PhoneDeploymentHandoff(3, 10f);
		AssertEx.Equal(PhoneDeploymentHandoffState.Waiting, handoff.Advance(3, 10.34f, false, false));
		AssertEx.Equal(PhoneDeploymentHandoffState.Waiting, handoff.Advance(3, 10.4f, true, false));
		AssertEx.Equal(PhoneDeploymentHandoffState.Waiting, handoff.Advance(3, 10.8f, true, false));
		AssertEx.Equal(PhoneDeploymentHandoffState.Ready, handoff.Advance(3, 10.82f, false, false));
		AssertEx.Equal(PhoneDeploymentHandoffState.Cancelled, handoff.Advance(3, 11f, false, false),
			"A finished handoff cannot dispatch another request.");
	}

	[RegressionTest]
	private static void PauseCannotBeMistakenForCompletedScaledTimeCameraRestore()
	{
		var handoff = new PhoneDeploymentHandoff(4, 1f);
		AssertEx.Equal(PhoneDeploymentHandoffState.Waiting, handoff.Advance(4, 30f, true, true));
		AssertEx.Equal(PhoneDeploymentHandoffState.Waiting, handoff.Advance(4, 60f, true, false),
			"Unpausing does not finish the native camera tween that was paused mid-transition.");
		AssertEx.Equal(PhoneDeploymentHandoffState.Ready, handoff.Advance(4, 60.5f, false, false));
	}

	[RegressionTest]
	private static void DisabledZoomStillSeparatesInputAndDoesNotDeployWhilePaused()
	{
		var handoff = new PhoneDeploymentHandoff(4, 1f);
		AssertEx.Equal(PhoneDeploymentHandoffState.Waiting, handoff.Advance(4, 1.2f, false, false));
		AssertEx.Equal(PhoneDeploymentHandoffState.Waiting, handoff.Advance(4, 90f, false, true));
		AssertEx.Equal(PhoneDeploymentHandoffState.Ready, handoff.Advance(4, 91f, false, false));
	}

	[RegressionTest]
	private static void ReopeningAnyPhonePermanentlyCancelsThePreviousDeploymentHandoff()
	{
		var handoff = new PhoneDeploymentHandoff(4, 1f);
		AssertEx.Equal(PhoneDeploymentHandoffState.Waiting, handoff.Advance(4, 1.2f, true, false));
		AssertEx.Equal(PhoneDeploymentHandoffState.Cancelled, handoff.Advance(5, 1.3f, true, true));
		AssertEx.Equal(PhoneDeploymentHandoffState.Cancelled, handoff.Advance(5, 2f, false, false));
		AssertEx.Equal(PhoneDeploymentHandoffState.Cancelled, handoff.Advance(4, 3f, false, false),
			"A stale handoff must not revive after a newer phone is closed.");
	}
}
