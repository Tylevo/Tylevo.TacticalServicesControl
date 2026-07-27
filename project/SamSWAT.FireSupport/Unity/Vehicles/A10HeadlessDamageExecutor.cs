using Cysharp.Threading.Tasks;
using System.Threading;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public sealed class A10HeadlessDamageExecutor : IA10StrikeExecutor
{
	public static bool TryPreflight(
		A10StrikeRequest request,
		out string reason)
	{
		A10HeadlessFikaMode mode =
			FireSupportTuningSettings.GetA10HeadlessFikaMode();
		if (mode == A10HeadlessFikaMode.Disabled)
		{
			reason = "HeadlessA10Disabled";
			return false;
		}

		return A10DamageOnlyPass.TryPreflight(request, out reason);
	}

	public static UniTask<bool> ExecuteAcceptedAsync(
		A10StrikeRequest request,
		CancellationToken cancellationToken)
	{
		return A10DamageOnlyPass.ExecuteAsync(request, cancellationToken);
	}

	public async UniTask<bool> ExecuteAsync(A10StrikeRequest request, CancellationToken cancellationToken)
	{
		A10HeadlessFikaMode mode = FireSupportTuningSettings.GetA10HeadlessFikaMode();
		A10AuthorityDiagnostics.LogExecutorSelected(
			request.Role,
			"HeadlessDamage",
			request.SupportType,
			request.PassIndex,
			request.VisualSeed,
			request.SupportRequestId,
			$"mode={mode} requester={request.RequesterProfileId}");

		if (mode == A10HeadlessFikaMode.Disabled)
		{
			A10AuthorityDiagnostics.LogWarning("TSC A-10 damage is disabled on Fika headless.");
			return false;
		}

		return await A10DamageOnlyPass.ExecuteAsync(request, cancellationToken);
	}
}
