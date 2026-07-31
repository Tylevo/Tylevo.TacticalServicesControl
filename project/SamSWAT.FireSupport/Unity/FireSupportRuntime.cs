using Cysharp.Threading.Tasks;
using SamSWAT.FireSupport.ArysReloaded.Utils;
using System;
using System.Threading;
using UnityEngine;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public static class FireSupportRuntime
{
	private static readonly SemaphoreSlim s_initializeLock = new(1, 1);
	private static int s_lifecycleGeneration;

	public static async UniTask EnsureInitialized(
		CancellationToken cancellationToken = default)
	{
		int lifecycleGeneration = Volatile.Read(ref s_lifecycleGeneration);
		ThrowIfInitializationInvalid(lifecycleGeneration, cancellationToken);
		if (IsInitialized())
		{
			return;
		}

		await s_initializeLock.WaitAsync(cancellationToken);
		bool createdAudio = false;
		bool createdPool = false;
		try
		{
			ThrowIfInitializationInvalid(lifecycleGeneration, cancellationToken);
			if (FireSupportAudio.Instance == null)
			{
				createdAudio = true;
				await FireSupportAudio.Create();
				ThrowIfInitializationInvalid(lifecycleGeneration, cancellationToken);
			}

			if (FireSupportPoolManager.Instance == null)
			{
				createdPool = true;
				await FireSupportPoolManager.Initialize(
					10,
					() => ThrowIfInitializationInvalid(
						lifecycleGeneration,
						cancellationToken));
				ThrowIfInitializationInvalid(lifecycleGeneration, cancellationToken);
			}
		}
		catch
		{
			if (createdPool)
			{
				FireSupportPoolManager.Instance?.Dispose();
			}

			if (createdAudio)
			{
				FireSupportAudio.Instance?.Dispose();
			}

			if (lifecycleGeneration != Volatile.Read(ref s_lifecycleGeneration))
			{
				AssetLoader.UnloadAllBundles();
			}

			throw;
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
		int passIndex = 0,
		HelicopterTimingSnapshot? helicopterTimingSnapshot = null,
		bool allowLocalHelicopterServicePoint = true,
		string supportRequestId = "")
	{
		FireSupportBehaviour leasedBehaviour = null;
		bool requestStarted = false;
		try
		{
			cancellationToken.ThrowIfCancellationRequested();
			await EnsureInitialized(cancellationToken);
			cancellationToken.ThrowIfCancellationRequested();

			ESupportType pooledSupportType = GetPooledSupportType(supportType);
			leasedBehaviour =
				(FireSupportBehaviour)FireSupportPoolManager.Instance.TakeFromPool(
					pooledSupportType);
			ApplyVariantSettings(
				leasedBehaviour,
				supportType,
				helicopterTimingSnapshot,
				allowLocalHelicopterServicePoint,
				supportRequestId);
			cancellationToken.ThrowIfCancellationRequested();
			leasedBehaviour.ProcessRequest(
				position,
				direction,
				rotation,
				cancellationToken,
				visualOnly,
				visualSeed,
				passIndex);
			requestStarted = true;
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
		finally
		{
			if (!requestStarted && leasedBehaviour != null)
			{
				leasedBehaviour.ReturnToPool();
			}
		}
	}

	private static bool IsInitialized()
	{
		return FireSupportAudio.Instance != null && FireSupportPoolManager.Instance != null;
	}

	private static ESupportType GetPooledSupportType(ESupportType supportType)
	{
		// Cargo reuses the released UH-60 prefab stored under the Extract asset
		// key. This maps assets only; the leased behavior creates a distinct
		// cargo or extraction service point from the requested support type.
		return supportType switch
		{
			ESupportType.DoubleStrafe => ESupportType.Strafe,
			ESupportType.PriorityExfil => ESupportType.Extract,
			_ => supportType
		};
	}

	private static void ApplyVariantSettings(
		IFireSupportBehaviour behaviour,
		ESupportType requestedSupportType,
		HelicopterTimingSnapshot? helicopterTimingSnapshot,
		bool allowLocalHelicopterServicePoint,
		string supportRequestId)
	{
		if (behaviour is UH60Behaviour uh60Behaviour)
		{
			uh60Behaviour.SetRequestTiming(
				requestedSupportType,
				helicopterTimingSnapshot,
				allowLocalHelicopterServicePoint,
				supportRequestId);
		}
	}

	public static void Dispose()
	{
		Interlocked.Increment(ref s_lifecycleGeneration);
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

	private static void ThrowIfInitializationInvalid(
		int lifecycleGeneration,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (lifecycleGeneration != Volatile.Read(ref s_lifecycleGeneration))
		{
			throw new OperationCanceledException(
				"Fire-support runtime lifecycle changed during initialization.");
		}
	}
}
