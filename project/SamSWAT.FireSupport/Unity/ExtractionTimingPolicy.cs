using System;

#nullable enable

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

/// <summary>
/// Dependency-free extraction timing values shared by the client runtime,
/// server configuration validator, and regression tests.
/// </summary>
public readonly struct ExtractionTimingValues
{
	public ExtractionTimingValues(
		float dispatchDelaySeconds,
		int waitTimeSeconds,
		float extractTimeSeconds,
		float speedMultiplier)
	{
		DispatchDelaySeconds = dispatchDelaySeconds;
		WaitTimeSeconds = waitTimeSeconds;
		ExtractTimeSeconds = extractTimeSeconds;
		SpeedMultiplier = speedMultiplier;
	}

	public float DispatchDelaySeconds { get; }
	public int WaitTimeSeconds { get; }
	public float ExtractTimeSeconds { get; }
	public float SpeedMultiplier { get; }
}

public readonly struct HelicopterTimingSnapshot
{
	public HelicopterTimingSnapshot(
		ESupportType supportType,
		float dispatchDelaySeconds,
		int waitTimeSeconds,
		float extractTimeSeconds,
		float speedMultiplier)
	{
		SupportType = supportType;
		DispatchDelaySeconds = dispatchDelaySeconds;
		WaitTimeSeconds = waitTimeSeconds;
		ExtractTimeSeconds = extractTimeSeconds;
		SpeedMultiplier = speedMultiplier;
	}

	public ESupportType SupportType { get; }
	public float DispatchDelaySeconds { get; }
	public int WaitTimeSeconds { get; }
	public float ExtractTimeSeconds { get; }
	public float SpeedMultiplier { get; }
}

/// <summary>
/// Defines the single extraction timing contract used by both server
/// configuration and the client-side fallback runtime.
/// </summary>
public static class ExtractionTimingPolicy
{
	public const float MinimumExtractionWindowMarginSeconds = 1f;
	public const float MinimumAuthorizationSettlementMarginSeconds = 35f;
	public const float MaxDispatchDelaySeconds = 120f;
	public const int MinWaitTimeSeconds = 5;
	public const int MaxWaitTimeSeconds = 300;
	public const float MinExtractTimeSeconds = 1f;
	public const float MaxExtractTimeSeconds = 60f;
	public const float MinSpeedMultiplier = 0.5f;
	public const float MaxSpeedMultiplier = 3f;
	public const float RuntimeMinExtractTimeSeconds = 0.1f;
	public const float RuntimeMinSpeedMultiplier = 0.01f;

	public static int GetMinimumSafeWaitTimeSeconds(float extractTimeSeconds)
	{
		return (int)Math.Ceiling(
			extractTimeSeconds + MinimumExtractionWindowMarginSeconds);
	}

	public static int GetRequiredPendingUseTimeoutSeconds()
	{
		return (int)Math.Ceiling(
			MaxDispatchDelaySeconds +
			MinimumAuthorizationSettlementMarginSeconds);
	}

	public static bool TryValidate(
		ExtractionTimingValues settings,
		string path,
		out string error)
	{
		if (!IsFinite(settings.DispatchDelaySeconds) ||
		    settings.DispatchDelaySeconds < 0f ||
		    settings.DispatchDelaySeconds > MaxDispatchDelaySeconds)
		{
			error =
				$"{path}.dispatchDelaySeconds ({settings.DispatchDelaySeconds}) must be between 0 and " +
				$"{MaxDispatchDelaySeconds:0.##}.";
			return false;
		}

		if (settings.WaitTimeSeconds < MinWaitTimeSeconds ||
		    settings.WaitTimeSeconds > MaxWaitTimeSeconds)
		{
			error =
				$"{path}.waitTimeSeconds ({settings.WaitTimeSeconds}) must be between " +
				$"{MinWaitTimeSeconds} and {MaxWaitTimeSeconds}.";
			return false;
		}

		if (!IsFinite(settings.ExtractTimeSeconds) ||
		    settings.ExtractTimeSeconds < MinExtractTimeSeconds ||
		    settings.ExtractTimeSeconds > MaxExtractTimeSeconds)
		{
			error =
				$"{path}.extractTimeSeconds ({settings.ExtractTimeSeconds}) must be between " +
				$"{MinExtractTimeSeconds:0.##} and " +
				$"{MaxExtractTimeSeconds:0.##}.";
			return false;
		}

		if (!IsFinite(settings.SpeedMultiplier) ||
		    settings.SpeedMultiplier < MinSpeedMultiplier ||
		    settings.SpeedMultiplier > MaxSpeedMultiplier)
		{
			error =
				$"{path}.speedMultiplier ({settings.SpeedMultiplier}) must be between " +
				$"{MinSpeedMultiplier:0.##} and " +
				$"{MaxSpeedMultiplier:0.##}.";
			return false;
		}

		float requiredWaitTime =
			settings.ExtractTimeSeconds + MinimumExtractionWindowMarginSeconds;
		if (settings.WaitTimeSeconds >= requiredWaitTime)
		{
			error = string.Empty;
			return true;
		}

		error =
			$"{path}.waitTimeSeconds ({settings.WaitTimeSeconds}) must be >= " +
			$"{path}.extractTimeSeconds ({settings.ExtractTimeSeconds:0.##}) + " +
			$"{MinimumExtractionWindowMarginSeconds:0.##} second so the extraction zone remains usable.";
		return false;
	}

	public static ExtractionTimingValues Repair(
		ExtractionTimingValues settings,
		ExtractionTimingValues defaults)
	{
		float dispatchDelaySeconds = settings.DispatchDelaySeconds;
		int waitTimeSeconds = settings.WaitTimeSeconds;
		float extractTimeSeconds = settings.ExtractTimeSeconds;
		float speedMultiplier = settings.SpeedMultiplier;

		if (!IsFinite(dispatchDelaySeconds) ||
		    dispatchDelaySeconds < 0f ||
		    dispatchDelaySeconds > MaxDispatchDelaySeconds)
		{
			dispatchDelaySeconds = defaults.DispatchDelaySeconds;
		}

		if (waitTimeSeconds < MinWaitTimeSeconds ||
		    waitTimeSeconds > MaxWaitTimeSeconds)
		{
			waitTimeSeconds = defaults.WaitTimeSeconds;
		}

		if (!IsFinite(extractTimeSeconds) ||
		    extractTimeSeconds < MinExtractTimeSeconds ||
		    extractTimeSeconds > MaxExtractTimeSeconds)
		{
			extractTimeSeconds = defaults.ExtractTimeSeconds;
		}

		if (!IsFinite(speedMultiplier) ||
		    speedMultiplier < MinSpeedMultiplier ||
		    speedMultiplier > MaxSpeedMultiplier)
		{
			speedMultiplier = defaults.SpeedMultiplier;
		}

		int minimumSafeWaitTime =
			GetMinimumSafeWaitTimeSeconds(extractTimeSeconds);
		if (waitTimeSeconds < minimumSafeWaitTime)
		{
			waitTimeSeconds = minimumSafeWaitTime;
		}

		return new ExtractionTimingValues(
			dispatchDelaySeconds,
			waitTimeSeconds,
			extractTimeSeconds,
			speedMultiplier);
	}

	public static HelicopterTimingSnapshot CreateRuntimeSnapshot(
		ESupportType supportType,
		ExtractionTimingValues settings,
		Action<string>? warning = null)
	{
		ESupportType effectiveSupportType = ESupportType.Extract;
		float dispatchDelaySeconds = Math.Max(
			0f,
			settings.DispatchDelaySeconds);
		int waitTimeSeconds = Math.Max(
			1,
			settings.WaitTimeSeconds);
		float extractTimeSeconds = Math.Max(
			RuntimeMinExtractTimeSeconds,
			settings.ExtractTimeSeconds);
		float speedMultiplier = Math.Max(
			RuntimeMinSpeedMultiplier,
			settings.SpeedMultiplier);
		int minimumSafeWaitTime =
			GetMinimumSafeWaitTimeSeconds(extractTimeSeconds);
		if (waitTimeSeconds < minimumSafeWaitTime)
		{
			warning?.Invoke(
				$"Unsafe {effectiveSupportType} helicopter timing: wait={waitTimeSeconds}s, " +
				$"extract={extractTimeSeconds:0.##}s. Using safe wait={minimumSafeWaitTime}s; " +
				"set waitTimeSeconds to at least extractTimeSeconds + 1 second.");
			waitTimeSeconds = minimumSafeWaitTime;
		}

		return new HelicopterTimingSnapshot(
			effectiveSupportType,
			dispatchDelaySeconds,
			waitTimeSeconds,
			extractTimeSeconds,
			speedMultiplier);
	}

	private static bool IsFinite(float value)
	{
		return !float.IsNaN(value) && !float.IsInfinity(value);
	}
}
