using System;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

/// <summary>
/// Dependency-free countdown state for a single extraction trigger.
/// Unity collider and UI lifecycle remain in <see cref="HeliExfiltrationPoint"/>.
/// </summary>
public sealed class ExtractionCountdownClock
{
	public const float MinimumDurationSeconds = 0.1f;

	public float DurationSeconds { get; private set; } =
		MinimumDurationSeconds;

	public float RemainingSeconds { get; private set; } =
		MinimumDurationSeconds;

	public bool IsComplete { get; private set; }

	public void Initialize(float durationSeconds)
	{
		DurationSeconds = Math.Max(
			MinimumDurationSeconds,
			durationSeconds);
		Reset();
	}

	public void Reset()
	{
		RemainingSeconds = DurationSeconds;
		IsComplete = false;
	}

	/// <summary>
	/// Advances the clock and returns true only for the transition to complete.
	/// </summary>
	public bool Advance(float deltaTimeSeconds)
	{
		if (IsComplete || deltaTimeSeconds <= 0f)
		{
			return false;
		}

		RemainingSeconds = Math.Max(
			0f,
			RemainingSeconds - deltaTimeSeconds);
		if (RemainingSeconds > 0f)
		{
			return false;
		}

		IsComplete = true;
		return true;
	}
}
