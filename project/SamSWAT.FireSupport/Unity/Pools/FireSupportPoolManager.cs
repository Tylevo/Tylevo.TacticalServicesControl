using Cysharp.Threading.Tasks;
using SamSWAT.FireSupport.ArysReloaded.Utils;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public class FireSupportPoolManager : IDisposable
{
	private readonly Dictionary<ESupportType, FireSupportPool> _pools = new(new SupportTypeComparer());

	public static FireSupportPoolManager Instance { get; private set; }

	public Transform PoolTransform { get; private set; }

	public static async UniTask Initialize(int poolSize)
	{
		if (Instance?.PoolTransform != null)
		{
			return;
		}

		Instance ??= new FireSupportPoolManager();

		GameObject jetAsset = await AssetLoader.LoadAssetAsync("assets/content/vehicles/a10_warthog.bundle");
		A10Behaviour jetStrafeObj = jetAsset != null ? jetAsset.GetComponent<A10Behaviour>() : null;
		if (jetStrafeObj == null)
		{
			throw new InvalidOperationException("A-10 asset bundle did not contain A10Behaviour.");
		}

		GameObject heliAsset = await AssetLoader.LoadAssetAsync("assets/content/vehicles/uh60_blackhawk.bundle");
		UH60Behaviour heliExfilObj = heliAsset != null ? heliAsset.GetComponent<UH60Behaviour>() : null;
		if (heliExfilObj == null)
		{
			throw new InvalidOperationException("UH-60 asset bundle did not contain UH60Behaviour.");
		}

		Transform poolTransform = new GameObject("FireSupportPool").transform;
		Instance.PoolTransform = poolTransform;

		var jetStrafePool = new FireSupportPool(poolSize, jetStrafeObj, poolTransform);
		jetStrafePool.Fill();
		Instance._pools.Add(jetStrafeObj.SupportType, jetStrafePool);

		var heliExfilPool = new FireSupportPool(poolSize, heliExfilObj, poolTransform);
		heliExfilPool.Fill();
		Instance._pools.Add(heliExfilObj.SupportType, heliExfilPool);
	}

	public void Dispose()
	{
		_pools.Clear();
		if (PoolTransform != null)
		{
			UnityEngine.Object.DestroyImmediate(PoolTransform.gameObject);
			PoolTransform = null;
		}

		Instance = null;
	}

	public IFireSupportBehaviour TakeFromPool(ESupportType supportType)
	{
		if (!_pools.TryGetValue(supportType, out FireSupportPool pool))
		{
			throw new ArgumentException("No pool found for support type: " + supportType);
		}

		FireSupportBehaviour behaviour = pool.TakeFromPool();
		behaviour.transform.SetParent(null, true);
		behaviour.gameObject.SetActive(true);

		return behaviour;
	}

	public void ReturnToPool(FireSupportBehaviour behaviour)
	{
		if (!_pools.TryGetValue(behaviour.SupportType, out FireSupportPool pool))
		{
			throw new ArgumentException("No pool found for support type: " + behaviour.SupportType);
		}

		behaviour.gameObject.SetActive(false);
		behaviour.transform.SetParent(PoolTransform);
		behaviour.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
		pool.ReturnToPool(behaviour);
	}
}
