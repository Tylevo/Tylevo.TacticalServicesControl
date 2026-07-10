namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public static class FireSupportTuningSettings
{
	private const float DefaultA10HeadlessDamageOriginDistance = 425f;
	private const float DefaultA10HeadlessDamageOriginAltitude = 150f;
	private static float? _syncedDoubleStrafeSecondPassDelay;
	private static int? _syncedHelicopterWaitTime;
	private static int? _syncedPriorityExfilHelicopterWaitTime;
	private static float? _syncedPriorityExfilDispatchDelay;
	private static float? _syncedHelicopterExtractTime;
	private static float? _syncedHelicopterSpeedMultiplier;
	private static float? _syncedPriorityExfilHelicopterSpeedMultiplier;
	private static int? _syncedRequestCooldown;
	private static float? _serverDoubleStrafeSecondPassDelay;
	private static int? _serverHelicopterWaitTime;
	private static int? _serverPriorityExfilHelicopterWaitTime;
	private static float? _serverPriorityExfilDispatchDelay;
	private static float? _serverHelicopterExtractTime;
	private static float? _serverHelicopterSpeedMultiplier;
	private static float? _serverPriorityExfilHelicopterSpeedMultiplier;
	private static int? _serverRequestCooldown;

	public static bool HasSyncedTuning =>
		_syncedDoubleStrafeSecondPassDelay.HasValue ||
		_syncedHelicopterWaitTime.HasValue ||
		_syncedPriorityExfilHelicopterWaitTime.HasValue ||
		_syncedPriorityExfilDispatchDelay.HasValue ||
		_syncedHelicopterExtractTime.HasValue ||
		_syncedHelicopterSpeedMultiplier.HasValue ||
		_syncedPriorityExfilHelicopterSpeedMultiplier.HasValue ||
		_syncedRequestCooldown.HasValue;

	public static bool HasServerConfigTuning =>
		_serverDoubleStrafeSecondPassDelay.HasValue ||
		_serverHelicopterWaitTime.HasValue ||
		_serverPriorityExfilHelicopterWaitTime.HasValue ||
		_serverPriorityExfilDispatchDelay.HasValue ||
		_serverHelicopterExtractTime.HasValue ||
		_serverHelicopterSpeedMultiplier.HasValue ||
		_serverPriorityExfilHelicopterSpeedMultiplier.HasValue ||
		_serverRequestCooldown.HasValue;

	public static void SetSyncedTuning(
		float doubleStrafeSecondPassDelay,
		int helicopterWaitTime,
		int priorityExfilHelicopterWaitTime,
		float priorityExfilDispatchDelay,
		float helicopterExtractTime,
		float helicopterSpeedMultiplier,
		float priorityExfilHelicopterSpeedMultiplier,
		int requestCooldown)
	{
		_syncedDoubleStrafeSecondPassDelay = doubleStrafeSecondPassDelay;
		_syncedHelicopterWaitTime = helicopterWaitTime;
		_syncedPriorityExfilHelicopterWaitTime = priorityExfilHelicopterWaitTime;
		_syncedPriorityExfilDispatchDelay = priorityExfilDispatchDelay;
		_syncedHelicopterExtractTime = helicopterExtractTime;
		_syncedHelicopterSpeedMultiplier = helicopterSpeedMultiplier;
		_syncedPriorityExfilHelicopterSpeedMultiplier = priorityExfilHelicopterSpeedMultiplier;
		_syncedRequestCooldown = requestCooldown;
		TscDiagnostics.LogFika(
			$"Using host TSC tuning: doublePassDelay={doubleStrafeSecondPassDelay:0.##}s, helicopterWait={helicopterWaitTime}s, priorityWait={priorityExfilHelicopterWaitTime}s, priorityDispatch={priorityExfilDispatchDelay:0.##}s, extractTime={helicopterExtractTime:0.##}s, cooldown={requestCooldown}s");
	}

	public static void ClearSyncedTuning()
	{
		bool hadSyncedTuning = HasSyncedTuning;
		_syncedDoubleStrafeSecondPassDelay = null;
		_syncedHelicopterWaitTime = null;
		_syncedPriorityExfilHelicopterWaitTime = null;
		_syncedPriorityExfilDispatchDelay = null;
		_syncedHelicopterExtractTime = null;
		_syncedHelicopterSpeedMultiplier = null;
		_syncedPriorityExfilHelicopterSpeedMultiplier = null;
		_syncedRequestCooldown = null;
		if (hadSyncedTuning)
		{
			TscDiagnostics.LogFika("Cleared host TSC tuning.");
		}
	}

	public static void SetServerConfigTuning(
		float doubleStrafeSecondPassDelay,
		int helicopterWaitTime,
		int priorityExfilHelicopterWaitTime,
		float priorityExfilDispatchDelay,
		float helicopterExtractTime,
		float helicopterSpeedMultiplier,
		float priorityExfilHelicopterSpeedMultiplier,
		int requestCooldown,
		int revision)
	{
		_serverDoubleStrafeSecondPassDelay = doubleStrafeSecondPassDelay;
		_serverHelicopterWaitTime = helicopterWaitTime;
		_serverPriorityExfilHelicopterWaitTime = priorityExfilHelicopterWaitTime;
		_serverPriorityExfilDispatchDelay = priorityExfilDispatchDelay;
		_serverHelicopterExtractTime = helicopterExtractTime;
		_serverHelicopterSpeedMultiplier = helicopterSpeedMultiplier;
		_serverPriorityExfilHelicopterSpeedMultiplier = priorityExfilHelicopterSpeedMultiplier;
		_serverRequestCooldown = requestCooldown;
		TscDiagnostics.LogDashboard(
			$"Using server URL TSC tuning revision={revision}: doublePassDelay={doubleStrafeSecondPassDelay:0.##}s, helicopterWait={helicopterWaitTime}s, priorityWait={priorityExfilHelicopterWaitTime}s, priorityDispatch={priorityExfilDispatchDelay:0.##}s, extractTime={helicopterExtractTime:0.##}s, cooldown={requestCooldown}s");
	}

	public static void ClearServerConfigTuning()
	{
		bool hadServerTuning = HasServerConfigTuning;
		_serverDoubleStrafeSecondPassDelay = null;
		_serverHelicopterWaitTime = null;
		_serverPriorityExfilHelicopterWaitTime = null;
		_serverPriorityExfilDispatchDelay = null;
		_serverHelicopterExtractTime = null;
		_serverHelicopterSpeedMultiplier = null;
		_serverPriorityExfilHelicopterSpeedMultiplier = null;
		_serverRequestCooldown = null;
		if (hadServerTuning)
		{
			TscDiagnostics.LogDashboard("Cleared server URL TSC tuning.");
		}
	}

	public static float GetDoubleStrafeSecondPassDelay()
	{
		return _syncedDoubleStrafeSecondPassDelay ?? _serverDoubleStrafeSecondPassDelay ?? GetConfiguredDoubleStrafeSecondPassDelay();
	}

	public static float GetConfiguredDoubleStrafeSecondPassDelay()
	{
		return PluginSettings.DoubleStrafeSecondPassDelay.Value;
	}

	public static A10HeadlessFikaMode GetA10HeadlessFikaMode()
	{
		return PluginSettings.A10FikaHeadlessMode?.Value ?? A10HeadlessFikaMode.ExperimentalDamageOnly;
	}

	public static float GetA10HeadlessDamageOriginDistance()
	{
		return PluginSettings.A10HeadlessDamageOriginDistance?.Value ?? DefaultA10HeadlessDamageOriginDistance;
	}

	public static float GetA10HeadlessDamageOriginAltitude()
	{
		return PluginSettings.A10HeadlessDamageOriginAltitude?.Value ?? DefaultA10HeadlessDamageOriginAltitude;
	}

	public static A10ProjectileOwnerMode GetA10HeadlessProjectileOwnerMode()
	{
		return PluginSettings.A10HeadlessProjectileOwnerMode?.Value ?? A10ProjectileOwnerMode.RequesterProfile;
	}

	public static bool IsA10ClientVisualPredictionEnabled()
	{
		return PluginSettings.EnableA10ClientVisualPrediction?.Value == true;
	}

	public static bool IsA10HeadlessDirectDamageFallbackEnabled()
	{
		return PluginSettings.EnableA10HeadlessDirectDamageFallback?.Value != false;
	}

	public static bool IsA10HeadlessRequesterSelfDamageEnabled()
	{
		return PluginSettings.A10HeadlessAllowRequesterSelfDamage?.Value != false;
	}

	public static int GetHelicopterWaitTime(ESupportType supportType)
	{
		return supportType == ESupportType.PriorityExfil
			? _syncedPriorityExfilHelicopterWaitTime ?? _serverPriorityExfilHelicopterWaitTime ?? GetConfiguredPriorityExfilHelicopterWaitTime()
			: _syncedHelicopterWaitTime ?? _serverHelicopterWaitTime ?? GetConfiguredHelicopterWaitTime();
	}

	public static int GetConfiguredHelicopterWaitTime()
	{
		return PluginSettings.HelicopterWaitTime.Value;
	}

	public static int GetConfiguredPriorityExfilHelicopterWaitTime()
	{
		return PluginSettings.PriorityExfilHelicopterWaitTime.Value;
	}

	public static float GetPriorityExfilDispatchDelay()
	{
		return _syncedPriorityExfilDispatchDelay ?? _serverPriorityExfilDispatchDelay ?? GetConfiguredPriorityExfilDispatchDelay();
	}

	public static float GetConfiguredPriorityExfilDispatchDelay()
	{
		return PluginSettings.PriorityExfilDispatchDelay.Value;
	}

	public static float GetHelicopterExtractTime()
	{
		return _syncedHelicopterExtractTime ?? _serverHelicopterExtractTime ?? GetConfiguredHelicopterExtractTime();
	}

	public static float GetConfiguredHelicopterExtractTime()
	{
		return PluginSettings.HelicopterExtractTime.Value;
	}

	public static float GetHelicopterSpeedMultiplier(ESupportType supportType)
	{
		return supportType == ESupportType.PriorityExfil
			? _syncedPriorityExfilHelicopterSpeedMultiplier ?? _serverPriorityExfilHelicopterSpeedMultiplier ?? GetConfiguredPriorityExfilHelicopterSpeedMultiplier()
			: _syncedHelicopterSpeedMultiplier ?? _serverHelicopterSpeedMultiplier ?? GetConfiguredHelicopterSpeedMultiplier();
	}

	public static float GetConfiguredHelicopterSpeedMultiplier()
	{
		return PluginSettings.HelicopterSpeedMultiplier.Value;
	}

	public static float GetConfiguredPriorityExfilHelicopterSpeedMultiplier()
	{
		return PluginSettings.PriorityExfilHelicopterSpeedMultiplier.Value;
	}

	public static int GetRequestCooldown()
	{
		return _syncedRequestCooldown ?? _serverRequestCooldown ?? GetConfiguredRequestCooldown();
	}

	public static int GetConfiguredRequestCooldown()
	{
		return PluginSettings.RequestCooldown.Value;
	}
}
