using SamSWAT.FireSupport.ArysReloaded.Unity;

internal static class PhonePurchaseDeploymentTransitionTests
{
	[RegressionTest]
	private static void DisabledAndNonPurchasePhoneSessionsKeepTheManualFlow()
	{
		foreach ((bool enabled, bool purchasePhone) in new[] { (false, true), (true, false), (false, false) })
		{
			var transition = new PhonePurchaseDeploymentTransition();
			transition.Begin(enabled, purchasePhone, ESupportType.Strafe);
			AssertEx.False(transition.RecordPurchaseResult(true, Granted(ESupportType.Strafe)));
			AssertEx.False(transition.TryComplete(true, true, true, true, out ESupportType type));
			AssertEx.Equal(ESupportType.None, type);
		}
	}

	[RegressionTest]
	private static void ConfirmedPurchasesKeepTheExactServiceAndHandoffOnlyOnce()
	{
		foreach (ESupportType service in new[]
		         {
			         ESupportType.Strafe, ESupportType.DoubleStrafe, ESupportType.Extract,
			         ESupportType.PriorityExfil, ESupportType.Uav, ESupportType.FocusedSweep
		         })
		{
			var transition = Begin(service);
			FireSupportPurchaseResponse receipt = Granted(service);
			AssertEx.True(transition.RecordPurchaseResult(true, receipt));
			AssertEx.Equal(service, transition.PendingSupport);
			AssertEx.True(transition.TryComplete(true, true, true, true, out ESupportType type));
			AssertEx.Equal(service, type);
			AssertEx.False(receipt.AuthorizationConsumed,
				"The handoff must leave spending/credit consumption to the normal deployment flow.");
			AssertEx.Equal(ESupportType.None, transition.PendingSupport);
			transition.Begin(true, true, ESupportType.Uav);
			AssertEx.False(transition.RecordPurchaseResult(true, receipt));
			AssertEx.False(transition.TryComplete(true, true, true, true, out _),
				"A repeated completion or callback cannot dispatch another request or start a purchase loop.");
		}
	}

	[RegressionTest]
	private static void FailedMissingAndAmbiguousResultsNeverArmDeployment()
	{
		FireSupportPurchaseResponse?[] results =
		{
			null,
			new() { Ok = false, AuthorizationGranted = true, SupportType = "Strafe" },
			new() { Ok = true, AuthorizationGranted = false, SupportType = "Strafe" },
			new() { Ok = true, AuthorizationGranted = true, AuthorizationConsumed = true, SupportType = "Strafe" },
			new() { Ok = true, AuthorizationGranted = true, SupportType = "" },
			new() { Ok = true, AuthorizationGranted = true, SupportType = "2" },
			Granted(ESupportType.DoubleStrafe),
			Granted(ESupportType.Uav)
		};
		foreach (FireSupportPurchaseResponse? result in results)
		{
			var transition = Begin(ESupportType.Strafe);
			AssertEx.False(transition.RecordPurchaseResult(true, result!));
			AssertEx.False(transition.RecordPurchaseResult(true, Granted(ESupportType.Strafe)),
				"A duplicate result must not turn an ambiguous purchase into an automatic deployment.");
			AssertEx.False(transition.TryComplete(true, true, true, true, out _));
		}

		var denied = Begin(ESupportType.Strafe);
		AssertEx.False(denied.RecordPurchaseResult(false, Granted(ESupportType.Strafe)));
		AssertEx.False(denied.TryComplete(true, true, true, true, out _));
	}

	[RegressionTest]
	private static void CancelledAndFinishedSessionsIgnoreLatePurchaseCallbacks()
	{
		var cancelledBeforeResult = Begin(ESupportType.Extract);
		cancelledBeforeResult.Cancel();
		cancelledBeforeResult.Cancel();
		AssertEx.False(cancelledBeforeResult.RecordPurchaseResult(true, Granted(ESupportType.Extract)));
		AssertEx.False(cancelledBeforeResult.TryComplete(true, true, true, true, out _));

		var cancelledAfterResult = Begin(ESupportType.Extract);
		AssertEx.True(cancelledAfterResult.RecordPurchaseResult(true, Granted(ESupportType.Extract)));
		cancelledAfterResult.Cancel();
		AssertEx.Equal(ESupportType.None, cancelledAfterResult.PendingSupport);
		AssertEx.False(cancelledAfterResult.TryComplete(true, true, true, true, out _));

		var finishedBeforeResult = Begin(ESupportType.Extract);
		AssertEx.False(finishedBeforeResult.TryComplete(false, true, true, true, out _));
		AssertEx.False(finishedBeforeResult.RecordPurchaseResult(true, Granted(ESupportType.Extract)));
		finishedBeforeResult.Begin(true, true, ESupportType.Extract);
		AssertEx.False(finishedBeforeResult.TryComplete(true, true, true, true, out _));
	}

	[RegressionTest]
	private static void FailedSessionOrChangedDeploymentGatesDoNotRetryOrSpend()
	{
		foreach ((bool success, bool prepaid, bool available, bool ready) in new[]
		         {
			         (false, true, true, true), (true, false, true, true),
			         (true, true, false, true), (true, true, true, false)
		         })
		{
			var transition = Begin(ESupportType.FocusedSweep);
			FireSupportPurchaseResponse receipt = Granted(ESupportType.FocusedSweep);
			AssertEx.True(transition.RecordPurchaseResult(true, receipt));
			AssertEx.False(transition.TryComplete(success, prepaid, available, ready, out ESupportType type));
			AssertEx.Equal(ESupportType.None, type);
			AssertEx.False(transition.TryComplete(true, true, true, true, out _),
				"Recovering readiness later must require a manual deploy, not an unexpected delayed request.");
			AssertEx.True(receipt.AuthorizationGranted);
			AssertEx.False(receipt.AuthorizationConsumed);
		}
	}

	[RegressionTest]
	private static void DirectPaymentAndUnknownServicesCannotUsePurchaseAutoDeployment()
	{
		AssertEx.True(PhonePurchaseDeploymentTransition.UsesPrepaidAuthorization(PaymentMode.PhoneAuthorizations));
		AssertEx.True(PhonePurchaseDeploymentTransition.UsesPrepaidAuthorization(PaymentMode.Hybrid));
		AssertEx.False(PhonePurchaseDeploymentTransition.UsesPrepaidAuthorization(PaymentMode.DirectRadial),
			"Direct deployment ignores prepaid authorizations and could charge again.");
		AssertEx.False(PhonePurchaseDeploymentTransition.UsesPrepaidAuthorization((PaymentMode)999));
		foreach (ESupportType service in new[] { ESupportType.None, (ESupportType)999 })
		{
			var transition = Begin(service);
			AssertEx.False(transition.RecordPurchaseResult(true, Granted(service)));
			AssertEx.False(transition.TryComplete(true, true, true, true, out _));
		}
	}

	private static PhonePurchaseDeploymentTransition Begin(ESupportType service)
	{
		var transition = new PhonePurchaseDeploymentTransition();
		transition.Begin(true, true, service);
		return transition;
	}

	private static FireSupportPurchaseResponse Granted(ESupportType service) => new()
	{
		Ok = true,
		AuthorizationGranted = true,
		SupportType = service.ToString()
	};
}
