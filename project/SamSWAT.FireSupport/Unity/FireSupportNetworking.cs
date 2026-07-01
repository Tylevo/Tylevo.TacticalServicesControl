using System.Threading;
using UnityEngine;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public static class FireSupportNetworking
{
	public delegate bool SupportRequestHandler(
		ESupportType supportType,
		Vector3 position,
		Vector3 direction,
		Vector3 rotation,
		int visualSeed,
		float durationSeconds,
		int passIndex,
		CancellationToken cancellationToken);

	public static event SupportRequestHandler SupportRequested;

	public static bool TryHandleSupportRequest(
		ESupportType supportType,
		Vector3 position,
		Vector3 direction,
		Vector3 rotation,
		CancellationToken cancellationToken,
		float durationSeconds = 0f,
		int passIndex = 0)
	{
		SupportRequestHandler handler = SupportRequested;
		return handler != null &&
		       handler.Invoke(
			       supportType,
			       position,
			       direction,
			       rotation,
			       UnityEngine.Random.Range(1, int.MaxValue),
			       durationSeconds,
			       passIndex,
			       cancellationToken);
	}
}
