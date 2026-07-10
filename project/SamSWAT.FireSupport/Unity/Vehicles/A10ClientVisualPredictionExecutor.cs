using Cysharp.Threading.Tasks;
using System.Threading;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public sealed class A10ClientVisualPredictionExecutor : IA10StrikeExecutor
{
	private static readonly A10VisualRuntimeExecutor s_visualRuntime = new();

	public UniTask<bool> ExecuteAsync(A10StrikeRequest request, CancellationToken cancellationToken)
	{
		var visualRequest = new A10StrikeRequest
		{
			SupportRequestId = request.SupportRequestId,
			SupportType = request.SupportType,
			Position = request.Position,
			Direction = request.Direction,
			Rotation = request.Rotation,
			VisualSeed = request.VisualSeed,
			PassIndex = request.PassIndex,
			RequesterProfileId = request.RequesterProfileId,
			VisualOnly = true,
			Role = A10AuthorityRole.FikaClient
		};

		A10TracerNetworking.MarkClientVisualPassStarted(
			visualRequest.SupportRequestId,
			visualRequest.VisualSeed,
			visualRequest.PassIndex,
			cancellationToken);

		return s_visualRuntime.ExecuteAsync(visualRequest, cancellationToken);
	}
}
