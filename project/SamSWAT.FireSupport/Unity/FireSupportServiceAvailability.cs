using SamSWAT.FireSupport.ArysReloaded.Integration;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public static class FireSupportServiceAvailability
{
	private static bool? _syncedPriorityExfilEnabled;
	private static bool? _syncedDoublePassEnabled;
	private static bool? _syncedFocusedSweepEnabled;
	private static bool? _serverPriorityExfilEnabled;
	private static bool? _serverDoublePassEnabled;
	private static bool? _serverFocusedSweepEnabled;

	public static bool HasSyncedAvailability =>
		_syncedPriorityExfilEnabled.HasValue ||
		_syncedDoublePassEnabled.HasValue ||
		_syncedFocusedSweepEnabled.HasValue;

	public static bool HasServerConfigAvailability =>
		_serverPriorityExfilEnabled.HasValue ||
		_serverDoublePassEnabled.HasValue ||
		_serverFocusedSweepEnabled.HasValue;

	public static void SetSyncedAvailability(
		bool priorityExfilEnabled,
		bool doublePassEnabled,
		bool focusedSweepEnabled)
	{
		_syncedPriorityExfilEnabled = priorityExfilEnabled;
		_syncedDoublePassEnabled = doublePassEnabled;
		_syncedFocusedSweepEnabled = focusedSweepEnabled;
		TscDiagnostics.LogFika(
			$"Using host TSC availability: UH-60 cargo transfer={priorityExfilEnabled}, A-10 double pass={doublePassEnabled}, Focused sweep={focusedSweepEnabled}");
	}

	public static void ClearSyncedAvailability()
	{
		bool hadSyncedAvailability = HasSyncedAvailability;
		_syncedPriorityExfilEnabled = null;
		_syncedDoublePassEnabled = null;
		_syncedFocusedSweepEnabled = null;
		if (hadSyncedAvailability)
		{
			TscDiagnostics.LogFika("Cleared host TSC availability.");
		}
	}

	public static void SetServerConfigAvailability(
		bool priorityExfilEnabled,
		bool doublePassEnabled,
		bool focusedSweepEnabled,
		int revision)
	{
		_serverPriorityExfilEnabled = priorityExfilEnabled;
		_serverDoublePassEnabled = doublePassEnabled;
		_serverFocusedSweepEnabled = focusedSweepEnabled;
		TscDiagnostics.LogDashboard(
			$"Using server URL TSC availability revision={revision}: UH-60 cargo transfer={priorityExfilEnabled}, A-10 double pass={doublePassEnabled}, Focused sweep={focusedSweepEnabled}");
	}

	public static void ClearServerConfigAvailability()
	{
		bool hadServerAvailability = HasServerConfigAvailability;
		_serverPriorityExfilEnabled = null;
		_serverDoublePassEnabled = null;
		_serverFocusedSweepEnabled = null;
		if (hadServerAvailability)
		{
			TscDiagnostics.LogDashboard("Cleared server URL TSC availability.");
		}
	}

	public static bool IsServiceEnabled(ESupportType supportType)
	{
		if (!IsLocalUseAllowed(supportType))
		{
			return false;
		}

		return supportType switch
		{
			ESupportType.PriorityExfil =>
				_syncedPriorityExfilEnabled ??
				 _serverPriorityExfilEnabled ??
				 GetConfiguredPriorityExfilEnabled(),
			ESupportType.DoubleStrafe => _syncedDoublePassEnabled ?? _serverDoublePassEnabled ?? GetConfiguredDoublePassEnabled(),
			ESupportType.FocusedSweep => _syncedFocusedSweepEnabled ?? _serverFocusedSweepEnabled ?? GetConfiguredFocusedSweepEnabled(),
			_ => true
		};
	}

	public static bool IsLocalUseAllowed(ESupportType supportType)
	{
		return string.IsNullOrEmpty(GetLocalRestrictionReason(supportType));
	}

	public static string GetLocalRestrictionReason(ESupportType supportType)
	{
		if (IsA10Type(supportType) && SeasonalModifiersBridge.IsDangerCloseActive)
		{
			return "A-10 tasking is unavailable while Danger Close autonomous air operations are active.";
		}

		if (supportType != ESupportType.PriorityExfil)
		{
			return string.Empty;
		}

		if (FireSupportServerConfigClient.IsFikaClientHostAuthorityActive)
		{
			return "UH-60 cargo transfer is host-only in Fika. Non-host clients cannot purchase or deploy it.";
		}

		if (!(PluginSettings.EnableHelicopterItemTransfer?.Value ?? true))
		{
			return "UH-60 cargo transfer is disabled because Enable mid-raid item transfer is off in F12.";
		}

		return string.Empty;
	}

	public static string GetLocalRestrictionStatus(ESupportType supportType)
	{
		if (IsA10Type(supportType) && SeasonalModifiersBridge.IsDangerCloseActive)
		{
			return "AUTONOMOUS OPS";
		}

		if (supportType != ESupportType.PriorityExfil)
		{
			return string.Empty;
		}

		if (FireSupportServerConfigClient.IsFikaClientHostAuthorityActive)
		{
			return "FIKA HOST ONLY";
		}

		return PluginSettings.EnableHelicopterItemTransfer?.Value == false
			? "CARGO DISABLED"
			: string.Empty;
	}

	public static bool GetConfiguredPriorityExfilEnabled()
	{
		return PluginSettings.EnablePriorityExfil?.Value ?? true;
	}

	public static bool GetConfiguredDoublePassEnabled()
	{
		return PluginSettings.EnableDoublePass?.Value ?? true;
	}
	public static bool GetConfiguredFocusedSweepEnabled()
	{
		return PluginSettings.EnableFocusedSweep?.Value ?? true;
	}

	private static bool IsA10Type(ESupportType supportType)
	{
		return supportType == ESupportType.Strafe ||
		       supportType == ESupportType.DoubleStrafe;
	}
}
