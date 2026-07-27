using Cysharp.Threading.Tasks;
using System;
using System.Threading;
using UnityEngine;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public enum FireSupportNetworkRequestState
{
	NotHandled,
	Accepted,
	Rejected,
	TimedOut,
	Cancelled
}

public readonly struct FireSupportNetworkRequestResult
{
	private FireSupportNetworkRequestResult(
		FireSupportNetworkRequestState state,
		string reason,
		float durationSeconds = 0f,
		float scanIntervalSeconds = 0f,
		float rangeMeters = 0f)
	{
		State = state;
		Reason = reason ?? string.Empty;
		DurationSeconds = durationSeconds;
		ScanIntervalSeconds = scanIntervalSeconds;
		RangeMeters = rangeMeters;
	}

	public FireSupportNetworkRequestState State { get; }
	public string Reason { get; }
	public float DurationSeconds { get; }
	public float ScanIntervalSeconds { get; }
	public float RangeMeters { get; }
	public bool Handled => State != FireSupportNetworkRequestState.NotHandled;
	public bool Accepted => State == FireSupportNetworkRequestState.Accepted;

	public static FireSupportNetworkRequestResult NotHandled(string reason = "")
	{
		return new FireSupportNetworkRequestResult(FireSupportNetworkRequestState.NotHandled, reason);
	}

	public static FireSupportNetworkRequestResult Accept(
		string reason = "",
		float durationSeconds = 0f,
		float scanIntervalSeconds = 0f,
		float rangeMeters = 0f)
	{
		return new FireSupportNetworkRequestResult(
			FireSupportNetworkRequestState.Accepted,
			reason,
			durationSeconds,
			scanIntervalSeconds,
			rangeMeters);
	}

	public static FireSupportNetworkRequestResult Reject(string reason)
	{
		return new FireSupportNetworkRequestResult(FireSupportNetworkRequestState.Rejected, reason);
	}

	public static FireSupportNetworkRequestResult Timeout(string reason = "AuthorityResponseTimedOut")
	{
		return new FireSupportNetworkRequestResult(FireSupportNetworkRequestState.TimedOut, reason);
	}

	public static FireSupportNetworkRequestResult Cancel(string reason = "RequestCancelled")
	{
		return new FireSupportNetworkRequestResult(FireSupportNetworkRequestState.Cancelled, reason);
	}
}

public static class FireSupportNetworking
{
	public delegate UniTask<FireSupportNetworkRequestResult> SupportRequestHandler(
		ESupportType supportType,
		Vector3 position,
		Vector3 direction,
		Vector3 rotation,
		int visualSeed,
		float durationSeconds,
		int passIndex,
		string supportRequestId,
		HelicopterTimingSnapshot? helicopterTimingSnapshot,
		CancellationToken cancellationToken);

	public static event SupportRequestHandler SupportRequested;

	public static async UniTask<FireSupportNetworkRequestResult> TryHandleSupportRequestAsync(
		ESupportType supportType,
		Vector3 position,
		Vector3 direction,
		Vector3 rotation,
		CancellationToken cancellationToken,
		float durationSeconds = 0f,
		int passIndex = 0,
		string supportRequestId = "",
		HelicopterTimingSnapshot? helicopterTimingSnapshot = null)
	{
		SupportRequestHandler handler = SupportRequested;
		if (handler == null)
		{
			return FireSupportNetworkRequestResult.NotHandled();
		}

		try
		{
			return await handler.Invoke(
				supportType,
				position,
				direction,
				rotation,
				UnityEngine.Random.Range(1, int.MaxValue),
				durationSeconds,
				passIndex,
				supportRequestId ?? string.Empty,
				helicopterTimingSnapshot,
				cancellationToken);
		}
		catch (OperationCanceledException)
		{
			return FireSupportNetworkRequestResult.Cancel();
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource?.LogError(
				$"TSC network support handler failed type={supportType}, requestId={supportRequestId}. {ex}");
			return FireSupportNetworkRequestResult.Reject("NetworkHandlerFailed");
		}
	}
}
