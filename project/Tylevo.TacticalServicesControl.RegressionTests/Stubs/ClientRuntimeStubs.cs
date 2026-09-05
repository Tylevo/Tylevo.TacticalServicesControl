using SamSWAT.FireSupport.ArysReloaded.Unity;

namespace SamSWAT.FireSupport.ArysReloaded
{
	internal sealed class RegressionConfigEntry<T>
	{
		public RegressionConfigEntry(T value)
		{
			Value = value;
		}

		public T Value { get; set; }
	}

	internal static class PluginSettings
	{
		internal static RegressionConfigEntry<float> DoubleStrafeSecondPassDelay { get; } = new(8f);
		internal static RegressionConfigEntry<A10HeadlessFikaMode> A10FikaHeadlessMode { get; } =
			new(A10HeadlessFikaMode.ExperimentalDamageOnly);
		internal static RegressionConfigEntry<float> A10HeadlessDamageOriginDistance { get; } = new(425f);
		internal static RegressionConfigEntry<float> A10HeadlessDamageOriginAltitude { get; } = new(150f);
		internal static RegressionConfigEntry<A10ProjectileOwnerMode> A10HeadlessProjectileOwnerMode { get; } =
			new(A10ProjectileOwnerMode.RequesterProfile);
		internal static RegressionConfigEntry<bool> EnableA10ClientVisualPrediction { get; } = new(false);
		internal static RegressionConfigEntry<bool> EnableA10HeadlessDirectDamageFallback { get; } = new(true);
		internal static RegressionConfigEntry<bool> A10HeadlessAllowRequesterSelfDamage { get; } = new(true);
		internal static RegressionConfigEntry<int> HelicopterWaitTime { get; } = new(30);
		internal static RegressionConfigEntry<int> PriorityExfilHelicopterWaitTime { get; } = new(20);
		internal static RegressionConfigEntry<float> HelicopterDispatchDelay { get; } = new(10f);
		internal static RegressionConfigEntry<float> PriorityExfilDispatchDelay { get; } = new(5f);
		internal static RegressionConfigEntry<float> HelicopterExtractTime { get; } = new(8f);
		internal static RegressionConfigEntry<float> PriorityExfilHelicopterExtractTime { get; } = new(5f);
		internal static RegressionConfigEntry<float> HelicopterSpeedMultiplier { get; } = new(1f);
		internal static RegressionConfigEntry<float> PriorityExfilHelicopterSpeedMultiplier { get; } = new(1.25f);
		internal static RegressionConfigEntry<int> RequestCooldown { get; } = new(300);
	}

	internal sealed class RegressionLogSource
	{
		public void LogWarning(string message)
		{
		}

		public void LogInfo(string message)
		{
		}
	}

	internal static class FireSupportPlugin
	{
		internal static RegressionLogSource? LogSource { get; set; } = new();
	}
}

namespace SamSWAT.FireSupport.ArysReloaded.Unity
{
	public static class TscDiagnostics
	{
		public static void LogFika(string message)
		{
		}

		public static void LogDashboard(string message)
		{
		}
	}
}
