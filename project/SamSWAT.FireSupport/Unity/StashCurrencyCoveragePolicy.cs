using System;
using System.Collections.Generic;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

/// <summary>Defines which absences in a stash snapshot mean an item was removed.</summary>
public static class StashCurrencyCoveragePolicy
{
	public static bool TryGetCoveredTemplates(FireSupportStashCurrencyState state, out HashSet<string> covered)
	{
		covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (state == null) return false;
		string[] cash = { PaymentCurrencyInfo.RoubleTemplateId, PaymentCurrencyInfo.DollarTemplateId, PaymentCurrencyInfo.EuroTemplateId };
		if (state.SchemaVersion == 0)
		{
			if (state.CoveredTemplateIds?.Count > 0) return false;
			covered.UnionWith(cash);
			return true;
		}
		if (state.SchemaVersion != FireSupportStashCurrencyState.CurrentSchemaVersion || state.CoveredTemplateIds == null)
			return false;
		foreach (string template in state.CoveredTemplateIds)
			if (!PaymentCurrencyInfo.IsSupportedTemplateId(template) || !covered.Add(template)) return false;
		var expected = new HashSet<string>(cash, StringComparer.OrdinalIgnoreCase)
			{ PaymentCurrencyInfo.GpCoinTemplateId, PaymentCurrencyInfo.BitcoinTemplateId };
		return covered.SetEquals(expected);
	}
}
