using Cysharp.Threading.Tasks;
using System;
using System.Linq;
using System.Threading;
using UnityEngine;
using UnityEngine.Audio;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

internal static class A10StrikeAudio
{
	// Native point sounds use 64-128. Protect each authored strike layer equally.
	public const int Priority = 64;

	public static void PlayAtPoint(
		BetterAudio audio, Vector3 position, AudioClip clip, float distance,
		int rolloff, float volume, string layer, string requestId, int pass,
		CancellationToken cancellationToken)
	{
		if (audio == null || clip == null)
		{
			FireSupportPlugin.LogSource?.LogWarning($"TSC A-10 recording unavailable layer={layer}: audio manager or clip missing.");
			return;
		}

		BetterSource source = audio.PlayAtPoint(position, clip, distance,
			BetterAudio.AudioSourceGroupType.Gunshots, rolloff, volume);
		if (source == null)
		{
			FireSupportPlugin.LogSource?.LogWarning(
				$"TSC A-10 recording unavailable layer={layer} clip={clip.name} distance={distance:0.0}m: native playback returned no source.");
			return;
		}

		// Use the virtual setter: SuperSource applies this to both audio channels.
		source.SetPriority(Priority);
		ObserveAsync(source, clip, distance, volume, layer, requestId, pass, cancellationToken).Forget();
	}

	private static async UniTask ObserveAsync(
		BetterSource source, AudioClip clip, float distance, float volume,
		string layer, string requestId, int pass, CancellationToken cancellationToken)
	{
		bool released = false;
		void OnReleased(BetterSource returned)
		{
			released = true;
			returned.OnReleased -= OnReleased;
		}

		try
		{
			// A pooled source can be reused for another sound. Never observe that lease.
			source.OnReleased += OnReleased;
			string context = $"layer={layer} requestId={A10AuthorityDiagnostics.ShortId(requestId)} pass={pass} clip={clip.name} length={clip.length:0.000}s distance={distance:0.0}m requestedVolume={volume:0.00}";
			LogState(source, context, "started");
			await UniTask.WaitForSeconds(1f, cancellationToken: cancellationToken);
			if (released || source == null) return;
			LogState(source, context, "after-1s");
			await UniTask.WaitForSeconds(2f, cancellationToken: cancellationToken);
			if (released || source == null) return;
			LogState(source, context, "after-3s");
		}
		catch (OperationCanceledException)
		{
			// The raid ended; native audio owns the sound's release.
		}
		catch (Exception)
		{
			// Optional diagnostics must not interrupt playback or raid teardown.
		}
		finally
		{
			if (!released) source.OnReleased -= OnReleased;
		}
	}

	private static void LogState(BetterSource source, string context, string phase)
	{
		// Native SimpleSource uses PlayOneShot: AudioSource.clip/time need not refer
		// to this recording. Report its selected clip above and inspect live voices.
		string voices = string.Join("; ", source.GetComponentsInChildren<AudioSource>(true)
			.Select(voice => $"name={voice.name} playing={voice.isPlaying} virtual={voice.isVirtual} enabled={voice.isActiveAndEnabled} volume={voice.volume:0.000} mute={voice.mute} pitch={voice.pitch:0.00} priority={voice.priority} mixer={voice.outputAudioMixerGroup?.name ?? "<none>"}"));
		FireSupportPlugin.LogSource?.LogInfo(
			$"TSC A-10 recording state phase={phase} {context} sourceType={source.GetType().Name} playback={source.PlayBackState} baseVolume={source.BaseVolume:0.000} occlusion={source.OcclusionVolumeFactor:0.000} fade={source.FadeFactor:0.000} voices=[{voices}].");

		if (phase == "started" && source.source1 != null)
		{
			AudioMixer mixer = source.source1.outputAudioMixerGroup != null
				? source.source1.outputAudioMixerGroup.audioMixer : null;
			FireSupportPlugin.LogSource?.LogInfo(
				$"TSC A-10 recording mixer {context} GunsVolume={ReadMixer(mixer, "GunsVolume")} EnvironmentVolume={ReadMixer(mixer, "EnvironmentVolume")} GunsCompressorSendLevel={ReadMixer(mixer, "GunsCompressorSendLevel")} EnvTechnicalCompressorSendLevel={ReadMixer(mixer, "EnvTechnicalCompressorSendLevel")}.");
		}
	}

	private static string ReadMixer(AudioMixer mixer, string parameter)
	{
		return mixer != null && mixer.GetFloat(parameter, out float value)
			? value.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)
			: "unavailable";
	}
}
