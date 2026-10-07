using SamSWAT.FireSupport.ArysReloaded.Unity;

internal static class StashCurrencyCoveragePolicyTests
{
	[RegressionTest]
	private static void LegacySnapshotAbsenceNeverRemovesBarterPayments()
	{
		var state = new FireSupportStashCurrencyState();
		AssertEx.True(StashCurrencyCoveragePolicy.TryGetCoveredTemplates(state, out var covered));
		AssertEx.Equal(3, covered.Count);
		AssertEx.True(covered.Contains(PaymentCurrencyInfo.RoubleTemplateId));
		AssertEx.False(covered.Contains(PaymentCurrencyInfo.GpCoinTemplateId));
		AssertEx.False(covered.Contains(PaymentCurrencyInfo.BitcoinTemplateId));
	}

	[RegressionTest]
	private static void CompleteCurrentCoverageIncludesSpentBitcoinAbsence()
	{
		var state = Current();
		AssertEx.True(StashCurrencyCoveragePolicy.TryGetCoveredTemplates(state, out var covered));
		AssertEx.Equal(5, covered.Count);
		AssertEx.True(covered.Contains(PaymentCurrencyInfo.BitcoinTemplateId));
		AssertEx.True(covered.Contains(PaymentCurrencyInfo.GpCoinTemplateId));
	}

	[RegressionTest]
	private static void AmbiguousCoverageIsRejectedBeforeReconciliation()
	{
		var state = Current(); state.CoveredTemplateIds.RemoveAt(0);
		AssertEx.False(StashCurrencyCoveragePolicy.TryGetCoveredTemplates(state, out _));
		state = Current(); state.CoveredTemplateIds.Add(PaymentCurrencyInfo.BitcoinTemplateId);
		AssertEx.False(StashCurrencyCoveragePolicy.TryGetCoveredTemplates(state, out _));
		state = Current(); state.SchemaVersion = 2;
		AssertEx.False(StashCurrencyCoveragePolicy.TryGetCoveredTemplates(state, out _));
		state = Current(); state.SchemaVersion = 0;
		AssertEx.False(StashCurrencyCoveragePolicy.TryGetCoveredTemplates(state, out _));
		state = Current(); state.CoveredTemplateIds[0] = new string('a', 24);
		AssertEx.False(StashCurrencyCoveragePolicy.TryGetCoveredTemplates(state, out _));
	}

	private static FireSupportStashCurrencyState Current() => new()
	{
		SchemaVersion = FireSupportStashCurrencyState.CurrentSchemaVersion,
		CoveredTemplateIds = [PaymentCurrencyInfo.RoubleTemplateId, PaymentCurrencyInfo.DollarTemplateId,
			PaymentCurrencyInfo.EuroTemplateId, PaymentCurrencyInfo.GpCoinTemplateId, PaymentCurrencyInfo.BitcoinTemplateId]
	};
}
