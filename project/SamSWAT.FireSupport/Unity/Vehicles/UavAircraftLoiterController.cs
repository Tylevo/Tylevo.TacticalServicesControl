using Comfort.Common;
using Cysharp.Threading.Tasks;
using SamSWAT.FireSupport.ArysReloaded.Utils;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public sealed class UavAircraftLoiterController : UpdatableComponentBase
{
	private const string A10BundlePath = "assets/content/vehicles/a10_warthog.bundle";
	private const string Uh60BundlePath = "assets/content/vehicles/uh60_blackhawk.bundle";
	private const float FlyoffDurationSeconds = 8f;
	private const float A10TurnBankDegrees = 17f;
	private const float Uh60TurnBankDegrees = 7f;
	private const float A10MinimumCruiseSpeed = 120f;
	private const float Uh60MinimumCruiseSpeed = 45f;
	private const float UavDistantAudioScale = 0.18f;

	private static UavAircraftLoiterController s_activeLoiter;

	private readonly List<AudioSource> _audioSources = new();
	private UavA10LoiterRequest _request;
	private CancellationToken _cancellationToken;
	private Vector3 _flyoffPosition;
	private Vector3 _flyoffDirection;
	private float _flyoffStartTime;
	private float _flyoffSpeed;
	private bool _expired;
	private bool _destroyed;

	public static void StartConfigured(Vector3 center, float durationSeconds, CancellationToken cancellationToken)
	{
		if (!UavA10LoiterSettings.IsEnabled())
		{
			return;
		}

		UavA10LoiterRequest request = UavA10LoiterSettings.CreateConfiguredRequest(center, durationSeconds);
		if (UavA10LoiterNetworking.TryHandleStart(request, cancellationToken))
		{
			return;
		}

		StartLocal(request, cancellationToken);
	}

	public static void StartLocal(UavA10LoiterRequest request, CancellationToken cancellationToken)
	{
		if (!UavA10LoiterSettings.IsEnabled() || request.DurationSeconds <= 0f)
		{
			return;
		}

		StartLocalAsync(request, cancellationToken).Forget();
	}

	public override void ManualUpdate()
	{
		if (_destroyed)
		{
			return;
		}

		if (_cancellationToken.IsCancellationRequested)
		{
			DestroyAircraft();
			return;
		}

		float elapsed = Mathf.Max(0f, Time.time - _request.StartTime);
		if (!_expired && elapsed >= _request.DurationSeconds)
		{
			StartFlyoff();
		}

		if (_expired)
		{
			UpdateFlyoff();
			return;
		}

		if (elapsed < _request.IngressDuration)
		{
			UpdateIngress(elapsed);
			return;
		}

		UpdateOrbit(elapsed - _request.IngressDuration);
	}

	protected override void OnDisable()
	{
		base.OnDisable();

		if (s_activeLoiter == this)
		{
			s_activeLoiter = null;
		}
	}

	private static async UniTaskVoid StartLocalAsync(UavA10LoiterRequest request, CancellationToken cancellationToken)
	{
		try
		{
			await FireSupportRuntime.EnsureInitialized();
			if (cancellationToken.IsCancellationRequested)
			{
				return;
			}

			request = SanitizeRequest(request);
			string bundlePath = GetBundlePath(request.AircraftType);
			GameObject prefab = await AssetLoader.LoadAssetAsync(bundlePath);
			if (prefab == null)
			{
				FireSupportPlugin.LogSource.LogWarning($"UAV {GetAircraftName(request.AircraftType)} loiter skipped: prefab failed to load.");
				return;
			}

			FireSupportPlugin.LogSource.LogInfo($"UAV {GetAircraftName(request.AircraftType)} loiter asset bundle/prefab loaded.");

			if (s_activeLoiter != null)
			{
				s_activeLoiter.DestroyAircraft();
			}

			GameObject aircraft = Instantiate(prefab);
			aircraft.name = $"FireSupport UAV {GetAircraftName(request.AircraftType)} Loiter";

			var controller = aircraft.AddComponent<UavAircraftLoiterController>();
			controller.Initialize(request, cancellationToken);
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource.LogWarning($"UAV loiter visual skipped. {ex}");
		}
	}

	private void Initialize(UavA10LoiterRequest request, CancellationToken cancellationToken)
	{
		_request = request;
		_cancellationToken = cancellationToken;
		s_activeLoiter = this;

		ConfigurePrefabForCosmeticLoiter(gameObject, request);
		UpdateIngress(0f);

		FireSupportPlugin.LogSource.LogInfo(
			$"UAV {GetAircraftName(_request.AircraftType)} loiter started. Center={_request.Center}, duration={_request.DurationSeconds:0.0}s, radius={_request.Radius:0.0}m, altitude={_request.Altitude:0.0}m.");
		HasFinishedInitialization = true;
	}

	private static UavA10LoiterRequest SanitizeRequest(UavA10LoiterRequest request)
	{
		request.DurationSeconds = Mathf.Max(1f, request.DurationSeconds);
		request.Radius = Mathf.Max(25f, request.Radius);
		request.Altitude = Mathf.Max(25f, request.Altitude);
		request.OrbitPeriod = Mathf.Max(5f, request.OrbitPeriod);
		request.IngressDuration = Mathf.Min(
			Mathf.Max(0f, request.IngressDuration),
			Mathf.Max(0f, request.DurationSeconds - 1f));
		request.IngressDistance = Mathf.Max(0f, request.IngressDistance);
		request.EngineVolume = Mathf.Clamp01(request.EngineVolume);
		request.Direction = request.Direction >= 0 ? 1 : -1;

		float elapsed = Time.time - request.StartTime;
		if (request.StartTime <= 0f || elapsed < -1f || elapsed > request.DurationSeconds + FlyoffDurationSeconds + 5f)
		{
			request.StartTime = Time.time;
		}

		return request;
	}

	private void ConfigurePrefabForCosmeticLoiter(GameObject aircraft, UavA10LoiterRequest request)
	{
		ConfigureAudio(aircraft, request);
		DisableSupportBehaviours(aircraft);
		DisableGameplayAndWeaponComponents(aircraft);
	}

	private void ConfigureAudio(GameObject aircraft, UavA10LoiterRequest request)
	{
		A10Behaviour a10Behaviour = aircraft.GetComponent<A10Behaviour>();
		if (request.AircraftType == UavLoiterAircraftType.A10 && a10Behaviour?.engineSource != null)
		{
			AudioSource engineSource = a10Behaviour.engineSource;
			AudioClip[] engineSounds = a10Behaviour.engineSounds;
			if ((engineSource.clip == null || !engineSource.clip) && engineSounds != null && engineSounds.Length > 0)
			{
				int index = Mathf.Abs(Mathf.RoundToInt(request.StartAngle * 1000f)) % engineSounds.Length;
				engineSource.clip = engineSounds[index];
			}

			ConfigureAudioSource(engineSource, request.EngineVolume * UavDistantAudioScale, minDistance: 450f, maxDistance: 5000f);
			return;
		}

		float perSourceVolume = request.AircraftType == UavLoiterAircraftType.Uh60
			? request.EngineVolume * UavDistantAudioScale * 0.7f
			: request.EngineVolume * UavDistantAudioScale;
		foreach (AudioSource source in aircraft.GetComponentsInChildren<AudioSource>(includeInactive: true))
		{
			ConfigureAudioSource(source, perSourceVolume, minDistance: 450f, maxDistance: 5000f);
		}
	}

	private void ConfigureAudioSource(AudioSource source, float volume, float minDistance, float maxDistance)
	{
		if (source == null)
		{
			return;
		}

		source.loop = false;
		source.playOnAwake = false;
		source.volume = Mathf.Clamp01(volume);
		source.spatialBlend = 1f;
		source.dopplerLevel = 0.15f;
		source.rolloffMode = AudioRolloffMode.Logarithmic;
		source.minDistance = minDistance;
		source.maxDistance = maxDistance;

		BetterAudio betterAudio = Singleton<BetterAudio>.Instance;
		if (betterAudio != null)
		{
			source.outputAudioMixerGroup = betterAudio.EnvTechnicalSoundsGroup;
		}

		if (source.clip != null && source.volume > 0f)
		{
			source.Stop();
			if (source.clip.length > 1f)
			{
				source.time = Mathf.Min(source.clip.length - 0.05f, source.clip.length * UnityEngine.Random.Range(0.15f, 0.45f));
			}

			source.Play();
		}

		_audioSources.Add(source);
	}

	private static void DisableSupportBehaviours(GameObject aircraft)
	{
		A10Behaviour a10Behaviour = aircraft.GetComponent<A10Behaviour>();
		if (a10Behaviour != null)
		{
			a10Behaviour.enabled = false;
			Destroy(a10Behaviour);
		}

		UH60Behaviour uh60Behaviour = aircraft.GetComponent<UH60Behaviour>();
		if (uh60Behaviour != null)
		{
			uh60Behaviour.enabled = false;
			Destroy(uh60Behaviour);
		}
	}

	private static void DisableGameplayAndWeaponComponents(GameObject aircraft)
	{
		foreach (Collider collider in aircraft.GetComponentsInChildren<Collider>(includeInactive: true))
		{
			collider.enabled = false;
		}

		foreach (Rigidbody body in aircraft.GetComponentsInChildren<Rigidbody>(includeInactive: true))
		{
			body.detectCollisions = false;
			body.isKinematic = true;
			body.useGravity = false;
		}

		foreach (Renderer renderer in aircraft.GetComponentsInChildren<Renderer>(includeInactive: true))
		{
			renderer.shadowCastingMode = ShadowCastingMode.Off;
			renderer.receiveShadows = false;
		}

		foreach (ParticleSystem particleSystem in aircraft.GetComponentsInChildren<ParticleSystem>(includeInactive: true))
		{
			particleSystem.Stop(withChildren: true, ParticleSystemStopBehavior.StopEmittingAndClear);
			particleSystem.Clear(withChildren: true);
			particleSystem.gameObject.SetActive(false);
		}

		foreach (LineRenderer lineRenderer in aircraft.GetComponentsInChildren<LineRenderer>(includeInactive: true))
		{
			lineRenderer.enabled = false;
		}

		foreach (TrailRenderer trailRenderer in aircraft.GetComponentsInChildren<TrailRenderer>(includeInactive: true))
		{
			trailRenderer.enabled = false;
		}

		foreach (Light light in aircraft.GetComponentsInChildren<Light>(includeInactive: true))
		{
			light.enabled = false;
		}
	}

	private void UpdateIngress(float elapsed)
	{
		GetOrbitFrame(0f, out Vector3 entryPosition, out Vector3 tangent);
		Vector3 ingressDirection = tangent.sqrMagnitude > 0.001f ? tangent : Vector3.forward;
		Vector3 startPosition = entryPosition - ingressDirection * _request.IngressDistance;
		float t = _request.IngressDuration <= 0f ? 1f : Mathf.Clamp01(elapsed / _request.IngressDuration);
		Vector3 position = Vector3.Lerp(startPosition, entryPosition, t);

		transform.SetPositionAndRotation(position, GetAircraftRotation(ingressDirection, GetIngressBank()));
	}

	private void UpdateOrbit(float elapsed)
	{
		float angularSpeed = GetCurrentSpeed() / Mathf.Max(1f, _request.Radius);
		float angle = _request.Direction * angularSpeed * elapsed;
		GetOrbitFrame(angle, out Vector3 position, out Vector3 tangent);

		transform.SetPositionAndRotation(position, GetAircraftRotation(tangent, GetOrbitBank()));
	}

	private void StartFlyoff()
	{
		_expired = true;
		float orbitElapsed = Mathf.Max(0f, _request.DurationSeconds - _request.IngressDuration);
		float angle = _request.Direction * Mathf.PI * 2f * (orbitElapsed / _request.OrbitPeriod);
		GetOrbitFrame(angle, out _flyoffPosition, out _flyoffDirection);
		_flyoffStartTime = Time.time;
		_flyoffSpeed = GetCurrentSpeed();
		transform.SetPositionAndRotation(_flyoffPosition, GetAircraftRotation(_flyoffDirection, 0f));

		FireSupportPlugin.LogSource.LogInfo($"UAV {GetAircraftName(_request.AircraftType)} loiter expired; flying off.");
	}

	private void UpdateFlyoff()
	{
		float flyoffElapsed = Time.time - _flyoffStartTime;
		if (flyoffElapsed >= FlyoffDurationSeconds)
		{
			DestroyAircraft();
			return;
		}

		_flyoffPosition += _flyoffDirection * _flyoffSpeed * Time.deltaTime;
		transform.SetPositionAndRotation(_flyoffPosition, GetAircraftRotation(_flyoffDirection, 0f));
	}

	private void GetOrbitFrame(float angleOffset, out Vector3 position, out Vector3 tangent)
	{
		float angle = _request.StartAngle + angleOffset;
		float sin = Mathf.Sin(angle);
		float cos = Mathf.Cos(angle);
		position = _request.Center + new Vector3(cos * _request.Radius, _request.Altitude, sin * _request.Radius);
		tangent = new Vector3(-sin * _request.Direction, 0f, cos * _request.Direction).normalized;
	}

	private Quaternion GetAircraftRotation(Vector3 tangent, float bank)
	{
		if (tangent.sqrMagnitude < 0.001f)
		{
			tangent = transform.forward;
		}

		return Quaternion.LookRotation(tangent, Vector3.up) *
		       Quaternion.Euler(
			       _request.ModelRotationOffset.x,
			       _request.ModelRotationOffset.y,
			       _request.ModelRotationOffset.z + bank);
	}

	private float GetOrbitBank()
	{
		float bank = _request.AircraftType == UavLoiterAircraftType.Uh60
			? Uh60TurnBankDegrees
			: A10TurnBankDegrees;
		return -_request.Direction * bank;
	}

	private float GetIngressBank()
	{
		return _request.AircraftType == UavLoiterAircraftType.Uh60
			? -_request.Direction * 3f
			: 0f;
	}

	private float GetCurrentSpeed()
	{
		float configuredOrbitSpeed = 2f * Mathf.PI * _request.Radius / _request.OrbitPeriod;
		float ingressSpeed = _request.IngressDuration > 0f
			? _request.IngressDistance / _request.IngressDuration
			: configuredOrbitSpeed;
		if (_request.AircraftType == UavLoiterAircraftType.Uh60)
		{
			return Mathf.Max(Uh60MinimumCruiseSpeed, configuredOrbitSpeed, ingressSpeed * 0.85f);
		}

		return Mathf.Max(A10MinimumCruiseSpeed, configuredOrbitSpeed, ingressSpeed * 0.85f);
	}

	private static string GetBundlePath(UavLoiterAircraftType aircraftType)
	{
		return aircraftType == UavLoiterAircraftType.Uh60 ? Uh60BundlePath : A10BundlePath;
	}

	private static string GetAircraftName(UavLoiterAircraftType aircraftType)
	{
		return aircraftType == UavLoiterAircraftType.Uh60 ? "UH-60" : "A-10";
	}

	private void DestroyAircraft()
	{
		if (_destroyed)
		{
			return;
		}

		_destroyed = true;
		foreach (AudioSource source in _audioSources)
		{
			if (source != null)
			{
				source.Stop();
			}
		}

		FireSupportPlugin.LogSource.LogInfo($"UAV {GetAircraftName(_request.AircraftType)} destroyed.");
		Destroy(gameObject);
	}
}
