using SamSWAT.FireSupport.ArysReloaded.Integration;

internal static class DangerCloseIncomingCallStateTests
{
	private const string OpportunityId = "seasonal:raid-1:opportunity-1";
	private const string OtherOpportunityId = "seasonal:raid-1:opportunity-2";

	[RegressionTest]
	private static void AdvanceStartsOneBoundedCallAndDuplicateDoesNotExtendIt()
	{
		var state = new DangerCloseIncomingCallState();

		AssertEx.True(state.TryBeginAdvance(OpportunityId, 90, 100d, 15d));
		AssertEx.Equal(DangerCloseIncomingCallPhase.Ringing, state.Phase);
		AssertEx.False(state.TryBeginAdvance(OpportunityId, 90, 110d, 15d));
		AssertEx.Equal(
			DangerCloseIncomingCallTickResult.None,
			state.Tick(114.999d));
		AssertEx.Equal(
			DangerCloseIncomingCallTickResult.RingTimedOut,
			state.Tick(115d));
		AssertEx.Equal(DangerCloseIncomingCallPhase.TimedOut, state.Phase);
		AssertEx.False(state.TryBeginAnswer(115d, 8d, out _));
	}

	[RegressionTest]
	private static void ShortAdvanceDeadlineEndsTheRingBeforeItsConfiguredWindow()
	{
		var state = new DangerCloseIncomingCallState();

		AssertEx.True(state.TryBeginAdvance(OpportunityId, 5, 10d, 15d));
		AssertEx.Equal(
			DangerCloseIncomingCallTickResult.None,
			state.Tick(14.999d));
		AssertEx.Equal(
			DangerCloseIncomingCallTickResult.AdvanceExpired,
			state.Tick(15d));
		AssertEx.Equal(DangerCloseIncomingCallPhase.Completed, state.Phase);
		AssertEx.False(state.TryBeginAnswer(15d, 8d, out _));
	}

	[RegressionTest]
	private static void ExpiredCallCannotBlockANewOpportunityBeforeTheNextTick()
	{
		var state = new DangerCloseIncomingCallState();
		AssertEx.True(state.TryBeginAdvance(OpportunityId, 90, 0d, 15d));

		AssertEx.True(state.TryBeginAdvance(OtherOpportunityId, 80, 15d, 15d));
		AssertEx.Equal(DangerCloseIncomingCallPhase.Ringing, state.Phase);
		AssertEx.Equal(
			DangerCloseIncomingCallTickResult.None,
			state.Tick(29.999d));
		AssertEx.False(state.TryBeginAdvance(OpportunityId, 90, 16d, 15d));
	}

	[RegressionTest]
	private static void AnsweredPhoneCanBeStowedUntilTheOriginalAdvanceDeadline()
	{
		var state = new DangerCloseIncomingCallState();
		AssertEx.True(state.TryBeginAdvance(OpportunityId, 90, 10d, 15d));

		AssertEx.True(state.TryBeginAnswer(14d, 8d, out int secondsRemaining));
		AssertEx.Equal(86, secondsRemaining);
		AssertEx.Equal(DangerCloseIncomingCallPhase.Answering, state.Phase);
		AssertEx.False(state.TryBeginAnswer(14.1d, 8d, out _));
		AssertEx.True(state.TryMarkAnswerPresented(18d));
		AssertEx.Equal(DangerCloseIncomingCallPhase.Answered, state.Phase);

		// The former five-second display window must not complete the forecast.
		AssertEx.Equal(
			DangerCloseIncomingCallTickResult.None,
			state.Tick(23d));
		state.MarkAnswerStowed();
		AssertEx.Equal(DangerCloseIncomingCallPhase.AnsweredStowed, state.Phase);
		AssertEx.True(state.IsActive);
		AssertEx.False(state.IsAnswerActive);
		AssertEx.Equal(1, state.GetSecondsRemaining(99.999d));
		AssertEx.Equal(
			DangerCloseIncomingCallTickResult.AdvanceExpired,
			state.Tick(100d));
		AssertEx.Equal(DangerCloseIncomingCallPhase.Completed, state.Phase);
	}

	[RegressionTest]
	private static void ReopenCyclesNeverExtendTheAdvanceDeadlineOrEta()
	{
		var state = CreateStowedAnsweredState(secondsRemaining: 60);

		AssertEx.True(state.TryBeginReopen(20d, 8d, out int firstEta));
		AssertEx.Equal(40, firstEta);
		AssertEx.True(state.IsReopening);
		AssertEx.True(state.IsAnswerActive);
		AssertEx.False(state.TryBeginReopen(20.1d, 8d, out _));
		AssertEx.True(state.TryMarkAnswerPresented(22d));
		state.MarkAnswerStowed();

		AssertEx.True(state.TryBeginReopen(50d, 8d, out int secondEta));
		AssertEx.Equal(10, secondEta);
		AssertEx.True(state.TryMarkAnswerPresented(51d));
		state.MarkAnswerStowed();
		AssertEx.Equal(
			DangerCloseIncomingCallTickResult.None,
			state.Tick(59.999d));
		AssertEx.Equal(
			DangerCloseIncomingCallTickResult.AdvanceExpired,
			state.Tick(60d));
	}

	[RegressionTest]
	private static void FailedInitialAnswerCanResumeOnlyInsideTheOriginalRingWindow()
	{
		var state = new DangerCloseIncomingCallState();
		AssertEx.True(state.TryBeginAdvance(OpportunityId, 60, 0d, 15d));
		AssertEx.True(state.TryBeginAnswer(5d, 8d, out _));

		state.ResumeRingingAfterFailedAnswer(6d);
		AssertEx.Equal(DangerCloseIncomingCallPhase.Ringing, state.Phase);
		AssertEx.True(state.TryBeginAnswer(14d, 8d, out int secondsRemaining));
		AssertEx.Equal(46, secondsRemaining);

		state.ResumeRingingAfterFailedAnswer(15d);
		AssertEx.Equal(DangerCloseIncomingCallPhase.TimedOut, state.Phase);
	}

	[RegressionTest]
	private static void InitialAnswerEquipHasItsOwnTerminalDeadline()
	{
		var state = new DangerCloseIncomingCallState();
		AssertEx.True(state.TryBeginAdvance(OpportunityId, 90, 0d, 15d));
		AssertEx.True(state.TryBeginAnswer(1d, 8d, out _));

		AssertEx.Equal(
			DangerCloseIncomingCallTickResult.None,
			state.Tick(8.999d));
		AssertEx.Equal(
			DangerCloseIncomingCallTickResult.AnswerEquipTimedOut,
			state.Tick(9d));
		AssertEx.Equal(DangerCloseIncomingCallPhase.TimedOut, state.Phase);
		AssertEx.False(state.TryMarkAnswerPresented(9d));
	}

	[RegressionTest]
	private static void FailedOrTimedOutReopenReturnsToTheStowedSession()
	{
		var state = CreateStowedAnsweredState(secondsRemaining: 60);

		AssertEx.True(state.TryBeginReopen(10d, 8d, out int firstEta));
		AssertEx.Equal(50, firstEta);
		state.ResumeStowedAfterFailedReopen(11d);
		AssertEx.True(state.IsAnsweredStowed);
		AssertEx.Equal(49, state.GetSecondsRemaining(11d));

		AssertEx.True(state.TryBeginReopen(20d, 8d, out int secondEta));
		AssertEx.Equal(40, secondEta);
		AssertEx.Equal(
			DangerCloseIncomingCallTickResult.None,
			state.Tick(27.999d));
		AssertEx.Equal(
			DangerCloseIncomingCallTickResult.ReopenEquipTimedOut,
			state.Tick(28d));
		AssertEx.True(state.IsAnsweredStowed);
		AssertEx.True(state.TryBeginReopen(29d, 8d, out int retryEta));
		AssertEx.Equal(31, retryEta);
	}

	[RegressionTest]
	private static void CancelInboundAndResetTerminateEveryAnsweredPhase()
	{
		var state = CreateStowedAnsweredState(secondsRemaining: 90);
		AssertEx.False(state.TryCancel(OtherOpportunityId));
		AssertEx.True(state.IsAnsweredStowed);
		AssertEx.True(state.TryCancel(OpportunityId));
		AssertEx.Equal(DangerCloseIncomingCallPhase.Cancelled, state.Phase);
		AssertEx.False(state.TryBeginReopen(5d, 8d, out _));

		AssertEx.True(state.TryBeginAdvance(OtherOpportunityId, 80, 6d, 15d));
		AssertEx.True(state.TryBeginAnswer(7d, 8d, out _));
		AssertEx.True(state.TryMarkAnswerPresented(8d));
		state.MarkAnswerStowed();
		AssertEx.True(state.TryBeginReopen(9d, 8d, out _));
		AssertEx.False(state.TryMarkInbound(OpportunityId));
		AssertEx.True(state.IsReopening);
		AssertEx.True(state.TryMarkInbound(OtherOpportunityId));
		AssertEx.Equal(DangerCloseIncomingCallPhase.Inbound, state.Phase);

		// Late local callbacks must not resurrect a terminal opportunity.
		AssertEx.False(state.TryMarkAnswerPresented(10d));
		state.ResumeStowedAfterFailedReopen(10d);
		state.MarkAnswerStowed();
		AssertEx.Equal(DangerCloseIncomingCallPhase.Inbound, state.Phase);

		state.Reset();
		AssertEx.Equal(DangerCloseIncomingCallPhase.Idle, state.Phase);
		AssertEx.False(state.IsActive);
	}

	[RegressionTest]
	private static void ResetDuringReopenRejectsLatePresentationCallbacks()
	{
		var state = CreateStowedAnsweredState(secondsRemaining: 90);
		AssertEx.True(state.TryBeginReopen(10d, 8d, out _));

		state.Reset();
		AssertEx.False(state.TryMarkAnswerPresented(11d));
		state.ResumeStowedAfterFailedReopen(11d);
		state.MarkAnswerStowed();
		AssertEx.Equal(DangerCloseIncomingCallPhase.Idle, state.Phase);
		AssertEx.Equal(0, state.GetSecondsRemaining(11d));
	}

	[RegressionTest]
	private static void AdvanceExpiryWinsTheReopenAndLateCallbackRace()
	{
		var state = CreateStowedAnsweredState(secondsRemaining: 10);
		AssertEx.True(state.TryBeginReopen(9d, 8d, out int eta));
		AssertEx.Equal(1, eta);

		AssertEx.Equal(
			DangerCloseIncomingCallTickResult.AdvanceExpired,
			state.Tick(10d));
		AssertEx.Equal(DangerCloseIncomingCallPhase.Completed, state.Phase);
		AssertEx.False(state.TryMarkAnswerPresented(10d));
		state.ResumeStowedAfterFailedReopen(10d);
		AssertEx.Equal(DangerCloseIncomingCallPhase.Completed, state.Phase);

		var exactDeadline = CreateStowedAnsweredState(secondsRemaining: 10);
		AssertEx.False(exactDeadline.TryBeginReopen(10d, 8d, out int expiredEta));
		AssertEx.Equal(0, expiredEta);
		AssertEx.Equal(DangerCloseIncomingCallPhase.Completed, exactDeadline.Phase);
	}

	[RegressionTest]
	private static void LateTerminalEventsCannotMutateANewerStowedCall()
	{
		var state = new DangerCloseIncomingCallState();
		AssertEx.True(state.TryBeginAdvance(OpportunityId, 90, 0d, 15d));
		AssertEx.True(state.TryMarkInbound(OpportunityId));
		AssertEx.Equal(DangerCloseIncomingCallPhase.Inbound, state.Phase);
		AssertEx.False(state.TryCancel(OpportunityId));

		AssertEx.True(state.TryBeginAdvance(OtherOpportunityId, 80, 1d, 15d));
		AssertEx.True(state.TryBeginAnswer(2d, 8d, out _));
		AssertEx.True(state.TryMarkAnswerPresented(3d));
		state.MarkAnswerStowed();
		AssertEx.False(state.TryMarkInbound(OpportunityId));
		AssertEx.False(state.TryCancel(OpportunityId));
		AssertEx.True(state.IsAnsweredStowed);
		AssertEx.True(state.TryCancel(OtherOpportunityId));
		AssertEx.Equal(DangerCloseIncomingCallPhase.Cancelled, state.Phase);
	}

	private static DangerCloseIncomingCallState CreateStowedAnsweredState(
		int secondsRemaining)
	{
		var state = new DangerCloseIncomingCallState();
		AssertEx.True(state.TryBeginAdvance(
			OpportunityId,
			secondsRemaining,
			now: 0d,
			ringDurationSeconds: 15d));
		AssertEx.True(state.TryBeginAnswer(1d, 8d, out _));
		AssertEx.True(state.TryMarkAnswerPresented(2d));
		state.MarkAnswerStowed();
		AssertEx.True(state.IsAnsweredStowed);
		return state;
	}
}
