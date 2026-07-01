using System.Threading;
using UnityEngine;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public abstract class FireSupportBehaviour : UpdatableComponentBase, IFireSupportBehaviour
{
	public abstract ESupportType SupportType { get; }

	public abstract void ProcessRequest(
		Vector3 position,
		Vector3 direction,
		Vector3 rotation,
		CancellationToken cancellationToken,
		bool visualOnly = false,
		int visualSeed = 0,
		int passIndex = 0);

	public void ReturnToPool()
	{
		FireSupportPoolManager.Instance.ReturnToPool(this);
	}
}
