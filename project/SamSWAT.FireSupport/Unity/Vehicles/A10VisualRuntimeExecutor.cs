using Cysharp.Threading.Tasks;
using System.Threading;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public sealed class A10VisualRuntimeExecutor : IA10StrikeExecutor
{
	public async UniTask<bool> ExecuteAsync(A10StrikeRequest request, CancellationToken cancellationToken)
	{
		A10AuthorityDiagnostics.LogExecutorSelected(
			request.Role,
			request.VisualOnly ? "VisualRuntime/VisualOnly" : "VisualRuntime/Authoritative",
			request.SupportType,
			request.PassIndex,
			request.VisualSeed,
			request.SupportRequestId,
			$"requester={request.RequesterProfileId}");

		// SeasonalAmbient remains a distinct authority/payment semantic, but EFT
		// 4.1 ballistics must resolve every live shot to a real player bridge.
		string projectileOwnerProfileId = request.RequesterProfileId;
		return await FireSupportRuntime.TryProcessRequest(
				request.SupportType,
				request.Position,
				request.Direction,
				request.Rotation,
				request.VisualOnly,
				request.VisualSeed,
				cancellationToken,
				request.PassIndex,
				a10RequestContext: new A10RuntimeRequestContext(
					request.SupportRequestId,
					request.RequesterProfileId,
					projectileOwnerProfileId));
	}
}
