using System;
using System.Linq;
using UnityEngine;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public static class A10AuthorityDiagnostics
{
	private static bool s_optionalVisualModsLogged;

	public static void LogRequest(
		string role,
		ESupportType supportType,
		bool visualOnly,
		bool authoritativeDamage,
		int passIndex,
		int seed,
		Vector3 centerPosition,
		int shotCount,
		int segmentCount,
		bool fireProjectile,
		bool publishTracerBurst,
		string extra = "")
	{
		FireSupportPlugin.LogSource?.LogInfo(
			$"TSC A-10 request role={role} support={supportType} visualOnly={visualOnly} authoritativeDamage={authoritativeDamage} pass={passIndex} seed={seed} center={FormatVector(centerPosition)} shots={shotCount} segments={segmentCount} fireProjectile={fireProjectile} publishTracerBurst={publishTracerBurst}{(string.IsNullOrWhiteSpace(extra) ? string.Empty : " " + extra)}");
	}

	public static void LogExecutorSelected(
		A10AuthorityRole role,
		string executor,
		ESupportType supportType,
		int passIndex,
		int seed,
		string supportRequestId,
		string extra = "")
	{
		FireSupportPlugin.LogSource?.LogInfo(
			$"TSC A-10 executor selected role={role} executor={executor} support={supportType} pass={passIndex} seed={seed} requestId={FormatRequestId(supportRequestId)}{(string.IsNullOrWhiteSpace(extra) ? string.Empty : " " + extra)}");
	}

	public static void LogWarning(string message)
	{
		FireSupportPlugin.LogSource?.LogWarning(message);
	}

	public static string FormatVector(Vector3 value)
	{
		return $"({value.x:0.##},{value.y:0.##},{value.z:0.##})";
	}

	public static string ShortId(string requestId) => FormatRequestId(requestId);

	public static string FormatRequestId(string requestId)
	{
		if (string.IsNullOrWhiteSpace(requestId))
		{
			return "<empty>";
		}

		int keep = Mathf.Min(8, requestId.Length);
		return $"...{requestId[^keep..]}";
	}

	public static void LogOptionalVisualModsOnce()
	{
		if (s_optionalVisualModsLogged)
		{
			return;
		}

		s_optionalVisualModsLogged = true;
		try
		{
			bool hollywoodFxPresent = AppDomain.CurrentDomain.GetAssemblies().Any(static assembly =>
				assembly.GetName().Name?.IndexOf("Hollywood", StringComparison.OrdinalIgnoreCase) >= 0);
			FireSupportPlugin.LogSource?.LogInfo(
				$"TSC A-10 optional visual mods: HollywoodFX present={hollywoodFxPresent}. Damage authority does not depend on visual effects mods.");
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource?.LogWarning($"TSC A-10 optional visual mod scan failed. {ex.Message}");
		}
	}
}
