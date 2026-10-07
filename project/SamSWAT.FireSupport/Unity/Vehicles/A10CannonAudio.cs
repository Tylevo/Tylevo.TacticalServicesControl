using Cysharp.Threading.Tasks;
using System;
using System.Threading;
using UnityEngine;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

internal static class A10CannonAudio
{
	private const float MinimumDistance = 450f;
	private const float MaximumDistance = 3200f;
	private const float CannonGain = 2f;

	public static void PlayAtPoint(
		BetterAudio audio, Vector3 position, AudioClip clip, float listenerDistance,
		string requestId, int pass, CancellationToken cancellationToken)
	{
		PlayAsync(audio, position, clip, listenerDistance, requestId, pass, cancellationToken).Forget();
	}

	private static async UniTask PlayAsync(
		BetterAudio audio, Vector3 position, AudioClip clip, float listenerDistance,
		string requestId, int pass, CancellationToken cancellationToken)
	{
		GameObject soundObject = null;
		try
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (audio == null || audio.GunshotMixerGroup == null || clip == null)
			{
				FireSupportPlugin.LogSource?.LogWarning("TSC A-10 cannon audio unavailable: gunshot mixer or clip missing.");
				return;
			}
			if (listenerDistance > MaximumDistance) return;

			// Own this long aircraft report separately. EFT 4.1's SuperSource clamps
			// the original gain of 2 to 1 and applies the ordinary gunshot falloff.
			soundObject = new GameObject("TSC A-10 Cannon Report");
			soundObject.transform.position = position;
			AudioSource source = soundObject.AddComponent<AudioSource>();
			source.playOnAwake = false;
			source.loop = false;
			source.volume = 1f;
			source.pitch = 1f;
			source.dopplerLevel = 0f;
			source.spatialBlend = 1f;
			source.priority = A10StrikeAudio.Priority;
			source.rolloffMode = AudioRolloffMode.Logarithmic;
			source.minDistance = MinimumDistance;
			source.maxDistance = MaximumDistance;
			source.outputAudioMixerGroup = audio.GunshotMixerGroup;
			source.PlayOneShot(clip, CannonGain);

			double startDspTime = AudioSettings.dspTime;
			double endDspTime = startDspTime + clip.length + 0.25d;
			string context = $"requestId={A10AuthorityDiagnostics.ShortId(requestId)} pass={pass} clip={clip.name} length={clip.length:0.000}s distance={listenerDistance:0.0}m gain={CannonGain:0.00} minDistance={MinimumDistance:0} maxDistance={MaximumDistance:0}";
			LogState(source, context, 0d);
			int nextObservation = 1;
			// Follow the audio clock so gameplay time scale or audio suspension cannot
			// cut the recording short. The object is never parented to the pooled jet.
			while (soundObject != null && AudioSettings.dspTime < endDspTime)
			{
				await UniTask.NextFrame(PlayerLoopTiming.Update, cancellationToken);
				double elapsed = AudioSettings.dspTime - startDspTime;
				if (nextObservation <= 3 && elapsed >= nextObservation)
				{
					LogState(source, context, elapsed);
					nextObservation += 2;
				}
			}
		}
		catch (OperationCanceledException)
		{
			// Raid cancellation releases only this owned recording.
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource?.LogWarning($"TSC A-10 cannon playback failed: {ex.Message}");
		}
		finally
		{
			if (soundObject != null) UnityEngine.Object.Destroy(soundObject);
		}
	}

	private static void LogState(AudioSource source, string context, double elapsed)
	{
		try
		{
			if (source == null) return;
			FireSupportPlugin.LogSource?.LogInfo(
				$"TSC A-10 dedicated cannon state {context} elapsed={elapsed:0.000}s playing={source.isPlaying} virtual={source.isVirtual} enabled={source.isActiveAndEnabled} volume={source.volume:0.00} mute={source.mute} priority={source.priority} mixer={source.outputAudioMixerGroup?.name ?? "<none>"}.");
		}
		catch (Exception)
		{
			// Diagnostic failures must not interrupt the recording.
		}
	}
}
