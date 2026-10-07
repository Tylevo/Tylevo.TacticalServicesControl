using Comfort.Common;
using Cysharp.Threading.Tasks;
using EFT;
using SamSWAT.FireSupport.ArysReloaded.Utils;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public sealed class A10Behaviour : FireSupportBehaviour
{
	public AudioSource engineSource;
	public AudioClip[] engineSounds;

	[SerializeField] private AudioClip[] gau8Sound;
	[SerializeField] private AudioClip[] gau8ExpSounds;
	[SerializeField] private Transform gau8Transform;
	[SerializeField] private GameObject gau8Particles;
	[SerializeField] private GameObject flareCountermeasure;

	private GameObject _flareCountermeasureInstance;

	private BetterAudio _betterAudio;
	private FireSupportAudio _fireSupportAudio;

	private VehicleWeapon _weapon;
	private GameWorld _gameWorld;
	private Player _player;
	private string _supportRequestId = string.Empty;
	private string _requesterProfileId = string.Empty;
	private string _projectileOwnerProfileId = string.Empty;
	private bool _visualOnly;
	private int _visualSeed;
	private int _passIndex;
	private A10RuntimeRequestContext _pendingRequestContext;
	private static Material _visualTracerMaterial;

	private const float TOP_SPEED = 180f;
	private const float VISUAL_TRACER_LIFETIME = 0.08f;
	private const float NETWORK_REPLAY_TRACER_LIFETIME = 0.18f;
	private const float FALLBACK_GAU8_TIME_BETWEEN_SHOTS = 0.067f;
	private const float STRIKE_ENGINE_VOLUME = 1f;
	private const float STRIKE_ENGINE_MIN_DISTANCE = 450f;
	private const float STRIKE_ENGINE_MAX_DISTANCE = 5000f;
	private float _currentSpeed = A10ShotPlanner.StrafeSpeed;
	private float _enginePlaybackStartedAt = -1f;

	public override ESupportType SupportType => ESupportType.Strafe;

	public void SetRequestContext(A10RuntimeRequestContext requestContext)
	{
		_pendingRequestContext = requestContext;
	}

	public override void ProcessRequest(
		Vector3 position,
		Vector3 direction,
		Vector3 rotation,
		CancellationToken cancellationToken,
		bool visualOnly = false,
		int visualSeed = 0,
		int passIndex = 0)
	{
		CleanupTransientObjects();
		_visualOnly = visualOnly;
		A10RuntimeRequestContext requestContext = _pendingRequestContext;
		_pendingRequestContext = null;
		_supportRequestId = requestContext?.SupportRequestId ?? string.Empty;
		_requesterProfileId = requestContext?.RequesterProfileId ?? string.Empty;
		_projectileOwnerProfileId = requestContext?.ProjectileOwnerProfileId ?? _requesterProfileId;
		_visualSeed = visualSeed != 0 ? visualSeed : System.Environment.TickCount;
		if (!_visualOnly && !string.IsNullOrWhiteSpace(_projectileOwnerProfileId))
		{
			try
			{
				_weapon = new VehicleWeapon(
					_projectileOwnerProfileId,
					ItemConstants.GAU8_WEAPON_TPL,
					ItemConstants.GAU8_AMMO_TPL);
			}
			catch (System.Exception ex)
			{
				FireSupportPlugin.LogSource?.LogWarning($"TSC A-10 visual runtime could not create VehicleWeapon owner profile={A10AuthorityDiagnostics.ShortId(_projectileOwnerProfileId)} requester={A10AuthorityDiagnostics.ShortId(_requesterProfileId)}; using existing owner if available. {ex.Message}");
			}
		}

		_passIndex = passIndex;
		_currentSpeed = A10ShotPlanner.StrafeSpeed;
		Vector3 a10StartPos = A10ShotPlanner.GetOriginalAircraftOrigin(position, direction);
		Vector3 a10Heading = A10ShotPlanner.GetAircraftForward(direction);

		float a10YAngle = Mathf.Atan2(a10Heading.x, a10Heading.z) * Mathf.Rad2Deg;
		Quaternion a10Rotation = Quaternion.Euler(0, a10YAngle, 0);

		transform.SetPositionAndRotation(a10StartPos, a10Rotation);
		_flareCountermeasureInstance = flareCountermeasure != null
			? Instantiate(flareCountermeasure, null)
			: null;
		A10StrikeLifecycle.Begin(_supportRequestId);
		RunFlySequence(position, cancellationToken).Forget();
	}

	public override void ManualUpdate()
	{
		Transform t = transform;
		if (_flareCountermeasureInstance != null)
		{
			_flareCountermeasureInstance.transform.position = t.position - t.forward * 6.5f;
			_flareCountermeasureInstance.transform.eulerAngles = new Vector3(90, t.eulerAngles.y, 0);
		}

		transform.Translate(0, 0, _currentSpeed * Time.deltaTime, Space.Self);
	}

	protected override void OnAwake()
	{
		_fireSupportAudio = FireSupportAudio.Instance;
		_betterAudio = Singleton<BetterAudio>.Instance;
		if (engineSource != null && _betterAudio != null)
		{
			engineSource.outputAudioMixerGroup = _betterAudio.EnvTechnicalSoundsGroup;
		}

		_gameWorld = Singleton<GameWorld>.Instance;
		_player = _gameWorld?.MainPlayer;
		if (_player == null)
		{
			FireSupportPlugin.LogSource?.LogWarning(
				"TSC A-10 visual runtime initialized without GameWorld.MainPlayer. This is expected on Fika headless; authoritative GAU-8 damage must use the headless executor.");
			_weapon = null;
			HasFinishedInitialization = true;
			return;
		}

		_weapon = new VehicleWeapon(_player.ProfileId, ItemConstants.GAU8_WEAPON_TPL, ItemConstants.GAU8_AMMO_TPL);
		HasFinishedInitialization = true;
	}

	// My main motto for the next 2 methods is: if it works - it works (ツ)
	private async UniTaskVoid RunFlySequence(
		Vector3 strafePos,
		CancellationToken cancellationToken)
	{
		try
		{
			await FlySequence(strafePos, cancellationToken);
		}
		catch (System.OperationCanceledException)
		{
			if (this != null && gameObject.activeSelf)
			{
				ReturnToPool();
			}
		}
		catch (System.Exception ex)
		{
			FireSupportPlugin.LogSource?.LogError(
				$"TSC A-10 flight sequence failed requestId={A10AuthorityDiagnostics.ShortId(_supportRequestId)}. {ex}");
			if (this != null && gameObject.activeSelf)
			{
				ReturnToPool();
			}
		}
	}

	private async UniTask FlySequence(Vector3 strafePos, CancellationToken cancellationToken)
	{
		await UniTask.WaitForSeconds(3f, cancellationToken: cancellationToken);

		PlayStrikeFlyoverAudio();
		await UniTask.WaitForSeconds(1f, cancellationToken: cancellationToken);

		// Disable flares
		_flareCountermeasureInstance?.SetActive(false);
		await UniTask.WaitForSeconds(3f, cancellationToken: cancellationToken);

		// Enable gun particles
		gau8Particles.SetActive(true);
		// Play jet firing voiceover
		_fireSupportAudio.PlayVoiceover(EVoiceoverType.JetFiring);
		await UniTask.WaitForSeconds(1f, cancellationToken: cancellationToken);

		// Audio and projectiles must use the same plan and local launch clock.
		List<A10TracerSegment> shotPlan = BuildGau8ShotPlan(strafePos, GetGau8TimeBetweenShots());
		float fireStartNetworkTime = Time.time;
		Gau8Sequence(shotPlan, fireStartNetworkTime, cancellationToken).Forget();

		if (_gameWorld?.IsMainPlayerAlive() != true || _player?.CameraPosition == null)
		{
			gau8Particles.SetActive(false);
			ReturnToPool();
			return;
		}

		float distanceFromPlayer = Vector3.Distance(_player.CameraPosition.position, strafePos);
		bool hasImpactAudio = A10AudioTiming.TryGetImpactPlaybackWindow(
			shotPlan, distanceFromPlayer, out float impactAudioDelay, out float impactAudioDuration) &&
			(_visualOnly || _weapon != null);
		if (hasImpactAudio)
		{
			FireSupportPlugin.LogSource?.LogInfo(
				$"TSC A-10 impact audio requestId={A10AuthorityDiagnostics.ShortId(_supportRequestId)} pass={_passIndex} launch={fireStartNetworkTime:0.000} delay={impactAudioDelay:0.000}s impactSpan={impactAudioDuration:0.000}s shots={shotPlan.Count}");
			float waitSeconds = fireStartNetworkTime + impactAudioDelay - Time.time;
			if (waitSeconds > 0f)
			{
				await UniTask.WaitForSeconds(waitSeconds, cancellationToken: cancellationToken);
			}

			if (_gameWorld?.IsMainPlayerAlive() != true || _player?.CameraPosition == null)
			{
				ReturnToPool();
				return;
			}

			// Keep the original single impact-burst recording, but wait for ballistic
			// arrival as well as sound propagation. This is a prediction, not a hit callback.
			A10StrikeAudio.PlayAtPoint(
				_betterAudio,
				strafePos,
				gau8ExpSounds.GetRandomClip(),
				Vector3.Distance(_player.CameraPosition.position, strafePos),
				1200, 1f, "impacts", _supportRequestId, _passIndex, cancellationToken
			);
			LogStrikeFlyoverState("impacts-started");
		}
		await UniTask.WaitForSeconds(A10AudioTiming.ImpactToCannonDelaySeconds, cancellationToken: cancellationToken);

		if (_gameWorld?.IsMainPlayerAlive() != true || _player?.CameraPosition == null)
		{
			ReturnToPool();
			return;
		}

		// Play GAU8 BRRRT sfx
		if (hasImpactAudio)
		{
			Vector3 cannonPosition = gau8Transform.position - gau8Transform.forward * 100 - gau8Transform.up * 100;
			A10CannonAudio.PlayAtPoint(
				_betterAudio,
				cannonPosition,
				gau8Sound.GetRandomClip(),
				Vector3.Distance(_player.CameraPosition.position, cannonPosition),
				_supportRequestId, _passIndex, cancellationToken
			);
		}

		LogStrikeFlyoverState("cannon-report");
		await UniTask.WaitForSeconds(8f, cancellationToken: cancellationToken);

		// Play jet leaving voiceover
		LogStrikeFlyoverState("leaving");
		_fireSupportAudio.PlayVoiceover(EVoiceoverType.JetLeaving);
		await UniTask.WaitForSeconds(4f, cancellationToken: cancellationToken);

		// Play strafe over voiceover
		_fireSupportAudio.PlayVoiceover(EVoiceoverType.StationStrafeEnd);
		await UniTask.WaitForSeconds(4f, cancellationToken: cancellationToken);

		ReturnToPool();
	}

	protected override void OnDisable()
	{
		string completedRequestId = _supportRequestId;
		CleanupTransientObjects();
		_supportRequestId = string.Empty;
		_requesterProfileId = string.Empty;
		_projectileOwnerProfileId = string.Empty;
		_pendingRequestContext = null;
		base.OnDisable();
		A10StrikeLifecycle.Complete(completedRequestId);
	}

	private void OnDestroy()
	{
		CleanupTransientObjects();
	}

	private void CleanupTransientObjects()
	{
		if (_enginePlaybackStartedAt >= 0f)
		{
			LogStrikeFlyoverState("cleanup");
			_enginePlaybackStartedAt = -1f;
		}

		if (_flareCountermeasureInstance != null)
		{
			DestroyImmediate(_flareCountermeasureInstance);
			_flareCountermeasureInstance = null;
		}

		if (engineSource != null)
		{
			engineSource.Stop();
		}

		if (gau8Particles != null)
		{
			gau8Particles.SetActive(false);
		}
	}

	private void PlayStrikeFlyoverAudio()
	{
		if (engineSource == null || engineSounds == null || engineSounds.Length == 0)
		{
			FireSupportPlugin.LogSource?.LogWarning("TSC A-10 strike flyover audio unavailable: engine source or clips are missing.");
			return;
		}

		AudioClip clip = engineSounds.GetRandomClip();
		if (clip == null)
		{
			FireSupportPlugin.LogSource?.LogWarning("TSC A-10 strike flyover audio unavailable: selected engine clip is null.");
			return;
		}

		// Strike playback is deliberately independent from the quiet, one-shot UAV loiter audio.
		engineSource.Stop();
		engineSource.clip = clip;
		engineSource.playOnAwake = false;
		engineSource.loop = false;
		engineSource.mute = false;
		engineSource.volume = STRIKE_ENGINE_VOLUME;
		// All three authored recordings share priority so the flyby does not
		// outrank the impact burst and cannon report during voice contention.
		engineSource.priority = A10StrikeAudio.Priority;
		engineSource.pitch = 1f;
		engineSource.dopplerLevel = 0f;
		engineSource.spatialBlend = 1f;
		engineSource.rolloffMode = AudioRolloffMode.Logarithmic;
		engineSource.minDistance = STRIKE_ENGINE_MIN_DISTANCE;
		engineSource.maxDistance = STRIKE_ENGINE_MAX_DISTANCE;
		if (_betterAudio != null)
		{
			engineSource.outputAudioMixerGroup = _betterAudio.EnvTechnicalSoundsGroup;
		}

		engineSource.time = 0f;
		engineSource.Play();
		_enginePlaybackStartedAt = Time.time;
		AudioConfiguration audioConfiguration = AudioSettings.GetConfiguration();
		FireSupportPlugin.LogSource?.LogInfo(
			$"TSC A-10 strike flyover audio started clip={clip.name} length={clip.length:0.000}s volume={engineSource.volume:0.00} loop={engineSource.loop} priority={engineSource.priority} realVoices={audioConfiguration.numRealVoices} virtualVoices={audioConfiguration.numVirtualVoices} minDistance={engineSource.minDistance:0} maxDistance={engineSource.maxDistance:0}.");
	}

	private void LogStrikeFlyoverState(string phase)
	{
		try
		{
			if (engineSource == null || _enginePlaybackStartedAt < 0f)
			{
				return;
			}

			float listenerDistance = _player != null && _player.CameraPosition != null
				? Vector3.Distance(_player.CameraPosition.position, engineSource.transform.position)
				: -1f;
			FireSupportPlugin.LogSource?.LogInfo(
				$"TSC A-10 flyover state phase={phase} requestId={A10AuthorityDiagnostics.ShortId(_supportRequestId)} pass={_passIndex} elapsed={Time.time - _enginePlaybackStartedAt:0.000}s clipTime={engineSource.time:0.000}s playing={engineSource.isPlaying} virtual={engineSource.isVirtual} enabled={engineSource.isActiveAndEnabled} volume={engineSource.volume:0.00} mute={engineSource.mute} priority={engineSource.priority} listenerDistance={listenerDistance:0.0}m listenerVolume={AudioListener.volume:0.00} listenerPaused={AudioListener.pause} mixer={engineSource.outputAudioMixerGroup?.name ?? "<none>"} flares={_flareCountermeasureInstance != null && _flareCountermeasureInstance.activeInHierarchy}.");
		}
		catch (System.Exception)
		{
			// Raid teardown may already have destroyed the camera or mixer. Optional
			// telemetry must never interrupt flight playback or pooled-object cleanup.
		}
	}

	private async UniTaskVoid Gau8Sequence(
		List<A10TracerSegment> shotPlan,
		float fireStartNetworkTime,
		CancellationToken cancellationToken)
	{
		bool networkTracerAuthority = A10TracerNetworking.IsNetworkAuthorityActive;
		if (shotPlan.Count > 0)
		{
			A10TracerSegment firstShot = shotPlan[0];
			A10TracerSegment lastShot = shotPlan[shotPlan.Count - 1];
			FireSupportPlugin.LogSource?.LogInfo(
				$"TSC A-10 ballistic shot plan requestId={A10AuthorityDiagnostics.ShortId(_supportRequestId)} pass={_passIndex} seed={_visualSeed} shots={shotPlan.Count} valid={shotPlan.Count(static shot => shot.IsValid)} muzzleFirst={A10AuthorityDiagnostics.FormatVector(firstShot.ProjectileOrigin)} muzzleLast={A10AuthorityDiagnostics.FormatVector(lastShot.ProjectileOrigin)} intendedFirst={A10AuthorityDiagnostics.FormatVector(firstShot.IntendedImpact)} predictedFirst={A10AuthorityDiagnostics.FormatVector(firstShot.TracerEnd)} predictedLast={A10AuthorityDiagnostics.FormatVector(lastShot.TracerEnd)} flightFirst={firstShot.FlightTimeSeconds:0.000}s flightLast={lastShot.FlightTimeSeconds:0.000}s muzzleTravel={Vector3.Distance(firstShot.ProjectileOrigin, lastShot.ProjectileOrigin):0.0}m");
		}

		if (!_visualOnly && networkTracerAuthority && shotPlan.Count > 0)
		{
			A10TracerSegment[] segments = shotPlan.Where(static segment => segment.IsValid).ToArray();
			if (segments.Length > 0)
			{
				var burst = new A10TracerBurst(
					A10TracerNetworking.NextBurstId(),
					_supportRequestId,
					_visualSeed,
					_passIndex,
					fireStartNetworkTime,
					segments);
				A10TracerNetworking.PublishBurst(burst);
			}
		}

		if (!_visualOnly && _weapon == null)
		{
			FireSupportPlugin.LogSource?.LogWarning(
				"TSC A-10 visual runtime cannot fire authoritative GAU-8 damage because no VehicleWeapon is available.");
		}

		if (_visualOnly && !networkTracerAuthority)
		{
			A10TracerPlayback.PlayAtLocalTime(shotPlan.ToArray(), fireStartNetworkTime, cancellationToken);
		}

		int shotIndex = -1;
		foreach (A10TracerSegment shot in shotPlan)
		{
			shotIndex++;
			if (!shot.IsValid) continue;
			if (cancellationToken.IsCancellationRequested || _gameWorld?.IsMainPlayerAlive() == false)
			{
				break;
			}

			float waitSeconds = fireStartNetworkTime + shot.DelaySeconds - Time.time;
			if (waitSeconds > 0f)
			{
				await UniTask.WaitForSeconds(waitSeconds, cancellationToken: cancellationToken);
			}

			if (!_visualOnly && _weapon != null)
			{
				var bullet = _weapon.FireProjectile(shot.ProjectileOrigin, shot.ProjectileDirection);
				if (shotIndex == 0 || shotIndex == shotPlan.Count / 2 || shotIndex == shotPlan.Count - 1)
				{
					A10ShotDiagnostics.Observe(bullet, shot, _supportRequestId, _passIndex, shotIndex, cancellationToken);
				}
			}
		}

		// Aircraft effects follow the firing burst, not the listener's audio delay.
		gau8Particles.SetActive(false);
		if (!cancellationToken.IsCancellationRequested && _gameWorld?.IsMainPlayerAlive() != false)
		{
			_flareCountermeasureInstance?.SetActive(true);
		}
		LogStrikeFlyoverState("burst-complete");
		AccelerateSequence(cancellationToken).Forget();
	}

	private List<A10TracerSegment> BuildGau8ShotPlan(
		Vector3 strafePos,
		float timeBetweenShots)
	{
		Transform muzzle = gau8Transform != null ? gau8Transform : transform;
		Vector3 aircraftForward = A10ShotPlanner.NormalizeAircraftForward(transform.forward);
		IReadOnlyList<Vector3> impactPlan = A10ShotPlanner.BuildImpactPlan(
			strafePos,
			aircraftForward,
			_visualSeed);
		using A10EftTrajectoryEvaluator trajectoryEvaluator = _weapon != null
			? _weapon.CreateTrajectoryEvaluator()
			: VehicleWeapon.CreateGau8VisualTrajectoryEvaluator();
		return A10ShotPlanner.BuildMovingMuzzlePlan(
			muzzle.position,
			aircraftForward,
			impactPlan,
			timeBetweenShots,
			trajectoryEvaluator);
	}

	private float GetGau8TimeBetweenShots()
	{
		return _weapon != null ? _weapon.timeBetweenShots : FALLBACK_GAU8_TIME_BETWEEN_SHOTS;
	}

	private async UniTaskVoid AccelerateSequence(CancellationToken cancellationToken)
	{
		const float acceleration = 5.38f;

		while (!cancellationToken.IsCancellationRequested && _currentSpeed < TOP_SPEED)
		{
			await UniTask.NextFrame(PlayerLoopTiming.Update, cancellationToken);
			_currentSpeed += acceleration * Time.deltaTime;
		}
	}

	public static A10TracerSegment BuildVisualTracerSegment(Vector3 origin, Vector3 direction, float delaySeconds)
	{
		return A10ShotPlanner.BuildRaycastTracerSegment(origin, direction, delaySeconds);
	}

	public static void RenderVisualTracerSegment(A10TracerSegment segment, bool prominentReplay = false)
	{
		if (!segment.IsValid)
		{
			return;
		}

		GameObject tracerObject = new GameObject("A10 Visual Tracer");
		AddTracerLine(
			tracerObject,
			segment.TracerStart,
			segment.TracerEnd,
			prominentReplay ? 0.095f : 0.045f,
			prominentReplay ? 0.024f : 0.012f);
		Destroy(tracerObject, prominentReplay ? NETWORK_REPLAY_TRACER_LIFETIME : VISUAL_TRACER_LIFETIME);
	}

	private static LineRenderer AddTracerLine(
		GameObject tracerObject,
		Vector3 start,
		Vector3 end,
		float startWidth,
		float endWidth)
	{
		LineRenderer lineRenderer = tracerObject.AddComponent<LineRenderer>();
		lineRenderer.useWorldSpace = true;
		lineRenderer.positionCount = 2;
		lineRenderer.alignment = LineAlignment.View;
		lineRenderer.numCapVertices = 2;
		lineRenderer.numCornerVertices = 0;
		lineRenderer.textureMode = LineTextureMode.Stretch;
		lineRenderer.shadowCastingMode = ShadowCastingMode.Off;
		lineRenderer.receiveShadows = false;
		lineRenderer.widthMultiplier = 1f;
		lineRenderer.startWidth = startWidth;
		lineRenderer.endWidth = endWidth;
		lineRenderer.material = GetVisualTracerMaterial();
		lineRenderer.SetPosition(0, start);
		lineRenderer.SetPosition(1, end);
		SetTracerColor(lineRenderer, 1f);
		return lineRenderer;
	}

	private static void SetTracerColor(LineRenderer lineRenderer, float alpha)
	{
		if (lineRenderer == null)
		{
			return;
		}

		lineRenderer.startColor = new Color(1f, 0.72f, 0.25f, alpha * 0.72f);
		lineRenderer.endColor = new Color(1f, 0.36f, 0.08f, alpha * 0.12f);
	}

	private static Material GetVisualTracerMaterial()
	{
		if (_visualTracerMaterial != null)
		{
			return _visualTracerMaterial;
		}

		Shader shader = Shader.Find("Sprites/Default")
			?? Shader.Find("Particles/Standard Unlit")
			?? Shader.Find("Unlit/Color")
			?? Shader.Find("Sprites/Default")
			?? Shader.Find("Hidden/Internal-Colored");

		_visualTracerMaterial = new Material(shader)
		{
			color = new Color(1f, 0.7f, 0.2f, 1f)
		};
		ConfigureTransparentMaterial(_visualTracerMaterial);

		return _visualTracerMaterial;
	}

	private static void ConfigureTransparentMaterial(Material material)
	{
		if (material == null)
		{
			return;
		}

		if (material.HasProperty("_SrcBlend"))
		{
			material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
		}

		if (material.HasProperty("_DstBlend"))
		{
			material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
		}

		if (material.HasProperty("_Cull"))
		{
			material.SetInt("_Cull", (int)CullMode.Off);
		}

		if (material.HasProperty("_ZWrite"))
		{
			material.SetInt("_ZWrite", 0);
		}

		material.renderQueue = 3000;
	}
}
