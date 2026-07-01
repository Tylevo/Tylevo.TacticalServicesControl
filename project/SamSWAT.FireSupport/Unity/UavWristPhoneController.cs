using Comfort.Common;
using Cysharp.Threading.Tasks;
using EFT;
using SamSWAT.FireSupport.ArysReloaded.Utils;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public sealed class UavWristPhoneController : UpdatableComponentBase
{
	private const string NativePhoneBundlePath = "assets/content/items/barter/phone/item_phone.bundle";
	private const string NativePhoneAssetName = "item_phone";
	private const float SequenceDurationSeconds = 3.15f;
	private const float TapTimeSeconds = 1.35f;
	private const float StowStartSeconds = 2.55f;

	private static readonly int s_tintColor = Shader.PropertyToID("_TintColor");
	private static readonly int s_color = Shader.PropertyToID("_Color");
	private static UavWristPhoneController s_activePhone;

	private readonly List<Renderer> _renderers = new();
	private CancellationToken _cancellationToken;
	private Transform _cameraTransform;
	private Transform _mountTransform;
	private Transform _forearmTransform;
	private Transform _handTransform;
	private GameObject _phoneRoot;
	private Transform _phoneTransform;
	private MeshRenderer _screenGlowRenderer;
	private MeshRenderer _tapPulseRenderer;
	private TextMesh _statusText;
	private TextMesh _subText;
	private AudioSource _audioSource;
	private float _startedAt;
	private float _tapPlayedAt = -1f;
	private bool _destroyed;
	private bool _isArmMounted;
	private bool _usedNativePrefab;
	private bool _canPoseArm;

	public static void Play(CancellationToken cancellationToken)
	{
		if (!PluginSettings.UavWristPhoneVisual.Value)
		{
			return;
		}

		PlayAsync(cancellationToken).Forget();
	}

	public override void ManualUpdate()
	{
		if (_destroyed)
		{
			return;
		}

		if (_cancellationToken.IsCancellationRequested)
		{
			DestroyPhone();
			return;
		}

		if (_cameraTransform == null || _mountTransform == null || _phoneTransform == null)
		{
			DestroyPhone();
			return;
		}

		float elapsed = Time.time - _startedAt;
		if (elapsed >= SequenceDurationSeconds)
		{
			DestroyPhone();
			return;
		}

		UpdatePhonePose(elapsed);
		UpdateScreen(elapsed);
	}

	protected override void OnDisable()
	{
		base.OnDisable();

		if (s_activePhone == this)
		{
			s_activePhone = null;
		}
	}

	private void LateUpdate()
	{
		if (_destroyed || !HasFinishedInitialization || !_canPoseArm)
		{
			return;
		}

		if (_cameraTransform == null || _forearmTransform == null)
		{
			return;
		}

		float elapsed = Time.time - _startedAt;
		if (elapsed < SequenceDurationSeconds)
		{
			ApplyWatchArmPose(elapsed);
		}
	}

	private static async UniTaskVoid PlayAsync(CancellationToken cancellationToken)
	{
		try
		{
			GameWorld gameWorld = Singleton<GameWorld>.Instance;
			Player player = gameWorld?.MainPlayer;
			Transform cameraTransform = player?.CameraPosition;
			if (cameraTransform == null)
			{
				return;
			}

			GameObject prefab = await AssetLoader.LoadNativeAssetAsync(NativePhoneBundlePath, NativePhoneAssetName);
			if (cancellationToken.IsCancellationRequested)
			{
				return;
			}

			if (s_activePhone != null)
			{
				s_activePhone.DestroyPhone();
			}

			ArmRig armRig = FindArmRig(player, cameraTransform);
			Transform mountTransform = armRig.MountTransform ?? cameraTransform;
			bool isArmMounted = mountTransform != cameraTransform;
			GameObject phone;
			if (prefab != null)
			{
				phone = Instantiate(prefab);
			}
			else
			{
				FireSupportPlugin.LogSource.LogWarning("TSC wrist phone visual using fallback phone shell: native phone prefab failed to load.");
				phone = CreateFallbackPhone();
			}

			phone.name = "TSC UAV Wrist Phone";
			phone.transform.SetParent(mountTransform, false);

			var controller = phone.AddComponent<UavWristPhoneController>();
			controller.Initialize(cameraTransform, mountTransform, armRig.ForearmTransform, armRig.HandTransform, isArmMounted, prefab != null, CancellationToken.None);
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource.LogWarning($"UAV wrist phone visual skipped. {ex}");
		}
	}

	private readonly struct ArmRig(Transform mountTransform, Transform forearmTransform, Transform handTransform)
	{
		public readonly Transform MountTransform = mountTransform;
		public readonly Transform ForearmTransform = forearmTransform;
		public readonly Transform HandTransform = handTransform;
	}

	private static ArmRig FindArmRig(Player player, Transform cameraTransform)
	{
		Transform animatorForearm = FindAnimatorBone(player, HumanBodyBones.LeftLowerArm);
		Transform animatorHand = FindAnimatorBone(player, HumanBodyBones.LeftHand);
		if (animatorForearm != null || animatorHand != null)
		{
			return new ArmRig(animatorForearm ?? animatorHand, animatorForearm, animatorHand);
		}

		var visitedRoots = new HashSet<Transform>();
		Transform bestMount = null;
		Transform bestForearm = null;
		Transform bestHand = null;
		int bestMountScore = 0;
		int bestForearmScore = 0;
		int bestHandScore = 0;

		foreach (Transform root in GetCandidateRoots(player, cameraTransform))
		{
			if (root == null || !visitedRoots.Add(root))
			{
				continue;
			}

			foreach (Transform transform in root.GetComponentsInChildren<Transform>(includeInactive: true))
			{
				int mountScore = GetArmMountScore(transform.name);
				if (mountScore > bestMountScore)
				{
					bestMountScore = mountScore;
					bestMount = transform;
				}

				int forearmScore = GetArmBoneScore(transform.name, ArmBoneKind.Forearm);
				if (forearmScore > bestForearmScore)
				{
					bestForearmScore = forearmScore;
					bestForearm = transform;
				}

				int handScore = GetArmBoneScore(transform.name, ArmBoneKind.Hand);
				if (handScore > bestHandScore)
				{
					bestHandScore = handScore;
					bestHand = transform;
				}
			}
		}

		return bestMountScore >= 45
			? new ArmRig(bestMount, bestForearm, bestHand)
			: new ArmRig(null, null, null);
	}

	private enum ArmBoneKind
	{
		Forearm,
		Hand
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

	private static IEnumerable<Transform> GetCandidateRoots(Player player, Transform cameraTransform)
	{
		if (player != null)
		{
			yield return ((Component)player).transform.root;
		}

		if (cameraTransform != null)
		{
			yield return cameraTransform.root;
		}
	}

	private static int GetArmMountScore(string transformName)
	{
		if (string.IsNullOrEmpty(transformName))
		{
			return 0;
		}

		string name = transformName.ToLowerInvariant().Replace("_", " ").Replace("-", " ");
		bool left = name.Contains("left") ||
		            name.Contains(" l ") ||
		            name.Contains("humanl") ||
		            name.Contains(" lforearm") ||
		            name.Contains("l forearm") ||
		            name.Contains("lhand") ||
		            name.Contains("l hand") ||
		            name.Contains("lpalm") ||
		            name.Contains("l palm");
		bool right = name.Contains("right") ||
		             name.Contains(" r ") ||
		             name.Contains("humanr") ||
		             name.Contains(" rforearm") ||
		             name.Contains("r forearm") ||
		             name.Contains("rhand") ||
		             name.Contains("r hand") ||
		             name.Contains("rpalm") ||
		             name.Contains("r palm");

		if (!left || right)
		{
			return 0;
		}

		if (name.Contains("forearm") || name.Contains("lowerarm"))
		{
			return 100;
		}

		if (name.Contains("wrist"))
		{
			return 95;
		}

		if (name.Contains("hand") || name.Contains("palm"))
		{
			return 70;
		}

		if (name.Contains("upperarm"))
		{
			return 45;
		}

		return 0;
	}

	private static int GetArmBoneScore(string transformName, ArmBoneKind boneKind)
	{
		if (string.IsNullOrEmpty(transformName))
		{
			return 0;
		}

		string name = transformName.ToLowerInvariant().Replace("_", " ").Replace("-", " ");
		bool left = name.Contains("left") ||
		            name.Contains("humanl") ||
		            name.Contains("l forearm") ||
		            name.Contains("l hand") ||
		            name.Contains("l palm") ||
		            name.Contains("l wrist") ||
		            name.Contains("lforearm") ||
		            name.Contains("lhand") ||
		            name.Contains("lpalm") ||
		            name.Contains("lwrist");
		bool right = name.Contains("right") ||
		             name.Contains("humanr") ||
		             name.Contains("r forearm") ||
		             name.Contains("r hand") ||
		             name.Contains("r palm") ||
		             name.Contains("r wrist") ||
		             name.Contains("rforearm") ||
		             name.Contains("rhand") ||
		             name.Contains("rpalm") ||
		             name.Contains("rwrist");

		if (!left || right)
		{
			return 0;
		}

		if (boneKind == ArmBoneKind.Forearm)
		{
			if (name.Contains("forearm") || name.Contains("lowerarm"))
			{
				return 100;
			}

			if (name.Contains("wrist"))
			{
				return 70;
			}
		}
		else
		{
			if (name.Contains("hand") || name.Contains("palm"))
			{
				return 100;
			}

			if (name.Contains("wrist"))
			{
				return 85;
			}
		}

		return 0;
	}

	private static GameObject CreateFallbackPhone()
	{
		GameObject root = new("TSC UAV Fallback Phone");

		GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
		body.name = "TSC Fallback Phone Body";
		body.transform.SetParent(root.transform, false);
		body.transform.localPosition = Vector3.zero;
		body.transform.localRotation = Quaternion.identity;
		body.transform.localScale = new Vector3(0.064f, 0.122f, 0.012f);
		Destroy(body.GetComponent<Collider>());

		MeshRenderer bodyRenderer = body.GetComponent<MeshRenderer>();
		bodyRenderer.material = CreateOpaqueMaterial(new Color(0.015f, 0.017f, 0.018f, 1f));
		bodyRenderer.shadowCastingMode = ShadowCastingMode.Off;
		bodyRenderer.receiveShadows = false;

		GameObject screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
		screen.name = "TSC Fallback Phone Glass";
		screen.transform.SetParent(root.transform, false);
		screen.transform.localPosition = new Vector3(0f, 0.003f, -0.0064f);
		screen.transform.localRotation = Quaternion.identity;
		screen.transform.localScale = new Vector3(0.052f, 0.09f, 1f);
		Destroy(screen.GetComponent<Collider>());

		MeshRenderer screenRenderer = screen.GetComponent<MeshRenderer>();
		screenRenderer.material = CreateTransparentMaterial(new Color(0.02f, 0.12f, 0.11f, 0.88f));
		screenRenderer.shadowCastingMode = ShadowCastingMode.Off;
		screenRenderer.receiveShadows = false;

		return root;
	}

	private void Initialize(
		Transform cameraTransform,
		Transform mountTransform,
		Transform forearmTransform,
		Transform handTransform,
		bool isArmMounted,
		bool usedNativePrefab,
		CancellationToken cancellationToken)
	{
		_cameraTransform = cameraTransform;
		_mountTransform = mountTransform;
		_forearmTransform = forearmTransform;
		_handTransform = handTransform;
		_cancellationToken = cancellationToken;
		_phoneRoot = gameObject;
		_phoneTransform = transform;
		_startedAt = Time.time;
		_isArmMounted = isArmMounted;
		_usedNativePrefab = usedNativePrefab;
		_canPoseArm = PluginSettings.UavWristPhoneArmPose.Value && _isArmMounted && _forearmTransform != null;
		s_activePhone = this;

		ConfigureNativePhone();
		CreateScreenOverlay();
		UpdatePhonePose(0f);
		UpdateScreen(0f);

		TscDiagnostics.LogPhone(
			$"TSC wrist phone visual started. Mount={_mountTransform?.name ?? "null"}, Forearm={_forearmTransform?.name ?? "null"}, Hand={_handTransform?.name ?? "null"}, armMount={_isArmMounted}, armPose={_canPoseArm}, nativePrefab={_usedNativePrefab}.");
		HasFinishedInitialization = true;
	}

	private void ConfigureNativePhone()
	{
		foreach (Collider collider in GetComponentsInChildren<Collider>(includeInactive: true))
		{
			collider.enabled = false;
		}

		foreach (Rigidbody rigidbody in GetComponentsInChildren<Rigidbody>(includeInactive: true))
		{
			rigidbody.isKinematic = true;
			rigidbody.detectCollisions = false;
		}

		foreach (Renderer renderer in GetComponentsInChildren<Renderer>(includeInactive: true))
		{
			renderer.shadowCastingMode = ShadowCastingMode.Off;
			renderer.receiveShadows = false;
			_renderers.Add(renderer);
		}
	}

	private void CreateScreenOverlay()
	{
		Transform screenAnchor = CreateChild("TSC Phone Screen Anchor", _phoneTransform);
		screenAnchor.localPosition = new Vector3(0f, -0.004f, -0.00635f);
		screenAnchor.localRotation = Quaternion.Euler(0f, 180f, 0f);
		screenAnchor.localScale = Vector3.one;

		GameObject screenGlow = GameObject.CreatePrimitive(PrimitiveType.Quad);
		screenGlow.name = "TSC Phone Screen Glow";
		screenGlow.transform.SetParent(screenAnchor, false);
		screenGlow.transform.localPosition = new Vector3(0f, 0.012f, -0.0004f);
		screenGlow.transform.localRotation = Quaternion.identity;
		screenGlow.transform.localScale = new Vector3(0.045f, 0.064f, 1f);
		Destroy(screenGlow.GetComponent<Collider>());
		_screenGlowRenderer = screenGlow.GetComponent<MeshRenderer>();
		_screenGlowRenderer.material = CreateTransparentMaterial(new Color(0.03f, 0.95f, 0.72f, 0.16f));
		_screenGlowRenderer.shadowCastingMode = ShadowCastingMode.Off;
		_screenGlowRenderer.receiveShadows = false;

		GameObject tapPulse = CreateRingMesh("TSC Phone Tap Pulse", 0.016f, 0.0022f, new Color(0.68f, 1f, 0.9f, 0.72f));
		tapPulse.transform.SetParent(screenAnchor, false);
		tapPulse.transform.localPosition = new Vector3(-0.005f, 0.002f, -0.0008f);
		tapPulse.transform.localRotation = Quaternion.identity;
		_tapPulseRenderer = tapPulse.GetComponent<MeshRenderer>();

		_statusText = CreateScreenText("TSC Phone Status", screenAnchor, new Vector3(0f, 0.023f, -0.0012f), 0.0065f, "TSC");
		_subText = CreateScreenText("TSC Phone Substatus", screenAnchor, new Vector3(0f, 0.009f, -0.0012f), 0.0044f, "UAV LINK");

		_audioSource = _phoneRoot.AddComponent<AudioSource>();
		_audioSource.playOnAwake = false;
		_audioSource.spatialBlend = 0f;
		_audioSource.volume = 0.32f;
		_audioSource.clip = CreateTapClip();
	}

	private static Transform CreateChild(string name, Transform parent)
	{
		GameObject obj = new(name);
		obj.transform.SetParent(parent, false);
		return obj.transform;
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
		textMesh.color = new Color(0.78f, 1f, 0.92f, 0.9f);
		return textMesh;
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

		if (material.HasProperty(s_color))
		{
			material.SetColor(s_color, color);
		}

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

		material.SetColor(s_color, color);
		if (material.HasProperty(s_tintColor))
		{
			material.SetColor(s_tintColor, color);
		}

		material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
		material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
		material.SetInt("_ZWrite", 0);
		material.DisableKeyword("_ALPHATEST_ON");
		material.EnableKeyword("_ALPHABLEND_ON");
		material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
		return material;
	}

	private static GameObject CreateRingMesh(string name, float radius, float width, Color color)
	{
		const int segments = 48;
		var vertices = new Vector3[segments * 2];
		var triangles = new int[segments * 6];
		float inner = Mathf.Max(0.001f, radius - width);

		for (var i = 0; i < segments; i++)
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

	private static AudioClip CreateTapClip()
	{
		const int frequency = 44100;
		const float duration = 0.075f;
		int sampleCount = Mathf.CeilToInt(frequency * duration);
		float[] samples = new float[sampleCount];

		for (var i = 0; i < sampleCount; i++)
		{
			float t = i / (float)frequency;
			float envelope = Mathf.Exp(-t * 46f);
			samples[i] = Mathf.Sin(Mathf.PI * 2f * 1180f * t) * envelope * 0.18f;
		}

		AudioClip clip = AudioClip.Create("TSC Phone Tap", sampleCount, 1, frequency, false);
		clip.SetData(samples, 0);
		return clip;
	}

	private void ApplyWatchArmPose(float elapsed)
	{
		float intro = EaseOutCubic(Mathf.Clamp01(elapsed / 0.62f));
		float outro = Mathf.Clamp01((elapsed - StowStartSeconds) / Mathf.Max(0.01f, SequenceDurationSeconds - StowStartSeconds));
		float hold = intro * (1f - EaseInCubic(outro));
		if (hold <= 0.001f)
		{
			return;
		}

		float tapKick = 0f;
		if (_tapPlayedAt >= 0f)
		{
			float tapElapsed = Mathf.Clamp01((elapsed - _tapPlayedAt) / 0.34f);
			tapKick = Mathf.Sin(tapElapsed * Mathf.PI) * 0.24f;
		}

		Quaternion cameraForward = Quaternion.LookRotation(_cameraTransform.forward, _cameraTransform.up);
		Quaternion forearmTarget = cameraForward * Quaternion.Euler(20f, -34f, -72f);
		_forearmTransform.rotation = Quaternion.Slerp(_forearmTransform.rotation, forearmTarget, Mathf.Clamp01(hold * 0.78f));

		if (_handTransform != null)
		{
			Quaternion handTarget = cameraForward * Quaternion.Euler(8f + tapKick * 11f, -7f, -21f - tapKick * 16f);
			_handTransform.rotation = Quaternion.Slerp(_handTransform.rotation, handTarget, Mathf.Clamp01(hold * 0.88f));
		}
	}

	private void UpdatePhonePose(float elapsed)
	{
		if (_isArmMounted)
		{
			UpdateArmMountedPhonePose(elapsed);
			return;
		}

		float intro = EaseOutCubic(Mathf.Clamp01(elapsed / 0.55f));
		float outro = Mathf.Clamp01((elapsed - StowStartSeconds) / Mathf.Max(0.01f, SequenceDurationSeconds - StowStartSeconds));
		float visible = intro * (1f - EaseInCubic(outro));

		Vector3 stowedPosition = new(0.18f, -0.34f, 0.48f);
		Vector3 activePosition = new(0.17f, -0.16f, 0.43f);
		Vector3 localPosition = Vector3.Lerp(stowedPosition, activePosition, visible);
		localPosition += new Vector3(0f, Mathf.Sin(elapsed * 10f) * 0.0035f * visible, 0f);
		_phoneTransform.localPosition = localPosition;

		Quaternion stowedRotation = Quaternion.Euler(78f, -12f, -26f);
		Quaternion activeRotation = Quaternion.Euler(63f, -8f, -14f);
		_phoneTransform.localRotation = Quaternion.Slerp(stowedRotation, activeRotation, visible);
		_phoneTransform.localScale = Vector3.one * 0.88f;
	}

	private void UpdateArmMountedPhonePose(float elapsed)
	{
		float intro = EaseOutCubic(Mathf.Clamp01(elapsed / 0.55f));
		float outro = Mathf.Clamp01((elapsed - StowStartSeconds) / Mathf.Max(0.01f, SequenceDurationSeconds - StowStartSeconds));
		float visible = intro * (1f - EaseInCubic(outro));

		Vector3 stowedOffset =
			_cameraTransform.right * -0.085f +
			_cameraTransform.up * -0.075f +
			_cameraTransform.forward * 0.11f;
		Vector3 activeOffset =
			_cameraTransform.right * -0.055f +
			_cameraTransform.up * -0.012f +
			_cameraTransform.forward * 0.055f;

		Vector3 localBob = _cameraTransform.up * (Mathf.Sin(elapsed * 9f) * 0.0025f * visible);
		_phoneTransform.position = _mountTransform.position + Vector3.Lerp(stowedOffset, activeOffset, visible) + localBob;

		Quaternion stowedRotation = Quaternion.LookRotation(_cameraTransform.forward, _cameraTransform.up) *
		                            Quaternion.Euler(18f, -8f, -38f);
		Quaternion activeRotation = Quaternion.LookRotation(_cameraTransform.forward, _cameraTransform.up) *
		                            Quaternion.Euler(2f, 0f, -11f);
		_phoneTransform.rotation = Quaternion.Slerp(stowedRotation, activeRotation, visible);
		_phoneTransform.localScale = Vector3.one * (_usedNativePrefab ? 0.92f : 0.95f);
	}

	private void UpdateScreen(float elapsed)
	{
		float intro = Mathf.Clamp01(elapsed / 0.45f);
		float screenAlpha = Mathf.SmoothStep(0f, 1f, intro);

		if (_screenGlowRenderer != null)
		{
			SetRendererColor(_screenGlowRenderer, new Color(0.03f, 0.95f, 0.72f, 0.06f + screenAlpha * 0.22f));
		}

		if (elapsed >= TapTimeSeconds && _tapPlayedAt < 0f)
		{
			_tapPlayedAt = elapsed;
			_audioSource?.Play();
		}

		float tapElapsed = _tapPlayedAt < 0f ? -1f : elapsed - _tapPlayedAt;
		float tapT = tapElapsed < 0f ? 1f : Mathf.Clamp01(tapElapsed / 0.55f);

		if (_tapPulseRenderer != null)
		{
			float pulseAlpha = tapElapsed < 0f ? 0f : (1f - tapT) * 0.78f;
			float pulseScale = Mathf.Lerp(0.28f, 1.45f, EaseOutCubic(tapT));
			_tapPulseRenderer.transform.localScale = Vector3.one * pulseScale;
			SetRendererColor(_tapPulseRenderer, new Color(0.72f, 1f, 0.9f, pulseAlpha));
		}

		if (_statusText != null)
		{
			_statusText.text = elapsed < TapTimeSeconds ? "TSC" : "UAV LINK";
			Color color = _statusText.color;
			color.a = 0.35f + screenAlpha * 0.55f;
			_statusText.color = color;
		}

		if (_subText != null)
		{
			_subText.text = elapsed < TapTimeSeconds ? "STANDBY" : "SCANNING";
			Color color = _subText.color;
			color.a = 0.3f + screenAlpha * 0.55f;
			_subText.color = color;
		}
	}

	private static void SetRendererColor(Renderer renderer, Color color)
	{
		if (renderer?.material == null)
		{
			return;
		}

		renderer.material.color = color;
		if (renderer.material.HasProperty(s_color))
		{
			renderer.material.SetColor(s_color, color);
		}
		if (renderer.material.HasProperty(s_tintColor))
		{
			renderer.material.SetColor(s_tintColor, color);
		}
	}

	private static float EaseOutCubic(float t)
	{
		float inverse = 1f - t;
		return 1f - inverse * inverse * inverse;
	}

	private static float EaseInCubic(float t)
	{
		return t * t * t;
	}

	private void DestroyPhone()
	{
		if (_destroyed)
		{
			return;
		}

		_destroyed = true;
		if (s_activePhone == this)
		{
			s_activePhone = null;
		}

		TscDiagnostics.LogPhone("TSC wrist phone visual destroyed.");
		Destroy(gameObject);
	}
}
