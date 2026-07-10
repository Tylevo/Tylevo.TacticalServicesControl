using Comfort.Common;
using Cysharp.Threading.Tasks;
using EFT;
using SamSWAT.FireSupport.ArysReloaded.Utils;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public sealed class UavReconOverlay : UpdatableComponentBase
{
	private sealed class Contact
	{
		public RectTransform RectTransform;
		public Image Image;
		public Vector3 Position;
	}

	private sealed class RadarAssets
	{
		public GameObject RadarPrefab;
		public GameObject BlipPrefab;
	}

	private readonly struct RadarPaletteColors(
		Color backgroundColor,
		Color borderColor,
		Color pulseColor,
		Color timerColor)
	{
		public readonly Color BackgroundColor = backgroundColor;
		public readonly Color BorderColor = borderColor;
		public readonly Color PulseColor = pulseColor;
		public readonly Color TimerColor = timerColor;
	}

	private const string RadarBundlePath = "assets/content/ui/uav_radarhud.bundle";
	private const string RadarHudAssetName = "Assets/Examples/Halo Reach/Hud/RadarHUD.prefab";
	private const string RadarBlipAssetName = "Assets/Examples/Halo Reach/Hud/RadarBlipHUD.prefab";
	private const float RadarBaseScale = 0.72f;
	private const float RadarIntroDurationSeconds = 0.45f;
	private const float RadarIntroScaleMultiplier = 0.82f;
	private const float RadarOutroDurationSeconds = 0.24f;
	private const float RadarOutroScaleMultiplier = 0.92f;
	private static readonly Vector2 s_radarAnchoredPosition = new(-255f, 205f);
	private static readonly Vector2 s_radarIntroOffset = new(10f, -8f);

	private readonly Dictionary<string, Contact> _contacts = new();
	private readonly HashSet<string> _seenContactIds = new();
	private readonly List<string> _contactsToRemove = new();

	private GameObject _radarHud;
	private GameObject _blipPrefab;
	private RectTransform _radarBaseTransform;
	private RectTransform _radarBackgroundTransform;
	private RectTransform _radarBorderTransform;
	private RectTransform _pulseTransform;
	private RectTransform _timerProgressTransform;
	private CanvasGroup _canvasGroup;
	private Image _pulseImage;
	private Image _timerBackgroundImage;
	private Image _timerRailImage;
	private Image _timerProgressImage;
	private RadarPaletteColors _paletteColors;
	private UavRadarPalette _currentPalette;
	private Text _timerLabelText;
	private Text _timerValueText;
	private GameWorld _gameWorld;
	private Player _player;
	private CancellationToken _cancellationToken;
	private float _activeUntil;
	private float _activeDuration;
	private float _activeScanInterval;
	private float _activeRangeMeters;
	private float _nextScanTime;
	private float _introStartedAt;
	private float _outroStartedAt;
	private bool _isClosing;
	private static Sprite s_timerBackgroundSprite;
	private static Sprite s_solidSprite;
	private static UniTask<RadarAssets>? s_radarAssetsLoadTask;
	private static RadarAssets s_radarAssets;

	public static UavReconOverlay Instance { get; private set; }

	public static void Activate(
		float durationSeconds,
		CancellationToken cancellationToken,
		bool playActivationVisual = true,
		float scanInterval = 0f,
		float rangeMeters = 0f)
	{
		if (durationSeconds <= 0)
		{
			return;
		}

		void StartRadar()
		{
			if (Instance != null)
			{
				TscDiagnostics.LogDashboard($"UAV radar activate reuse duration={durationSeconds:0.#}s scan={scanInterval:0.##} range={rangeMeters:0.#}");
				Instance.StartRecon(durationSeconds, cancellationToken, scanInterval, rangeMeters);
				return;
			}

			if (Instance == null)
			{
				var overlayObject = new GameObject("FireSupportUavReconOverlay");
				Instance = overlayObject.AddComponent<UavReconOverlay>();
			}

			Instance.StartRecon(durationSeconds, cancellationToken, scanInterval, rangeMeters);
		}

		if (playActivationVisual &&
		    UavDeviceActivationController.TryPlay(StartRadar, cancellationToken))
		{
			return;
		}

		if (playActivationVisual)
		{
			FireSupportPlugin.LogSource.LogInfo("UAV activation device animation did not start; using immediate radar fallback.");
			UavWristPhoneController.Play(cancellationToken);
		}

		StartRadar();
	}

	public override void ManualUpdate()
	{
		if (_cancellationToken.IsCancellationRequested || !_gameWorld.IsMainPlayerAlive())
		{
			Close();
			return;
		}

		if (_isClosing)
		{
			UpdateOutroVisuals();
			return;
		}

		if (Time.time >= _activeUntil)
		{
			BeginCloseAnimation();
			return;
		}

		UpdateIntroVisuals();
		UpdateTimer();
		RefreshPaletteIfChanged();
		UpdatePulse();

		if (Time.time >= _nextScanTime)
		{
			ScanContacts();
			_nextScanTime = Time.time + GetActiveScanInterval();
		}

		UpdateContactPositions();
	}

	protected override void OnAwake()
	{
		Instance = this;
		if (TryResolveWorldAndPlayer())
		{
			CreateUi().Forget();
			return;
		}

		WaitForWorldAndPlayerThenCreateUi().Forget();
	}

	private bool TryResolveWorldAndPlayer()
	{
		_gameWorld = Singleton<GameWorld>.Instance;
		_player = _gameWorld?.MainPlayer;
		return _gameWorld != null && _player != null;
	}

	private async UniTaskVoid WaitForWorldAndPlayerThenCreateUi()
	{
		float deadline = Time.time + 8f;
		while (Time.time < deadline)
		{
			if (TryResolveWorldAndPlayer())
			{
				FireSupportPlugin.LogSource?.LogInfo("UAV radar delayed initialization completed after GameWorld/MainPlayer became available.");
				CreateUi().Forget();
				return;
			}

			await UniTask.DelayFrame(1);
		}

		FireSupportPlugin.LogSource?.LogWarning("UAV radar skipped: GameWorld/MainPlayer was unavailable after waiting. This is expected on Fika headless but not on a player client.");
		Close();
	}

	protected override void OnDisable()
	{
		base.OnDisable();
		Instance = null;
	}

	private void StartRecon(
		float durationSeconds,
		CancellationToken cancellationToken,
		float scanInterval,
		float rangeMeters)
	{
		_cancellationToken = cancellationToken;
		_activeUntil = Mathf.Max(_activeUntil, Time.time + durationSeconds);
		_activeDuration = Mathf.Max(1f, _activeUntil - Time.time);
		_activeScanInterval = scanInterval > 0f
			? scanInterval
			: UavReconSettings.GetScanInterval(ESupportType.Uav);
		_activeRangeMeters = rangeMeters > 0f
			? rangeMeters
			: UavReconSettings.GetRangeMeters(ESupportType.Uav);
		_nextScanTime = 0f;
		RestartIntroAnimation();
		gameObject.SetActive(true);
	}

	private async UniTaskVoid CreateUi()
	{
		try
		{
			RadarAssets assets = await LoadRadarAssetsOnceAsync();
			GameObject radarPrefab = assets.RadarPrefab;
			_blipPrefab = assets.BlipPrefab;

			_radarHud = Instantiate(radarPrefab, transform, false);
			_radarHud.name = "FireSupport UAV Radar HUD";
			_canvasGroup = _radarHud.GetComponent<CanvasGroup>() ?? _radarHud.AddComponent<CanvasGroup>();
			_canvasGroup.alpha = 0f;

			Canvas canvas = _radarHud.GetComponent<Canvas>();
			if (canvas != null)
			{
				canvas.renderMode = RenderMode.ScreenSpaceOverlay;
				canvas.sortingOrder = 3000;
			}

			CanvasScaler scaler = _radarHud.GetComponent<CanvasScaler>();
			if (scaler != null)
			{
				scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
				scaler.referenceResolution = new Vector2(1920, 1080);
				scaler.matchWidthOrHeight = 0.5f;
			}

			_radarBaseTransform = _radarHud.transform.Find("Radar") as RectTransform;
			_radarBackgroundTransform = _radarHud.transform.Find("Radar/RadarBackground") as RectTransform;
			_radarBorderTransform = _radarHud.transform.Find("Radar/RadarBorder") as RectTransform;
			_pulseTransform = _radarHud.transform.Find("Radar/RadarPulse") as RectTransform;
			if (_radarBaseTransform == null || _radarBackgroundTransform == null || _radarBorderTransform == null || _pulseTransform == null)
			{
				throw new InvalidOperationException("UAV radar HUD prefab hierarchy is not in the expected format.");
			}

			ConfigureRadarLayout();
			TintRadarImages();
			DisableRaycasts(_radarHud.transform);
			CreateTimerUi();
			ApplyTimerPalette();
			RestartIntroAnimation();

			HasFinishedInitialization = true;
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource.LogError(ex);
			Close();
		}
	}

	private static UniTask<RadarAssets> LoadRadarAssetsOnceAsync()
	{
		if (s_radarAssets != null)
		{
			return UniTask.FromResult(s_radarAssets);
		}

		s_radarAssetsLoadTask ??= LoadRadarAssetsAsync().Preserve();
		return s_radarAssetsLoadTask.Value;
	}

	private static async UniTask<RadarAssets> LoadRadarAssetsAsync()
	{
		try
		{
			GameObject radarPrefab = await AssetLoader.LoadAssetAsync<GameObject>(RadarBundlePath, RadarHudAssetName);
			GameObject blipPrefab = await AssetLoader.LoadAssetAsync<GameObject>(RadarBundlePath, RadarBlipAssetName);

			if (radarPrefab == null || blipPrefab == null)
			{
				throw new InvalidOperationException("UAV radar HUD bundle did not load expected prefabs.");
			}

			s_radarAssets = new RadarAssets
			{
				RadarPrefab = radarPrefab,
				BlipPrefab = blipPrefab
			};
			return s_radarAssets;
		}
		catch
		{
			s_radarAssetsLoadTask = null;
			throw;
		}
	}

	private void ConfigureRadarLayout()
	{
		_radarBaseTransform.anchorMin = new Vector2(1f, 0f);
		_radarBaseTransform.anchorMax = new Vector2(1f, 0f);
		_radarBaseTransform.pivot = new Vector2(0.5f, 0.5f);
		_radarBaseTransform.anchoredPosition = s_radarAnchoredPosition + s_radarIntroOffset;
		_radarBaseTransform.localScale = Vector3.one * (RadarBaseScale * RadarIntroScaleMultiplier);
		_radarBaseTransform.SetAsLastSibling();
	}

	private void TintRadarImages()
	{
		_radarBackgroundTransform.SetAsFirstSibling();
		_pulseTransform.SetSiblingIndex(1);
		_radarBorderTransform.SetAsLastSibling();

		_currentPalette = PluginSettings.UavRadarPalette.Value;
		_paletteColors = GetPaletteColors(_currentPalette);

		SetImageColor("Radar/RadarBackground", _paletteColors.BackgroundColor);
		SetImageColor("Radar/RadarBorder", _paletteColors.BorderColor);
		SetImageColor("Radar/RadarPulse", _paletteColors.PulseColor);
		_pulseImage = _pulseTransform.GetComponent<Image>();
	}

	private void CreateTimerUi()
	{
		RectTransform timerContainer = CreateRect("UAV Timer", _radarBaseTransform);
		timerContainer.anchorMin = new Vector2(0.5f, 0.5f);
		timerContainer.anchorMax = new Vector2(0.5f, 0.5f);
		timerContainer.pivot = new Vector2(0.5f, 1f);
		timerContainer.anchoredPosition = new Vector2(0f, -208f);
		timerContainer.sizeDelta = new Vector2(150f, 44f);
		timerContainer.SetAsLastSibling();

		_timerBackgroundImage = timerContainer.gameObject.AddComponent<Image>();
		_timerBackgroundImage.sprite = GetTimerBackgroundSprite();
		_timerBackgroundImage.raycastTarget = false;

		_timerLabelText = CreateText("Timer Label", timerContainer);
		_timerLabelText.rectTransform.anchorMin = new Vector2(0f, 1f);
		_timerLabelText.rectTransform.anchorMax = new Vector2(1f, 1f);
		_timerLabelText.rectTransform.pivot = new Vector2(0.5f, 1f);
		_timerLabelText.rectTransform.anchoredPosition = new Vector2(0f, -6f);
		_timerLabelText.rectTransform.sizeDelta = new Vector2(-18f, 13f);
		_timerLabelText.fontSize = 9;
		_timerLabelText.fontStyle = FontStyle.Bold;
		_timerLabelText.text = "UAV RECON";

		_timerValueText = CreateText("Timer Value", timerContainer);
		_timerValueText.rectTransform.anchorMin = new Vector2(0f, 1f);
		_timerValueText.rectTransform.anchorMax = new Vector2(1f, 1f);
		_timerValueText.rectTransform.pivot = new Vector2(0.5f, 1f);
		_timerValueText.rectTransform.anchoredPosition = new Vector2(0f, -18f);
		_timerValueText.rectTransform.sizeDelta = new Vector2(-18f, 20f);
		_timerValueText.fontSize = 17;
		_timerValueText.fontStyle = FontStyle.Bold;

		_timerRailImage = CreateImage("Timer Rail", timerContainer, GetSolidSprite(), Color.white);
		_timerRailImage.rectTransform.anchorMin = new Vector2(0.5f, 0f);
		_timerRailImage.rectTransform.anchorMax = new Vector2(0.5f, 0f);
		_timerRailImage.rectTransform.pivot = new Vector2(0.5f, 0f);
		_timerRailImage.rectTransform.anchoredPosition = new Vector2(0f, 7f);
		_timerRailImage.rectTransform.sizeDelta = new Vector2(112f, 3f);

		_timerProgressImage = CreateImage("Timer Progress", _timerRailImage.transform, GetSolidSprite(), Color.white);
		_timerProgressTransform = _timerProgressImage.rectTransform;
		_timerProgressTransform.anchorMin = new Vector2(0f, 0f);
		_timerProgressTransform.anchorMax = new Vector2(1f, 1f);
		_timerProgressTransform.pivot = new Vector2(0f, 0.5f);
		_timerProgressTransform.offsetMin = Vector2.zero;
		_timerProgressTransform.offsetMax = Vector2.zero;
	}

	private void RefreshPaletteIfChanged()
	{
		UavRadarPalette selectedPalette = PluginSettings.UavRadarPalette.Value;
		if (selectedPalette == _currentPalette)
		{
			return;
		}

		_currentPalette = selectedPalette;
		_paletteColors = GetPaletteColors(selectedPalette);
		SetImageColor("Radar/RadarBackground", _paletteColors.BackgroundColor);
		SetImageColor("Radar/RadarBorder", _paletteColors.BorderColor);
		SetImageColor("Radar/RadarPulse", _paletteColors.PulseColor);
		ApplyTimerPalette();
	}

	private void ApplyTimerPalette()
	{
		if (_timerBackgroundImage != null)
		{
			_timerBackgroundImage.color = new Color(0.012f, 0.018f, 0.02f, 0.68f);
		}

		if (_timerLabelText != null)
		{
			_timerLabelText.color = new Color(_paletteColors.TimerColor.r, _paletteColors.TimerColor.g, _paletteColors.TimerColor.b, 0.72f);
		}

		if (_timerValueText != null)
		{
			_timerValueText.color = _paletteColors.TimerColor;
		}

		if (_timerRailImage != null)
		{
			_timerRailImage.color = new Color(_paletteColors.BorderColor.r, _paletteColors.BorderColor.g, _paletteColors.BorderColor.b, 0.18f);
		}

		if (_timerProgressImage != null)
		{
			_timerProgressImage.color = new Color(_paletteColors.BorderColor.r, _paletteColors.BorderColor.g, _paletteColors.BorderColor.b, 0.82f);
		}
	}

	private static RadarPaletteColors GetPaletteColors(UavRadarPalette palette)
	{
		return palette switch
		{
			UavRadarPalette.SourceWhite => new RadarPaletteColors(
				new Color(0.922f, 0.961f, 0.961f, 0.392f),
				new Color(0.922f, 0.961f, 0.961f, 1f),
				new Color(0.922f, 0.961f, 0.961f, 0.588f),
				new Color(0.92f, 0.96f, 0.96f, 0.98f)),
			UavRadarPalette.CyanRecon => new RadarPaletteColors(
				new Color(0f, 0.322f, 0.373f, 0.471f),
				new Color(0.314f, 0.902f, 1f, 1f),
				new Color(0f, 0.871f, 0.98f, 0.686f),
				new Color(0.804f, 0.98f, 1f, 0.98f)),
			UavRadarPalette.MintGlass => new RadarPaletteColors(
				new Color(0f, 0.29f, 0.227f, 0.431f),
				new Color(0.549f, 1f, 0.843f, 1f),
				new Color(0.345f, 1f, 0.843f, 0.647f),
				new Color(0.804f, 1f, 0.933f, 0.98f)),
			UavRadarPalette.WhiteDrone => new RadarPaletteColors(
				new Color(0.071f, 0.11f, 0.118f, 0.412f),
				new Color(0.941f, 0.98f, 0.969f, 1f),
				new Color(0.706f, 0.973f, 0.973f, 0.608f),
				new Color(0.957f, 0.988f, 0.973f, 0.98f)),
			UavRadarPalette.SoftLime => new RadarPaletteColors(
				new Color(0.094f, 0.282f, 0.055f, 0.431f),
				new Color(0.651f, 1f, 0.412f, 1f),
				new Color(0.369f, 1f, 0.392f, 0.647f),
				new Color(0.824f, 1f, 0.573f, 0.98f)),
			UavRadarPalette.IceBlue => new RadarPaletteColors(
				new Color(0.031f, 0.133f, 0.345f, 0.451f),
				new Color(0.49f, 0.714f, 1f, 1f),
				new Color(0.329f, 0.647f, 1f, 0.667f),
				new Color(0.776f, 0.878f, 1f, 0.98f)),
			_ => GetPaletteColors(UavRadarPalette.MintGlass)
		};
	}

	private void SetImageColor(string path, Color color)
	{
		Image image = _radarHud.transform.Find(path)?.GetComponent<Image>();
		if (image == null)
		{
			return;
		}

		image.color = color;
		image.raycastTarget = false;
	}

	private static void DisableRaycasts(Transform root)
	{
		foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>(includeInactive: true))
		{
			graphic.raycastTarget = false;
		}
	}

	private static RectTransform CreateRect(string name, Transform parent)
	{
		var obj = new GameObject(name, typeof(RectTransform));
		obj.transform.SetParent(parent, false);
		return (RectTransform)obj.transform;
	}

	private static Image CreateImage(string name, Transform parent, Sprite sprite, Color color)
	{
		var rect = CreateRect(name, parent);
		Image image = rect.gameObject.AddComponent<Image>();
		image.sprite = sprite;
		image.color = color;
		image.raycastTarget = false;
		return image;
	}

	private static void CreateLine(string name, Transform parent, Vector2 size, Vector2 position)
	{
		Image line = CreateImage(name, parent, null, new Color(0.36f, 0.9f, 0.78f, 0.3f));
		line.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
		line.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
		line.rectTransform.pivot = new Vector2(0.5f, 0.5f);
		line.rectTransform.anchoredPosition = position;
		line.rectTransform.sizeDelta = size;
	}

	private static Text CreateText(string name, Transform parent)
	{
		var rect = CreateRect(name, parent);
		Text text = rect.gameObject.AddComponent<Text>();
		text.alignment = TextAnchor.MiddleCenter;
		text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
		text.fontSize = 16;
		text.color = new Color(0.78f, 1f, 0.9f, 0.95f);
		text.raycastTarget = false;
		return text;
	}

	private void RestartIntroAnimation()
	{
		_isClosing = false;
		_introStartedAt = Time.time;
		_outroStartedAt = 0f;

		if (_canvasGroup != null)
		{
			_canvasGroup.alpha = 0f;
		}

		UpdateIntroVisuals();
	}

	private void BeginCloseAnimation()
	{
		if (_isClosing)
		{
			return;
		}

		_isClosing = true;
		_outroStartedAt = Time.time;
		UpdateOutroVisuals();
	}

	private void UpdateIntroVisuals()
	{
		float t = EaseOutCubic(Mathf.Clamp01((Time.time - _introStartedAt) / RadarIntroDurationSeconds));

		if (_canvasGroup != null)
		{
			_canvasGroup.alpha = t;
		}

		if (_radarBaseTransform != null)
		{
			float scale = Mathf.Lerp(RadarBaseScale * RadarIntroScaleMultiplier, RadarBaseScale, t);
			_radarBaseTransform.localScale = Vector3.one * scale;
			_radarBaseTransform.anchoredPosition = Vector2.Lerp(s_radarAnchoredPosition + s_radarIntroOffset, s_radarAnchoredPosition, t);
		}
	}

	private void UpdateOutroVisuals()
	{
		float t = Mathf.Clamp01((Time.time - _outroStartedAt) / RadarOutroDurationSeconds);
		float eased = Mathf.SmoothStep(0f, 1f, t);

		if (_canvasGroup != null)
		{
			_canvasGroup.alpha = 1f - eased;
		}

		if (_radarBaseTransform != null)
		{
			float scale = Mathf.Lerp(RadarBaseScale, RadarBaseScale * RadarOutroScaleMultiplier, eased);
			_radarBaseTransform.localScale = Vector3.one * scale;
			_radarBaseTransform.anchoredPosition = Vector2.Lerp(s_radarAnchoredPosition, s_radarAnchoredPosition + s_radarIntroOffset * 0.45f, eased);
		}

		if (t >= 1f)
		{
			Close();
		}
	}

	private float GetVisualAlpha()
	{
		return _canvasGroup != null ? Mathf.Clamp01(_canvasGroup.alpha) : 1f;
	}

	private static float EaseOutCubic(float t)
	{
		float inverse = 1f - t;
		return 1f - inverse * inverse * inverse;
	}

	private void UpdateTimer()
	{
		if (_timerValueText == null)
		{
			return;
		}

		int remainingSeconds = Mathf.CeilToInt(Mathf.Max(0f, _activeUntil - Time.time));
		int minutes = remainingSeconds / 60;
		int seconds = remainingSeconds % 60;
		_timerValueText.text = $"{minutes:00}:{seconds:00}";

		if (_timerProgressTransform != null)
		{
			float remainingRatio = Mathf.Clamp01((_activeUntil - Time.time) / Mathf.Max(1f, _activeDuration));
			_timerProgressTransform.anchorMax = new Vector2(remainingRatio, 1f);
			_timerProgressTransform.offsetMin = Vector2.zero;
			_timerProgressTransform.offsetMax = Vector2.zero;
		}
	}

	private void UpdatePulse()
	{
		float interval = GetActiveScanInterval();
		float t = 1f - Mathf.Clamp01((_nextScanTime - Time.time) / interval);

		_pulseTransform.localEulerAngles = new Vector3(0f, 0f, Mathf.Lerp(360f, 0f, t));
		if (_pulseImage != null)
		{
			float alpha = Mathf.Lerp(_paletteColors.PulseColor.a, _paletteColors.PulseColor.a * 0.35f, t) * GetVisualAlpha();
			_pulseImage.color = new Color(_paletteColors.PulseColor.r, _paletteColors.PulseColor.g, _paletteColors.PulseColor.b, alpha);
		}
	}

	private void ScanContacts()
	{
		_seenContactIds.Clear();

		foreach (Player target in _gameWorld.AllPlayersEverExisted)
		{
			if (!IsValidTarget(target))
			{
				continue;
			}

			Vector3 position = target.Transform.position;
			if (!IsInRange(position))
			{
				continue;
			}

			string id = target.ProfileId;
			_seenContactIds.Add(id);

			if (!_contacts.TryGetValue(id, out Contact contact))
			{
				contact = CreateContact(target);
				_contacts[id] = contact;
			}

			contact.Position = position;
		}

		_contactsToRemove.Clear();
		foreach (string id in _contacts.Keys)
		{
			if (!_seenContactIds.Contains(id))
			{
				_contactsToRemove.Add(id);
			}
		}

		foreach (string id in _contactsToRemove)
		{
			Destroy(_contacts[id].Image.gameObject);
			_contacts.Remove(id);
		}
	}

	private bool IsValidTarget(Player target)
	{
		if (target == null || target == _player || target.Transform == null)
		{
			return false;
		}

		return target.HealthController != null && target.HealthController.IsAlive;
	}

	private bool IsInRange(Vector3 position)
	{
		Vector3 relative = position - _player.Transform.position;
		relative.y = 0f;
		float activeRange = GetActiveRangeMeters();
		return relative.sqrMagnitude <= activeRange * activeRange;
	}

	private Contact CreateContact(Player target)
	{
		Sprite blipSprite = _blipPrefab.transform.Find("Blip/RadarEnemyBlip")?.GetComponent<Image>()?.sprite;
		Image image = CreateImage("UAV Contact", _radarBorderTransform, blipSprite, GetContactColor(target));
		image.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
		image.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
		image.rectTransform.pivot = new Vector2(0.5f, 0.5f);
		image.rectTransform.sizeDelta = new Vector2(16f, 16f);
		image.transform.SetAsLastSibling();

		return new Contact
		{
			RectTransform = image.rectTransform,
			Image = image,
			Position = target.Transform.position
		};
	}

	private static Color GetContactColor(Player target)
	{
		return target.Profile.Info.Side switch
		{
			EPlayerSide.Savage => new Color(0.64f, 0.95f, 0.48f, 0.92f),
			EPlayerSide.Usec => new Color(0.35f, 0.82f, 1f, 0.95f),
			EPlayerSide.Bear => new Color(1f, 0.7f, 0.22f, 0.95f),
			_ => new Color(1f, 0.32f, 0.18f, 0.95f)
		};
	}

	private void UpdateContactPositions()
	{
		_radarBorderTransform.localEulerAngles = new Vector3(0f, 0f, _player.Transform.rotation.eulerAngles.y);
		float eulerZ = _radarBorderTransform.rotation.eulerAngles.z;
		Vector3 rotatedDirection = _radarBorderTransform.rotation * Vector3.forward;
		float radarAngle = Mathf.Atan2(rotatedDirection.x, rotatedDirection.z);
		float radarRange = GetActiveRangeMeters();
		Vector3 borderScale = _radarBorderTransform.localScale;
		Vector2 borderSize = _radarBorderTransform.sizeDelta;
		borderSize.x *= borderScale.x;
		borderSize.y *= borderScale.y;
		float graphicRadius = Mathf.Min(borderSize.x, borderSize.y) * 0.68f;

		foreach (Contact contact in _contacts.Values)
		{
			Vector3 relative = contact.Position - _player.Transform.position;
			float distance = Mathf.Sqrt(relative.x * relative.x + relative.z * relative.z);
			float offsetRadius = Mathf.Pow(Mathf.Clamp01(distance / radarRange), 0.645f);
			float targetAngle = Mathf.Atan2(relative.x, relative.z);

			contact.RectTransform.localRotation = Quaternion.Euler(0f, 0f, 360f - eulerZ);
			contact.RectTransform.localPosition = new Vector3(
				Mathf.Sin(targetAngle - radarAngle),
				Mathf.Cos(targetAngle - radarAngle),
				-0.01f) * offsetRadius * graphicRadius;

			Color color = contact.Image.color;
			color.a = Mathf.Abs(relative.y) > 3.5f ? 0.58f : 0.95f;
			contact.Image.color = color;
		}
	}

	private float GetActiveScanInterval()
	{
		return Mathf.Max(0.1f, _activeScanInterval > 0f
			? _activeScanInterval
			: UavReconSettings.GetScanInterval(ESupportType.Uav));
	}

	private float GetActiveRangeMeters()
	{
		return Mathf.Max(1f, _activeRangeMeters > 0f
			? _activeRangeMeters
			: UavReconSettings.GetRangeMeters(ESupportType.Uav));
	}

	private static Sprite CreateCircleSprite(int size, Color fillColor, Color ringColor, float ringWidth)
	{
		var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
		var pixels = new Color32[size * size];
		float center = (size - 1) * 0.5f;
		float radius = center;
		float innerRadius = Mathf.Max(0f, radius - ringWidth);

		for (var y = 0; y < size; y++)
		{
			for (var x = 0; x < size; x++)
			{
				float distance = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
				Color color = Color.clear;

				if (distance <= radius)
				{
					color = distance >= innerRadius ? ringColor : fillColor;
				}

				pixels[y * size + x] = color;
			}
		}

		texture.SetPixels32(pixels);
		texture.Apply();
		return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
	}

	private static Sprite GetSolidSprite()
	{
		if (s_solidSprite != null)
		{
			return s_solidSprite;
		}

		var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
		texture.SetPixel(0, 0, Color.white);
		texture.Apply();
		s_solidSprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1);
		return s_solidSprite;
	}

	private static Sprite GetTimerBackgroundSprite()
	{
		if (s_timerBackgroundSprite != null)
		{
			return s_timerBackgroundSprite;
		}

		const int width = 150;
		const int height = 44;
		const float radius = 10f;
		var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
		var pixels = new Color32[width * height];

		for (int y = 0; y < height; y++)
		{
			for (int x = 0; x < width; x++)
			{
				float dx = Mathf.Max(radius - x, 0f, x - (width - radius - 1));
				float dy = Mathf.Max(radius - y, 0f, y - (height - radius - 1));
				float distance = Mathf.Sqrt(dx * dx + dy * dy);
				float alpha = Mathf.Clamp01(radius + 0.5f - distance);
				pixels[y * width + x] = new Color(1f, 1f, 1f, alpha);
			}
		}

		texture.SetPixels32(pixels);
		texture.Apply();
		s_timerBackgroundSprite = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f);
		return s_timerBackgroundSprite;
	}

	private void Close()
	{
		Instance = null;
		Destroy(gameObject);
	}
}
