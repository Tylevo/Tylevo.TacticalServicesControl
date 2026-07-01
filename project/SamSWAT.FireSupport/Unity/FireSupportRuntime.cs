using Cysharp.Threading.Tasks;
using SamSWAT.FireSupport.ArysReloaded.Utils;
using System;
using System.Threading;
using UnityEngine;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public static class FireSupportRuntime
{
	private static readonly SemaphoreSlim s_initializeLock = new(1, 1);

	public static async UniTask EnsureInitialized()
	{
		if (IsInitialized())
		{
			return;
		}

		await s_initializeLock.WaitAsync();
		try
		{
			if (FireSupportAudio.Instance == null)
			{
				await FireSupportAudio.Create();
			}

			if (FireSupportPoolManager.Instance == null)
			{
				await FireSupportPoolManager.Initialize(10);
			}
		}
		finally
		{
			s_initializeLock.Release();
		}
	}

	public static async UniTask<bool> TryProcessRequest(
		ESupportType supportType,
		Vector3 position,
		Vector3 direction,
		Vector3 rotation,
		bool visualOnly,
		int visualSeed,
		CancellationToken cancellationToken,
		int passIndex = 0)
	{
		try
		{
			await EnsureInitialized();

			ESupportType pooledSupportType = GetPooledSupportType(supportType);
			IFireSupportBehaviour behaviour = FireSupportPoolManager.Instance.TakeFromPool(pooledSupportType);
			ApplyVariantSettings(behaviour, supportType);
			behaviour.ProcessRequest(position, direction, rotation, cancellationToken, visualOnly, visualSeed, passIndex);
			return true;
		}
		catch (OperationCanceledException)
		{
			return false;
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource.LogError(ex);
			return false;
		}
	}

	private static bool IsInitialized()
	{
		return FireSupportAudio.Instance != null && FireSupportPoolManager.Instance != null;
	}

	private static ESupportType GetPooledSupportType(ESupportType supportType)
	{
		return supportType switch
		{
			ESupportType.PriorityExfil => ESupportType.Extract,
			_ => supportType
		};
	}

	private static void ApplyVariantSettings(IFireSupportBehaviour behaviour, ESupportType requestedSupportType)
	{
		if (behaviour is UH60Behaviour uh60Behaviour)
		{
			uh60Behaviour.SetPriorityExfil(requestedSupportType == ESupportType.PriorityExfil);
		}
	}

	public static void Dispose()
	{
		try
		{
			FireSupportPoolManager.Instance?.Dispose();
			FireSupportAudio.Instance?.Dispose();
			AssetLoader.UnloadAllBundles();
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource.LogError(ex);
		}
	}
}
