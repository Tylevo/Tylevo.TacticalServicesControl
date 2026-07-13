using Comfort.Common;
using Cysharp.Threading.Tasks;
using SamSWAT.FireSupport.ArysReloaded.Utils;
using System;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public sealed class UavA10LoiterController : UpdatableComponentBase
{
	private const string A10BundlePath = "assets/content/vehicles/a10_warthog.bundle";
	private const float FlyoffDurationSeconds = 8f;
	private const float TurnBankDegrees = 17f;
	private const float UavDistantAudioScale = 0.18f;

	private static UavA10LoiterController s_activeLoiter;

	private UavA10LoiterRequest _request;
	private CancellationToken _cancellationToken;
	private AudioSource _engineSource;
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
			DestroyPlane();
			return;
		}

		float elapsed = Mathf.Max(0f, Time.time - _request.StartTime);
		if (!_expired && elapsed >= _request.DurationSeconds)
		{
			StartFlyoff(elapsed);
		}

		if (_expired)
		{
			UpdateFlyoff();
			return;
		}

		UpdateOrbit(elapsed);
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

			GameObject prefab = await AssetLoader.LoadAssetAsync(A10BundlePath);
			if (prefab == null)
			{
				FireSupportPlugin.LogSource.LogWarning("UAV A-10 loiter skipped: A-10 prefab failed to load.");
				return;
			}

			FireSupportPlugin.LogSource.LogInfo("UAV A-10 loiter asset bundle/prefab loaded.");

			if (s_activeLoiter != null)
			{
				s_activeLoiter.DestroyPlane();
			}

			GameObject plane = Instantiate(prefab);
			plane.name = "FireSupport UAV A-10 Loiter";

			A10Behaviour a10Behaviour = plane.GetComponent<A10Behaviour>();
			AudioSource engineSource = ConfigureEngineAudio(plane, a10Behaviour, request);
			DisableStrafeBehaviour(a10Behaviour);
			DisableGameplayAndWeaponComponents(plane);

			UavA10LoiterController controller = plane.AddComponent<UavA10LoiterController>();
			controller.Initialize(request, cancellationToken, engineSource);
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource.LogWarning($"UAV A-10 loiter visual skipped. {ex}");
		}
	}

	private void Initialize(
		UavA10LoiterRequest request,
		CancellationToken cancellationToken,
		AudioSource engineSource)
	{
		_request = SanitizeRequest(request);
		_cancellationToken = cancellationToken;
		_engineSource = engineSource;
		s_activeLoiter = this;

		float elapsed = Mathf.Clamp(Time.time - _request.StartTime, 0f, _request.DurationSeconds);
		UpdateOrbit(elapsed);

		FireSupportPlugin.LogSource.LogInfo(
			$"UAV A-10 loiter started. Center={_request.Center}, duration={_request.DurationSeconds:0.0}s, radius={_request.Radius:0.0}m, altitude={_request.Altitude:0.0}m.");
		HasFinishedInitialization = true;
	}

	private static UavA10LoiterRequest SanitizeRequest(UavA10LoiterRequest request)
	{
		request.DurationSeconds = Mathf.Max(1f, request.DurationSeconds);
		request.Radius = Mathf.Max(25f, request.Radius);
		request.Altitude = Mathf.Max(25f, request.Altitude);
		request.OrbitPeriod = Mathf.Max(5f, request.OrbitPeriod);
		request.EngineVolume = Mathf.Clamp01(request.EngineVolume);
		request.Direction = request.Direction >= 0 ? 1 : -1;

		float elapsed = Time.time - request.StartTime;
		if (request.StartTime <= 0f || elapsed < -1f || elapsed > request.DurationSeconds + FlyoffDurationSeconds + 5f)
		{
			request.StartTime = Time.time;
		}

		return request;
	}

	private static AudioSource ConfigureEngineAudio(
		GameObject plane,
		A10Behaviour a10Behaviour,
		UavA10LoiterRequest request)
	{
		AudioSource engineSource = a10Behaviour?.engineSource ?? plane.GetComponentInChildren<AudioSource>(includeInactive: true);
		if (engineSource == null)
		{
			return null;
		}

		AudioClip[] engineSounds = a10Behaviour?.engineSounds;
		if ((engineSource.clip == null || !engineSource.clip) && engineSounds != null && engineSounds.Length > 0)
		{
			int index = Mathf.Abs(Mathf.RoundToInt(request.StartAngle * 1000f)) % engineSounds.Length;
			engineSource.clip = engineSounds[index];
		}

		engineSource.loop = false;
		engineSource.playOnAwake = false;
		engineSource.volume = Mathf.Clamp01(request.EngineVolume * UavDistantAudioScale);
		engineSource.spatialBlend = 1f;
		engineSource.dopplerLevel = 0.15f;
		engineSource.rolloffMode = AudioRolloffMode.Logarithmic;
		engineSource.minDistance = 450f;
		engineSource.maxDistance = 5000f;

		BetterAudio betterAudio = Singleton<BetterAudio>.Instance;
		if (betterAudio != null)
		{
			engineSource.outputAudioMixerGroup = betterAudio.EnvTechnicalSoundsGroup;
		}

		if (engineSource.clip != null && engineSource.volume > 0f)
		{
			engineSource.Stop();
			if (engineSource.clip.length > 1f)
			{
				engineSource.time = Mathf.Min(
					engineSource.clip.length - 0.05f,
					engineSource.clip.length * UnityEngine.Random.Range(0.15f, 0.45f));
			}

			engineSource.Play();
		}

		return engineSource;
	}

	private static void DisableStrafeBehaviour(A10Behaviour a10Behaviour)
	{
		if (a10Behaviour == null)
		{
			return;
		}

		a10Behaviour.enabled = false;
		Destroy(a10Behaviour);
	}

	private static void DisableGameplayAndWeaponComponents(GameObject plane)
	{
		foreach (Collider collider in plane.GetComponentsInChildren<Collider>(includeInactive: true))
		{
			collider.enabled = false;
		}

		foreach (Rigidbody body in plane.GetComponentsInChildren<Rigidbody>(includeInactive: true))
		{
			body.detectCollisions = false;
			body.isKinematic = true;
			body.useGravity = false;
		}

		foreach (Renderer renderer in plane.GetComponentsInChildren<Renderer>(includeInactive: true))
		{
			renderer.shadowCastingMode = ShadowCastingMode.Off;
			renderer.receiveShadows = false;
		}

		foreach (ParticleSystem particleSystem in plane.GetComponentsInChildren<ParticleSystem>(includeInactive: true))
		{
			particleSystem.Stop(withChildren: true, ParticleSystemStopBehavior.StopEmittingAndClear);
			particleSystem.Clear(withChildren: true);
			particleSystem.gameObject.SetActive(false);
		}

		foreach (LineRenderer lineRenderer in plane.GetComponentsInChildren<LineRenderer>(includeInactive: true))
		{
			lineRenderer.enabled = false;
		}

		foreach (TrailRenderer trailRenderer in plane.GetComponentsInChildren<TrailRenderer>(includeInactive: true))
		{
			trailRenderer.enabled = false;
		}

		foreach (Light light in plane.GetComponentsInChildren<Light>(includeInactive: true))
		{
			light.enabled = false;
		}
	}

	private void UpdateOrbit(float elapsed)
	{
		float angle = _request.StartAngle + _request.Direction * Mathf.PI * 2f * (elapsed / _request.OrbitPeriod);
		Vector3 position;
		Vector3 tangent;
		GetOrbitFrame(angle, out position, out tangent);

		float bank = _request.Direction * TurnBankDegrees;
		transform.SetPositionAndRotation(position, GetPlaneRotation(tangent, bank));
	}

	private void StartFlyoff(float elapsed)
	{
		_expired = true;
		float angle = _request.StartAngle + _request.Direction * Mathf.PI * 2f * (_request.DurationSeconds / _request.OrbitPeriod);
		GetOrbitFrame(angle, out _flyoffPosition, out _flyoffDirection);
		_flyoffStartTime = Time.time;
		_flyoffSpeed = Mathf.Max(85f, 2f * Mathf.PI * _request.Radius / _request.OrbitPeriod);
		transform.SetPositionAndRotation(_flyoffPosition, GetPlaneRotation(_flyoffDirection, 0f));

		FireSupportPlugin.LogSource.LogInfo("UAV A-10 loiter expired; flying off.");
	}

	private void UpdateFlyoff()
	{
		float flyoffElapsed = Time.time - _flyoffStartTime;
		if (flyoffElapsed >= FlyoffDurationSeconds)
		{
			DestroyPlane();
			return;
		}

		_flyoffPosition += _flyoffDirection * _flyoffSpeed * Time.deltaTime;
		transform.SetPositionAndRotation(_flyoffPosition, GetPlaneRotation(_flyoffDirection, 0f));
	}

	private void GetOrbitFrame(float angle, out Vector3 position, out Vector3 tangent)
	{
		float sin = Mathf.Sin(angle);
		float cos = Mathf.Cos(angle);
		position = _request.Center + new Vector3(cos * _request.Radius, _request.Altitude, sin * _request.Radius);
		tangent = new Vector3(-sin * _request.Direction, 0f, cos * _request.Direction).normalized;
	}

	private Quaternion GetPlaneRotation(Vector3 tangent, float bank)
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

	private void DestroyPlane()
	{
		if (_destroyed)
		{
			return;
		}

		_destroyed = true;
		if (_engineSource != null)
		{
			_engineSource.Stop();
		}

		FireSupportPlugin.LogSource.LogInfo("UAV A-10 destroyed.");
		Destroy(gameObject);
	}
}
