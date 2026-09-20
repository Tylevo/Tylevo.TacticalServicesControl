using System;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

internal static class PhoneZoomPolicy
{
	public static bool IsPresentationEnabled(UavPhoneLaunchMode mode, bool purchaseEnabled, bool deployEnabled)
	{
		return mode == UavPhoneLaunchMode.DeployMenu ? deployEnabled : purchaseEnabled;
	}

	public static bool PreservesRaidFov(UavPhoneLaunchMode mode)
	{
		return mode != UavPhoneLaunchMode.ManualAuthorization &&
		       mode != UavPhoneLaunchMode.InternalUavActivation &&
		       mode != UavPhoneLaunchMode.DeployMenu;
	}

	public static bool SupportsSprintZoom(UavPhoneLaunchMode mode)
	{
		return mode == UavPhoneLaunchMode.ManualAuthorization || mode == UavPhoneLaunchMode.DeployMenu;
	}

	public static float GetRaisedFov(UavPhoneLaunchMode mode, float raidFov, float purchaseFov, float deployFov)
	{
		if (PreservesRaidFov(mode))
		{
			return raidFov;
		}

		float configured = mode == UavPhoneLaunchMode.DeployMenu ? deployFov : purchaseFov;
		if (float.IsNaN(configured) || float.IsInfinity(configured))
		{
			configured = 45f;
		}
		return Math.Min(raidFov, Math.Max(20f, Math.Min(75f, configured)));
	}
}
