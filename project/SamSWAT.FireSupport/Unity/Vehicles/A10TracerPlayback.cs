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
		CancellationToken cancellationToken,
		bool spawnImpactEffects = false)
	{
		// Historical callers pass Time.time as fireStartNetworkTime. Treat it as a
		// local playback start time here. Fika clients should prefer PlayAtLocalTime
		// via A10TracerNetworking so host timestamps are never used directly across machines.
		PlayAtLocalTime(segments, fireStartNetworkTime, cancellationToken, spawnImpactEffects);
	}

	public static void PlayAtLocalTime(
		A10TracerSegment[] segments,
		float localPlaybackStartTime,
		CancellationToken cancellationToken,
		bool spawnImpactEffects = false,
		string reason = "direct")
	{
		if (segments == null || segments.Length == 0)
		{
			return;
		}

		A10TracerSegment[] orderedSegments = new A10TracerSegment[segments.Length];
		Array.Copy(segments, orderedSegments, segments.Length);
		Array.Sort(orderedSegments, (left, right) => left.DelaySeconds.CompareTo(right.DelaySeconds));
		FireSupportPlugin.LogSource?.LogInfo(
			$"TSC A-10 tracer playback scheduled reason={reason} segments={orderedSegments.Length} localFireStart={localPlaybackStartTime:0.000} now={Time.time:0.000} spawnImpactEffects={spawnImpactEffects}");
		PlayAsync(orderedSegments, localPlaybackStartTime, cancellationToken, spawnImpactEffects).Forget();
	}

	private static async UniTaskVoid PlayAsync(
		A10TracerSegment[] segments,
		float localPlaybackStartTime,
		CancellationToken cancellationToken,
		bool spawnImpactEffects)
	{
		try
		{
			int renderedTracers = 0;
			int spawnedImpactEffects = 0;
			foreach (A10TracerSegment segment in segments)
			{
				if (!segment.IsValid)
				{
					continue;
				}

				float waitSeconds = localPlaybackStartTime + segment.DelaySeconds - Time.time;
				if (waitSeconds > 0f)
				{
					await UniTask.WaitForSeconds(waitSeconds, cancellationToken: cancellationToken);
				}

				if (cancellationToken.IsCancellationRequested)
				{
					return;
				}

				A10Behaviour.RenderVisualTracerSegment(segment, prominentReplay: spawnImpactEffects);
				renderedTracers++;
				if (spawnImpactEffects)
				{
					if (A10ImpactEffectPlayback.TrySpawn(segment))
					{
						spawnedImpactEffects++;
					}
				}
			}

			if (spawnImpactEffects)
			{
				FireSupportPlugin.LogSource?.LogInfo(
					$"TSC A-10 tracer replay complete rendered={renderedTracers}/{segments.Length} impactEffects={spawnedImpactEffects}/{segments.Length}");
			}
			else
			{
				FireSupportPlugin.LogSource?.LogInfo(
					$"TSC A-10 tracer replay complete rendered={renderedTracers}/{segments.Length} impactEffects=disabled");
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
