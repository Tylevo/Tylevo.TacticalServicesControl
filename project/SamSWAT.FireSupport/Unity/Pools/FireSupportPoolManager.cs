using Cysharp.Threading.Tasks;
using SamSWAT.FireSupport.ArysReloaded.Utils;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public class FireSupportPoolManager : IDisposable
{
	private readonly Dictionary<ESupportType, FireSupportPool> _pools = new(new SupportTypeComparer());
	private readonly HashSet<FireSupportBehaviour> _leasedBehaviours = new();

	public static FireSupportPoolManager Instance { get; private set; }

	public Transform PoolTransform { get; private set; }

	public static async UniTask Initialize(
		int poolSize,
		Action validateState = null)
	{
		validateState?.Invoke();
		if (Instance?.PoolTransform != null)
		{
			return;
		}

		GameObject jetAsset = await AssetLoader.LoadAssetAsync("assets/content/vehicles/a10_warthog.bundle");
		validateState?.Invoke();
		A10Behaviour jetStrafeObj = jetAsset != null ? jetAsset.GetComponent<A10Behaviour>() : null;
		if (jetStrafeObj == null)
		{
			throw new InvalidOperationException("A-10 asset bundle did not contain A10Behaviour.");
		}

		GameObject heliAsset = await AssetLoader.LoadAssetAsync("assets/content/vehicles/uh60_blackhawk.bundle");
		validateState?.Invoke();
		UH60Behaviour heliExfilObj = heliAsset != null ? heliAsset.GetComponent<UH60Behaviour>() : null;
		if (heliExfilObj == null)
		{
			throw new InvalidOperationException("UH-60 asset bundle did not contain UH60Behaviour.");
		}

		var manager = new FireSupportPoolManager();
		Transform poolTransform = null;
		try
		{
			poolTransform = new GameObject("FireSupportPool").transform;
			manager.PoolTransform = poolTransform;

			var jetStrafePool = new FireSupportPool(poolSize, jetStrafeObj, poolTransform);
			jetStrafePool.Fill();
			manager._pools.Add(jetStrafeObj.SupportType, jetStrafePool);

			var heliExfilPool = new FireSupportPool(poolSize, heliExfilObj, poolTransform);
			heliExfilPool.Fill();
			manager._pools.Add(heliExfilObj.SupportType, heliExfilPool);

			if (Instance?.PoolTransform != null)
			{
				UnityEngine.Object.DestroyImmediate(poolTransform.gameObject);
				return;
			}

			Instance = manager;
		}
		catch
		{
			if (poolTransform != null)
			{
				UnityEngine.Object.DestroyImmediate(poolTransform.gameObject);
			}

			throw;
		}
	}

	public void Dispose()
	{
		foreach (FireSupportBehaviour behaviour in
		         new List<FireSupportBehaviour>(_leasedBehaviours))
		{
			if (behaviour != null)
			{
				UnityEngine.Object.DestroyImmediate(behaviour.gameObject);
			}
		}

		_leasedBehaviours.Clear();
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
		_leasedBehaviours.Add(behaviour);
		behaviour.transform.SetParent(null, true);
		behaviour.gameObject.SetActive(true);

		return behaviour;
	}

	public void ReturnToPool(FireSupportBehaviour behaviour)
	{
		if (behaviour == null)
		{
			return;
		}

		if (!_leasedBehaviours.Remove(behaviour))
		{
			return;
		}

		if (!_pools.TryGetValue(behaviour.SupportType, out FireSupportPool pool))
		{
			UnityEngine.Object.DestroyImmediate(behaviour.gameObject);
			return;
		}

		behaviour.gameObject.SetActive(false);
		behaviour.transform.SetParent(PoolTransform);
		behaviour.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
		pool.ReturnToPool(behaviour);
	}
}
