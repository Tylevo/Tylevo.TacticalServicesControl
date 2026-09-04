using SamSWAT.FireSupport.ArysReloaded.Integration;

internal static class DangerCloseWarningRegistryTests
{
	private const string OpportunityId = "seasonal:raid-1:opportunity-1";
	private const string SourceId = "Tylevo.SeasonalModifiers";

	[RegressionTest]
	private static void AuthorityLifecycleIsOwnedAndIdempotent()
	{
		var registry = new DangerCloseWarningRegistry();

		AssertEx.True(registry.TryRegisterAuthority(
			DangerCloseWarningKind.Advance,
			OpportunityId,
			90,
			SourceId,
			out bool publishAdvance,
			out string advanceReason));
		AssertEx.True(publishAdvance);
		AssertEx.Equal("Published", advanceReason);

		AssertEx.True(registry.TryRegisterAuthority(
			DangerCloseWarningKind.Advance,
			OpportunityId,
			90,
			SourceId,
			out bool publishDuplicate,
			out string duplicateReason));
		AssertEx.False(publishDuplicate);
		AssertEx.Equal("AlreadyPublished", duplicateReason);

		AssertEx.False(registry.TryRegisterAuthority(
			DangerCloseWarningKind.Cancel,
			OpportunityId,
			0,
			"another.source",
			out _,
			out string foreignReason));
		AssertEx.Equal("OpportunityOwnedByAnotherSource", foreignReason);

		AssertEx.True(registry.TryRegisterAuthority(
			DangerCloseWarningKind.Cancel,
			OpportunityId,
			0,
			SourceId,
			out bool publishCancel,
			out string cancelReason));
		AssertEx.True(publishCancel);
		AssertEx.Equal("Published", cancelReason);

		AssertEx.True(registry.TryRegisterAuthority(
			DangerCloseWarningKind.Cancel,
			OpportunityId,
			0,
			SourceId,
			out bool publishDuplicateCancel,
			out string duplicateCancelReason));
		AssertEx.False(publishDuplicateCancel);
		AssertEx.Equal("AlreadyCancelled", duplicateCancelReason);

		// An accepted physical pass wins a late cancellation race and always
		// publishes the final safety alert once.
		AssertEx.True(registry.TryRegisterAuthority(
			DangerCloseWarningKind.Inbound,
			OpportunityId,
			0,
			SourceId,
			out bool publishInbound,
			out string inboundReason));
		AssertEx.True(publishInbound);
		AssertEx.Equal("Published", inboundReason);
		AssertEx.True(registry.TryRegisterAuthority(
			DangerCloseWarningKind.Inbound,
			OpportunityId,
			0,
			SourceId,
			out bool publishDuplicateInbound,
			out string duplicateInboundReason));
		AssertEx.False(publishDuplicateInbound);
		AssertEx.Equal("AlreadyInbound", duplicateInboundReason);
	}

	[RegressionTest]
	private static void UnknownCancellationIsAnIdempotentPublishedTombstone()
	{
		var registry = new DangerCloseWarningRegistry();

		AssertEx.True(registry.TryRegisterAuthority(
			DangerCloseWarningKind.Cancel,
			OpportunityId,
			0,
			SourceId,
			out bool firstPublish,
			out string firstReason));
		AssertEx.True(firstPublish);
		AssertEx.Equal("Published", firstReason);

		AssertEx.True(registry.TryRegisterAuthority(
			DangerCloseWarningKind.Cancel,
			OpportunityId,
			0,
			SourceId,
			out bool secondPublish,
			out string secondReason));
		AssertEx.False(secondPublish);
		AssertEx.Equal("AlreadyCancelled", secondReason);
	}

	[RegressionTest]
	private static void ReceiverShowsCancelOnlyAfterItsAdvanceWasPresented()
	{
		var withoutPresentation = new DangerCloseWarningRegistry();
		AssertEx.True(withoutPresentation.TryRegisterReceived(
			DangerCloseWarningKind.Advance,
			OpportunityId,
			90,
			out bool advanceEligible,
			out _));
		AssertEx.True(advanceEligible);
		AssertEx.True(withoutPresentation.TryRegisterReceived(
			DangerCloseWarningKind.Advance,
			OpportunityId,
			90,
			out bool duplicateAdvanceEligible,
			out string duplicateAdvanceReason));
		AssertEx.False(duplicateAdvanceEligible);
		AssertEx.Equal("Duplicate", duplicateAdvanceReason);
		AssertEx.True(withoutPresentation.TryRegisterReceived(
			DangerCloseWarningKind.Cancel,
			OpportunityId,
			0,
			out bool cancelWithoutAdvance,
			out _));
		AssertEx.False(cancelWithoutAdvance);

		var withPresentation = new DangerCloseWarningRegistry();
		AssertEx.True(withPresentation.TryRegisterReceived(
			DangerCloseWarningKind.Advance,
			OpportunityId,
			90,
			out _,
			out _));
		withPresentation.MarkAdvancePresented(OpportunityId);
		AssertEx.True(withPresentation.TryRegisterReceived(
			DangerCloseWarningKind.Cancel,
			OpportunityId,
			0,
			out bool cancelAfterAdvance,
			out _));
		AssertEx.True(cancelAfterAdvance);
	}

	[RegressionTest]
	private static void ReceiverAlwaysShowsInboundExactlyOnce()
	{
		var registry = new DangerCloseWarningRegistry();
		AssertEx.True(registry.TryRegisterReceived(
			DangerCloseWarningKind.Cancel,
			OpportunityId,
			0,
			out bool showUnknownCancel,
			out _));
		AssertEx.False(showUnknownCancel);

		AssertEx.True(registry.TryRegisterReceived(
			DangerCloseWarningKind.Inbound,
			OpportunityId,
			0,
			out bool showInbound,
			out _));
		AssertEx.True(showInbound);

		AssertEx.True(registry.TryRegisterReceived(
			DangerCloseWarningKind.Inbound,
			OpportunityId,
			0,
			out bool showDuplicateInbound,
			out string duplicateReason));
		AssertEx.False(showDuplicateInbound);
		AssertEx.Equal("Duplicate", duplicateReason);

		AssertEx.True(registry.TryRegisterReceived(
			DangerCloseWarningKind.Cancel,
			OpportunityId,
			0,
			out bool showLateCancel,
			out string lateCancelReason));
		AssertEx.False(showLateCancel);
		AssertEx.Equal("AlreadyInbound", lateCancelReason);
	}

	[RegressionTest]
	private static void InvalidPayloadsFailClosed()
	{
		var registry = new DangerCloseWarningRegistry();
		AssertEx.False(registry.TryRegisterAuthority(
			(DangerCloseWarningKind)99,
			OpportunityId,
			0,
			SourceId,
			out _,
			out string kindReason));
		AssertEx.Equal("InvalidWarningKind", kindReason);

		AssertEx.False(registry.TryRegisterAuthority(
			DangerCloseWarningKind.Advance,
			"bad id",
			90,
			SourceId,
			out _,
			out string idReason));
		AssertEx.Equal("InvalidOpportunityId", idReason);

		AssertEx.False(registry.TryRegisterAuthority(
			DangerCloseWarningKind.Advance,
			OpportunityId,
			0,
			SourceId,
			out _,
			out string secondsReason));
		AssertEx.Equal("InvalidSecondsRemaining", secondsReason);
	}
}
