using SamSWAT.FireSupport.ArysReloaded.Unity;

internal static class AuthorizationSnapshotPresenceTests
{
	[RegressionTest]
	private static void OmittedEmptySnapshotPreservesExistingCredits()
	{
		var existing = new Dictionary<string, int>
		{
			["A10"] = 2
		};
		var omitted = new Dictionary<string, int>();

		if (AuthorizationSnapshotPresence.ShouldApply(
			    authorizationsIncluded: false,
			    omitted))
		{
			existing = omitted;
		}

		AssertEx.Equal(2, existing["A10"]);
	}

	[RegressionTest]
	private static void ExplicitEmptySnapshotClearsExistingCredits()
	{
		var existing = new Dictionary<string, int>
		{
			["A10"] = 2
		};
		var authoritativeEmpty = new Dictionary<string, int>();

		if (AuthorizationSnapshotPresence.ShouldApply(
			    authorizationsIncluded: true,
			    authoritativeEmpty))
		{
			existing = authoritativeEmpty;
		}

		AssertEx.Equal(0, existing.Count);
	}

	[RegressionTest]
	private static void LegacyNonEmptySnapshotRemainsCompatible()
	{
		var legacy = new Dictionary<string, int>
		{
			["Extraction"] = 1
		};

		AssertEx.True(
			AuthorizationSnapshotPresence.ShouldApply(
				authorizationsIncluded: false,
				legacy));
	}

	[RegressionTest]
	private static void IncludedNonEmptySnapshotIsAuthoritativeEvenOnDenial()
	{
		var response = new FireSupportPurchaseResponse
		{
			Ok = false,
			Reason = "AuthorizationLimitReached",
			AuthorizationsIncluded = true,
			Authorizations = new Dictionary<string, int>
			{
				["Uav"] = 2
			}
		};

		AssertEx.False(response.Ok);
		AssertEx.True(
			AuthorizationSnapshotPresence.ShouldApply(
				response.AuthorizationsIncluded,
				response.Authorizations));
	}

	[RegressionTest]
	private static void NullSnapshotIsNeverPresent()
	{
		AssertEx.False(
			AuthorizationSnapshotPresence.ShouldApply(
				authorizationsIncluded: true,
				null!));
	}
}
