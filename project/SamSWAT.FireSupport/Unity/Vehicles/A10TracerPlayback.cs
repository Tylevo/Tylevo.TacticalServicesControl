using Cysharp.Threading.Tasks;
using System;
using System.Threading;
using UnityEngine;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public static class A10TracerPlayback
{
	public static void Play(
		A10TracerSegment[] segments,
		float fireStartNetworkTime,
		CancellationToken cancellationToken)
	{
		if (segments == null || segments.Length == 0)
		{
			return;
		}

		A10TracerSegment[] orderedSegments = new A10TracerSegment[segments.Length];
		Array.Copy(segments, orderedSegments, segments.Length);
		Array.Sort(orderedSegments, (left, right) => left.DelaySeconds.CompareTo(right.DelaySeconds));
		PlayAsync(orderedSegments, fireStartNetworkTime, cancellationToken).Forget();
	}

	private static async UniTaskVoid PlayAsync(
		A10TracerSegment[] segments,
		float fireStartNetworkTime,
		CancellationToken cancellationToken)
	{
		try
		{
			foreach (A10TracerSegment segment in segments)
			{
				if (!segment.IsValid)
				{
					continue;
				}

				float waitSeconds = fireStartNetworkTime + segment.DelaySeconds - Time.time;
				if (waitSeconds > 0f)
				{
					await UniTask.WaitForSeconds(waitSeconds, cancellationToken: cancellationToken);
				}

				if (cancellationToken.IsCancellationRequested)
				{
					return;
				}

				A10Behaviour.RenderVisualTracerSegment(segment);
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource?.LogWarning($"A-10 tracer playback failed. {ex}");
		}
	}
}
