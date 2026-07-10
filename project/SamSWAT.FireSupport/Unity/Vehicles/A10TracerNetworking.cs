using Cysharp.Threading.Tasks;
using EFT.Communications;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public static class A10TracerNetworking
{
	public delegate void TracerBurstCreatedHandler(A10TracerBurst burst);

	private const float PendingBurstFallbackSeconds = 10f;
	public const float ClientVisualFireDelaySeconds = 8f;

	private static int s_nextBurstId;
	private static readonly object s_predictionGate = new();
	private static readonly Dictionary<string, int> s_localPredictions = new(StringComparer.Ordinal);
	private static readonly HashSet<string> s_confirmedBursts = new(StringComparer.Ordinal);
	private static readonly Dictionary<string, float> s_clientVisualFireStartTimes = new(StringComparer.Ordinal);
	private static readonly Dictionary<string, List<PendingTracerBurst>> s_pendingHostBursts = new(StringComparer.Ordinal);
	private static string s_currentSupportRequestId = string.Empty;
	private static string s_currentRequesterProfileId = string.Empty;

	public static event TracerBurstCreatedHandler TracerBurstCreated;

	public static bool IsNetworkAuthorityActive { get; private set; }
	public static string CurrentAuthorityRole { get; private set; } = "Singleplayer";
	public static string CurrentSupportRequestId => s_currentSupportRequestId;
	public static string CurrentRequesterProfileId => s_currentRequesterProfileId;

	public static void SetNetworkAuthorityActive(bool active, string reason)
	{
		IsNetworkAuthorityActive = active;
		if (!active)
		{
			lock (s_predictionGate)
			{
				s_localPredictions.Clear();
				s_confirmedBursts.Clear();
				s_clientVisualFireStartTimes.Clear();
				s_pendingHostBursts.Clear();
			}
		}

		FireSupportPlugin.LogSource?.LogInfo(
			$"A-10 tracer network authority {(active ? "enabled" : "disabled")} role={CurrentAuthorityRole} reason={reason}");
	}

	public static void SetAuthorityRole(string role)
	{
		CurrentAuthorityRole = string.IsNullOrWhiteSpace(role) ? "Singleplayer" : role.Trim();
	}

	public static void PushSupportRequestContext(string supportRequestId, string requesterProfileId)
	{
		s_currentSupportRequestId = supportRequestId ?? string.Empty;
		s_currentRequesterProfileId = requesterProfileId ?? string.Empty;
	}

	public static void PopSupportRequestContext(string supportRequestId)
	{
		if (string.IsNullOrWhiteSpace(supportRequestId) || string.Equals(s_currentSupportRequestId, supportRequestId, StringComparison.Ordinal))
		{
			s_currentSupportRequestId = string.Empty;
			s_currentRequesterProfileId = string.Empty;
		}
	}

	public static int NextBurstId()
	{
		return Interlocked.Increment(ref s_nextBurstId);
	}

	public static void PublishBurst(A10TracerBurst burst)
	{
		TracerBurstCreatedHandler handler = TracerBurstCreated;
		handler?.Invoke(burst);
	}

	public static void TrackLocalVisualPrediction(string supportRequestId, ESupportType supportType, int visualSeed, int passIndex, Vector3 centerPosition, CancellationToken cancellationToken)
	{
		if (supportType != ESupportType.Strafe)
		{
			return;
		}

		string key = BuildBurstKey(supportRequestId, visualSeed, passIndex);
		lock (s_predictionGate)
		{
			s_localPredictions[key] = s_localPredictions.TryGetValue(key, out int count) ? count + 1 : 1;
			s_confirmedBursts.Remove(key);
		}

		WatchLocalPredictionAsync(key, supportRequestId, visualSeed, passIndex, centerPosition, cancellationToken).Forget();
	}

	public static void MarkClientVisualPassStarted(string supportRequestId, int visualSeed, int passIndex, CancellationToken cancellationToken)
	{
		string key = BuildBurstKey(supportRequestId, visualSeed, passIndex);
		List<PendingTracerBurst> pendingBursts = null;
		float localFireStartTime = Time.time + ClientVisualFireDelaySeconds;

		lock (s_predictionGate)
		{
			s_clientVisualFireStartTimes[key] = localFireStartTime;
			if (s_pendingHostBursts.TryGetValue(key, out pendingBursts))
			{
				s_pendingHostBursts.Remove(key);
			}
		}

		FireSupportPlugin.LogSource?.LogInfo(
			$"TSC A-10 client visual pass started role={CurrentAuthorityRole} requestId={A10AuthorityDiagnostics.ShortId(supportRequestId)} pass={passIndex} seed={visualSeed} tracerFireStartIn={ClientVisualFireDelaySeconds:0.0}s localFireStart={localFireStartTime:0.000}");

		if (pendingBursts == null)
		{
			return;
		}

		foreach (PendingTracerBurst pending in pendingBursts)
		{
			PlayPendingBurst(pending, localFireStartTime, "aligned-after-visual-start");
		}
	}

	public static void QueueOrPlayHostBurst(
		string supportRequestId,
		int visualSeed,
		int passIndex,
		A10TracerSegment[] segments,
		CancellationToken cancellationToken,
		bool spawnImpactEffects)
	{
		if (segments == null || segments.Length == 0)
		{
			return;
		}

		MarkHostBurstReceived(supportRequestId, visualSeed, passIndex, segments.Length, spawnImpactEffects);

		string key = BuildBurstKey(supportRequestId, visualSeed, passIndex);
		float localFireStartTime = 0f;
		bool hasVisualStart;
		var pending = new PendingTracerBurst(
			supportRequestId,
			visualSeed,
			passIndex,
			segments,
			cancellationToken,
			spawnImpactEffects);

		lock (s_predictionGate)
		{
			hasVisualStart = s_clientVisualFireStartTimes.TryGetValue(key, out localFireStartTime);
			if (!hasVisualStart)
			{
				if (!s_pendingHostBursts.TryGetValue(key, out List<PendingTracerBurst> pendingBursts))
				{
					pendingBursts = new List<PendingTracerBurst>();
					s_pendingHostBursts[key] = pendingBursts;
				}

				pendingBursts.Add(pending);
			}
		}

		if (hasVisualStart)
		{
			PlayPendingBurst(pending, localFireStartTime, "aligned-existing-visual");
			return;
		}

		FireSupportPlugin.LogSource?.LogInfo(
			$"TSC A-10 tracer burst queued until client visual starts role={CurrentAuthorityRole} requestId={A10AuthorityDiagnostics.ShortId(supportRequestId)} pass={passIndex} seed={visualSeed} segments={segments.Length}");
		PlayQueuedBurstFallbackAsync(key, pending).Forget();
	}

	public static void MarkHostBurstReceived(string supportRequestId, int visualSeed, int passIndex, int segmentCount, bool spawnImpactEffects = false)
	{
		string key = BuildBurstKey(supportRequestId, visualSeed, passIndex);
		lock (s_predictionGate)
		{
			s_confirmedBursts.Add(key);
			s_localPredictions.Remove(key);
		}

		FireSupportPlugin.LogSource?.LogInfo(
			$"TSC A-10 tracer burst received role={CurrentAuthorityRole} requestId={A10AuthorityDiagnostics.ShortId(supportRequestId)} pass={passIndex} seed={visualSeed} segments={segmentCount} spawnImpactEffects={spawnImpactEffects}");
	}

	private static void PlayPendingBurst(PendingTracerBurst pending, float localFireStartTime, string reason)
	{
		A10TracerPlayback.PlayAtLocalTime(
			pending.Segments,
			localFireStartTime,
			pending.CancellationToken,
			pending.SpawnImpactEffects,
			reason);
	}

	private static async UniTaskVoid PlayQueuedBurstFallbackAsync(string key, PendingTracerBurst pending)
	{
		try
		{
			await UniTask.WaitForSeconds(PendingBurstFallbackSeconds, cancellationToken: pending.CancellationToken);

			bool stillPending = false;
			lock (s_predictionGate)
			{
				if (s_pendingHostBursts.TryGetValue(key, out List<PendingTracerBurst> pendingBursts))
				{
					stillPending = pendingBursts.Remove(pending);
					if (pendingBursts.Count == 0)
					{
						s_pendingHostBursts.Remove(key);
					}
				}
			}

			if (!stillPending)
			{
				return;
			}

			FireSupportPlugin.LogSource?.LogWarning(
				$"TSC A-10 tracer burst fallback playback; no matching client visual pass started. requestId={A10AuthorityDiagnostics.ShortId(pending.SupportRequestId)} pass={pending.PassIndex} seed={pending.VisualSeed}");
			PlayPendingBurst(pending, Time.time, "fallback-no-visual-pass");
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource?.LogWarning($"TSC A-10 queued tracer fallback failed. {ex.Message}");
		}
	}

	private static async UniTaskVoid WatchLocalPredictionAsync(string key, string supportRequestId, int visualSeed, int passIndex, Vector3 centerPosition, CancellationToken cancellationToken)
	{
		try
		{
			await UniTask.WaitForSeconds(8f, cancellationToken: cancellationToken);
			lock (s_predictionGate)
			{
				if (s_confirmedBursts.Contains(key) || !s_localPredictions.Remove(key))
				{
					return;
				}
			}

			string message = "TSC A-10 warning: local visual played but no host authoritative A-10 burst was received. Damage may not have executed on host/headless.";
			FireSupportPlugin.LogSource?.LogWarning(
				$"{message} requestId={A10AuthorityDiagnostics.ShortId(supportRequestId)} pass={passIndex} seed={visualSeed} center={A10AuthorityDiagnostics.FormatVector(centerPosition)}");
			try
			{
				NotificationManagerClass.DisplayWarningNotification(message, ENotificationDurationType.Long);
			}
			catch
			{
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource?.LogWarning($"TSC A-10 local prediction monitor failed. {ex.Message}");
		}
	}

	private static string BuildBurstKey(string supportRequestId, int visualSeed, int passIndex)
	{
		return string.IsNullOrWhiteSpace(supportRequestId)
			? $"{visualSeed}:{passIndex}"
			: $"{supportRequestId}:{visualSeed}:{passIndex}";
	}

	private sealed class PendingTracerBurst
	{
		public readonly string SupportRequestId;
		public readonly int VisualSeed;
		public readonly int PassIndex;
		public readonly A10TracerSegment[] Segments;
		public readonly CancellationToken CancellationToken;
		public readonly bool SpawnImpactEffects;

		public PendingTracerBurst(
			string supportRequestId,
			int visualSeed,
			int passIndex,
			A10TracerSegment[] segments,
			CancellationToken cancellationToken,
			bool spawnImpactEffects)
		{
			SupportRequestId = supportRequestId ?? string.Empty;
			VisualSeed = visualSeed;
			PassIndex = passIndex;
			Segments = segments ?? Array.Empty<A10TracerSegment>();
			CancellationToken = cancellationToken;
			SpawnImpactEffects = spawnImpactEffects;
		}
	}
}
