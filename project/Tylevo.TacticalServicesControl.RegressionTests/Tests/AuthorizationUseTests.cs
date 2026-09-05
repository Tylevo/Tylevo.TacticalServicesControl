using SamSWAT.FireSupport.ArysReloaded.Unity;

internal static class AuthorizationUseTests
{
	private readonly record struct FinalizationAttempt(
		FireSupportAuthorizationUse.FinalizationIntent Intent,
		bool Selected,
		bool Owns,
		Task<bool> Completion);

	[RegressionTest]
	private static async Task CommitWinsAndAllDuplicateCallersShareItsCompletion()
	{
		var use = new FireSupportAuthorizationUse
		{
			Ok = true,
			ConsumedAuthorization = true,
			ConsumedAuthorizationType = ESupportType.Strafe,
			RequestId = "commit-first"
		};

		AssertEx.True(
			use.TrySelectFinalization(
				FireSupportAuthorizationUse.FinalizationIntent.Commit,
				out bool ownsCommit,
				out Task<bool> firstCompletion));
		AssertEx.True(ownsCommit);

		AssertEx.True(
			use.TrySelectFinalization(
				FireSupportAuthorizationUse.FinalizationIntent.Commit,
				out bool ownsDuplicate,
				out Task<bool> duplicateCompletion));
		AssertEx.False(ownsDuplicate);
		AssertEx.True(ReferenceEquals(firstCompletion, duplicateCompletion));

		AssertEx.False(
			use.TrySelectFinalization(
				FireSupportAuthorizationUse.FinalizationIntent.Refund,
				out bool ownsRefund,
				out Task<bool> conflictingCompletion));
		AssertEx.False(ownsRefund);
		AssertEx.True(ReferenceEquals(firstCompletion, conflictingCompletion));

		use.CompleteFinalization(success: true);
		AssertEx.True(await firstCompletion);
		AssertEx.True(await duplicateCompletion);
		AssertEx.True(use.IsCommitted);
		AssertEx.False(use.IsRefunded);
	}

	[RegressionTest]
	private static async Task RefundWinsAndCannotBeReplacedByCommit()
	{
		var use = new FireSupportAuthorizationUse
		{
			Ok = true,
			ConsumedAuthorization = true,
			ConsumedAuthorizationType = ESupportType.Uav,
			RequestId = "refund-first"
		};

		AssertEx.True(
			use.TrySelectFinalization(
				FireSupportAuthorizationUse.FinalizationIntent.Refund,
				out bool ownsRefund,
				out Task<bool> completion));
		AssertEx.True(ownsRefund);
		AssertEx.False(
			use.TrySelectFinalization(
				FireSupportAuthorizationUse.FinalizationIntent.Commit,
				out bool ownsCommit,
				out _));
		AssertEx.False(ownsCommit);

		use.CompleteFinalization(success: false);
		AssertEx.False(await completion);
		AssertEx.True(use.IsRefunded);
		AssertEx.False(use.IsCommitted);
	}

	[RegressionTest]
	private static async Task ConcurrentCommitAndRefundChooseOneSharedCompletion()
	{
		var use = new FireSupportAuthorizationUse
		{
			Ok = true,
			ConsumedAuthorization = true,
			ConsumedAuthorizationType = ESupportType.PriorityExfil,
			RequestId = "concurrent-finalization"
		};

		FinalizationAttempt[] attempts = await ConcurrentTest.RunTogether(
			() => Select(
				use,
				FireSupportAuthorizationUse.FinalizationIntent.Commit),
			() => Select(
				use,
				FireSupportAuthorizationUse.FinalizationIntent.Refund));
		FinalizationAttempt[] owners =
			attempts.Where(attempt => attempt.Owns).ToArray();
		FinalizationAttempt[] selected =
			attempts.Where(attempt => attempt.Selected).ToArray();

		AssertEx.Equal(1, owners.Length);
		AssertEx.Equal(1, selected.Length);
		AssertEx.Equal(owners[0].Intent, selected[0].Intent);
		AssertEx.True(ReferenceEquals(
			attempts[0].Completion,
			attempts[1].Completion));
		AssertEx.True(use.IsCommitted ^ use.IsRefunded);
		AssertEx.Equal(
			owners[0].Intent ==
			FireSupportAuthorizationUse.FinalizationIntent.Commit,
			use.IsCommitted);

		AssertEx.True(
			use.TrySelectFinalization(
				owners[0].Intent,
				out bool duplicateOwns,
				out Task<bool> duplicateCompletion));
		AssertEx.False(duplicateOwns);
		AssertEx.True(ReferenceEquals(
			owners[0].Completion,
			duplicateCompletion));

		use.CompleteFinalization(success: true);
		bool[] completions = await Task.WhenAll(
			attempts[0].Completion,
			attempts[1].Completion,
			duplicateCompletion);
		AssertEx.True(completions.All(completed => completed));
	}

	private static FinalizationAttempt Select(
		FireSupportAuthorizationUse use,
		FireSupportAuthorizationUse.FinalizationIntent intent)
	{
		bool selected = use.TrySelectFinalization(
			intent,
			out bool owns,
			out Task<bool> completion);
		return new FinalizationAttempt(
			intent,
			selected,
			owns,
			completion);
	}
}
