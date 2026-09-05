using SamSWAT.FireSupport.ArysReloaded.Integration;

internal static class DangerCloseLeaseRegistryTests
{
	[RegressionTest]
	private static void IndependentSourcesCannotReleaseEachOther()
	{
		DangerCloseLeaseRegistry.Reset();
		AssertEx.True(DangerCloseLeaseRegistry.TrySet(
			active: true,
			"tylevo.seasonalmodifiers:danger-close",
			out bool firstChanged,
			out string firstReason));
		AssertEx.True(firstChanged);
		AssertEx.Equal("Activated", firstReason);
		AssertEx.True(DangerCloseLeaseRegistry.TrySet(
			active: true,
			"another.integration",
			out bool secondChanged,
			out _));
		AssertEx.True(secondChanged);
		AssertEx.True(DangerCloseLeaseRegistry.IsActive);

		AssertEx.True(DangerCloseLeaseRegistry.TrySet(
			active: false,
			"tylevo.seasonalmodifiers:danger-close",
			out bool released,
			out string releaseReason));
		AssertEx.True(released);
		AssertEx.Equal("Deactivated", releaseReason);
		AssertEx.True(
			DangerCloseLeaseRegistry.IsActive,
			"The second integration's lease must continue locking A-10 tasking.");

		DangerCloseLeaseRegistry.Reset();
		AssertEx.False(DangerCloseLeaseRegistry.IsActive);
	}

	[RegressionTest]
	private static void LeaseOperationsAreIdempotent()
	{
		DangerCloseLeaseRegistry.Reset();
		AssertEx.True(DangerCloseLeaseRegistry.TrySet(
			true,
			"source-1",
			out bool firstChanged,
			out _));
		AssertEx.True(firstChanged);
		AssertEx.True(DangerCloseLeaseRegistry.TrySet(
			true,
			"source-1",
			out bool duplicateChanged,
			out string duplicateReason));
		AssertEx.False(duplicateChanged);
		AssertEx.Equal("AlreadyActive", duplicateReason);

		AssertEx.True(DangerCloseLeaseRegistry.TrySet(
			false,
			"source-1",
			out bool releaseChanged,
			out _));
		AssertEx.True(releaseChanged);
		AssertEx.True(DangerCloseLeaseRegistry.TrySet(
			false,
			"source-1",
			out bool duplicateReleaseChanged,
			out string duplicateReleaseReason));
		AssertEx.False(duplicateReleaseChanged);
		AssertEx.Equal("AlreadyInactive", duplicateReleaseReason);
	}

	[RegressionTest]
	private static void InvalidSourceIdsFailClosed()
	{
		DangerCloseLeaseRegistry.Reset();
		foreach (string sourceId in new[]
		         {
			         string.Empty,
			         " leading-space",
			         "trailing-space ",
			         "bad/source",
			         new string('x', 129)
		         })
		{
			AssertEx.False(DangerCloseLeaseRegistry.TrySet(
				true,
				sourceId,
				out bool changed,
				out string reason));
			AssertEx.False(changed);
			AssertEx.Equal("InvalidSourceId", reason);
		}

		AssertEx.False(DangerCloseLeaseRegistry.IsActive);
	}

	[RegressionTest]
	private static void ServerCapabilityMarkerIsVersionThree()
	{
		AssertEx.Equal(3, SeasonalModifiersServerBridge.ApiVersion);
	}
}
