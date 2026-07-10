using EFT.Communications;
using System.Collections.Generic;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public static class FireSupportAuthorizations
{
	// Two stores with different owners:
	// - server credits mirror the server ledger and are replaced wholesale by
	//   SetFromServer on every config sync;
	// - local credits are purchases the server never saw (carried roubles,
	//   zero-cost grants). They must survive SetFromServer, and consuming them
	//   must not round-trip the server ledger, or the server rejects the
	//   consume and the credit becomes unusable.
	private static readonly Dictionary<ESupportType, int> s_serverAuthorizations = new(new SupportTypeComparer());
	private static readonly Dictionary<ESupportType, int> s_localAuthorizations = new(new SupportTypeComparer());

	public static int Get(ESupportType type)
	{
		return GetServer(type) + GetLocal(type);
	}

	public static bool Has(ESupportType type)
	{
		return Get(type) > 0;
	}

	public static bool HasDeployable(ESupportType type)
	{
		return FireSupportServiceAvailability.IsServiceEnabled(type) && Has(type);
	}

	public static int GetDeployableCount(ESupportType type)
	{
		return FireSupportServiceAvailability.IsServiceEnabled(type) ? Get(type) : 0;
	}

	public static void Grant(ESupportType type)
	{
		Grant(type, 1);
	}

	public static void Grant(ESupportType type, int count)
	{
		if (!IsSupported(type) || count <= 0)
		{
			return;
		}

		s_localAuthorizations[type] = GetLocal(type) + count;
		TscDiagnostics.LogPayment(
			$"TSC authorizations granted (local): service={GetSupportName(type)}, count={count}, available={Get(type)}.");
	}

	public static void GrantServer(ESupportType type)
	{
		if (!IsSupported(type))
		{
			return;
		}

		// Optimistic ledger mirror until the next config sync replaces it.
		s_serverAuthorizations[type] = GetServer(type) + 1;
		TscDiagnostics.LogPayment(
			$"TSC authorizations granted (server): service={GetSupportName(type)}, available={Get(type)}.");
	}

	public static void SetFromServer(Dictionary<string, int> authorizations)
	{
		if (authorizations == null)
		{
			return;
		}

		s_serverAuthorizations.Clear();
		foreach ((string key, int count) in authorizations)
		{
			if (TryParseSupportType(key, out ESupportType type) && count > 0)
			{
				s_serverAuthorizations[type] = count;
			}
		}

		TscDiagnostics.LogPayment("TSC service authorizations synced from server.");
	}

	public static bool TryConsume(ESupportType type)
	{
		return TryConsume(type, out _);
	}

	public static bool TryConsume(ESupportType type, out bool serverBacked)
	{
		serverBacked = false;
		if (!FireSupportServiceAvailability.IsServiceEnabled(type))
		{
			TscDiagnostics.LogPayment(
				$"Ignored disabled prepaid {GetSupportName(type)} authorization.");
			return false;
		}

		// Local credits first: they have no ledger entry to consume server-side.
		int localCount = GetLocal(type);
		if (localCount > 0)
		{
			s_localAuthorizations[type] = localCount - 1;
			TscDiagnostics.LogPayment(
				$"Consumed prepaid {GetSupportName(type)} authorization (local). Remaining={Get(type)}.");
			return true;
		}

		int serverCount = GetServer(type);
		if (serverCount <= 0)
		{
			return false;
		}

		s_serverAuthorizations[type] = serverCount - 1;
		serverBacked = true;
		TscDiagnostics.LogPayment(
			$"Consumed prepaid {GetSupportName(type)} authorization (server). Remaining={Get(type)}.");
		return true;
	}

	public static bool TryConsumeForDeployment(ESupportType type, out ESupportType consumedType)
	{
		return TryConsumeForDeployment(type, out consumedType, out _);
	}

	public static bool TryConsumeForDeployment(
		ESupportType type,
		out ESupportType consumedType,
		out bool serverBacked)
	{
		consumedType = type;
		serverBacked = false;
		return TryConsume(type, out serverBacked);
	}

	public static void Refund(ESupportType type)
	{
		Refund(type, serverBacked: false);
	}

	public static void Refund(ESupportType type, bool serverBacked)
	{
		if (!IsSupported(type))
		{
			return;
		}

		if (serverBacked)
		{
			GrantServer(type);
		}
		else
		{
			Grant(type, 1);
		}

		NotificationManagerClass.DisplayMessageNotification(
			$"{GetSupportName(type)} authorization refunded.",
			ENotificationDurationType.Default,
			ENotificationIconType.Default,
			null);
	}

	public static void Reset()
	{
		if (s_serverAuthorizations.Count == 0 && s_localAuthorizations.Count == 0)
		{
			return;
		}

		s_serverAuthorizations.Clear();
		s_localAuthorizations.Clear();
		TscDiagnostics.LogPayment("TSC service authorizations reset.");
	}

	private static int GetServer(ESupportType type)
	{
		return s_serverAuthorizations.TryGetValue(type, out int count) ? count : 0;
	}

	private static int GetLocal(ESupportType type)
	{
		return s_localAuthorizations.TryGetValue(type, out int count) ? count : 0;
	}

	private static bool IsSupported(ESupportType type)
	{
		return type == ESupportType.Strafe ||
		       type == ESupportType.DoubleStrafe ||
		       type == ESupportType.Extract ||
		       type == ESupportType.PriorityExfil ||
		       type == ESupportType.Uav ||
		       type == ESupportType.FocusedSweep;
	}

	private static bool TryParseSupportType(string key, out ESupportType type)
	{
		type = key?.Trim().ToLowerInvariant() switch
		{
			"a10" => ESupportType.Strafe,
			"strafe" => ESupportType.Strafe,
			"doublestrafe" => ESupportType.DoubleStrafe,
			"doublepass" => ESupportType.DoubleStrafe,
			"extraction" => ESupportType.Extract,
			"extract" => ESupportType.Extract,
			"priorityexfil" => ESupportType.PriorityExfil,
			"uav" => ESupportType.Uav,
			"focusedsweep" => ESupportType.FocusedSweep,
			_ => ESupportType.None
		};
		return IsSupported(type);
	}

	private static string GetSupportName(ESupportType type)
	{
		return type switch
		{
			ESupportType.Strafe => "A-10 strafe",
			ESupportType.DoubleStrafe => "A-10 double pass",
			ESupportType.Extract => "UH-60 extraction",
			ESupportType.PriorityExfil => "priority exfil",
			ESupportType.Uav => "UAV recon",
			ESupportType.FocusedSweep => "focused sweep",
			_ => "fire support"
		};
	}
}
