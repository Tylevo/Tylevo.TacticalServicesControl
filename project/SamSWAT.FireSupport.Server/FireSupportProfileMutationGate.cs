using SPTarkov.DI.Annotations;
using System.Threading;

namespace SamSWAT.FireSupport.ArysReloaded;

/// <summary>
/// Serializes every TSC mutation of a live PMC profile. Authorization
/// purchases and UH-60 transfer-fee transactions share this singleton so
/// neither can snapshot or save over the other's inventory mutation.
/// </summary>
[Injectable(InjectionType.Singleton)]
public sealed class FireSupportProfileMutationGate
{
	private readonly SemaphoreSlim _gate = new(1, 1);

	public async Task<T> RunAsync<T>(Func<Task<T>> operation)
	{
		ArgumentNullException.ThrowIfNull(operation);
		await _gate.WaitAsync();
		try
		{
			return await operation();
		}
		finally
		{
			_gate.Release();
		}
	}
}
