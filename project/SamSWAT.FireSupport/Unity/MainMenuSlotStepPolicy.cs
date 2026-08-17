using System;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

/// <summary>
/// Selects one native main-menu row interval without treating a multi-row
/// anchor gap as the spacing for every entry below the TSC button.
/// </summary>
public static class MainMenuSlotStepPolicy
{
	public const float MinimumMagnitude = 20f;
	public const float MaximumAdjacentRowMagnitude = 160f;
	public const float MaximumAmbiguousAnchorMagnitude = 90f;

	public static float Resolve(
		float? tradeToHideout,
		float? hideoutToExit,
		float? cachedStep,
		float? playToCharacter,
		float fallback)
	{
		if (IsPlausibleAdjacentRow(tradeToHideout))
		{
			return tradeToHideout!.Value;
		}

		if (IsPlausibleAdjacentRow(hideoutToExit))
		{
			return hideoutToExit!.Value;
		}

		if (IsPlausibleAdjacentRow(cachedStep))
		{
			return cachedStep!.Value;
		}

		if (IsPlausibleAmbiguousAnchor(playToCharacter))
		{
			return playToCharacter!.Value;
		}

		return fallback;
	}

	public static bool IsPlausibleAdjacentRow(float? step)
	{
		return IsPlausible(step, MaximumAdjacentRowMagnitude);
	}

	public static bool IsPlausibleAmbiguousAnchor(float? step)
	{
		return IsPlausible(step, MaximumAmbiguousAnchorMagnitude);
	}

	private static bool IsPlausible(float? step, float maximumMagnitude)
	{
		if (!step.HasValue || float.IsNaN(step.Value) || float.IsInfinity(step.Value))
		{
			return false;
		}

		float magnitude = Math.Abs(step.Value);
		return magnitude >= MinimumMagnitude &&
		       magnitude <= maximumMagnitude;
	}
}
