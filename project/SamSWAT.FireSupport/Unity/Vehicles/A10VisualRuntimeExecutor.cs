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

		A10TracerNetworking.PushSupportRequestContext(request.SupportRequestId, request.RequesterProfileId);
		try
		{
			return await FireSupportRuntime.TryProcessRequest(
				request.SupportType,
				request.Position,
				request.Direction,
				request.Rotation,
				request.VisualOnly,
				request.VisualSeed,
				cancellationToken,
				request.PassIndex);
		}
		finally
		{
			// FireSupportRuntime starts the A10Behaviour coroutine immediately. Keep the
			// context narrow to avoid stale attribution leaking into later requests.
			A10TracerNetworking.PopSupportRequestContext(request.SupportRequestId);
		}
	}
}
