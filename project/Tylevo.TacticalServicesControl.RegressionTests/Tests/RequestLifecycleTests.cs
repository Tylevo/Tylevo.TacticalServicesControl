using SamSWAT.FireSupport.ArysReloaded.Fika;

internal static class RequestLifecycleTests
{
	private enum Outcome
	{
		Accepted,
		Rejected,
		TimedOut,
		Cancelled,
		Teardown
	}

	private sealed class Pending
	{
		public FirstResult<Outcome> Result { get; } = new();
	}

	[RegressionTest]
	private static void FirstTerminalResultWinsForAcceptRejectAndTimeout()
	{
		var accepted = new FirstResult<Outcome>();
		AssertEx.True(accepted.TrySet(Outcome.Accepted));
		AssertEx.False(accepted.TrySet(Outcome.Rejected));
		AssertEx.False(accepted.TrySet(Outcome.TimedOut));
		AssertEx.True(accepted.TryGet(out Outcome acceptedResult));
		AssertEx.Equal(Outcome.Accepted, acceptedResult);

		var rejected = new FirstResult<Outcome>();
		AssertEx.True(rejected.TrySet(Outcome.Rejected));
		AssertEx.False(rejected.TrySet(Outcome.Accepted));
		AssertEx.True(rejected.TryGet(out Outcome rejectedResult));
		AssertEx.Equal(Outcome.Rejected, rejectedResult);

		var timedOut = new FirstResult<Outcome>();
		AssertEx.True(timedOut.TrySet(Outcome.TimedOut));
		AssertEx.False(timedOut.TrySet(Outcome.Accepted));
		AssertEx.True(timedOut.TryGet(out Outcome timeoutResult));
		AssertEx.Equal(Outcome.TimedOut, timeoutResult);
	}

	[RegressionTest]
	private static async Task FirstResultChoosesExactlyOneConcurrentTerminalOutcome()
	{
		var first = new FirstResult<Outcome>();
		Outcome[] outcomes =
		[
			Outcome.Accepted,
			Outcome.Rejected,
			Outcome.TimedOut,
			Outcome.Cancelled,
			Outcome.Teardown
		];
		Func<(Outcome Outcome, bool Won)>[] contenders = outcomes
			.Select(outcome => new Func<(Outcome Outcome, bool Won)>(
				() => (outcome, first.TrySet(outcome))))
			.ToArray();

		(Outcome Outcome, bool Won)[] attempts =
			await ConcurrentTest.RunTogether(contenders);
		(Outcome Outcome, bool Won)[] winners =
			attempts.Where(attempt => attempt.Won).ToArray();

		AssertEx.Equal(1, winners.Length);
		AssertEx.True(first.IsCompleted);
		AssertEx.True(first.TryGet(out Outcome stored));
		AssertEx.Equal(winners[0].Outcome, stored);
		AssertEx.False(first.TrySet(Outcome.Accepted));
	}

	[RegressionTest]
	private static void PendingTableDedupesMatchingIdsAndRejectsMismatchAndCapacity()
	{
		var table = new PendingRequestTable<string, Pending>();
		int factoryCalls = 0;

		PendingRequestRegistration first = table.GetOrAdd(
			"request-1",
			"fingerprint-a",
			capacity: 1,
			_ =>
			{
				factoryCalls++;
				return new Pending();
			},
			out Pending firstEntry);
		AssertEx.Equal(PendingRequestRegistration.Created, first);

		PendingRequestRegistration duplicate = table.GetOrAdd(
			"request-1",
			"fingerprint-a",
			capacity: 1,
			_ =>
			{
				factoryCalls++;
				return new Pending();
			},
			out Pending duplicateEntry);
		AssertEx.Equal(PendingRequestRegistration.Existing, duplicate);
		AssertEx.True(ReferenceEquals(firstEntry, duplicateEntry));
		AssertEx.Equal(1, factoryCalls);

		PendingRequestRegistration mismatch = table.GetOrAdd(
			"request-1",
			"fingerprint-b",
			capacity: 1,
			_ => new Pending(),
			out _);
		AssertEx.Equal(PendingRequestRegistration.PayloadMismatch, mismatch);

		PendingRequestRegistration capacity = table.GetOrAdd(
			"request-2",
			"fingerprint-c",
			capacity: 1,
			_ => new Pending(),
			out _);
		AssertEx.Equal(PendingRequestRegistration.CapacityReached, capacity);
		AssertEx.Equal(1, table.Count);

		AssertEx.False(table.RemoveIfSame("request-1", new Pending()));
		AssertEx.True(table.RemoveIfSame("request-1", firstEntry));
		AssertEx.Equal(0, table.Count);
	}

	[RegressionTest]
	private static void AcceptedEventRegistryPlaysOnlyTheFirstMatchingPacket()
	{
		var registry = new AcceptedEventRegistry<string>();

		AssertEx.Equal(
			AcceptedEventRegistration.First,
			registry.Register("request-1", "payload-a"));
		AssertEx.Equal(
			AcceptedEventRegistration.Duplicate,
			registry.Register("request-1", "payload-a"));
		AssertEx.Equal(
			AcceptedEventRegistration.PayloadMismatch,
			registry.Register("request-1", "payload-b"));
		AssertEx.True(
			registry.TryGetValue("request-1", out string fingerprint));
		AssertEx.Equal("payload-a", fingerprint);

		registry.Clear();
		AssertEx.False(registry.TryGetValue("request-1", out _));
		AssertEx.Equal(
			AcceptedEventRegistration.First,
			registry.Register("request-1", "payload-b"));
	}

	[RegressionTest]
	private static void CancellationWinsBeforeExecutionButNotAfterStart()
	{
		var cancelled = new AuthorityExecutionTransition<Outcome>();
		AssertEx.True(
			cancelled.TryCancelBeforeExecution(Outcome.Cancelled));
		AssertEx.False(cancelled.TryBeginExecution());
		AssertEx.False(cancelled.TryComplete(Outcome.Accepted));
		AssertEx.True(cancelled.TryGetResult(out Outcome cancelResult));
		AssertEx.Equal(Outcome.Cancelled, cancelResult);

		var executing = new AuthorityExecutionTransition<Outcome>();
		AssertEx.True(executing.TryBeginExecution());
		AssertEx.True(executing.ExecutionStarted);
		AssertEx.False(
			executing.TryCancelBeforeExecution(Outcome.Cancelled));
		AssertEx.True(executing.TryComplete(Outcome.Accepted));
		AssertEx.False(executing.TryComplete(Outcome.Rejected));
		AssertEx.True(executing.ExecutionStarted);
		AssertEx.True(executing.TryGetResult(out Outcome acceptedResult));
		AssertEx.Equal(Outcome.Accepted, acceptedResult);
	}

	[RegressionTest]
	private static async Task CancellationAndExecutionStartSelectOneValidPath()
	{
		var transition = new AuthorityExecutionTransition<Outcome>();
		bool[] attempts = await ConcurrentTest.RunTogether(
			() => transition.TryBeginExecution(),
			() => transition.TryCancelBeforeExecution(Outcome.Cancelled));

		AssertEx.Equal(1, attempts.Count(succeeded => succeeded));
		if (attempts[0])
		{
			AssertEx.False(attempts[1]);
			AssertEx.Equal(
				AuthorityExecutionPhase.ExecutionStarted,
				transition.Phase);
			AssertEx.True(transition.ExecutionStarted);
			AssertEx.False(
				transition.TryCancelBeforeExecution(Outcome.Cancelled));
			AssertEx.True(transition.TryComplete(Outcome.Accepted));
			AssertEx.True(transition.ExecutionStarted);
		}
		else
		{
			AssertEx.True(attempts[1]);
			AssertEx.False(transition.ExecutionStarted);
			AssertEx.False(transition.TryBeginExecution());
			AssertEx.False(transition.TryComplete(Outcome.Accepted));
		}

		AssertEx.True(transition.TryGetResult(out Outcome result));
		AssertEx.Equal(
			attempts[0] ? Outcome.Accepted : Outcome.Cancelled,
			result);
		AssertEx.Equal(AuthorityExecutionPhase.Completed, transition.Phase);
	}

	[RegressionTest]
	private static async Task ConcurrentCompletionsPublishExactlyOneTerminalResult()
	{
		var transition = new AuthorityExecutionTransition<Outcome>();
		AssertEx.True(transition.TryBeginExecution());
		Outcome[] outcomes =
		[
			Outcome.Accepted,
			Outcome.Rejected,
			Outcome.TimedOut,
			Outcome.Teardown
		];
		Func<(Outcome Outcome, bool Won)>[] contenders = outcomes
			.Select(outcome => new Func<(Outcome Outcome, bool Won)>(
				() => (outcome, transition.TryComplete(outcome))))
			.ToArray();

		(Outcome Outcome, bool Won)[] attempts =
			await ConcurrentTest.RunTogether(contenders);
		(Outcome Outcome, bool Won)[] winners =
			attempts.Where(attempt => attempt.Won).ToArray();

		AssertEx.Equal(1, winners.Length);
		AssertEx.True(transition.TryGetResult(out Outcome stored));
		AssertEx.Equal(winners[0].Outcome, stored);
		AssertEx.Equal(AuthorityExecutionPhase.Completed, transition.Phase);
		AssertEx.True(transition.ExecutionStarted);
		AssertEx.False(transition.TryComplete(Outcome.Cancelled));
		AssertEx.True(transition.TryGetResult(out Outcome preserved));
		AssertEx.Equal(stored, preserved);
	}

	[RegressionTest]
	private static void TeardownClearsPendingTableAndCompletesEveryWaiterOnce()
	{
		var table = new PendingRequestTable<string, Pending>();
		table.GetOrAdd(
			"one",
			"one",
			capacity: 8,
			_ => new Pending(),
			out Pending one);
		table.GetOrAdd(
			"two",
			"two",
			capacity: 8,
			_ => new Pending(),
			out Pending two);
		one.Result.TrySet(Outcome.Accepted);

		List<Pending> cleared = table.ClearAndGetValues();
		foreach (Pending pending in cleared)
		{
			pending.Result.TrySet(Outcome.Teardown);
		}

		AssertEx.Equal(0, table.Count);
		AssertEx.Equal(2, cleared.Count);
		AssertEx.True(one.Result.TryGet(out Outcome oneResult));
		AssertEx.Equal(Outcome.Accepted, oneResult);
		AssertEx.True(two.Result.TryGet(out Outcome twoResult));
		AssertEx.Equal(Outcome.Teardown, twoResult);
	}

	[RegressionTest]
	private static void AuthorityAbandonCompletesPendingButPreservesTerminalOutcome()
	{
		var pending = new AuthorityExecutionTransition<Outcome>();
		AssertEx.True(
			pending.Abandon(Outcome.Teardown, out bool completedPending));
		AssertEx.True(completedPending);
		AssertEx.True(pending.IsAbandoned);
		AssertEx.True(pending.TryGetResult(out Outcome teardownResult));
		AssertEx.Equal(Outcome.Teardown, teardownResult);
		AssertEx.False(
			pending.Abandon(Outcome.Cancelled, out bool completedTwice));
		AssertEx.False(completedTwice);

		var terminal = new AuthorityExecutionTransition<Outcome>();
		AssertEx.True(terminal.TryBeginExecution());
		AssertEx.True(terminal.TryComplete(Outcome.Rejected));
		AssertEx.True(terminal.ExecutionStarted);
		AssertEx.True(
			terminal.Abandon(Outcome.Teardown, out bool completedTerminal));
		AssertEx.False(completedTerminal);
		AssertEx.True(terminal.ExecutionStarted);
		AssertEx.True(terminal.TryGetResult(out Outcome preserved));
		AssertEx.Equal(Outcome.Rejected, preserved);
	}
}
