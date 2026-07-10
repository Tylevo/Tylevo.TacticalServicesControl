using Cysharp.Threading.Tasks;
using System.Threading;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public static class A10StrikeExecutorSelector
{
	private static readonly A10VisualRuntimeExecutor s_visualRuntime = new();
	private static readonly A10ClientVisualPredictionExecutor s_clientVisualPrediction = new();
	private static readonly A10HeadlessDamageExecutor s_headlessDamage = new();

	public static UniTask<bool> ExecuteAsync(A10StrikeRequest request, CancellationToken cancellationToken)
	{
		IA10StrikeExecutor executor = request.Role switch
		{
			A10AuthorityRole.FikaHeadlessHost => s_headlessDamage,
			A10AuthorityRole.FikaClient => s_clientVisualPrediction,
			_ => s_visualRuntime
		};

		return executor.ExecuteAsync(request, cancellationToken);
	}
}
