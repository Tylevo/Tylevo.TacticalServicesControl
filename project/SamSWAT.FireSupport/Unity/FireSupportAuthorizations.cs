using EFT.Communications;
using System.Collections.Generic;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public static class FireSupportAuthorizations
{
	private static readonly Dictionary<ESupportType, int> s_authorizations = new(new SupportTypeComparer());

	public static int Get(ESupportType type)
	{
		return s_authorizations.TryGetValue(type, out int count) ? count : 0;
	}

	public static bool Has(ESupportType type)
	{
		return Get(type) > 0;
	}

	public static bool HasDeployable(ESupportType type)
	{
		return type switch
		{
			ESupportType.Strafe => HasEnabled(ESupportType.DoubleStrafe) || Has(ESupportType.Strafe),
			ESupportType.Extract => HasEnabled(ESupportType.PriorityExfil) || Has(ESupportType.Extract),
			ESupportType.Uav => HasEnabled(ESupportType.FocusedSweep) || Has(ESupportType.Uav),
			_ => FireSupportServiceAvailability.IsServiceEnabled(type) && Has(type)
		};
	}

	public static int GetDeployableCount(ESupportType type)
	{
		return type switch
		{
			ESupportType.Strafe => GetEnabled(ESupportType.DoubleStrafe) + Get(ESupportType.Strafe),
			ESupportType.Extract => GetEnabled(ESupportType.PriorityExfil) + Get(ESupportType.Extract),
			ESupportType.Uav => GetEnabled(ESupportType.FocusedSweep) + Get(ESupportType.Uav),
			_ => FireSupportServiceAvailability.IsServiceEnabled(type) ? Get(type) : 0
		};
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

		s_authorizations[type] = Get(type) + count;
		TscDiagnostics.LogPayment(
			$"TSC authorizations granted: service={GetSupportName(type)}, count={count}, available={Get(type)}.");
	}

	public static void SetFromServer(Dictionary<string, int> authorizations)
	{
		if (authorizations == null)
		{
			return;
		}

		s_authorizations.Clear();
		foreach ((string key, int count) in authorizations)
		{
			if (TryParseSupportType(key, out ESupportType type) && count > 0)
			{
				s_authorizations[type] = count;
			}
		}

		TscDiagnostics.LogPayment("TSC service authorizations synced from server.");
	}

	public static bool TryConsume(ESupportType type)
	{
		if (!FireSupportServiceAvailability.IsServiceEnabled(type))
		{
			TscDiagnostics.LogPayment(
				$"Ignored disabled prepaid {GetSupportName(type)} authorization.");
			return false;
		}

		int count = Get(type);
		if (count <= 0)
		{
			return false;
		}

		s_authorizations[type] = count - 1;
		TscDiagnostics.LogPayment(
			$"Consumed prepaid {GetSupportName(type)} authorization. Remaining={Get(type)}.");
		return true;
	}

	public static bool TryConsumeForDeployment(ESupportType type, out ESupportType consumedType)
	{
		consumedType = type;
		if (type == ESupportType.Strafe)
		{
			if (TryConsume(ESupportType.DoubleStrafe))
			{
				consumedType = ESupportType.DoubleStrafe;
				return true;
			}

			if (TryConsume(ESupportType.Strafe))
			{
				consumedType = ESupportType.Strafe;
				return true;
			}

			return false;
		}

		if (type == ESupportType.Extract)
		{
			if (TryConsume(ESupportType.PriorityExfil))
			{
				consumedType = ESupportType.PriorityExfil;
				return true;
			}

			if (TryConsume(ESupportType.Extract))
			{
				consumedType = ESupportType.Extract;
				return true;
			}

			return false;
		}

		if (type == ESupportType.Uav)
		{
			if (TryConsume(ESupportType.FocusedSweep))
			{
				consumedType = ESupportType.FocusedSweep;
				return true;
			}

			if (TryConsume(ESupportType.Uav))
			{
				consumedType = ESupportType.Uav;
				return true;
			}

			return false;
		}

		return TryConsume(type);
	}

	public static void Refund(ESupportType type)
	{
		if (!IsSupported(type))
		{
			return;
		}

		Grant(type, 1);
		NotificationManagerClass.DisplayMessageNotification(
			$"{GetSupportName(type)} authorization refunded.",
			ENotificationDurationType.Default,
			ENotificationIconType.Default,
			null);
	}

	public static void Reset()
	{
		if (s_authorizations.Count == 0)
		{
			return;
		}

		s_authorizations.Clear();
		TscDiagnostics.LogPayment("TSC service authorizations reset.");
	}

	private static bool IsSupported(ESupportType type)
	{
		return		       type == ESupportType.Strafe ||
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

	private static bool HasEnabled(ESupportType type)
	{
		return FireSupportServiceAvailability.IsServiceEnabled(type) && Has(type);
	}

	private static int GetEnabled(ESupportType type)
	{
		return FireSupportServiceAvailability.IsServiceEnabled(type) ? Get(type) : 0;
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
