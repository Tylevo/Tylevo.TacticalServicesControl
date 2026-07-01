using System;
using System.Threading;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public static class UavA10LoiterNetworking
{
	public delegate bool StartLoiterHandler(UavA10LoiterRequest request, CancellationToken cancellationToken);

	public static event StartLoiterHandler StartRequested;

	public static bool TryHandleStart(UavA10LoiterRequest request, CancellationToken cancellationToken)
	{
		StartLoiterHandler handler = StartRequested;
		if (handler == null)
		{
			return false;
		}

		try
		{
			return handler.Invoke(request, cancellationToken);
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource.LogWarning($"UAV A-10 loiter sync failed; skipping aircraft visual. {ex}");
			return true;
		}
	}
}
