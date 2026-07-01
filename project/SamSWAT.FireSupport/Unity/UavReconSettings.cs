namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public static class UavReconSettings
{
	private static int? _syncedDurationSeconds;
	private static float? _syncedScanInterval;
	private static float? _syncedRangeMeters;
	private static int? _syncedFocusedSweepDurationSeconds;
	private static float? _syncedFocusedSweepScanInterval;
	private static float? _syncedFocusedSweepRangeMeters;
	private static int? _serverDurationSeconds;
	private static float? _serverScanInterval;
	private static float? _serverRangeMeters;
	private static int? _serverFocusedSweepDurationSeconds;
	private static float? _serverFocusedSweepScanInterval;
	private static float? _serverFocusedSweepRangeMeters;

	public static bool HasSyncedSettings =>
		_syncedDurationSeconds.HasValue ||
		_syncedScanInterval.HasValue ||
		_syncedRangeMeters.HasValue ||
		_syncedFocusedSweepDurationSeconds.HasValue ||
		_syncedFocusedSweepScanInterval.HasValue ||
		_syncedFocusedSweepRangeMeters.HasValue;

	public static bool HasServerConfigSettings =>
		_serverDurationSeconds.HasValue ||
		_serverScanInterval.HasValue ||
		_serverRangeMeters.HasValue ||
		_serverFocusedSweepDurationSeconds.HasValue ||
		_serverFocusedSweepScanInterval.HasValue ||
		_serverFocusedSweepRangeMeters.HasValue;

	public static void SetSyncedDuration(int durationSeconds)
	{
		SetSyncedDuration(
			durationSeconds,
			PluginSettings.UavScanInterval.Value,
			PluginSettings.UavRangeMeters.Value);
	}

	public static void SetSyncedDuration(
		int durationSeconds,
		float scanInterval,
		float rangeMeters)
	{
		_syncedDurationSeconds = durationSeconds;
		_syncedScanInterval = scanInterval;
		_syncedRangeMeters = rangeMeters;
		TscDiagnostics.LogFika(
			$"Using host UAV settings: duration={durationSeconds}s, scan={scanInterval:0.##}s, range={rangeMeters:0.#}m");
	}

	public static void SetSyncedFocusedSweep(
		int durationSeconds,
		float scanInterval,
		float rangeMeters)
	{
		_syncedFocusedSweepDurationSeconds = durationSeconds;
		_syncedFocusedSweepScanInterval = scanInterval;
		_syncedFocusedSweepRangeMeters = rangeMeters;
		TscDiagnostics.LogFika(
			$"Using host focused sweep settings: duration={durationSeconds}s, scan={scanInterval:0.##}s, range={rangeMeters:0.#}m");
	}

	public static void ClearSyncedDuration()
	{
		bool hadSyncedSettings = HasSyncedSettings;
		_syncedDurationSeconds = null;
		_syncedScanInterval = null;
		_syncedRangeMeters = null;
		_syncedFocusedSweepDurationSeconds = null;
		_syncedFocusedSweepScanInterval = null;
		_syncedFocusedSweepRangeMeters = null;
		if (hadSyncedSettings)
		{
			TscDiagnostics.LogFika("Cleared host UAV recon settings.");
		}
	}

	public static void SetServerConfigDuration(
		int durationSeconds,
		float scanInterval,
		float rangeMeters,
		int revision)
	{
		_serverDurationSeconds = durationSeconds;
		_serverScanInterval = scanInterval;
		_serverRangeMeters = rangeMeters;
		TscDiagnostics.LogDashboard(
			$"Using server URL UAV settings revision={revision}: duration={durationSeconds}s, scan={scanInterval:0.##}s, range={rangeMeters:0.#}m");
	}

	public static void SetServerConfigFocusedSweep(
		int durationSeconds,
		float scanInterval,
		float rangeMeters,
		int revision)
	{
		_serverFocusedSweepDurationSeconds = durationSeconds;
		_serverFocusedSweepScanInterval = scanInterval;
		_serverFocusedSweepRangeMeters = rangeMeters;
		TscDiagnostics.LogDashboard(
			$"Using server URL focused sweep settings revision={revision}: duration={durationSeconds}s, scan={scanInterval:0.##}s, range={rangeMeters:0.#}m");
	}

	public static void ClearServerConfigDuration()
	{
		bool hadServerSettings = HasServerConfigSettings;
		_serverDurationSeconds = null;
		_serverScanInterval = null;
		_serverRangeMeters = null;
		_serverFocusedSweepDurationSeconds = null;
		_serverFocusedSweepScanInterval = null;
		_serverFocusedSweepRangeMeters = null;
		if (hadServerSettings)
		{
			TscDiagnostics.LogDashboard("Cleared server URL UAV recon settings.");
		}
	}

	public static int GetDurationSeconds()
	{
		return GetDurationSeconds(ESupportType.Uav);
	}

	public static int GetDurationSeconds(ESupportType supportType)
	{
		return supportType == ESupportType.FocusedSweep
			? _syncedFocusedSweepDurationSeconds ?? _serverFocusedSweepDurationSeconds ?? PluginSettings.FocusedSweepDurationSeconds.Value
			: _syncedDurationSeconds ?? _serverDurationSeconds ?? PluginSettings.UavDurationSeconds.Value;
	}

	public static int GetConfiguredDurationSeconds()
	{
		return GetConfiguredDurationSeconds(ESupportType.Uav);
	}

	public static int GetConfiguredDurationSeconds(ESupportType supportType)
	{
		return supportType == ESupportType.FocusedSweep
			? PluginSettings.FocusedSweepDurationSeconds.Value
			: PluginSettings.UavDurationSeconds.Value;
	}

	public static float GetScanInterval(ESupportType supportType)
	{
		return supportType == ESupportType.FocusedSweep
			? _syncedFocusedSweepScanInterval ?? _serverFocusedSweepScanInterval ?? PluginSettings.FocusedSweepScanInterval.Value
			: _syncedScanInterval ?? _serverScanInterval ?? PluginSettings.UavScanInterval.Value;
	}

	public static float GetConfiguredScanInterval(ESupportType supportType)
	{
		return supportType == ESupportType.FocusedSweep
			? PluginSettings.FocusedSweepScanInterval.Value
			: PluginSettings.UavScanInterval.Value;
	}

	public static float GetRangeMeters(ESupportType supportType)
	{
		return supportType == ESupportType.FocusedSweep
			? _syncedFocusedSweepRangeMeters ?? _serverFocusedSweepRangeMeters ?? PluginSettings.FocusedSweepRangeMeters.Value
			: _syncedRangeMeters ?? _serverRangeMeters ?? PluginSettings.UavRangeMeters.Value;
	}

	public static float GetConfiguredRangeMeters(ESupportType supportType)
	{
		return supportType == ESupportType.FocusedSweep
			? PluginSettings.FocusedSweepRangeMeters.Value
			: PluginSettings.UavRangeMeters.Value;
	}
}
