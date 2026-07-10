using Cysharp.Threading.Tasks;
using System.Threading;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public interface IA10StrikeExecutor
{
	UniTask<bool> ExecuteAsync(A10StrikeRequest request, CancellationToken cancellationToken);
}
