using Cysharp.Threading.Tasks;
using System.Threading;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public sealed class A10HeadlessDamageExecutor : IA10StrikeExecutor
{
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
