using SamSWAT.FireSupport.ArysReloaded.Unity;

internal static class PhonePointerGestureTests
{
	[RegressionTest]
	private static void AClickRequiresAPressAndCanOnlyBeReleasedOnce()
	{
		var gesture = new PhonePointerGesture();
		AssertEx.False(gesture.EndPress(0, 1));
		gesture.BeginPress(0, 1);
		AssertEx.True(gesture.EndPress(0, 1));
		AssertEx.False(gesture.EndPress(0, 1));
	}

	[RegressionTest]
	private static void PressingOutsideCannotActivateARegionOnRelease()
	{
		var gesture = new PhonePointerGesture();
		gesture.BeginPress(-1, 3);
		AssertEx.False(gesture.EndPress(4, 3));
		gesture.BeginPress(-9, 3);
		AssertEx.False(gesture.EndPress(-9, 3));
		gesture.BeginPress(4, 3);
		gesture.BeginPress(-1, 3);
		AssertEx.False(gesture.EndPress(4, 3),
			"An outside press must also retire any previously armed region.");
	}

	[RegressionTest]
	private static void DraggingOffThePhoneConsumesThePressWithoutAnAction()
	{
		var gesture = new PhonePointerGesture();
		gesture.BeginPress(5, 7);
		AssertEx.False(gesture.EndPress(-1, 7));
		AssertEx.False(gesture.EndPress(5, 7),
			"Moving back over the button after releasing outside must not resurrect a click.");
	}

	[RegressionTest]
	private static void ReleasingOverADifferentActionNeverActivatesEitherAction()
	{
		var gesture = new PhonePointerGesture();
		gesture.BeginPress(1, 4);
		AssertEx.False(gesture.EndPress(2, 4));
		AssertEx.False(gesture.EndPress(1, 4));
		gesture.BeginPress(2, 4);
		AssertEx.True(gesture.EndPress(2, 4));
	}

	[RegressionTest]
	private static void ARebuiltViewCannotReuseTheOldPressedRegion()
	{
		var gesture = new PhonePointerGesture();
		// Region indices can be reused for different actions on the next screen.
		gesture.BeginPress(2, 12);
		AssertEx.False(gesture.EndPress(2, 13));
		AssertEx.False(gesture.EndPress(2, 12));
		gesture.BeginPress(2, 13);
		AssertEx.True(gesture.EndPress(2, 13));
	}

	[RegressionTest]
	private static void CancellationPreventsInputLeakingAcrossInventoryOrControllerChanges()
	{
		var gesture = new PhonePointerGesture();
		gesture.BeginPress(6, 18);
		gesture.Cancel();
		gesture.Cancel();
		AssertEx.False(gesture.EndPress(6, 18));
		gesture.BeginPress(6, 19);
		AssertEx.True(gesture.EndPress(6, 19));
	}

	[RegressionTest]
	private static void ANewPressReplacesTheEarlierPress()
	{
		var gesture = new PhonePointerGesture();
		gesture.BeginPress(3, 20);
		gesture.BeginPress(4, 20);
		AssertEx.True(gesture.EndPress(4, 20));
		AssertEx.False(gesture.EndPress(3, 20));
	}
}
