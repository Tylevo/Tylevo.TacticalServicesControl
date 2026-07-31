using System;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

/// <summary>
/// Timing contract for the Cargo service. The persisted ExtractTimeSeconds
/// value belongs to the released PriorityExfil schema and is intentionally
/// carried forward by configuration migration only. Runtime Cargo snapshots
/// always zero that dormant compatibility field.
/// </summary>
public static class CargoTimingPolicy
{
	public static bool TryValidate(
		ExtractionTimingValues settings,
		string path,
		out string error)
	{
		if (!IsFinite(settings.DispatchDelaySeconds) ||
		    settings.DispatchDelaySeconds < 0f ||
		    settings.DispatchDelaySeconds >
		    ExtractionTimingPolicy.MaxDispatchDelaySeconds)
		{
			error =
				$"{path}.dispatchDelaySeconds ({settings.DispatchDelaySeconds}) must be between 0 and " +
				$"{ExtractionTimingPolicy.MaxDispatchDelaySeconds:0.##}.";
			return false;
		}

		if (settings.WaitTimeSeconds < ExtractionTimingPolicy.MinWaitTimeSeconds ||
		    settings.WaitTimeSeconds > ExtractionTimingPolicy.MaxWaitTimeSeconds)
		{
			error =
				$"{path}.waitTimeSeconds ({settings.WaitTimeSeconds}) must be between " +
				$"{ExtractionTimingPolicy.MinWaitTimeSeconds} and " +
				$"{ExtractionTimingPolicy.MaxWaitTimeSeconds}.";
			return false;
		}

		if (!IsFinite(settings.SpeedMultiplier) ||
		    settings.SpeedMultiplier < ExtractionTimingPolicy.MinSpeedMultiplier ||
		    settings.SpeedMultiplier > ExtractionTimingPolicy.MaxSpeedMultiplier)
		{
			error =
				$"{path}.speedMultiplier ({settings.SpeedMultiplier}) must be between " +
				$"{ExtractionTimingPolicy.MinSpeedMultiplier:0.##} and " +
				$"{ExtractionTimingPolicy.MaxSpeedMultiplier:0.##}.";
			return false;
		}

		error = string.Empty;
		return true;
	}

	public static ExtractionTimingValues Repair(
		ExtractionTimingValues settings,
		ExtractionTimingValues defaults)
	{
		float dispatchDelaySeconds = settings.DispatchDelaySeconds;
		int waitTimeSeconds = settings.WaitTimeSeconds;
		float speedMultiplier = settings.SpeedMultiplier;

		if (!IsFinite(dispatchDelaySeconds) ||
		    dispatchDelaySeconds < 0f ||
		    dispatchDelaySeconds >
		    ExtractionTimingPolicy.MaxDispatchDelaySeconds)
		{
			dispatchDelaySeconds = defaults.DispatchDelaySeconds;
		}

		if (waitTimeSeconds < ExtractionTimingPolicy.MinWaitTimeSeconds ||
		    waitTimeSeconds > ExtractionTimingPolicy.MaxWaitTimeSeconds)
		{
			waitTimeSeconds = defaults.WaitTimeSeconds;
		}

		if (!IsFinite(speedMultiplier) ||
		    speedMultiplier < ExtractionTimingPolicy.MinSpeedMultiplier ||
		    speedMultiplier > ExtractionTimingPolicy.MaxSpeedMultiplier)
		{
			speedMultiplier = defaults.SpeedMultiplier;
		}

		return new ExtractionTimingValues(
			dispatchDelaySeconds,
			waitTimeSeconds,
			settings.ExtractTimeSeconds,
			speedMultiplier);
	}

	public static HelicopterTimingSnapshot CreateRuntimeSnapshot(
		ExtractionTimingValues settings)
	{
		return new HelicopterTimingSnapshot(
			ESupportType.PriorityExfil,
			Math.Max(0f, settings.DispatchDelaySeconds),
			Math.Max(1, settings.WaitTimeSeconds),
			0f,
			Math.Max(
				ExtractionTimingPolicy.RuntimeMinSpeedMultiplier,
				settings.SpeedMultiplier));
	}

	private static bool IsFinite(float value)
	{
		return !float.IsNaN(value) && !float.IsInfinity(value);
	}
}
