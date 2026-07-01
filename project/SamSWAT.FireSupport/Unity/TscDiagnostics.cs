namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public static class TscDiagnostics
{
	public static bool VerbosePhone => PluginSettings.VerbosePhoneLogs?.Value == true;
	public static bool VerboseLcd => PluginSettings.VerboseLcdLogs?.Value == true;
	public static bool VerboseFika => PluginSettings.VerboseFikaLogs?.Value == true;
	public static bool VerboseDashboard => PluginSettings.VerboseDashboardLogs?.Value == true;
	public static bool VerbosePayment => PluginSettings.VerbosePaymentLogs?.Value == true;

	public static void LogPhone(string message)
	{
		if (VerbosePhone)
		{
			FireSupportPlugin.LogSource?.LogInfo(message);
		}
	}

	public static void LogLcd(string message)
	{
		if (VerboseLcd)
		{
			FireSupportPlugin.LogSource?.LogInfo(message);
		}
	}

	public static void LogFika(string message)
	{
		if (VerboseFika)
		{
			FireSupportPlugin.LogSource?.LogInfo(message);
		}
	}

	public static void LogPayment(string message)
	{
		if (VerbosePayment)
		{
			FireSupportPlugin.LogSource?.LogInfo(message);
		}
	}

	public static void LogDashboard(string message)
	{
		if (VerboseDashboard)
		{
			FireSupportPlugin.LogSource?.LogInfo(message);
		}
	}
}
