using System.Threading;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public static class A10TracerNetworking
{
	public delegate void TracerBurstCreatedHandler(A10TracerBurst burst);

	private static int s_nextBurstId;

	public static event TracerBurstCreatedHandler TracerBurstCreated;

	public static bool IsNetworkAuthorityActive { get; private set; }

	public static void SetNetworkAuthorityActive(bool active, string reason)
	{
		IsNetworkAuthorityActive = active;
		FireSupportPlugin.LogSource?.LogInfo(
			$"A-10 tracer network authority {(active ? "enabled" : "disabled")} reason={reason}");
	}

	public static int NextBurstId()
	{
		return Interlocked.Increment(ref s_nextBurstId);
	}

	public static void PublishBurst(A10TracerBurst burst)
	{
		TracerBurstCreatedHandler handler = TracerBurstCreated;
		handler?.Invoke(burst);
	}
}
