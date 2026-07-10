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
	private bool _visualOnly;
	private int _visualSeed;
	private int _passIndex;
	private System.Random _spreadRandom;
	private static Material _visualTracerMaterial;

	private const float TOP_SPEED = 180f;
	private const float STRAFE_SPEED = 150f;
	private const float VISUAL_TRACER_MAX_DISTANCE = 1400f;
	private const float VISUAL_TRACER_SEGMENT_LENGTH = 42f;
	private const float VISUAL_TRACER_LIFETIME = 0.08f;
	private const float NETWORK_REPLAY_TRACER_LIFETIME = 0.18f;
	private const float FALLBACK_GAU8_TIME_BETWEEN_SHOTS = 0.067f;
	private float _currentSpeed = STRAFE_SPEED;

	public override ESupportType SupportType => ESupportType.Strafe;

	public override void ProcessRequest(
		Vector3 position,
		Vector3 direction,
		Vector3 rotation,
		CancellationToken cancellationToken,
		bool visualOnly = false,
		int visualSeed = 0,
		int passIndex = 0)
	{
		_visualOnly = visualOnly;
		_supportRequestId = A10TracerNetworking.CurrentSupportRequestId;
		_requesterProfileId = A10TracerNetworking.CurrentRequesterProfileId;
		_visualSeed = visualSeed != 0 ? visualSeed : System.Environment.TickCount;
		if (!_visualOnly && !string.IsNullOrWhiteSpace(_requesterProfileId))
		{
			try
			{
				_weapon = new VehicleWeapon(_requesterProfileId, ItemConstants.GAU8_WEAPON_TPL, ItemConstants.GAU8_AMMO_TPL);
			}
			catch (System.Exception ex)
			{
				FireSupportPlugin.LogSource?.LogWarning($"TSC A-10 visual runtime could not create requester-owned VehicleWeapon profile={A10AuthorityDiagnostics.ShortId(_requesterProfileId)}; using existing owner if available. {ex.Message}");
			}
		}

		_passIndex = passIndex;
		_spreadRandom = new System.Random(_visualSeed);
		_currentSpeed = STRAFE_SPEED;
		Vector3 a10StartPos = position + 2650 * direction + 320 * Vector3.up;
		Vector3 a10Heading = position - a10StartPos;

		float a10YAngle = Mathf.Atan2(a10Heading.x, a10Heading.z) * Mathf.Rad2Deg;
		Quaternion a10Rotation = Quaternion.Euler(0, a10YAngle, 0);

		transform.SetPositionAndRotation(a10StartPos, a10Rotation);
		_flareCountermeasureInstance = Instantiate(flareCountermeasure, null);
		FlySequence(position, cancellationToken).Forget();
	}

	public override void ManualUpdate()
	{
		if (_flareCountermeasureInstance == null)
		{
			return;
		}

		Transform t = transform;
		_flareCountermeasureInstance.transform.position = t.position - t.forward * 6.5f;
		_flareCountermeasureInstance.transform.eulerAngles = new Vector3(90, t.eulerAngles.y, 0);
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
	private async UniTaskVoid FlySequence(Vector3 strafePos, CancellationToken cancellationToken)
	{
		await UniTask.WaitForSeconds(3f, cancellationToken: cancellationToken);

		// Play engine sound
		engineSource.clip = engineSounds.GetRandomClip();
		engineSource.Play();
		await UniTask.WaitForSeconds(1f, cancellationToken: cancellationToken);

		// Disable flares
		_flareCountermeasureInstance.SetActive(false);
		await UniTask.WaitForSeconds(3f, cancellationToken: cancellationToken);

		// Enable gun particles
		gau8Particles.SetActive(true);
		// Play jet firing voiceover
		_fireSupportAudio.PlayVoiceover(EVoiceoverType.JetFiring);
		await UniTask.WaitForSeconds(1f, cancellationToken: cancellationToken);

		// Fire GAU8
		Gau8Sequence(strafePos, cancellationToken).Forget();

		if (_gameWorld?.IsMainPlayerAlive() != true || _player?.CameraPosition == null)
		{
			gau8Particles.SetActive(false);
			ReturnToPool();
			return;
		}

		float distanceFromPlayer = Vector3.Distance(_player.CameraPosition.position, strafePos);
		const float soundSpeedMS = 343;
		await UniTask.WaitForSeconds(distanceFromPlayer / soundSpeedMS, cancellationToken: cancellationToken);

		// Play explosion sfx
		// TODO: This should be the sfx for the actual projectile instead of manually being played here
		_betterAudio.PlayAtPoint(
			strafePos,
			gau8ExpSounds.GetRandomClip(),
			distanceFromPlayer,
			BetterAudio.AudioSourceGroupType.Gunshots,
			1200
		);
		gau8Particles.SetActive(false);
		await UniTask.WaitForSeconds(3.5f, cancellationToken: cancellationToken);

		if (_gameWorld?.IsMainPlayerAlive() != true || _player?.CameraPosition == null)
		{
			ReturnToPool();
			return;
		}

		// Play GAU8 BRRRT sfx
		_betterAudio.PlayAtPoint(
			gau8Transform.position - gau8Transform.forward * 100 - gau8Transform.up * 100,
			gau8Sound.GetRandomClip(),
			Vector3.Distance(_player.CameraPosition.position, gau8Transform.position),
			BetterAudio.AudioSourceGroupType.Gunshots,
			3200,
			2
		);
		await UniTask.WaitForSeconds(1.5f, cancellationToken: cancellationToken);

		// Enable flares
		_flareCountermeasureInstance.SetActive(true);
		await UniTask.WaitForSeconds(8f, cancellationToken: cancellationToken);

		// Play jet leaving voiceover
		_fireSupportAudio.PlayVoiceover(EVoiceoverType.JetLeaving);
		await UniTask.WaitForSeconds(4f, cancellationToken: cancellationToken);

		// Play strafe over voiceover
		_fireSupportAudio.PlayVoiceover(EVoiceoverType.StationStrafeEnd);
		await UniTask.WaitForSeconds(4f, cancellationToken: cancellationToken);

		ReturnToPool();
	}

	private async UniTaskVoid Gau8Sequence(Vector3 strafePos, CancellationToken cancellationToken)
	{
		List<A10TracerSegment> shotPlan = BuildGau8ShotPlan(strafePos);
		bool networkTracerAuthority = A10TracerNetworking.IsNetworkAuthorityActive;
		float fireStartNetworkTime = Time.time;

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

		foreach (A10TracerSegment shot in shotPlan)
		{
			if (cancellationToken.IsCancellationRequested || _gameWorld?.IsMainPlayerAlive() == false)
			{
				break;
			}

			if (!_visualOnly && _weapon != null)
			{
				_weapon.FireProjectile(shot.ProjectileOrigin, shot.ProjectileDirection);
			}
			else if (_visualOnly && !networkTracerAuthority)
			{
				RenderVisualTracerSegment(shot);
			}

			await UniTask.WaitForSeconds(GetGau8TimeBetweenShots(), cancellationToken: cancellationToken);
		}

		AccelerateSequence(cancellationToken).Forget();
	}

	private List<A10TracerSegment> BuildGau8ShotPlan(Vector3 strafePos)
	{
		Vector3 gau8Pos = gau8Transform.position + gau8Transform.forward * 515;
		Vector3 gau8Dir = Vector3.Normalize(strafePos - gau8Pos);
		Vector3 gau8LeftDir = Vector3.Cross(gau8Dir, Vector3.up).normalized;
		float shotDelay = 0f;
		var plan = new List<A10TracerSegment>(50);

		for (int i = 0; i < 50; i++)
		{
			Vector3 leftRightSpread = gau8LeftDir * NextSpread(-0.007f, 0.007f);
			gau8Dir = Vector3.Normalize(gau8Dir + new Vector3(0, 0.00037f, 0));
			Vector3 projectileDir = Vector3.Normalize(gau8Dir + leftRightSpread);
			plan.Add(BuildVisualTracerSegment(gau8Pos, projectileDir, shotDelay));
			shotDelay += GetGau8TimeBetweenShots();
		}

		return plan;
	}

	private float NextSpread(float min, float max)
	{
		_spreadRandom ??= new System.Random(System.Environment.TickCount);
		return min + (float)_spreadRandom.NextDouble() * (max - min);
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
		direction = direction.normalized;
		float tracerDistance = VISUAL_TRACER_MAX_DISTANCE;
		if (Physics.Raycast(origin, direction, out RaycastHit hitInfo, tracerDistance, ~0, QueryTriggerInteraction.Ignore))
		{
			tracerDistance = hitInfo.distance;
		}

		if (tracerDistance <= 1f)
		{
			return A10TracerSegment.Invalid(origin, direction, delaySeconds);
		}

		float segmentLength = Mathf.Min(VISUAL_TRACER_SEGMENT_LENGTH, tracerDistance);
		Vector3 tracerEnd = origin + direction * tracerDistance;
		Vector3 tracerStart = tracerEnd - direction * segmentLength;

		return new A10TracerSegment(origin, direction, tracerStart, tracerEnd, delaySeconds);
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
