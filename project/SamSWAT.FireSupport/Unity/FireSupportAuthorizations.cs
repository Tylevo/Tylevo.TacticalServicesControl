using EFT.Communications;
using System.Collections.Generic;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public static class FireSupportAuthorizations
{
	// Two stores with different owners:
	// - server credits mirror the server ledger and are replaced wholesale by
	//   SetFromServer on every config sync;
	// - local credits have no persistent server ledger entry (legacy carried
	//   purchases, free grants, nonpersistent stash purchases). They survive SetFromServer, and consuming them
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

	internal static bool HasLocalDeployable(ESupportType type) =>
		FireSupportServiceAvailability.IsServiceEnabled(type) && GetLocal(type) > 0;

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
		return TryConsume(type, requiredServerBacked: null, out serverBacked);
	}

	private static bool TryConsume(ESupportType type, bool? requiredServerBacked, out bool serverBacked)
	{
		serverBacked = false;
		if (!FireSupportServiceAvailability.IsServiceEnabled(type))
		{
			TscDiagnostics.LogPayment(
				$"Ignored disabled prepaid {GetSupportName(type)} authorization.");
			return false;
		}

		// Ordinary use is local-first. An auto-purchase retry must consume only
		// the source it just purchased, leaving older credits untouched.
		if (!AuthorizationConsumePolicy.TryConsume(
			    s_localAuthorizations, s_serverAuthorizations, type, requiredServerBacked, out serverBacked))
		{
			return false;
		}
		TscDiagnostics.LogPayment(
			$"Consumed prepaid {GetSupportName(type)} authorization ({(serverBacked ? "server" : "local")}). Remaining={Get(type)}.");
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
		return TryConsumeForDeployment(type, out consumedType, out serverBacked, requiredServerBacked: null);
	}

	public static bool TryConsumeForDeployment(
		ESupportType type,
		out ESupportType consumedType,
		out bool serverBacked,
		bool? requiredServerBacked)
	{
		consumedType = type;
		serverBacked = false;
		return TryConsume(type, requiredServerBacked, out serverBacked);
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

		NotificationManager.DisplayMessageNotification(
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
			ESupportType.PriorityExfil => "UH-60 cargo transfer",
			ESupportType.Uav => "UAV recon",
			ESupportType.FocusedSweep => "focused sweep",
			_ => "fire support"
		};
	}
}
