using Comfort.Common;
using EFT;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public sealed class RemoteUavPhoneVisualController : UpdatableComponentBase
{
	private const float DefaultPurchaseDurationSeconds = 9.0f;
	private const float DefaultConfirmingDurationSeconds = 4.0f;
	private const float DefaultAuthorizedDurationSeconds = 0.9f;
	private const float DefaultCancelledDurationSeconds = 0.85f;
	private const float DefaultActivationDurationSeconds = 2.2f;
	private const float FailsafeLifetimeSeconds = 14.0f;

	private static readonly int s_color = Shader.PropertyToID("_Color");
	private static readonly int s_tintColor = Shader.PropertyToID("_TintColor");
	private static readonly Dictionary<string, RemoteUavPhoneVisualController> s_activeByOwner = new();

	private Player _owner;
	private Transform _anchor;
	private GameObject _propRoot;
	private Transform _propTransform;
	private MeshRenderer _screenRenderer;
	private MeshRenderer _pulseRenderer;
	private TextMesh _statusText;
	private TextMesh _subText;
	private AudioSource _audioSource;
	private string _ownerKey;
	private string _profileId;
	private bool _handAttached;
	private bool _activationStyle;
	private bool _destroyed;
	private UavPhoneVisualPhase _phase;
	private ESupportType _supportType;
	private float _phaseStartedAt;
	private float _phaseDuration;
	private float _createdAt;
	private Color _phaseColor = Amber();

	public static void Play(
		string profileId,
		string accountId,
		ESupportType supportType,
		UavPhoneVisualPhase phase,
		double startTime,
		float duration,
		bool success)
	{
		try
		{
			if (IsLocalPlayer(profileId, accountId))
			{
				FireSupportPlugin.LogSource?.LogInfo(
					$"UAV phone visual ignored local packet phase={phase}, owner={profileId}.");
				return;
			}

			string key = MakeOwnerKey(profileId, accountId);
			if (phase == UavPhoneVisualPhase.End)
			{
				if (s_activeByOwner.TryGetValue(key, out RemoteUavPhoneVisualController existing))
				{
					existing.DestroyVisual("end packet");
				}

				return;
			}

			Player owner = FindRemotePlayer(profileId, accountId);
			if (owner == null)
			{
				FireSupportPlugin.LogSource?.LogWarning(
					$"UAV phone visual remote player not found. profile={profileId ?? string.Empty}, account={accountId ?? string.Empty}, phase={phase}.");
				return;
			}

			FireSupportPlugin.LogSource?.LogInfo(
				$"UAV phone visual remote player found. profile={owner.ProfileId}, phase={phase}.");

			if (!s_activeByOwner.TryGetValue(key, out RemoteUavPhoneVisualController controller) ||
			    controller == null ||
			    controller._destroyed)
			{
				GameObject root = new($"Remote TerraGroup Uplink Visual {owner.ProfileId}");
				controller = root.AddComponent<RemoteUavPhoneVisualController>();
				controller.Initialize(owner, key, profileId);
				s_activeByOwner[key] = controller;
			}

			controller.ApplyPhase(supportType, phase, duration, success, startTime);
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource?.LogWarning($"UAV phone visual skipped. {ex}");
		}
	}

	public override void ManualUpdate()
	{
		if (_destroyed)
		{
			return;
		}

		if (_owner == null || _anchor == null || _propTransform == null)
		{
			DestroyVisual("owner or anchor lost");
			return;
		}

		float age = Time.time - _createdAt;
		if (age > FailsafeLifetimeSeconds)
		{
			DestroyVisual("failsafe lifetime expired");
			return;
		}

		float elapsed = Time.time - _phaseStartedAt;
		UpdatePose(elapsed);
		UpdateScreen(elapsed);

		if (_phase == UavPhoneVisualPhase.Cancelled && elapsed >= _phaseDuration)
		{
			DestroyVisual("cancelled phase expired");
			return;
		}

		if (_activationStyle &&
		    _phase == UavPhoneVisualPhase.Authorized &&
		    elapsed >= Mathf.Max(0.5f, _phaseDuration))
		{
			DestroyVisual("activation authorized phase expired");
		}
	}

	protected override void OnDisable()
	{
		base.OnDisable();

		if (!string.IsNullOrEmpty(_ownerKey) &&
		    s_activeByOwner.TryGetValue(_ownerKey, out RemoteUavPhoneVisualController existing) &&
		    existing == this)
		{
			s_activeByOwner.Remove(_ownerKey);
		}
	}

	private void Initialize(Player owner, string ownerKey, string profileId)
	{
		_owner = owner;
		_ownerKey = ownerKey;
		_profileId = profileId ?? string.Empty;
		_createdAt = Time.time;
		_phaseStartedAt = Time.time;

		_anchor = FindAttachAnchor(owner, out _handAttached);
		transform.SetParent(_anchor, false);
		transform.localPosition = Vector3.zero;
		transform.localRotation = Quaternion.identity;
		transform.localScale = Vector3.one;

		_propRoot = CreatePhoneProp();
		_propTransform = _propRoot.transform;
		_propTransform.SetParent(transform, false);
		_propTransform.localScale = Vector3.one;
		ConfigureProp(_propRoot);

		_audioSource = gameObject.AddComponent<AudioSource>();
		_audioSource.playOnAwake = false;
		_audioSource.spatialBlend = 1f;
		_audioSource.minDistance = 2f;
		_audioSource.maxDistance = 22f;
		_audioSource.volume = 0.34f;
		_audioSource.clip = CreateBeepClip();

		FireSupportPlugin.LogSource?.LogInfo(
			$"UAV phone visual phone prop loaded: runtime simple proxy. owner={_profileId}, anchor={_anchor?.name ?? "<null>"}.");
		FireSupportPlugin.LogSource?.LogInfo(
			$"UAV phone visual attached to {(_handAttached ? "right-hand bone" : "fallback")} anchor={_anchor?.name ?? "<null>"}, owner={_profileId}.");

		HasFinishedInitialization = true;
	}

	private void ApplyPhase(
		ESupportType supportType,
		UavPhoneVisualPhase phase,
		float duration,
		bool success,
		double startTime)
	{
		_supportType = supportType;
		_phase = phase;
		_phaseStartedAt = Time.time;
		_phaseDuration = duration > 0f ? duration : GetDefaultDuration(phase);
		_activationStyle = _activationStyle || phase == UavPhoneVisualPhase.StartActivationPhone;
		_phaseColor = GetPhaseColor(phase, success);

		SetPhaseText(phase, supportType, success);
		PlayPhaseAudio(phase, success);

		FireSupportPlugin.LogSource?.LogInfo(
			$"UAV phone visual phase applied. owner={_profileId}, phase={phase}, support={supportType}, start={startTime:0.000}, duration={_phaseDuration:0.00}, success={success}.");
	}

	private void UpdatePose(float elapsed)
	{
		float intro = EaseOutCubic(Mathf.Clamp01(elapsed / 0.35f));
		float pulse = Mathf.Sin((Time.time - _createdAt) * 6.5f) * 0.004f;
		Vector3 stowed = _handAttached ? new Vector3(0.015f, -0.035f, 0.035f) : new Vector3(0.10f, 0.02f, 0.16f);
		Vector3 active = _handAttached ? new Vector3(0.035f, -0.010f, 0.075f) : new Vector3(0.18f, 0.08f, 0.25f);
		Quaternion stowedRotation = _handAttached
			? Quaternion.Euler(82f, 12f, 88f)
			: Quaternion.Euler(72f, 0f, 0f);
		Quaternion activeRotation = _handAttached
			? Quaternion.Euler(58f, 18f, 72f)
			: Quaternion.Euler(54f, 0f, -8f);

		if (_phase == UavPhoneVisualPhase.Cancelled)
		{
			float outT = EaseInCubic(Mathf.Clamp01(elapsed / Mathf.Max(0.1f, _phaseDuration)));
			intro *= 1f - outT;
		}

		_propTransform.localPosition = Vector3.Lerp(stowed, active, intro) + new Vector3(0f, pulse * intro, 0f);
		_propTransform.localRotation = Quaternion.Slerp(stowedRotation, activeRotation, intro);
		_propTransform.localScale = Vector3.one * (_handAttached ? 0.82f : 1.0f);
	}

	private void UpdateScreen(float elapsed)
	{
		float phaseT = Mathf.Clamp01(elapsed / Mathf.Max(0.1f, _phaseDuration));
		float blink = _phase == UavPhoneVisualPhase.Cancelled
			? Mathf.PingPong(elapsed * 7f, 1f)
			: 0.78f + Mathf.Sin(Time.time * 8f) * 0.22f;
		float alpha = Mathf.Lerp(0.10f, 0.55f, blink);

		if (_phase == UavPhoneVisualPhase.Authorized)
		{
			alpha = Mathf.Lerp(0.62f, 0.25f, phaseT);
		}

		if (_screenRenderer != null)
		{
			SetRendererColor(_screenRenderer, new Color(_phaseColor.r, _phaseColor.g, _phaseColor.b, alpha));
		}

		if (_pulseRenderer != null)
		{
			float pulseAlpha = _phase == UavPhoneVisualPhase.Confirming
				? Mathf.Lerp(0.22f, 0.82f, Mathf.PingPong(Time.time * 2.2f, 1f))
				: Mathf.Lerp(0.56f, 0.08f, phaseT);
			SetRendererColor(_pulseRenderer, new Color(_phaseColor.r, _phaseColor.g, _phaseColor.b, pulseAlpha));
			_pulseRenderer.transform.localScale = Vector3.one * Mathf.Lerp(0.75f, 1.25f, Mathf.PingPong(Time.time * 1.9f, 1f));
		}
	}

	private void SetPhaseText(UavPhoneVisualPhase phase, ESupportType supportType, bool success)
	{
		if (_statusText == null || _subText == null)
		{
			return;
		}

		switch (phase)
		{
			case UavPhoneVisualPhase.StartPurchasePhone:
				_statusText.text = "TG LINK";
				_subText.text = GetShortSupportName(supportType);
				break;
			case UavPhoneVisualPhase.Confirming:
				_statusText.text = "VERIFY";
				_subText.text = "AUTH";
				break;
			case UavPhoneVisualPhase.Authorized:
				_statusText.text = success ? "READY" : "LINK";
				_subText.text = _activationStyle ? "UAV ACTIVE" : "AUTH READY";
				break;
			case UavPhoneVisualPhase.Cancelled:
				_statusText.text = "CANCEL";
				_subText.text = "LINK END";
				break;
			case UavPhoneVisualPhase.StartActivationPhone:
				_statusText.text = "UAV LINK";
				_subText.text = "ACTIVE";
				break;
			default:
				_statusText.text = "TG";
				_subText.text = "UPLINK";
				break;
		}

		Color textColor = phase == UavPhoneVisualPhase.Cancelled ? new Color(1f, 0.42f, 0.26f, 0.95f) : _phaseColor;
		_statusText.color = textColor;
		_subText.color = new Color(textColor.r, textColor.g, textColor.b, 0.82f);
	}

	private void PlayPhaseAudio(UavPhoneVisualPhase phase, bool success)
	{
		if (_audioSource == null)
		{
			return;
		}

		if (phase == UavPhoneVisualPhase.StartPurchasePhone ||
		    phase == UavPhoneVisualPhase.Confirming ||
		    phase == UavPhoneVisualPhase.Authorized ||
		    phase == UavPhoneVisualPhase.StartActivationPhone ||
		    phase == UavPhoneVisualPhase.Cancelled)
		{
			_audioSource.pitch = phase switch
			{
				UavPhoneVisualPhase.Cancelled => 0.72f,
				UavPhoneVisualPhase.Authorized => success ? 1.28f : 0.9f,
				UavPhoneVisualPhase.Confirming => 1.05f,
				UavPhoneVisualPhase.StartActivationPhone => 1.18f,
				_ => 0.95f
			};
			_audioSource.Play();
		}
	}

	private static Transform FindAttachAnchor(Player player, out bool handAttached)
	{
		handAttached = false;
		Transform hand = FindAnimatorBone(player, HumanBodyBones.RightHand) ??
		                 FindNamedTransform(player, GetRightHandScore);
		if (hand != null)
		{
			handAttached = true;
			return hand;
		}

		Transform chest = FindAnimatorBone(player, HumanBodyBones.Chest) ??
		                  FindAnimatorBone(player, HumanBodyBones.Spine) ??
		                  FindNamedTransform(player, GetChestScore);
		if (chest != null)
		{
			return chest;
		}

		return player != null ? ((Component)player).transform : null;
	}

	private static Transform FindAnimatorBone(Player player, HumanBodyBones bone)
	{
		if (player == null)
		{
			return null;
		}

		foreach (Animator animator in player.GetComponentsInChildren<Animator>(includeInactive: true))
		{
			if (animator == null || !animator.isHuman)
			{
				continue;
			}

			Transform boneTransform = animator.GetBoneTransform(bone);
			if (boneTransform != null)
			{
				return boneTransform;
			}
		}

		return null;
	}

	private static Transform FindNamedTransform(Player player, Func<string, int> score)
	{
		if (player == null)
		{
			return null;
		}

		Transform best = null;
		int bestScore = 0;
		foreach (Transform candidate in ((Component)player).transform.root.GetComponentsInChildren<Transform>(includeInactive: true))
		{
			int candidateScore = score(candidate.name);
			if (candidateScore > bestScore)
			{
				bestScore = candidateScore;
				best = candidate;
			}
		}

		return bestScore > 0 ? best : null;
	}

	private static int GetRightHandScore(string transformName)
	{
		if (string.IsNullOrEmpty(transformName))
		{
			return 0;
		}

		string name = NormalizeBoneName(transformName);
		bool right = name.Contains("right") ||
		             name.Contains("humanr") ||
		             name.Contains("r hand") ||
		             name.Contains("rhand") ||
		             name.Contains("rpalm") ||
		             name.Contains("r palm");
		bool left = name.Contains("left") ||
		            name.Contains("humanl") ||
		            name.Contains("l hand") ||
		            name.Contains("lhand");
		if (!right || left)
		{
			return 0;
		}

		if (name.Contains("hand") || name.Contains("palm"))
		{
			return 100;
		}

		if (name.Contains("wrist"))
		{
			return 80;
		}

		return 0;
	}

	private static int GetChestScore(string transformName)
	{
		if (string.IsNullOrEmpty(transformName))
		{
			return 0;
		}

		string name = NormalizeBoneName(transformName);
		if (name.Contains("chest") || name.Contains("spine2") || name.Contains("spine 2"))
		{
			return 100;
		}

		return name.Contains("spine") ? 65 : 0;
	}

	private static string NormalizeBoneName(string transformName)
	{
		return transformName.ToLowerInvariant().Replace("_", " ").Replace("-", " ");
	}

	private static Player FindRemotePlayer(string profileId, string accountId)
	{
		GameWorld gameWorld = Singleton<GameWorld>.Instance;
		Player mainPlayer = gameWorld?.MainPlayer;
		if (gameWorld?.AllPlayersEverExisted == null)
		{
			return null;
		}

		foreach (Player player in gameWorld.AllPlayersEverExisted)
		{
			if (player == null || player == mainPlayer)
			{
				continue;
			}

			if (!string.IsNullOrEmpty(profileId) &&
			    string.Equals(player.ProfileId, profileId, StringComparison.OrdinalIgnoreCase))
			{
				return player;
			}

			if (!string.IsNullOrEmpty(accountId) &&
			    string.Equals(UavPhoneVisualNetworkService.GetAccountId(player), accountId, StringComparison.OrdinalIgnoreCase))
			{
				return player;
			}
		}

		return null;
	}

	private static bool IsLocalPlayer(string profileId, string accountId)
	{
		Player localPlayer = Singleton<GameWorld>.Instance?.MainPlayer;
		if (localPlayer == null)
		{
			return false;
		}

		return (!string.IsNullOrEmpty(profileId) &&
		        string.Equals(localPlayer.ProfileId, profileId, StringComparison.OrdinalIgnoreCase)) ||
		       (!string.IsNullOrEmpty(accountId) &&
		        string.Equals(UavPhoneVisualNetworkService.GetAccountId(localPlayer), accountId, StringComparison.OrdinalIgnoreCase));
	}

	private static string MakeOwnerKey(string profileId, string accountId)
	{
		if (!string.IsNullOrWhiteSpace(profileId))
		{
			return "profile:" + profileId;
		}

		if (!string.IsNullOrWhiteSpace(accountId))
		{
			return "account:" + accountId;
		}

		return "unknown";
	}

	private static float GetDefaultDuration(UavPhoneVisualPhase phase)
	{
		return phase switch
		{
			UavPhoneVisualPhase.Confirming => DefaultConfirmingDurationSeconds,
			UavPhoneVisualPhase.Authorized => DefaultAuthorizedDurationSeconds,
			UavPhoneVisualPhase.Cancelled => DefaultCancelledDurationSeconds,
			UavPhoneVisualPhase.StartActivationPhone => DefaultActivationDurationSeconds,
			_ => DefaultPurchaseDurationSeconds
		};
	}

	private static Color GetPhaseColor(UavPhoneVisualPhase phase, bool success)
	{
		return phase switch
		{
			UavPhoneVisualPhase.Authorized => success ? Green() : Amber(),
			UavPhoneVisualPhase.Cancelled => Red(),
			UavPhoneVisualPhase.StartActivationPhone => Green(),
			UavPhoneVisualPhase.Confirming => AmberBright(),
			_ => Amber()
		};
	}

	private static string GetShortSupportName(ESupportType supportType)
	{
		return supportType switch
		{
			ESupportType.Strafe => "A10",
			ESupportType.DoubleStrafe => "A10X2",
			ESupportType.Extract => "EXFIL",
			ESupportType.PriorityExfil => "PXFIL",
			ESupportType.FocusedSweep => "FOCUS",
			ESupportType.Uav => "UAV",
			_ => "LINK"
		};
	}

	private static GameObject CreatePhoneProp()
	{
		GameObject root = new("Remote TerraGroup Phone Prop");

		GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
		body.name = "Remote TerraGroup Phone Body";
		body.transform.SetParent(root.transform, false);
		body.transform.localPosition = Vector3.zero;
		body.transform.localRotation = Quaternion.identity;
		body.transform.localScale = new Vector3(0.072f, 0.132f, 0.014f);
		Destroy(body.GetComponent<Collider>());

		MeshRenderer bodyRenderer = body.GetComponent<MeshRenderer>();
		bodyRenderer.material = CreateOpaqueMaterial(new Color(0.012f, 0.014f, 0.013f, 1f));
		bodyRenderer.shadowCastingMode = ShadowCastingMode.Off;
		bodyRenderer.receiveShadows = false;

		GameObject screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
		screen.name = "Remote TerraGroup Phone Screen Glow";
		screen.transform.SetParent(root.transform, false);
		screen.transform.localPosition = new Vector3(0f, 0f, -0.0074f);
		screen.transform.localRotation = Quaternion.identity;
		screen.transform.localScale = new Vector3(0.055f, 0.096f, 1f);
		Destroy(screen.GetComponent<Collider>());

		MeshRenderer screenRenderer = screen.GetComponent<MeshRenderer>();
		screenRenderer.material = CreateTransparentMaterial(new Color(0.98f, 0.62f, 0.20f, 0.28f));
		screenRenderer.shadowCastingMode = ShadowCastingMode.Off;
		screenRenderer.receiveShadows = false;

		GameObject pulse = CreateRingMesh("Remote TerraGroup Phone Pulse", 0.019f, 0.0022f, AmberBright());
		pulse.transform.SetParent(screen.transform, false);
		pulse.transform.localPosition = new Vector3(0f, -0.012f, -0.0008f);
		pulse.transform.localRotation = Quaternion.identity;

		TextMesh statusText = CreateScreenText("Remote TerraGroup Status", screen.transform, new Vector3(0f, 0.017f, -0.0012f), 0.0062f, "TG");
		TextMesh subText = CreateScreenText("Remote TerraGroup Substatus", screen.transform, new Vector3(0f, -0.001f, -0.0012f), 0.0041f, "UPLINK");

		return root;
	}

	private void ConfigureProp(GameObject prop)
	{
		foreach (Collider collider in prop.GetComponentsInChildren<Collider>(includeInactive: true))
		{
			collider.enabled = false;
		}

		foreach (Rigidbody body in prop.GetComponentsInChildren<Rigidbody>(includeInactive: true))
		{
			body.isKinematic = true;
			body.detectCollisions = false;
			body.useGravity = false;
		}

		foreach (Renderer renderer in prop.GetComponentsInChildren<Renderer>(includeInactive: true))
		{
			renderer.shadowCastingMode = ShadowCastingMode.Off;
			renderer.receiveShadows = false;
			if (renderer.name.Contains("Screen Glow"))
			{
				_screenRenderer = renderer as MeshRenderer;
			}
			else if (renderer.name.Contains("Pulse"))
			{
				_pulseRenderer = renderer as MeshRenderer;
			}
		}

		_statusText = prop.transform.Find("Remote TerraGroup Phone Screen Glow/Remote TerraGroup Status")?.GetComponent<TextMesh>();
		_subText = prop.transform.Find("Remote TerraGroup Phone Screen Glow/Remote TerraGroup Substatus")?.GetComponent<TextMesh>();
	}

	private static TextMesh CreateScreenText(string name, Transform parent, Vector3 localPosition, float characterSize, string text)
	{
		GameObject obj = new(name);
		obj.transform.SetParent(parent, false);
		obj.transform.localPosition = localPosition;
		obj.transform.localRotation = Quaternion.identity;
		obj.transform.localScale = Vector3.one;

		TextMesh textMesh = obj.AddComponent<TextMesh>();
		textMesh.text = text;
		textMesh.anchor = TextAnchor.MiddleCenter;
		textMesh.alignment = TextAlignment.Center;
		textMesh.characterSize = characterSize;
		textMesh.fontSize = 42;
		textMesh.color = AmberBright();
		return textMesh;
	}

	private static GameObject CreateRingMesh(string name, float radius, float width, Color color)
	{
		const int segments = 42;
		var vertices = new Vector3[segments * 2];
		var triangles = new int[segments * 6];
		float inner = Mathf.Max(0.001f, radius - width);

		for (int i = 0; i < segments; i++)
		{
			float angle = Mathf.PI * 2f * i / segments;
			float sin = Mathf.Sin(angle);
			float cos = Mathf.Cos(angle);
			vertices[i * 2] = new Vector3(cos * radius, sin * radius, 0f);
			vertices[i * 2 + 1] = new Vector3(cos * inner, sin * inner, 0f);

			int next = (i + 1) % segments;
			int tri = i * 6;
			triangles[tri] = i * 2;
			triangles[tri + 1] = next * 2;
			triangles[tri + 2] = i * 2 + 1;
			triangles[tri + 3] = next * 2;
			triangles[tri + 4] = next * 2 + 1;
			triangles[tri + 5] = i * 2 + 1;
		}

		Mesh mesh = new()
		{
			name = name + " Mesh",
			vertices = vertices,
			triangles = triangles
		};
		mesh.RecalculateBounds();
		mesh.RecalculateNormals();

		GameObject obj = new(name);
		obj.AddComponent<MeshFilter>().sharedMesh = mesh;
		MeshRenderer renderer = obj.AddComponent<MeshRenderer>();
		renderer.material = CreateTransparentMaterial(color);
		renderer.shadowCastingMode = ShadowCastingMode.Off;
		renderer.receiveShadows = false;
		return obj;
	}

	private static Material CreateOpaqueMaterial(Color color)
	{
		Shader shader = Shader.Find("Standard")
		                ?? Shader.Find("Unlit/Color")
		                ?? Shader.Find("Hidden/Internal-Colored");
		Material material = new(shader)
		{
			color = color
		};
		SetMaterialColor(material, color);
		return material;
	}

	private static Material CreateTransparentMaterial(Color color)
	{
		Shader shader = Shader.Find("Particles/Standard Unlit")
		                ?? Shader.Find("Sprites/Default")
		                ?? Shader.Find("Unlit/Color")
		                ?? Shader.Find("Hidden/Internal-Colored");
		Material material = new(shader)
		{
			color = color,
			renderQueue = 3100
		};
		SetMaterialColor(material, color);
		material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
		material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
		material.SetInt("_ZWrite", 0);
		material.DisableKeyword("_ALPHATEST_ON");
		material.EnableKeyword("_ALPHABLEND_ON");
		material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
		return material;
	}

	private static void SetRendererColor(Renderer renderer, Color color)
	{
		if (renderer?.material == null)
		{
			return;
		}

		SetMaterialColor(renderer.material, color);
	}

	private static void SetMaterialColor(Material material, Color color)
	{
		if (material == null)
		{
			return;
		}

		material.color = color;
		if (material.HasProperty(s_color))
		{
			material.SetColor(s_color, color);
		}

		if (material.HasProperty(s_tintColor))
		{
			material.SetColor(s_tintColor, color);
		}
	}

	private static AudioClip CreateBeepClip()
	{
		const int frequency = 44100;
		const float duration = 0.085f;
		int sampleCount = Mathf.CeilToInt(frequency * duration);
		float[] samples = new float[sampleCount];

		for (int i = 0; i < sampleCount; i++)
		{
			float t = i / (float)frequency;
			float envelope = Mathf.Exp(-t * 38f);
			float tone = Mathf.Sin(Mathf.PI * 2f * 1320f * t) * 0.16f;
			float staticClick = Mathf.Sin(Mathf.PI * 2f * 240f * t) * 0.025f;
			samples[i] = (tone + staticClick) * envelope;
		}

		AudioClip clip = AudioClip.Create("Remote TerraGroup Uplink Beep", sampleCount, 1, frequency, false);
		clip.SetData(samples, 0);
		return clip;
	}

	private static Color Amber()
	{
		return new Color(0.95f, 0.58f, 0.18f, 0.9f);
	}

	private static Color AmberBright()
	{
		return new Color(1f, 0.72f, 0.32f, 0.96f);
	}

	private static Color Green()
	{
		return new Color(0.30f, 1f, 0.62f, 0.95f);
	}

	private static Color Red()
	{
		return new Color(1f, 0.28f, 0.18f, 0.95f);
	}

	private static float EaseOutCubic(float t)
	{
		return 1f - Mathf.Pow(1f - t, 3f);
	}

	private static float EaseInCubic(float t)
	{
		return t * t * t;
	}

	private void DestroyVisual(string reason)
	{
		if (_destroyed)
		{
			return;
		}

		_destroyed = true;
		_audioSource?.Stop();
		FireSupportPlugin.LogSource?.LogInfo(
			$"UAV phone visual ended/destroyed. owner={_profileId}, reason={reason}.");
		Destroy(gameObject);
	}
}
