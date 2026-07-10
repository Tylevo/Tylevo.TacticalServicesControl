using System.Collections.Generic;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

/// <summary>
/// Shared source of truth for the TSC Uplink deploy selector: which purchased
/// authorizations are currently deployable and in what display order.
/// </summary>
public static class FireSupportDeployMenu
{
	private static readonly ESupportType[] s_displayOrder =
	{
		ESupportType.Strafe,
		ESupportType.DoubleStrafe,
		ESupportType.Extract,
		ESupportType.PriorityExfil,
		ESupportType.Uav,
		ESupportType.FocusedSweep
	};

	public static List<ESupportType> GetOwnedEntries()
	{
		var entries = new List<ESupportType>(s_displayOrder.Length);
		foreach (ESupportType type in s_displayOrder)
		{
			if (FireSupportAuthorizations.GetDeployableCount(type) > 0)
			{
				entries.Add(type);
			}
		}

		return entries;
	}
}
