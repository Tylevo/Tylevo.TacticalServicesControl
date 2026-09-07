using SamSWAT.FireSupport.ArysReloaded.Unity;

internal static class ClientProgressionTests
{
	private static readonly string Permit = new('a', 64);

	[RegressionTest]
	private static void PermissionCannotCrossSessionsOrProfiles()
	{
		var state = new FireSupportProgressionState();
		state.Apply("session-one|pmc-one", true, true, Permit);
		AssertEx.True(state.IsUnlocked("session-one|pmc-one"));
		AssertEx.Equal(Permit, state.GetPermit("session-one|pmc-one"));
		AssertEx.False(state.IsUnlocked("session-two|pmc-one"));
		AssertEx.False(state.IsUnlocked("session-one|pmc-two"));
		AssertEx.Equal(string.Empty, state.GetPermit("session-two|pmc-two"));
		AssertEx.False(state.IsUnlocked(string.Empty));
	}

	[RegressionTest]
	private static void MissingOrLockedSnapshotsRevokePreviousPermission()
	{
		var state = new FireSupportProgressionState();
		foreach ((bool included, bool? unlocked, string permit) in new[]
		{
			(false, (bool?)true, Permit),
			(true, (bool?)null, Permit),
			(true, (bool?)false, Permit),
			(true, (bool?)true, string.Empty),
			(true, (bool?)true, new string('A', 64)),
			(true, (bool?)true, new string('g', 64)),
			(true, (bool?)true, new string('a', 63))
		})
		{
			state.Apply("session|profile", true, true, Permit);
			state.Apply("session|profile", included, unlocked, permit);
			AssertEx.False(state.IsUnlocked("session|profile"));
			AssertEx.Equal(string.Empty, state.GetPermit("session|profile"));
		}
	}

	[RegressionTest]
	private static void ClearAndRefreshReplaceTheCapability()
	{
		var state = new FireSupportProgressionState();
		state.Apply("session|profile", true, true, Permit);
		state.Clear();
		AssertEx.False(state.IsUnlocked("session|profile"));
		string refreshed = new('b', 64);
		state.Apply("session|profile", true, true, refreshed);
		AssertEx.Equal(refreshed, state.GetPermit("session|profile"));
	}
}
