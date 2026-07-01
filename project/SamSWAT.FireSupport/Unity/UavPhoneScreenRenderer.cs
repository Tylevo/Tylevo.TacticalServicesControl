using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public readonly struct UavPhoneScreenContext
{
	public UavPhoneScreenContext(int costRoubles, int balanceRoubles, int durationSeconds)
		: this(ESupportType.Uav, costRoubles, balanceRoubles, durationSeconds)
	{
	}

	public UavPhoneScreenContext(ESupportType supportType, int costRoubles, int balanceRoubles, int durationSeconds)
	{
		SupportType = supportType;
		CostRoubles = costRoubles;
		BalanceRoubles = balanceRoubles;
		DurationSeconds = durationSeconds;
	}

	public ESupportType SupportType { get; }
	public int CostRoubles { get; }
	public int BalanceRoubles { get; }
	public int DurationSeconds { get; }
}

public sealed class UavPhoneScreenRenderer : MonoBehaviour
{
	private const int RenderLayer = 31;
	private const int LongSide = 1024;
	private const int MinSide = 256;
	private const string PhoneAssetRoot = "assets/content/ui/phone";
	private const string DynamicTextLayoutRelativePath = "docs/dynamic_text_layout.json";
	private const int LandscapeLayoutWidth = 1024;
	private const int LandscapeLayoutHeight = 512;
	private const int PortraitLayoutWidth = 512;
	private const int PortraitLayoutHeight = 1024;
	private const float OpaqueScreenPlaneScale = 1.01f;
	private const float SwipeArrowAnimationSeconds = 1.05f;
	private const string SwipeArrowRelativePath = "animations/swipe_up/swipe_arrow_sprite.png";
	private const string SwipeFrameRelativePathFormat = "animations/swipe_up/frames_512x1024/swipe_{0:00}.png";
	private const int SwipeFrameCount = 12;
	private static readonly Rect SwipeAnimationMaskRect = new Rect(46f, 482f, 422f, 270f);

	private static Sprite s_whiteSprite;
	private static readonly Dictionary<string, Sprite> s_phoneSprites = new();
	private static readonly Dictionary<string, Sprite> s_overlaySprites = new();
	private static readonly HashSet<string> s_missingPhoneSprites = new();
	private static Dictionary<string, List<DynamicTextField>> s_dynamicTextLayout;
	private static string s_pluginDirectory;

	private int _texWidth = 432;
	private int _texHeight = 768;
	private float _canvasRotation = 90f;
	private UavPhoneScreenContext _context;

	private Camera _camera;
	private Canvas _canvas;
	private RenderTexture _renderTexture;
	private Renderer _screenRenderer;
	private Transform _rendererRoot;
	private Material _previousScreenMaterial;
	private Material[] _previousScreenMaterials;
	private Material _screenMaterial;
	private Texture _previousMainTex;
	private Vector2 _previousTextureScale = Vector2.one;
	private Vector2 _previousTextureOffset = Vector2.zero;
	private bool _texTransformApplied;
	private bool _forceOpaqueDebug;
	private bool _previousScreenRendererEnabled = true;
	private Coroutine _stateFadeCoroutine;
	private GameObject _opaqueScreenObject;
	private Mesh _opaqueScreenMesh;
	private MeshRenderer _opaqueScreenRenderer;
	private MeshFilter _opaqueScreenMeshFilter;
	private GameObject _opaqueBackplateObject;
	private Mesh _opaqueBackplateMesh;
	private MeshRenderer _opaqueBackplateRenderer;
	private MeshFilter _opaqueBackplateMeshFilter;
	private Material _opaqueBackplateMaterial;
	private Texture2D _opaqueBackplateTexture;
	private readonly List<Renderer> _debugDisabledRenderers = new();
	private readonly List<bool> _debugDisabledRendererStates = new();
	private Font _font;
	private TerraGroupPhoneState _currentState = TerraGroupPhoneState.Home;
	private bool _settingsSubscribed;

	private CanvasGroup _homeGroup;
	private CanvasGroup _tacticalServicesGroup;
	private CanvasGroup _serviceCategoryGroup;
	private CanvasGroup _requestGroup;
	private CanvasGroup _rotateGroup;
	private CanvasGroup _confirmPaymentGroup;
	private CanvasGroup _authorizingGroup;
	private CanvasGroup _authorizedGroup;
	private CanvasGroup _deniedGroup;
	private Text _deniedReasonText;
	private Text _deniedDetailText;
	private Image _swipeArrowImage;
	private Sprite[] _swipeFrameSprites;
	private Coroutine _swipeAnimationCoroutine;

	public void Initialize(
		Renderer screenRenderer,
		Rect uvRect,
		float canvasRotation,
		UavPhoneScreenContext context,
		Transform rendererRoot = null)
	{
		_screenRenderer = screenRenderer;
		_rendererRoot = rendererRoot ?? screenRenderer?.transform.root;
		_canvasRotation = canvasRotation;
		_context = context;
		_font = Resources.GetBuiltinResource<Font>("Arial.ttf");
		_forceOpaqueDebug = PluginSettings.PhoneForceOpaqueLcdDebug?.Value ?? false;

		(_texWidth, _texHeight) = ComputeRTDimensions(screenRenderer, canvasRotation);
		SubscribeSettingChanges();

		BuildRenderTextureCamera();
		BuildCanvas();
		BuildHomeScreen();
		BuildTacticalServicesScreen();
		BuildServiceCategoryScreen();
		BuildRequestScreen();
		BuildRotateToConfirmScreen();
		BuildConfirmPaymentPortraitScreen();
		BuildAuthorizingScreen();
		BuildAuthorizedScreen();
		BuildDeniedScreen();
		BindRenderTexture(uvRect);
		if (TscDiagnostics.VerboseLcd)
		{
			StartCoroutine(LogRenderTextureAlpha(_renderTexture));
		}

		ShowState(TerraGroupPhoneState.Home);
		FireSupportPlugin.LogSource.LogInfo("TSC Uplink UI started.");
	}

	public void Rebuild(UavPhoneScreenContext context, TerraGroupPhoneState state)
	{
		_context = context;
		if (_canvas == null)
		{
			return;
		}

		StopSwipeAnimation();
		_swipeArrowImage = null;
		_swipeFrameSprites = null;
		for (int i = _canvas.transform.childCount - 1; i >= 0; i--)
		{
			Destroy(_canvas.transform.GetChild(i).gameObject);
		}

		BuildHomeScreen();
		BuildTacticalServicesScreen();
		BuildServiceCategoryScreen();
		BuildRequestScreen();
		BuildRotateToConfirmScreen();
		BuildConfirmPaymentPortraitScreen();
		BuildAuthorizingScreen();
		BuildAuthorizedScreen();
		BuildDeniedScreen();
		ShowState(state);
	}

	public void ShowState(TerraGroupPhoneState state)
	{
		if (_stateFadeCoroutine != null)
		{
			StopCoroutine(_stateFadeCoroutine);
			_stateFadeCoroutine = null;
		}

		_currentState = state;
		SetScreenState(GetGroupForState(state));
		HandleVisibleState(state);
	}

	public void FadeToState(TerraGroupPhoneState state, float durationSeconds)
	{
		if (_canvas == null || durationSeconds <= 0f)
		{
			ShowState(state);
			return;
		}

		if (_stateFadeCoroutine != null)
		{
			StopCoroutine(_stateFadeCoroutine);
		}

		_stateFadeCoroutine = StartCoroutine(FadeToStateCoroutine(state, durationSeconds));
	}

	public void ShowAuthorizing()
	{
		ShowState(TerraGroupPhoneState.Authorizing);
	}

	public void ShowAuthorized()
	{
		ShowState(TerraGroupPhoneState.Authorized);
	}

	public void ShowDenied()
	{
		ShowState(TerraGroupPhoneState.Denied);
	}

	public void Shutdown()
	{
		UnsubscribeSettingChanges();
		StopSwipeAnimation();

		RestoreDebugDisabledRenderers();
		DestroyOpaqueScreenPlane();

		if (_screenRenderer != null)
		{
			_screenRenderer.enabled = _previousScreenRendererEnabled;
			try
			{
				if (_previousScreenMaterial != null)
				{
					if (_previousScreenMaterials is { Length: > 0 })
					{
						_screenRenderer.materials = _previousScreenMaterials;
					}
					else
					{
						_screenRenderer.material = _previousScreenMaterial;
					}
				}
				else
				{
					Material material = _screenRenderer.material;
					if (_previousMainTex != null)
					{
						material.mainTexture = _previousMainTex;
					}

					if (_texTransformApplied)
					{
						material.mainTextureScale = _previousTextureScale;
						material.mainTextureOffset = _previousTextureOffset;
					}
				}
			}
			catch
			{
			}
		}

		if (_screenMaterial != null)
		{
			Destroy(_screenMaterial);
			_screenMaterial = null;
		}

		if (_canvas != null)
		{
			Destroy(_canvas.gameObject);
		}

		if (_camera != null)
		{
			Destroy(_camera.gameObject);
		}

		if (_renderTexture != null)
		{
			_renderTexture.Release();
		}
	}

	private void OnDestroy()
	{
		UnsubscribeSettingChanges();
	}

	public static Renderer FindBestScreenRenderer(Transform root, string context, bool logCandidates = true)
	{
		if (root == null)
		{
			FireSupportPlugin.LogSource.LogWarning($"TerraGroup phone LCD renderer scan skipped for {context}: root was null.");
			return null;
		}

		Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
		if (logCandidates)
		{
			TscDiagnostics.LogLcd(
				$"TerraGroup phone LCD renderer scan started. context={context}, root='{root.name}', rendererCount={renderers.Length}.");
		}

		Renderer bestRenderer = null;
		int bestScore = int.MinValue;
		for (int i = 0; i < renderers.Length; i++)
		{
			Renderer renderer = renderers[i];
			int score = ScoreLcdRenderer(renderer, root);
			if (logCandidates)
			{
				LogRendererCandidate(root, renderer, i, score, context);
			}

			if (bestRenderer == null || score > bestScore)
			{
				bestRenderer = renderer;
				bestScore = score;
			}
		}

		if (bestRenderer == null)
		{
			FireSupportPlugin.LogSource.LogWarning($"TerraGroup phone LCD renderer scan found no renderer. context={context}");
			return null;
		}

		Material selectedMaterial = bestRenderer.sharedMaterial;
		TscDiagnostics.LogLcd(
			$"TerraGroup phone LCD renderer selected. context={context}, renderer='{GetTransformPath(bestRenderer.transform, root)}', material='{selectedMaterial?.name ?? "<null>"}', shader='{selectedMaterial?.shader?.name ?? "<null>"}', renderQueue={selectedMaterial?.renderQueue.ToString() ?? "<null>"}, score={bestScore}.");
		return bestRenderer;
	}

	private static int ScoreLcdRenderer(Renderer renderer, Transform root)
	{
		if (renderer == null)
		{
			return int.MinValue;
		}

		string path = GetTransformPath(renderer.transform, root).ToLowerInvariant();
		string materialSummary = GetMaterialSummary(renderer.sharedMaterials).ToLowerInvariant();
		int score = 0;

		if (path.Contains("screen"))
		{
			score += 120;
		}

		if (path.Contains("lcd") || path.Contains("display") || path.Contains("monitor") || path.Contains("panel"))
		{
			score += 80;
		}

		if (materialSummary.Contains("screen") || materialSummary.Contains("lcd") || materialSummary.Contains("display"))
		{
			score += 60;
		}

		if (path.Contains("glass") || path.Contains("cover") || path.Contains("lens") ||
		    materialSummary.Contains("glass") || materialSummary.Contains("transparent") || materialSummary.Contains("fade"))
		{
			score -= 120;
		}

		Mesh mesh = GetRendererMesh(renderer);
		if (mesh != null)
		{
			Vector3 size = mesh.bounds.size;
			float[] dimensions = { Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z) };
			Array.Sort(dimensions);
			float thin = dimensions[0];
			float medium = dimensions[1];
			float large = dimensions[2];
			if (large > 0.0001f && medium > 0.0001f)
			{
				float aspect = large / medium;
				float thinness = thin / large;
				if (thinness < 0.12f)
				{
					score += 35;
				}

				if (aspect >= 1.15f && aspect <= 3.4f)
				{
					score += 25;
				}

				score += Mathf.Clamp(Mathf.RoundToInt(large * medium * 250f), 0, 40);
			}
		}

		return score;
	}

	private static void LogRendererCandidate(Transform root, Renderer renderer, int index, int score, string context)
	{
		if (renderer == null)
		{
			return;
		}

		Mesh mesh = GetRendererMesh(renderer);
		Vector3 localSize = mesh == null ? Vector3.zero : mesh.bounds.size;
		Vector3 worldSize = renderer.bounds.size;
		TscDiagnostics.LogLcd(
			$"TSC phone renderer[{index}] context={context} path='{GetTransformPath(renderer.transform, root)}', type={renderer.GetType().Name}, enabled={renderer.enabled}, localBounds=({localSize.x:F4},{localSize.y:F4},{localSize.z:F4}), worldBounds=({worldSize.x:F4},{worldSize.y:F4},{worldSize.z:F4}), materials=[{GetMaterialSummary(renderer.sharedMaterials)}], score={score}.");
	}

	public static Rect CaptureScreenUVRect(Renderer renderer)
	{
		if (renderer == null)
		{
			return new Rect(0f, 0f, 1f, 1f);
		}

		Mesh mesh = null;
		MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
		if (meshFilter != null)
		{
			mesh = meshFilter.sharedMesh;
		}
		else if (renderer is SkinnedMeshRenderer skinnedMeshRenderer)
		{
			mesh = skinnedMeshRenderer.sharedMesh;
		}

		if (mesh?.uv == null || mesh.uv.Length == 0)
		{
			return new Rect(0f, 0f, 1f, 1f);
		}

		Vector2[] uvs = mesh.uv;
		float u0 = uvs[0].x;
		float u1 = uvs[0].x;
		float v0 = uvs[0].y;
		float v1 = uvs[0].y;

		for (int i = 1; i < uvs.Length; i++)
		{
			u0 = Mathf.Min(u0, uvs[i].x);
			u1 = Mathf.Max(u1, uvs[i].x);
			v0 = Mathf.Min(v0, uvs[i].y);
			v1 = Mathf.Max(v1, uvs[i].y);
		}

		Rect rect = Rect.MinMaxRect(u0, v0, u1, v1);
		TscDiagnostics.LogLcd($"TSC phone screen UV rect: u({u0:F3}-{u1:F3}) v({v0:F3}-{v1:F3})");
		return rect;
	}

	private void BuildRenderTextureCamera()
	{
		_renderTexture = new RenderTexture(_texWidth, _texHeight, 16, RenderTextureFormat.ARGB32)
		{
			name = "TSC.PhoneRT",
			useMipMap = false,
			autoGenerateMips = false,
			filterMode = FilterMode.Bilinear,
			wrapMode = TextureWrapMode.Clamp,
			antiAliasing = 1
		};
		_renderTexture.Create();
		TscDiagnostics.LogLcd(
			$"TSC phone render texture created: {_texWidth}x{_texHeight}, format={_renderTexture.format}, mipChain=false, wrap=Clamp, filter=Bilinear, alphaBackground=1.");

		GameObject cameraObject = new("TSC Phone Camera");
		cameraObject.transform.SetParent(transform, false);
		DontDestroyOnLoad(cameraObject);

		_camera = cameraObject.AddComponent<Camera>();
		_camera.orthographic = true;
		_camera.orthographicSize = _texHeight / 2f;
		_camera.aspect = (float)_texWidth / _texHeight;
		_camera.clearFlags = CameraClearFlags.SolidColor;
		_camera.backgroundColor = PhoneScreenBackground();
		_camera.cullingMask = 1 << RenderLayer;
		_camera.targetTexture = _renderTexture;
		_camera.depth = -100;
		_camera.allowHDR = false;
		_camera.allowMSAA = false;
	}

	private void BuildCanvas()
	{
		GameObject canvasObject = new("TSC Phone Canvas");
		canvasObject.transform.SetParent(transform, false);
		canvasObject.layer = RenderLayer;

		_canvas = canvasObject.AddComponent<Canvas>();
		_canvas.renderMode = RenderMode.ScreenSpaceCamera;
		_canvas.worldCamera = _camera;
		_canvas.planeDistance = 1f;
		_canvas.sortingOrder = -100;

		CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
		scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
		scaler.referencePixelsPerUnit = 100f;
	}

	private IEnumerator LogRenderTextureAlpha(RenderTexture target)
	{
		yield return new WaitForEndOfFrame();

		if (target == null)
		{
			yield break;
		}

		RenderTexture previous = RenderTexture.active;
		Texture2D sample = null;
		try
		{
			RenderTexture.active = target;
			sample = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
			sample.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
			sample.Apply(updateMipmaps: false, makeNoLongerReadable: false);

			Color32[] pixels = sample.GetPixels32();
			byte minAlpha = byte.MaxValue;
			byte maxAlpha = byte.MinValue;
			int nonOpaque = 0;
			for (int i = 0; i < pixels.Length; i++)
			{
				byte alpha = pixels[i].a;
				minAlpha = Math.Min(minAlpha, alpha);
				maxAlpha = Math.Max(maxAlpha, alpha);
				if (alpha != byte.MaxValue)
				{
					nonOpaque++;
				}
			}

			TscDiagnostics.LogLcd(
				$"Phone RT alpha: min={minAlpha}, max={maxAlpha}, nonOpaque={nonOpaque}/{pixels.Length}");
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource.LogWarning($"Phone RT alpha diagnostic failed. {ex}");
		}
		finally
		{
			RenderTexture.active = previous;
			if (sample != null)
			{
				Destroy(sample);
			}
		}
	}

	private void BindRenderTexture(Rect uvRect)
	{
		if (_screenRenderer == null)
		{
			return;
		}

		_previousScreenMaterials = _screenRenderer.materials;
		_previousScreenMaterial = _previousScreenMaterials is { Length: > 0 }
			? _previousScreenMaterials[0]
			: _screenRenderer.material;
		_previousMainTex = _previousScreenMaterial.mainTexture;
		_previousTextureScale = _previousScreenMaterial.mainTextureScale;
		_previousTextureOffset = _previousScreenMaterial.mainTextureOffset;
		_previousScreenRendererEnabled = _screenRenderer.enabled;

		_screenMaterial = CreateOpaqueLcdMaterial(_renderTexture, _forceOpaqueDebug);

		Material material = _screenMaterial;
		material.mainTexture = _renderTexture;
		if (uvRect.width > 0.001f && uvRect.height > 0.001f &&
		    !(Mathf.Approximately(uvRect.width, 1f) &&
		      Mathf.Approximately(uvRect.height, 1f) &&
		      Mathf.Approximately(uvRect.x, 0f) &&
		      Mathf.Approximately(uvRect.y, 0f)))
		{
			material.mainTextureScale = new Vector2(1f / uvRect.width, 1f / uvRect.height);
			material.mainTextureOffset = new Vector2(-uvRect.x / uvRect.width, -uvRect.y / uvRect.height);
			_texTransformApplied = true;
		}

		bool usingOpaquePlane = TryCreateOpaqueScreenPlane();

		_screenRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		_screenRenderer.receiveShadows = false;
		_screenRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
		_screenRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

		Material[] replacementMaterials = new Material[Mathf.Max(1, _previousScreenMaterials?.Length ?? 1)];
		for (int i = 0; i < replacementMaterials.Length; i++)
		{
			replacementMaterials[i] = material;
		}

		if (usingOpaquePlane)
		{
			TscDiagnostics.LogLcd(
				$"TerraGroup phone LCD material bound to generated opaque plane. selectedRenderer='{GetTransformPath(_screenRenderer.transform, _rendererRoot)}', selectedMaterial='{_previousScreenMaterial?.name ?? "<null>"}', shader='{_screenMaterial.shader?.name ?? "<null>"}', renderQueue={_screenMaterial.renderQueue}, texture={_renderTexture.width}x{_renderTexture.height}, alphaForcedOpaque=true, forceDebug={_forceOpaqueDebug}.");
		}
		else
		{
			_screenRenderer.materials = replacementMaterials;
			TscDiagnostics.LogLcd(
				$"TerraGroup phone LCD material replaced original renderer slots. selectedRenderer='{GetTransformPath(_screenRenderer.transform, _rendererRoot)}', selectedMaterial='{_previousScreenMaterial?.name ?? "<null>"}', shader='{_screenMaterial.shader?.name ?? "<null>"}', renderQueue={_screenMaterial.renderQueue}, texture={_renderTexture.width}x{_renderTexture.height}, materialSlots={replacementMaterials.Length}, alphaForcedOpaque=true, forceDebug={_forceOpaqueDebug}.");
		}
	}

	private static Material CreateOpaqueLcdMaterial(Texture texture, bool forceDebug)
	{
		Shader shader = Shader.Find("Unlit/Texture") ??
		                Shader.Find("Standard") ??
		                Shader.Find("Legacy Shaders/Self-Illumin/Diffuse") ??
		                Shader.Find("Legacy Shaders/Diffuse");
		Material material = new(shader)
		{
			name = forceDebug
				? "TSC TerraGroup Force Opaque LCD Debug"
				: "TSC TerraGroup Opaque LCD",
			mainTexture = texture,
			color = Color.white
		};

		material.mainTextureScale = Vector2.one;
		material.mainTextureOffset = Vector2.zero;
		ForceOpaqueMaterial(material);
		return material;
	}

	private static void ForceOpaqueMaterial(Material material)
	{
		if (material == null)
		{
			return;
		}

		material.SetOverrideTag("RenderType", "Opaque");
		material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
		material.DisableKeyword("_NORMALMAP");
		material.DisableKeyword("_METALLICGLOSSMAP");
		material.DisableKeyword("_PARALLAXMAP");
		material.DisableKeyword("_DETAIL_MULX2");
		material.DisableKeyword("_ALPHATEST_ON");
		material.DisableKeyword("_ALPHABLEND_ON");
		material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
		SetMaterialFloatIfPresent(material, "_Mode", 0f);
		SetMaterialFloatIfPresent(material, "_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
		SetMaterialFloatIfPresent(material, "_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
		SetMaterialFloatIfPresent(material, "_ZWrite", 1f);
		SetMaterialFloatIfPresent(material, "_Glossiness", 0f);
		SetMaterialFloatIfPresent(material, "_Metallic", 0f);
		SetMaterialFloatIfPresent(material, "_Cull", (float)UnityEngine.Rendering.CullMode.Off);
		SetMaterialColorIfPresent(material, "_Color", Color.white);
		SetMaterialColorIfPresent(material, "_TintColor", Color.white);
		SetMaterialColorIfPresent(material, "_SpecColor", Color.black);
		if (material.HasProperty("_BumpMap"))
		{
			material.SetTexture("_BumpMap", null);
		}

		if (material.HasProperty("_DetailAlbedoMap"))
		{
			material.SetTexture("_DetailAlbedoMap", null);
		}

		if (material.HasProperty("_MetallicGlossMap"))
		{
			material.SetTexture("_MetallicGlossMap", null);
		}
	}

	private static void SetMaterialFloatIfPresent(Material material, string property, float value)
	{
		if (material.HasProperty(property))
		{
			material.SetFloat(property, value);
		}
	}

	private static void SetMaterialColorIfPresent(Material material, string property, Color value)
	{
		if (material.HasProperty(property))
		{
			material.SetColor(property, value);
		}
	}

	private Material FindPhoneBodyMaterial()
	{
		Transform root = _rendererRoot ?? _screenRenderer?.transform.root;
		if (root == null)
		{
			return null;
		}

		Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
		for (int i = 0; i < renderers.Length; i++)
		{
			Renderer renderer = renderers[i];
			if (renderer == null)
			{
				continue;
			}

			string path = GetTransformPath(renderer.transform, root);
			Material[] materials = renderer.sharedMaterials;
			for (int j = 0; j < materials.Length; j++)
			{
				Material material = materials[j];
				if (material == null ||
				    material.name.IndexOf("PhoneBody", StringComparison.OrdinalIgnoreCase) < 0)
				{
					continue;
				}

				TscDiagnostics.LogLcd(
					$"TerraGroup phone LCD backplate source material found. renderer='{path}', slot={j}, material='{material.name}', shader='{material.shader?.name ?? "<null>"}', renderQueue={material.renderQueue}.");
				return material;
			}
		}

		FireSupportPlugin.LogSource.LogWarning("TerraGroup phone LCD backplate did not find PhoneBody material; using fallback opaque black material.");
		return null;
	}

	private static Material CreateOpaqueBackplateMaterial(Material source, out Texture2D blackTexture)
	{
		Shader shader = source?.shader ??
		                Shader.Find("Standard") ??
		                Shader.Find("Unlit/Texture") ??
		                Shader.Find("Legacy Shaders/Diffuse");
		Material material = new(shader)
		{
			name = "TSC LCD Opaque Backplate",
			color = Color.black
		};

		if (source != null)
		{
			material.CopyPropertiesFromMaterial(source);
			material.shader = source.shader;
			material.color = Color.black;
		}

		blackTexture = new Texture2D(2, 2, TextureFormat.RGB24, false)
		{
			name = "TSC LCD Opaque Black",
			hideFlags = HideFlags.HideAndDontSave,
			filterMode = FilterMode.Point,
			wrapMode = TextureWrapMode.Clamp,
			anisoLevel = 0
		};
		blackTexture.SetPixels(new[] { Color.black, Color.black, Color.black, Color.black });
		blackTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);

		material.mainTexture = blackTexture;
		material.SetOverrideTag("RenderType", "Opaque");
		material.renderQueue = 1999;
		material.DisableKeyword("_ALPHATEST_ON");
		material.DisableKeyword("_ALPHABLEND_ON");
		material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
		material.DisableKeyword("_NORMALMAP");
		material.DisableKeyword("_METALLICGLOSSMAP");
		material.DisableKeyword("_PARALLAXMAP");
		SetMaterialFloatIfPresent(material, "_Mode", 0f);
		SetMaterialFloatIfPresent(material, "_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
		SetMaterialFloatIfPresent(material, "_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
		SetMaterialFloatIfPresent(material, "_ZWrite", 1f);
		SetMaterialFloatIfPresent(material, "_Glossiness", 0f);
		SetMaterialFloatIfPresent(material, "_Glossness", 0f);
		SetMaterialFloatIfPresent(material, "_Specularness", 0f);
		SetMaterialFloatIfPresent(material, "_Metallic", 0f);
		SetMaterialFloatIfPresent(material, "_Cull", (float)UnityEngine.Rendering.CullMode.Off);
		SetMaterialColorIfPresent(material, "_Color", Color.black);
		SetMaterialColorIfPresent(material, "_BaseTintColor", Color.black);
		SetMaterialColorIfPresent(material, "_ReflectColor", new Color(0f, 0f, 0f, 0f));
		SetMaterialColorIfPresent(material, "_SpecColor", Color.black);

		if (material.HasProperty("_BumpMap"))
		{
			material.SetTexture("_BumpMap", null);
		}

		if (material.HasProperty("_DetailAlbedoMap"))
		{
			material.SetTexture("_DetailAlbedoMap", null);
		}

		if (material.HasProperty("_MetallicGlossMap"))
		{
			material.SetTexture("_MetallicGlossMap", null);
		}

		return material;
	}

	private bool TryCreateOpaqueScreenPlane()
	{
		DestroyOpaqueScreenPlane();

		if (_screenRenderer == null || _screenMaterial == null)
		{
			return false;
		}

		Mesh sourceMesh = GetRendererMesh(_screenRenderer);
		if (sourceMesh == null)
		{
			FireSupportPlugin.LogSource.LogWarning("TerraGroup phone opaque LCD plane skipped: screen renderer has no mesh.");
			return false;
		}

		Bounds bounds = sourceMesh.bounds;
		Vector3 size = bounds.size;
		if (size.sqrMagnitude < 0.000001f)
		{
			FireSupportPlugin.LogSource.LogWarning("TerraGroup phone opaque LCD plane skipped: screen mesh bounds are empty.");
			return false;
		}

		Material phoneBodySource = FindPhoneBodyMaterial();
		_opaqueBackplateMaterial = CreateOpaqueBackplateMaterial(phoneBodySource, out _opaqueBackplateTexture);
		_opaqueBackplateObject = CreateScreenMeshClone(
			"TSC LCD Backplate",
			sourceMesh,
			_opaqueBackplateMaterial,
			out _opaqueBackplateMesh,
			out _opaqueBackplateMeshFilter,
			out _opaqueBackplateRenderer);
		_opaqueBackplateObject.transform.localPosition = new Vector3(0f, 0f, -0.00020f);

		_opaqueScreenObject = CreateScreenMeshClone(
			"TSC LCD UI",
			sourceMesh,
			_screenMaterial,
			out _opaqueScreenMesh,
			out _opaqueScreenMeshFilter,
			out _opaqueScreenRenderer);
		_opaqueScreenObject.transform.localPosition = new Vector3(0f, 0f, 0.00005f);

		_screenRenderer.enabled = false;
		if (_forceOpaqueDebug)
		{
			DisableGlassLikeRenderersForDebug();
		}

		TscDiagnostics.LogLcd(
			$"TerraGroup phone opaque LCD stack created. sourceRenderer='{_screenRenderer.name}', bounds={size}, subMeshes={_opaqueScreenMesh.subMeshCount}, uvCount={_opaqueScreenMesh.uv?.Length ?? 0}, uiRenderQueue={_screenMaterial.renderQueue}, backplateMaterial='{phoneBodySource?.name ?? "<fallback>"}', backplateShader='{_opaqueBackplateMaterial?.shader?.name ?? "<null>"}', backplateRenderQueue={_opaqueBackplateMaterial?.renderQueue.ToString() ?? "<null>"}, uiOffset={_opaqueScreenObject.transform.localPosition}, backplateOffset={_opaqueBackplateObject.transform.localPosition}, textureScale={_screenMaterial.mainTextureScale}, textureOffset={_screenMaterial.mainTextureOffset}.");
		return true;
	}

	private GameObject CreateScreenMeshClone(
		string name,
		Mesh sourceMesh,
		Material material,
		out Mesh mesh,
		out MeshFilter meshFilter,
		out MeshRenderer meshRenderer)
	{
		mesh = Instantiate(sourceMesh);
		mesh.name = $"{name} Mesh Clone";

		GameObject gameObject = new(name);
		gameObject.layer = _screenRenderer.gameObject.layer;
		gameObject.transform.SetParent(_screenRenderer.transform, false);
		gameObject.transform.localPosition = Vector3.zero;
		gameObject.transform.localRotation = Quaternion.identity;
		gameObject.transform.localScale = Vector3.one;

		meshFilter = gameObject.AddComponent<MeshFilter>();
		meshFilter.sharedMesh = mesh;
		meshRenderer = gameObject.AddComponent<MeshRenderer>();

		int materialCount = Mathf.Max(1, mesh.subMeshCount);
		Material[] materials = new Material[materialCount];
		for (int i = 0; i < materials.Length; i++)
		{
			materials[i] = material;
		}

		meshRenderer.sharedMaterials = materials;
		meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		meshRenderer.receiveShadows = false;
		meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
		meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
		return gameObject;
	}

	private void DestroyOpaqueScreenPlane()
	{
		if (_opaqueBackplateObject != null)
		{
			Destroy(_opaqueBackplateObject);
			_opaqueBackplateObject = null;
		}

		if (_opaqueScreenObject != null)
		{
			Destroy(_opaqueScreenObject);
			_opaqueScreenObject = null;
		}

		if (_opaqueBackplateMesh != null)
		{
			Destroy(_opaqueBackplateMesh);
			_opaqueBackplateMesh = null;
		}

		if (_opaqueScreenMesh != null)
		{
			Destroy(_opaqueScreenMesh);
			_opaqueScreenMesh = null;
		}

		if (_opaqueBackplateMaterial != null)
		{
			Destroy(_opaqueBackplateMaterial);
			_opaqueBackplateMaterial = null;
		}

		if (_opaqueBackplateTexture != null)
		{
			Destroy(_opaqueBackplateTexture);
			_opaqueBackplateTexture = null;
		}

		_opaqueBackplateRenderer = null;
		_opaqueBackplateMeshFilter = null;
		_opaqueScreenRenderer = null;
		_opaqueScreenMeshFilter = null;
	}

	private void DisableGlassLikeRenderersForDebug()
	{
		if (_screenRenderer == null)
		{
			return;
		}

		Transform root = _rendererRoot ?? _screenRenderer.transform.root;
		Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
		for (int i = 0; i < renderers.Length; i++)
		{
			Renderer renderer = renderers[i];
			if (renderer == null || renderer == _screenRenderer || renderer == _opaqueScreenRenderer)
			{
				continue;
			}

			if (!LooksLikeGlassRenderer(renderer))
			{
				continue;
			}

			_debugDisabledRenderers.Add(renderer);
			_debugDisabledRendererStates.Add(renderer.enabled);
			renderer.enabled = false;
			TscDiagnostics.LogLcd(
				$"TSC phone Force Opaque LCD Debug disabled glass/cover renderer '{GetTransformPath(renderer.transform, root)}' materials=[{GetMaterialSummary(renderer.sharedMaterials)}].");
		}
	}

	private void RestoreDebugDisabledRenderers()
	{
		for (int i = 0; i < _debugDisabledRenderers.Count; i++)
		{
			Renderer renderer = _debugDisabledRenderers[i];
			if (renderer != null)
			{
				renderer.enabled = _debugDisabledRendererStates[i];
			}
		}

		_debugDisabledRenderers.Clear();
		_debugDisabledRendererStates.Clear();
	}

	private static bool LooksLikeGlassRenderer(Renderer renderer)
	{
		if (renderer == null)
		{
			return false;
		}

		string path = GetTransformPath(renderer.transform, null).ToLowerInvariant();
		string materials = GetMaterialSummary(renderer.sharedMaterials).ToLowerInvariant();
		return path.Contains("glass") ||
		       path.Contains("cover") ||
		       path.Contains("lens") ||
		       path.Contains("reflection") ||
		       materials.Contains("glass") ||
		       materials.Contains("transparent") ||
		       materials.Contains("fade") ||
		       materials.Contains("reflection");
	}

	private static Mesh GetRendererMesh(Renderer renderer)
	{
		if (renderer == null)
		{
			return null;
		}

		MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
		if (meshFilter != null)
		{
			return meshFilter.sharedMesh;
		}

		return renderer is SkinnedMeshRenderer skinnedMeshRenderer
			? skinnedMeshRenderer.sharedMesh
			: null;
	}

	private static string GetTransformPath(Transform transform, Transform root)
	{
		if (transform == null)
		{
			return "<null>";
		}

		Stack<string> names = new();
		Transform current = transform;
		while (current != null)
		{
			names.Push(current.name);
			if (root != null && current == root)
			{
				break;
			}

			current = current.parent;
		}

		return string.Join("/", names.ToArray());
	}

	private static string GetMaterialSummary(Material[] materials)
	{
		if (materials == null || materials.Length == 0)
		{
			return "<none>";
		}

		List<string> parts = new(materials.Length);
		for (int i = 0; i < materials.Length; i++)
		{
			Material material = materials[i];
			if (material == null)
			{
				parts.Add($"{i}:<null>");
				continue;
			}

			parts.Add($"{i}:{material.name}|shader={material.shader?.name ?? "<null>"}|queue={material.renderQueue}");
		}

		return string.Join("; ", parts);
	}

	private static int GetSmallestAxis(Vector3 value)
	{
		if (value.x <= value.y && value.x <= value.z)
		{
			return 0;
		}

		return value.y <= value.z ? 1 : 2;
	}

	private static float GetAxis(Vector3 value, int axis)
	{
		return axis switch
		{
			0 => value.x,
			1 => value.y,
			_ => value.z
		};
	}

	private static Vector3 AxisVector(int axis)
	{
		return axis switch
		{
			0 => Vector3.right,
			1 => Vector3.up,
			_ => Vector3.forward
		};
	}

	private void BuildHomeScreen()
	{
		if (TryBuildAssetScreen(
			    "TerraGroup Home Asset Screen",
			    "landscape_1024x512/TG_00_Boot_ResolvingDNS.png",
			    portrait: false,
			    out _homeGroup))
		{
			return;
		}

		RectTransform root = CreateScreenRoot("TerraGroup Home Screen");
		_homeGroup = root.gameObject.AddComponent<CanvasGroup>();
		BuildCommonChrome(root, "TERRAGROUP", "Secure Field Terminal");

		AddText(root, "TACTICAL", 48, FontStyle.Bold, new Color(0.9f, 0.95f, 0.93f), new Rect(42, 120, 680, 58), TextAnchor.MiddleCenter);
		AddText(root, "AUTHORIZATION NETWORK", 24, FontStyle.Bold, Teal(), new Rect(42, 174, 680, 34), TextAnchor.MiddleCenter);

		RectTransform card = AddPanel(root, new Rect(114, 232, 540, 100), new Color(0.045f, 0.065f, 0.065f, 0.94f));
		AddText(card, "PHONE AUTHORIZATIONS", 22, FontStyle.Bold, Color.white, new Rect(0, 18, 540, 34), TextAnchor.MiddleCenter);
		AddText(card, "PURCHASE HERE. DEPLOY FROM YY.", 18, FontStyle.Normal, Muted(), new Rect(0, 54, 540, 30), TextAnchor.MiddleCenter);

		RectTransform footer = AddPanel(root, new Rect(150, 366, 468, 42), new Color(0.09f, 0.28f, 0.26f, 0.58f));
		AddText(footer, "TAP TO OPEN SERVICES", 23, FontStyle.Bold, Teal(), new Rect(0, 4, 468, 32), TextAnchor.MiddleCenter);

		BuildScanlineOverlay(root);
	}

	private void BuildTacticalServicesScreen()
	{
		if (TryBuildAssetScreen(
			    "TerraGroup Tactical Services Asset Screen",
			    GetTacticalServicesAssetPath(_context.SupportType),
			    portrait: false,
			    out _tacticalServicesGroup))
		{
			AddDynamicTextOverlays(
				_tacticalServicesGroup.transform as RectTransform,
				GetTacticalServicesAssetPath(_context.SupportType),
				portrait: false);
			return;
		}

		RectTransform root = CreateScreenRoot("TerraGroup Tactical Services Screen");
		_tacticalServicesGroup = root.gameObject.AddComponent<CanvasGroup>();
		BuildCommonChrome(root, "TERRAGROUP", "Tactical Services");

		AddText(root, "SERVICE CATEGORY", 34, FontStyle.Bold, new Color(0.9f, 0.95f, 0.93f), new Rect(42, 110, 680, 44), TextAnchor.MiddleLeft);
		AddText(root, "SELECT AN AUTHORIZATION TYPE", 18, FontStyle.Normal, Teal(), new Rect(44, 150, 420, 28), TextAnchor.MiddleLeft);

		AddServiceCard(root, ESupportType.Extract, new Rect(42, 194, 206, 142));
		AddServiceCard(root, ESupportType.Strafe, new Rect(281, 194, 206, 142));
		AddServiceCard(root, ESupportType.Uav, new Rect(520, 194, 206, 142));

		RectTransform footer = AddPanel(root, new Rect(42, 356, 684, 52), new Color(0.045f, 0.06f, 0.06f, 0.92f));
		AddText(footer, "1 EXTRACTION   2 FIRE SUPPORT   3 RECON", 22, FontStyle.Bold, new Color(0.9f, 0.93f, 0.9f), new Rect(0, 5, 684, 34), TextAnchor.MiddleCenter);

		BuildScanlineOverlay(root);
	}

	private void BuildServiceCategoryScreen()
	{
		if (TryBuildAssetScreen(
			    "TerraGroup Service Category Asset Screen",
			    GetCategoryAssetPath(_context.SupportType),
			    portrait: false,
			    out _serviceCategoryGroup))
		{
			AddDynamicTextOverlays(
				_serviceCategoryGroup.transform as RectTransform,
				GetCategoryAssetPath(_context.SupportType),
				portrait: false);
			return;
		}

		RectTransform root = CreateScreenRoot("TerraGroup Service Category Screen");
		_serviceCategoryGroup = root.gameObject.AddComponent<CanvasGroup>();
		BuildCommonChrome(root, "TERRAGROUP", GetCategoryName(_context.SupportType));

		AddText(root, GetCategoryName(_context.SupportType), 34, FontStyle.Bold, new Color(0.9f, 0.95f, 0.93f), new Rect(42, 110, 680, 44), TextAnchor.MiddleLeft);
		AddText(root, "AVAILABLE SERVICE", 18, FontStyle.Normal, Teal(), new Rect(44, 150, 420, 28), TextAnchor.MiddleLeft);

		RectTransform card = AddPanel(root, new Rect(82, 196, 604, 142), new Color(0.06f, 0.078f, 0.078f, 0.94f));
		AddText(card, GetServiceTitle(_context.SupportType), 34, FontStyle.Bold, Color.white, new Rect(28, 20, 360, 44), TextAnchor.MiddleLeft);
		AddText(card, GetServiceDescription(_context.SupportType), 18, FontStyle.Normal, Muted(), new Rect(30, 66, 390, 48), TextAnchor.MiddleLeft);
		AddText(card, FormatRoubles(_context.CostRoubles), 32, FontStyle.Bold, Amber(), new Rect(400, 44, 172, 42), TextAnchor.MiddleRight);
		AddText(card, $"AUTH {FireSupportAuthorizations.Get(_context.SupportType)}", 18, FontStyle.Bold, Teal(), new Rect(400, 86, 172, 30), TextAnchor.MiddleRight);

		RectTransform footer = AddPanel(root, new Rect(150, 366, 468, 42), new Color(0.09f, 0.28f, 0.26f, 0.58f));
		AddText(footer, "TAP TO REVIEW SERVICE", 23, FontStyle.Bold, Teal(), new Rect(0, 4, 468, 32), TextAnchor.MiddleCenter);

		BuildScanlineOverlay(root);
	}

	private void BuildRequestScreen()
	{
		if (TryBuildAssetScreen(
			    "TerraGroup Request Review Asset Screen",
			    GetReviewAssetPath(_context.SupportType),
			    portrait: false,
			    out _requestGroup))
		{
			AddDynamicTextOverlays(
				_requestGroup.transform as RectTransform,
				GetReviewAssetPath(_context.SupportType),
				portrait: false);
			return;
		}

		RectTransform root = CreateScreenRoot("Request Screen");
		_requestGroup = root.gameObject.AddComponent<CanvasGroup>();
		BuildCommonChrome(root, "TERRAGROUP", "Tactical Services");

		AddText(root, $"{GetServiceTitle(_context.SupportType)} REQUEST", 40, FontStyle.Bold, new Color(0.9f, 0.95f, 0.93f), new Rect(40, 104, 660, 48), TextAnchor.MiddleLeft);
		AddText(root, "REVIEW AND AUTHORIZE", 18, FontStyle.Normal, Teal(), new Rect(42, 150, 360, 24), TextAnchor.MiddleLeft);

		RectTransform leftPanel = AddPanel(root, new Rect(42, 188, 330, 152), new Color(0.07f, 0.085f, 0.085f, 0.92f));
		AddText(leftPanel, "MISSION SUMMARY", 18, FontStyle.Normal, Muted(), new Rect(18, 10, 250, 28), TextAnchor.MiddleLeft);
		AddDetailRow(leftPanel, "DURATION", GetServiceDuration(), 44);
		AddDetailRow(leftPanel, "DEPLOYMENT", GetDeploymentMode(_context.SupportType), 78);
		AddDetailRow(leftPanel, "AUTH HELD", FireSupportAuthorizations.Get(_context.SupportType).ToString(), 112);

		RectTransform rightPanel = AddPanel(root, new Rect(396, 188, 330, 152), new Color(0.07f, 0.085f, 0.085f, 0.92f));
		AddText(rightPanel, "COST BREAKDOWN", 18, FontStyle.Normal, Muted(), new Rect(18, 10, 250, 28), TextAnchor.MiddleLeft);
		AddDetailRow(rightPanel, "SERVICE FEE", FormatRoubles(_context.CostRoubles), 44);
		AddDetailRow(rightPanel, "BALANCE", FormatRoubles(_context.BalanceRoubles), 78);
		AddLine(rightPanel, new Rect(18, 108, rightPanel.sizeDelta.x - 36, 1), new Color(0.45f, 0.53f, 0.5f, 0.18f));
		AddText(rightPanel, "TOTAL", 18, FontStyle.Bold, Muted(), new Rect(18, 114, 100, 28), TextAnchor.MiddleLeft);
		AddText(rightPanel, FormatRoubles(_context.CostRoubles), 25, FontStyle.Bold, Amber(), new Rect(124, 108, 188, 38), TextAnchor.MiddleRight);

		RectTransform footer = AddPanel(root, new Rect(42, 356, 684, 52), new Color(0.045f, 0.06f, 0.06f, 0.92f));
		AddText(footer, "TAP DEVICE TO AUTHORIZE", 28, FontStyle.Bold, new Color(0.9f, 0.93f, 0.9f), new Rect(0, 5, 684, 34), TextAnchor.MiddleCenter);

		BuildScanlineOverlay(root);
	}

	private void BuildRotateToConfirmScreen()
	{
		if (TryBuildAssetScreen(
			    "TerraGroup Rotate Confirm Asset Screen",
			    GetReviewAssetPath(_context.SupportType),
			    portrait: false,
			    out _rotateGroup))
		{
			AddDynamicTextOverlays(
				_rotateGroup.transform as RectTransform,
				GetReviewAssetPath(_context.SupportType),
				portrait: false);
			return;
		}

		RectTransform root = CreateScreenRoot("Rotate Confirm Screen");
		_rotateGroup = root.gameObject.AddComponent<CanvasGroup>();
		BuildCommonChrome(root, "TERRAGROUP", "Secure Payment");

		AddText(root, "ROTATE DEVICE", 44, FontStyle.Bold, new Color(0.9f, 0.95f, 0.93f), new Rect(42, 128, 682, 54), TextAnchor.MiddleCenter);
		AddText(root, "TO CONFIRM PAYMENT", 23, FontStyle.Bold, Teal(), new Rect(42, 180, 682, 34), TextAnchor.MiddleCenter);

		RectTransform card = AddPanel(root, new Rect(146, 246, 476, 86), new Color(0.055f, 0.07f, 0.075f, 0.94f));
		AddText(card, GetServiceTitle(_context.SupportType), 24, FontStyle.Bold, Color.white, new Rect(22, 16, 260, 32), TextAnchor.MiddleLeft);
		AddText(card, FormatRoubles(_context.CostRoubles), 28, FontStyle.Bold, Amber(), new Rect(260, 14, 190, 36), TextAnchor.MiddleRight);
		AddText(card, "PRESS ENTER TO CONTINUE", 17, FontStyle.Bold, Muted(), new Rect(22, 48, 428, 28), TextAnchor.MiddleCenter);

		BuildScanlineOverlay(root);
	}

	private void BuildConfirmPaymentPortraitScreen()
	{
		if (TryBuildAssetScreen(
			    "TerraGroup Confirm Payment Asset Screen",
			    GetConfirmSwipeAssetPath(_context.SupportType),
			    portrait: true,
			    out _confirmPaymentGroup))
		{
			AddDynamicTextOverlays(
				_confirmPaymentGroup.transform as RectTransform,
				GetConfirmSwipeAssetPath(_context.SupportType),
				portrait: true);
			BuildSwipeAnimationOverlay(_confirmPaymentGroup.transform as RectTransform);
			return;
		}

		RectTransform root = CreateScreenRoot("Confirm Payment Portrait Screen");
		_confirmPaymentGroup = root.gameObject.AddComponent<CanvasGroup>();
		BuildCommonChrome(root, "TERRAGROUP", "Secure Payment");

		RectTransform card = AddPanel(root, new Rect(184, 94, 400, 286), new Color(0.055f, 0.07f, 0.075f, 0.95f));
		AddText(card, "CONFIRM TRANSFER", 28, FontStyle.Bold, Color.white, new Rect(0, 20, 400, 38), TextAnchor.MiddleCenter);
		AddText(card, GetServiceTitle(_context.SupportType), 25, FontStyle.Bold, new Color(0.9f, 0.95f, 0.93f), new Rect(30, 78, 220, 36), TextAnchor.MiddleLeft);
		AddText(card, GetServiceDescription(_context.SupportType), 16, FontStyle.Normal, Muted(), new Rect(32, 114, 220, 54), TextAnchor.MiddleLeft);
		AddText(card, FormatRoubles(_context.CostRoubles), 31, FontStyle.Bold, Amber(), new Rect(226, 184, 140, 42), TextAnchor.MiddleRight);
		AddLine(card, new Rect(28, 174, 344, 1), new Color(0.45f, 0.53f, 0.5f, 0.22f));
		AddText(card, "TOTAL PAYMENT", 17, FontStyle.Bold, Muted(), new Rect(32, 188, 160, 28), TextAnchor.MiddleLeft);
		AddText(card, "SWIPE UP", 30, FontStyle.Bold, Teal(), new Rect(0, 232, 400, 38), TextAnchor.MiddleCenter);

		BuildSwipeAnimationOverlay(root);
		BuildScanlineOverlay(root);
	}

	private void BuildAuthorizingScreen()
	{
		if (TryBuildAssetScreen(
			    "TerraGroup Authorizing Asset Screen",
			    "portrait_512x1024/TG_05_Authorizing.png",
			    portrait: true,
			    out _authorizingGroup))
		{
			return;
		}

		RectTransform root = CreateScreenRoot("Authorizing Screen");
		_authorizingGroup = root.gameObject.AddComponent<CanvasGroup>();
		BuildCommonChrome(root, "TERRAGROUP", "Secure Payment");

		AddText(root, "CONFIRMING TRANSFER", 42, FontStyle.Bold, new Color(0.9f, 0.94f, 0.92f), new Rect(42, 118, 682, 54), TextAnchor.MiddleCenter);
		AddText(root, "AUTHENTICATING DEVICE KEY", 21, FontStyle.Normal, Teal(), new Rect(42, 168, 682, 30), TextAnchor.MiddleCenter);

		RectTransform card = AddPanel(root, new Rect(106, 222, 556, 132), new Color(0.055f, 0.07f, 0.075f, 0.94f));
		AddText(card, "SERVICE", 18, FontStyle.Normal, Muted(), new Rect(22, 18, 180, 28), TextAnchor.MiddleLeft);
		AddText(card, GetServiceTitle(_context.SupportType), 30, FontStyle.Bold, Color.white, new Rect(22, 54, 260, 38), TextAnchor.MiddleLeft);
		AddText(card, GetServiceDescription(_context.SupportType), 18, FontStyle.Normal, new Color(0.76f, 0.8f, 0.78f), new Rect(22, 90, 310, 30), TextAnchor.MiddleLeft);
		AddText(card, FormatRoubles(_context.CostRoubles), 34, FontStyle.Bold, Amber(), new Rect(318, 54, 210, 44), TextAnchor.MiddleRight);
		AddText(card, "TRANSFER PENDING", 18, FontStyle.Bold, Amber(), new Rect(318, 96, 210, 28), TextAnchor.MiddleRight);

		RectTransform progress = AddPanel(root, new Rect(116, 372, 536, 46), new Color(0.09f, 0.28f, 0.26f, 0.58f));
		AddText(progress, "SECURE LINK ESTABLISHED", 24, FontStyle.Bold, Teal(), new Rect(0, 5, 536, 34), TextAnchor.MiddleCenter);

		BuildScanlineOverlay(root);
	}

	private void BuildAuthorizedScreen()
	{
		if (TryBuildAssetScreen(
			    "TerraGroup Authorized Asset Screen",
			    GetAuthorizedAssetPath(_context.SupportType),
			    portrait: true,
			    out _authorizedGroup))
		{
			return;
		}

		RectTransform root = CreateScreenRoot("Authorized Screen");
		_authorizedGroup = root.gameObject.AddComponent<CanvasGroup>();
		BuildCommonChrome(root, "TERRAGROUP", "Verified");

		AddText(root, "REQUEST AUTHORIZED", 46, FontStyle.Bold, new Color(0.91f, 0.96f, 0.93f), new Rect(42, 120, 682, 58), TextAnchor.MiddleCenter);
		AddText(root, $"{GetServiceTitle(_context.SupportType)} READY IN YY", 24, FontStyle.Bold, Teal(), new Rect(42, 176, 682, 34), TextAnchor.MiddleCenter);

		RectTransform card = AddPanel(root, new Rect(118, 236, 532, 130), new Color(0.045f, 0.075f, 0.07f, 0.95f));
		AddText(card, "ACTIVE SERVICE", 18, FontStyle.Normal, Muted(), new Rect(24, 18, 200, 28), TextAnchor.MiddleLeft);
		AddText(card, GetServiceTitle(_context.SupportType), 32, FontStyle.Bold, Color.white, new Rect(24, 56, 320, 38), TextAnchor.MiddleLeft);
		AddText(card, "Authorizations", 18, FontStyle.Normal, Muted(), new Rect(24, 98, 180, 26), TextAnchor.MiddleLeft);
		AddText(card, FireSupportAuthorizations.Get(_context.SupportType).ToString(), 26, FontStyle.Bold, Teal(), new Rect(250, 92, 250, 32), TextAnchor.MiddleRight);

		RectTransform footer = AddPanel(root, new Rect(116, 382, 536, 42), new Color(0.09f, 0.28f, 0.26f, 0.62f));
		AddText(footer, "SECURE CHANNEL ACTIVE", 23, FontStyle.Bold, Teal(), new Rect(0, 4, 536, 32), TextAnchor.MiddleCenter);

		BuildScanlineOverlay(root);
	}

	private void BuildDeniedScreen()
	{
		_deniedReasonText = null;
		_deniedDetailText = null;
		if (TryBuildAssetScreen(
			    "TerraGroup Payment Denied Asset Screen",
			    "portrait_512x1024/TG_07_PaymentDenied.png",
			    portrait: true,
			    out _deniedGroup))
		{
			AddDeniedReasonOverlay(_deniedGroup.transform as RectTransform, portrait: true);
			return;
		}

		RectTransform root = CreateScreenRoot("Denied Screen");
		_deniedGroup = root.gameObject.AddComponent<CanvasGroup>();
		BuildCommonChrome(root, "TERRAGROUP", "Secure Payment");

		AddText(root, "TRANSFER DENIED", 46, FontStyle.Bold, new Color(1f, 0.55f, 0.45f), new Rect(42, 124, 682, 58), TextAnchor.MiddleCenter);
		_deniedReasonText = AddText(root, string.Empty, 22, FontStyle.Bold, Amber(), new Rect(42, 178, 682, 34), TextAnchor.MiddleCenter);

		RectTransform card = AddPanel(root, new Rect(118, 236, 532, 120), new Color(0.08f, 0.045f, 0.04f, 0.94f));
		AddText(card, "SERVICE", 18, FontStyle.Normal, Muted(), new Rect(24, 16, 180, 28), TextAnchor.MiddleLeft);
		AddText(card, GetServiceTitle(_context.SupportType), 30, FontStyle.Bold, Color.white, new Rect(24, 52, 300, 38), TextAnchor.MiddleLeft);
		AddText(card, FormatRoubles(_context.BalanceRoubles), 24, FontStyle.Bold, Amber(), new Rect(286, 52, 214, 38), TextAnchor.MiddleRight);
		_deniedDetailText = AddText(card, string.Empty, 16, FontStyle.Bold, Muted(), new Rect(24, 88, 476, 26), TextAnchor.MiddleCenter);
		UpdateDeniedReasonText();

		BuildScanlineOverlay(root);
	}

	private void AddDeniedReasonOverlay(RectTransform root, bool portrait)
	{
		if (root == null)
		{
			return;
		}

		Rect panelRect = ConvertLayoutRect(new Rect(92f, 584f, 328f, 96f), root, portrait);
		AddPanel(root, panelRect, new Color(0.055f, 0.062f, 0.06f, 0.98f));

		Rect reasonRect = ConvertLayoutRect(new Rect(102f, 594f, 308f, 34f), root, portrait);
		_deniedReasonText = AddText(
			root,
			string.Empty,
			Mathf.RoundToInt(ScaleFontSize(27, root, portrait)),
			FontStyle.Bold,
			new Color(1f, 0.34f, 0.29f),
			reasonRect,
			TextAnchor.MiddleCenter);

		Rect detailRect = ConvertLayoutRect(new Rect(108f, 636f, 296f, 40f), root, portrait);
		_deniedDetailText = AddText(
			root,
			string.Empty,
			Mathf.RoundToInt(ScaleFontSize(17, root, portrait)),
			FontStyle.Normal,
			new Color(0.82f, 0.8f, 0.72f),
			detailRect,
			TextAnchor.UpperCenter);
		UpdateDeniedReasonText();
	}

	private void UpdateDeniedReasonText()
	{
		if (_deniedReasonText != null)
		{
			_deniedReasonText.text = FireSupportPayment.GetLastPurchaseDenialTitle(_context.SupportType);
		}

		if (_deniedDetailText != null)
		{
			_deniedDetailText.text = FireSupportPayment.GetLastPurchaseDenialDetail(_context.SupportType);
		}
	}

	private bool TryBuildAssetScreen(
		string name,
		string relativePath,
		bool portrait,
		out CanvasGroup group)
	{
		group = null;
		Sprite sprite = LoadPhoneSprite(relativePath);
		if (sprite == null)
		{
			return false;
		}

		RectTransform root = CreateScreenRoot(name, portrait);
		group = root.gameObject.AddComponent<CanvasGroup>();

		Image image = root.GetComponent<Image>();
		image.sprite = sprite;
		image.color = Color.white;
		image.type = Image.Type.Simple;
		image.preserveAspect = false;

		return true;
	}

	private void BuildSwipeAnimationOverlay(RectTransform root)
	{
		_swipeArrowImage = null;
		_swipeFrameSprites = null;
		if (root == null)
		{
			return;
		}

		_swipeFrameSprites = LoadSwipeFrameSprites();
		Sprite firstFrame = _swipeFrameSprites.Length > 0 ? _swipeFrameSprites[0] : LoadOverlaySprite(SwipeArrowRelativePath);
		if (firstFrame == null)
		{
			return;
		}

		float scaleX = root.sizeDelta.x / PortraitLayoutWidth;
		float scaleY = root.sizeDelta.y / PortraitLayoutHeight;
		Rect scaledMaskRect = new Rect(
			SwipeAnimationMaskRect.x * scaleX,
			SwipeAnimationMaskRect.y * scaleY,
			SwipeAnimationMaskRect.width * scaleX,
			SwipeAnimationMaskRect.height * scaleY);

		GameObject maskObject = new("TerraGroup Swipe Animation Mask");
		maskObject.layer = RenderLayer;
		maskObject.transform.SetParent(root, false);

		RectTransform maskTransform = maskObject.AddComponent<RectTransform>();
		maskTransform.anchorMin = new Vector2(0f, 1f);
		maskTransform.anchorMax = new Vector2(0f, 1f);
		maskTransform.pivot = new Vector2(0f, 1f);
		maskTransform.anchoredPosition = new Vector2(scaledMaskRect.x, -scaledMaskRect.y);
		maskTransform.sizeDelta = new Vector2(scaledMaskRect.width, scaledMaskRect.height);

		Image maskImage = maskObject.AddComponent<Image>();
		maskImage.sprite = WhiteSprite;
		maskImage.color = new Color(1f, 1f, 1f, 0.01f);
		maskImage.raycastTarget = false;

		Mask mask = maskObject.AddComponent<Mask>();
		mask.showMaskGraphic = false;

		GameObject arrowObject = new("TerraGroup Swipe Arrow Overlay");
		arrowObject.layer = RenderLayer;
		arrowObject.transform.SetParent(maskTransform, false);

		RectTransform rectTransform = arrowObject.AddComponent<RectTransform>();
		rectTransform.anchorMin = new Vector2(0f, 1f);
		rectTransform.anchorMax = new Vector2(0f, 1f);
		rectTransform.pivot = new Vector2(0f, 1f);
		rectTransform.anchoredPosition = new Vector2(-scaledMaskRect.x, scaledMaskRect.y);
		rectTransform.sizeDelta = root.sizeDelta;

		Image image = arrowObject.AddComponent<Image>();
		image.sprite = firstFrame;
		image.color = new Color(1f, 1f, 1f, 0f);
		image.preserveAspect = false;
		image.raycastTarget = false;

		_swipeArrowImage = image;
		SetSwipeArrowProgress(0f);
	}

	private static Sprite[] LoadSwipeFrameSprites()
	{
		List<Sprite> frames = new();
		for (int i = 0; i < SwipeFrameCount; i++)
		{
			Sprite frame = LoadOverlaySprite(string.Format(CultureInfo.InvariantCulture, SwipeFrameRelativePathFormat, i));
			if (frame != null)
			{
				frames.Add(frame);
			}
		}

		return frames.ToArray();
	}

	private void AddDynamicTextOverlays(RectTransform root, string relativePath, bool portrait)
	{
		if (root == null)
		{
			return;
		}

		string fileName = Path.GetFileName(relativePath);
		if (string.IsNullOrEmpty(fileName) ||
		    !TryGetDynamicTextFields(fileName, out List<DynamicTextField> fields))
		{
			return;
		}

		foreach (DynamicTextField field in fields)
		{
			string value = ResolveDynamicTextValue(field);
			if (string.IsNullOrEmpty(value))
			{
				continue;
			}

			AddTmpText(root, value, field, portrait);
		}
	}

	private TextMeshProUGUI AddTmpText(
		RectTransform parent,
		string text,
		DynamicTextField field,
		bool portrait)
	{
		Rect rect = ConvertLayoutRect(field.Rect, parent, portrait);
		GameObject gameObject = new($"Dynamic Text - {field.Name}");
		gameObject.layer = RenderLayer;
		gameObject.transform.SetParent(parent, false);

		RectTransform rt = gameObject.AddComponent<RectTransform>();
		rt.anchorMin = new Vector2(0f, 1f);
		rt.anchorMax = new Vector2(0f, 1f);
		rt.pivot = new Vector2(0f, 1f);
		rt.anchoredPosition = new Vector2(rect.x, -rect.y);
		rt.sizeDelta = new Vector2(rect.width, rect.height);

		TextMeshProUGUI label = gameObject.AddComponent<TextMeshProUGUI>();
		label.text = text;
		label.fontSize = ScaleFontSize(field.FontSize, parent, portrait);
		label.fontStyle = FontStyles.Bold;
		label.color = field.Color;
		label.alignment = ToTmpAlignment(field.Alignment);
		label.enableWordWrapping = false;
		label.overflowMode = TextOverflowModes.Overflow;
		label.raycastTarget = false;
		return label;
	}

	private static Rect ConvertLayoutRect(Rect layoutRect, RectTransform root, bool portrait)
	{
		Vector2 size = root.sizeDelta;
		float layoutWidth = portrait ? PortraitLayoutWidth : LandscapeLayoutWidth;
		float layoutHeight = portrait ? PortraitLayoutHeight : LandscapeLayoutHeight;
		return new Rect(
			layoutRect.x / layoutWidth * size.x,
			layoutRect.y / layoutHeight * size.y,
			layoutRect.width / layoutWidth * size.x,
			layoutRect.height / layoutHeight * size.y);
	}

	private static float ScaleFontSize(int fontSize, RectTransform root, bool portrait)
	{
		Vector2 size = root.sizeDelta;
		float layoutHeight = portrait ? PortraitLayoutHeight : LandscapeLayoutHeight;
		return Mathf.Max(8f, fontSize * size.y / layoutHeight);
	}

	private string ResolveDynamicTextValue(DynamicTextField field)
	{
		return field.Source switch
		{
			"a10" => FormatServicePrice(ESupportType.Strafe),
			"a10_double" => FormatServicePrice(ESupportType.DoubleStrafe),
			"a10_double_pass" => FormatServicePrice(ESupportType.DoubleStrafe),
			"a10_doublepass" => FormatServicePrice(ESupportType.DoubleStrafe),
			"double_pass" => FormatServicePrice(ESupportType.DoubleStrafe),
			"double_strafe" => FormatServicePrice(ESupportType.DoubleStrafe),
			"extraction" => FormatServicePrice(ESupportType.Extract),
			"priority_exfil" => FormatServicePrice(ESupportType.PriorityExfil),
			"uav" => FormatServicePrice(ESupportType.Uav),
			"focused_sweep" => FormatServicePrice(ESupportType.FocusedSweep),
			"carried_roubles" => FormatLayoutRoubles(FireSupportPayment.GetEffectiveBalance()),
			"effective_roubles" => FormatLayoutRoubles(FireSupportPayment.GetEffectiveBalance()),
			"effective_balance" => FormatLayoutRoubles(FireSupportPayment.GetEffectiveBalance()),
			"duration" => FormatDynamicDuration(field.Format),
			"coverage_radius" => FormatCoverageRadius(),
			_ => string.Empty
		};
	}

	private string FormatDynamicDuration(string format)
	{
		int seconds = UavReconSettings.GetDurationSeconds(GetReconSupportTypeForContext());
		if (string.Equals(format, "{seconds}s", StringComparison.OrdinalIgnoreCase))
		{
			return $"{seconds}s";
		}

		return FormatDuration(seconds);
	}

	private string FormatCoverageRadius()
	{
		return $"~{Mathf.RoundToInt(UavReconSettings.GetRangeMeters(GetReconSupportTypeForContext()))} m";
	}

	private static string FormatServicePrice(ESupportType supportType)
	{
		return FireSupportServiceAvailability.IsServiceEnabled(supportType)
			? FormatLayoutRoubles(FireSupportPayment.GetActiveCost(supportType))
			: "LOCKED";
	}

	private ESupportType GetReconSupportTypeForContext()
	{
		return _context.SupportType == ESupportType.FocusedSweep
			? ESupportType.FocusedSweep
			: ESupportType.Uav;
	}

	private static string FormatLayoutRoubles(int amount)
	{
		if (amount < 0)
		{
			return "SYNC";
		}

		return "\u20BD " + Mathf.Max(0, amount).ToString("N0", CultureInfo.InvariantCulture).Replace(',', ' ');
	}

	private static TextAlignmentOptions ToTmpAlignment(string alignment)
	{
		return alignment switch
		{
			"MiddleRight" => TextAlignmentOptions.MidlineRight,
			"MiddleCenter" => TextAlignmentOptions.Midline,
			"MiddleLeft" => TextAlignmentOptions.MidlineLeft,
			_ => TextAlignmentOptions.Midline
		};
	}

	private static bool TryGetDynamicTextFields(string fileName, out List<DynamicTextField> fields)
	{
		Dictionary<string, List<DynamicTextField>> layout = GetDynamicTextLayout();
		return layout.TryGetValue(fileName, out fields);
	}

	private static Dictionary<string, List<DynamicTextField>> GetDynamicTextLayout()
	{
		if (s_dynamicTextLayout != null)
		{
			return s_dynamicTextLayout;
		}

		s_dynamicTextLayout = new Dictionary<string, List<DynamicTextField>>(StringComparer.OrdinalIgnoreCase);
		string path = GetPhoneAssetPath(DynamicTextLayoutRelativePath);
		if (!File.Exists(path))
		{
			FireSupportPlugin.LogSource.LogWarning($"TerraGroup phone dynamic text layout missing: {path}");
			return s_dynamicTextLayout;
		}

		try
		{
			JObject root = JObject.Parse(File.ReadAllText(path));
			JObject screens = root["screens"] as JObject;
			if (screens == null)
			{
				return s_dynamicTextLayout;
			}

			foreach (JProperty screen in screens.Properties())
			{
				if (screen.Value is not JObject fieldsObject)
				{
					continue;
				}

				List<DynamicTextField> screenFields = new();
				foreach (JProperty fieldProperty in fieldsObject.Properties())
				{
					DynamicTextField field = DynamicTextField.FromJson(fieldProperty.Name, fieldProperty.Value as JObject);
					if (field != null)
					{
						screenFields.Add(field);
					}
				}

				s_dynamicTextLayout[screen.Name] = screenFields;
			}

			TscDiagnostics.LogLcd($"TSC phone dynamic text layout loaded: {path}");
			return s_dynamicTextLayout;
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource.LogWarning($"TerraGroup phone dynamic text layout load failed: {path}. {ex}");
			return s_dynamicTextLayout;
		}
	}

	private void SubscribeSettingChanges()
	{
		if (_settingsSubscribed)
		{
			return;
		}

		_settingsSubscribed = true;
		FireSupportPayment.SettingsChanged += OnDynamicSettingChanged;
		if (PluginSettings.StrafeRequestCostRoubles != null)
		{
			PluginSettings.StrafeRequestCostRoubles.SettingChanged += OnDynamicSettingChanged;
		}
		if (PluginSettings.DoubleStrafeRequestCostRoubles != null)
		{
			PluginSettings.DoubleStrafeRequestCostRoubles.SettingChanged += OnDynamicSettingChanged;
		}

		if (PluginSettings.ExtractionRequestCostRoubles != null)
		{
			PluginSettings.ExtractionRequestCostRoubles.SettingChanged += OnDynamicSettingChanged;
		}

		if (PluginSettings.PriorityExfilRequestCostRoubles != null)
		{
			PluginSettings.PriorityExfilRequestCostRoubles.SettingChanged += OnDynamicSettingChanged;
		}

		if (PluginSettings.UavRequestCostRoubles != null)
		{
			PluginSettings.UavRequestCostRoubles.SettingChanged += OnDynamicSettingChanged;
		}

		if (PluginSettings.FocusedSweepRequestCostRoubles != null)
		{
			PluginSettings.FocusedSweepRequestCostRoubles.SettingChanged += OnDynamicSettingChanged;
		}

		if (PluginSettings.UavDurationSeconds != null)
		{
			PluginSettings.UavDurationSeconds.SettingChanged += OnDynamicSettingChanged;
		}

		if (PluginSettings.UavRangeMeters != null)
		{
			PluginSettings.UavRangeMeters.SettingChanged += OnDynamicSettingChanged;
		}

		if (PluginSettings.FocusedSweepDurationSeconds != null)
		{
			PluginSettings.FocusedSweepDurationSeconds.SettingChanged += OnDynamicSettingChanged;
		}

		if (PluginSettings.FocusedSweepScanInterval != null)
		{
			PluginSettings.FocusedSweepScanInterval.SettingChanged += OnDynamicSettingChanged;
		}

		if (PluginSettings.FocusedSweepRangeMeters != null)
		{
			PluginSettings.FocusedSweepRangeMeters.SettingChanged += OnDynamicSettingChanged;
		}
	}

	private void UnsubscribeSettingChanges()
	{
		if (!_settingsSubscribed)
		{
			return;
		}

		_settingsSubscribed = false;
		FireSupportPayment.SettingsChanged -= OnDynamicSettingChanged;
		if (PluginSettings.StrafeRequestCostRoubles != null)
		{
			PluginSettings.StrafeRequestCostRoubles.SettingChanged -= OnDynamicSettingChanged;
		}
		if (PluginSettings.DoubleStrafeRequestCostRoubles != null)
		{
			PluginSettings.DoubleStrafeRequestCostRoubles.SettingChanged -= OnDynamicSettingChanged;
		}

		if (PluginSettings.ExtractionRequestCostRoubles != null)
		{
			PluginSettings.ExtractionRequestCostRoubles.SettingChanged -= OnDynamicSettingChanged;
		}

		if (PluginSettings.PriorityExfilRequestCostRoubles != null)
		{
			PluginSettings.PriorityExfilRequestCostRoubles.SettingChanged -= OnDynamicSettingChanged;
		}

		if (PluginSettings.UavRequestCostRoubles != null)
		{
			PluginSettings.UavRequestCostRoubles.SettingChanged -= OnDynamicSettingChanged;
		}

		if (PluginSettings.FocusedSweepRequestCostRoubles != null)
		{
			PluginSettings.FocusedSweepRequestCostRoubles.SettingChanged -= OnDynamicSettingChanged;
		}

		if (PluginSettings.UavDurationSeconds != null)
		{
			PluginSettings.UavDurationSeconds.SettingChanged -= OnDynamicSettingChanged;
		}

		if (PluginSettings.UavRangeMeters != null)
		{
			PluginSettings.UavRangeMeters.SettingChanged -= OnDynamicSettingChanged;
		}

		if (PluginSettings.FocusedSweepDurationSeconds != null)
		{
			PluginSettings.FocusedSweepDurationSeconds.SettingChanged -= OnDynamicSettingChanged;
		}

		if (PluginSettings.FocusedSweepScanInterval != null)
		{
			PluginSettings.FocusedSweepScanInterval.SettingChanged -= OnDynamicSettingChanged;
		}

		if (PluginSettings.FocusedSweepRangeMeters != null)
		{
			PluginSettings.FocusedSweepRangeMeters.SettingChanged -= OnDynamicSettingChanged;
		}
	}

	private void OnDynamicSettingChanged(object sender, EventArgs args)
	{
		if (_canvas == null)
		{
			return;
		}

		Rebuild(
			new UavPhoneScreenContext(
				_context.SupportType,
				FireSupportPayment.GetActiveCost(_context.SupportType),
				FireSupportPayment.GetEffectiveBalance(),
			UavReconSettings.GetDurationSeconds(_context.SupportType)),
			_currentState);
	}

	private static Sprite LoadPhoneSprite(string relativePath)
	{
		if (string.IsNullOrWhiteSpace(relativePath))
		{
			return null;
		}

		if (s_phoneSprites.TryGetValue(relativePath, out Sprite cachedSprite))
		{
			return cachedSprite;
		}

		string fullPath = GetPhoneAssetPath(relativePath);
		if (!File.Exists(fullPath))
		{
			if (s_missingPhoneSprites.Add(relativePath))
			{
				FireSupportPlugin.LogSource.LogWarning($"TerraGroup phone UI asset missing: {fullPath}");
			}

			s_phoneSprites[relativePath] = null;
			return null;
		}

		try
		{
			byte[] bytes = File.ReadAllBytes(fullPath);
			Texture2D texture = new(2, 2, TextureFormat.RGBA32, false)
			{
				name = $"TerraGroup Phone UI - {Path.GetFileName(relativePath)}",
				hideFlags = HideFlags.HideAndDontSave,
				filterMode = FilterMode.Bilinear,
				wrapMode = TextureWrapMode.Clamp,
				anisoLevel = 0
			};

			if (!texture.LoadImage(bytes, markNonReadable: false))
			{
				Destroy(texture);
				FireSupportPlugin.LogSource.LogWarning($"TerraGroup phone UI asset failed to decode: {fullPath}");
				s_phoneSprites[relativePath] = null;
				return null;
			}

			texture.filterMode = FilterMode.Bilinear;
			texture.wrapMode = TextureWrapMode.Clamp;
			texture.anisoLevel = 0;
			PrepareLcdTexture(texture, PhoneScreenBackground(), relativePath);

			Sprite sprite = Sprite.Create(
				texture,
				new Rect(0, 0, texture.width, texture.height),
				new Vector2(0.5f, 0.5f),
				100f);
			sprite.hideFlags = HideFlags.HideAndDontSave;
			s_phoneSprites[relativePath] = sprite;
			TscDiagnostics.LogLcd($"TSC phone UI asset loaded: {relativePath} ({texture.width}x{texture.height}), format=RGBA32, mipChain=false, wrap=Clamp, filter=Bilinear, alphaForcedOpaque=true, lcdBackgroundCleanup=true");
			return sprite;
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource.LogWarning($"TerraGroup phone UI asset load failed: {fullPath}. {ex}");
			s_phoneSprites[relativePath] = null;
			return null;
		}
	}

	private static Sprite LoadOverlaySprite(string relativePath)
	{
		if (string.IsNullOrWhiteSpace(relativePath))
		{
			return null;
		}

		if (s_overlaySprites.TryGetValue(relativePath, out Sprite cachedSprite))
		{
			return cachedSprite;
		}

		string fullPath = GetPhoneAssetPath(relativePath);
		if (!File.Exists(fullPath))
		{
			if (s_missingPhoneSprites.Add(relativePath))
			{
				FireSupportPlugin.LogSource.LogWarning($"TerraGroup phone overlay asset missing: {fullPath}");
			}

			s_overlaySprites[relativePath] = null;
			return null;
		}

		try
		{
			byte[] bytes = File.ReadAllBytes(fullPath);
			Texture2D texture = new(2, 2, TextureFormat.RGBA32, false)
			{
				name = $"TerraGroup Phone Overlay - {Path.GetFileName(relativePath)}",
				hideFlags = HideFlags.HideAndDontSave,
				filterMode = FilterMode.Bilinear,
				wrapMode = TextureWrapMode.Clamp,
				anisoLevel = 0
			};

			if (!texture.LoadImage(bytes, markNonReadable: false))
			{
				Destroy(texture);
				FireSupportPlugin.LogSource.LogWarning($"TerraGroup phone overlay asset failed to decode: {fullPath}");
				s_overlaySprites[relativePath] = null;
				return null;
			}

			texture.filterMode = FilterMode.Bilinear;
			texture.wrapMode = TextureWrapMode.Clamp;
			texture.anisoLevel = 0;

			Sprite sprite = Sprite.Create(
				texture,
				new Rect(0, 0, texture.width, texture.height),
				new Vector2(0.5f, 0.5f),
				100f);
			sprite.hideFlags = HideFlags.HideAndDontSave;
			s_overlaySprites[relativePath] = sprite;
			TscDiagnostics.LogLcd($"TSC phone overlay asset loaded: {relativePath} ({texture.width}x{texture.height}), alphaPreserved=true");
			return sprite;
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource.LogWarning($"TerraGroup phone overlay asset load failed: {fullPath}. {ex}");
			s_overlaySprites[relativePath] = null;
			return null;
		}
	}

	private static void PrepareLcdTexture(Texture2D texture, Color background, string relativePath)
	{
		try
		{
			Color32 background32 = background;
			Color32[] pixels = texture.GetPixels32();
			int alphaAdjusted = 0;
			int backgroundCleaned = 0;
			float cleanupStrength = Mathf.Clamp01(PluginSettings.PhoneLcdBackgroundCleanupStrength?.Value ?? 0.9f);
			for (int i = 0; i < pixels.Length; i++)
			{
				Color32 pixel = pixels[i];
				byte alpha = pixel.a;
				if (alpha == byte.MaxValue)
				{
					pixel.a = byte.MaxValue;
				}
				else
				{
					alphaAdjusted++;
					if (alpha == 0)
					{
						pixel = background32;
					}
					else
					{
						float a = alpha / 255f;
						float inverse = 1f - a;
						pixel = new Color32(
							(byte)Mathf.RoundToInt(pixel.r * a + background32.r * inverse),
							(byte)Mathf.RoundToInt(pixel.g * a + background32.g * inverse),
							(byte)Mathf.RoundToInt(pixel.b * a + background32.b * inverse),
							byte.MaxValue);
					}
				}

				int max = Mathf.Max(pixel.r, Mathf.Max(pixel.g, pixel.b));
				int min = Mathf.Min(pixel.r, Mathf.Min(pixel.g, pixel.b));
				float brightness = max / 255f;
				float saturation = max <= 0 ? 0f : (max - min) / (float)max;
				bool brightUi = brightness >= 0.48f;
				bool coloredAccent = saturation >= 0.28f && brightness >= 0.34f;
				if (!brightUi && !coloredAccent && cleanupStrength > 0.001f)
				{
					float luminance = (pixel.r * 0.2126f + pixel.g * 0.7152f + pixel.b * 0.0722f) / 255f;
					float detail = Mathf.Clamp01(luminance * 0.18f);
					Color32 lcdPixel = new(
						(byte)Mathf.RoundToInt(Mathf.Clamp(background32.r + 255f * detail, 0f, 62f)),
						(byte)Mathf.RoundToInt(Mathf.Clamp(background32.g + 255f * detail, 0f, 68f)),
						(byte)Mathf.RoundToInt(Mathf.Clamp(background32.b + 255f * detail, 0f, 66f)),
						byte.MaxValue);

					float strength = brightness < 0.32f ? cleanupStrength : cleanupStrength * 0.72f;
					pixel = new Color32(
						(byte)Mathf.RoundToInt(Mathf.Lerp(pixel.r, lcdPixel.r, strength)),
						(byte)Mathf.RoundToInt(Mathf.Lerp(pixel.g, lcdPixel.g, strength)),
						(byte)Mathf.RoundToInt(Mathf.Lerp(pixel.b, lcdPixel.b, strength)),
						byte.MaxValue);
					backgroundCleaned++;
				}

				pixel.a = byte.MaxValue;
				pixels[i] = pixel;
			}

			texture.SetPixels32(pixels);
			texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
			TscDiagnostics.LogLcd(
				$"TSC phone UI prepared for opaque LCD: {relativePath} ({texture.width}x{texture.height}), alphaAdjustedPixels={alphaAdjusted}, backgroundCleanedPixels={backgroundCleaned}, cleanupStrength={cleanupStrength:F2}.");
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource.LogWarning($"TerraGroup phone UI LCD texture preparation failed for '{texture?.name ?? "<null>"}'. {ex}");
		}
	}

	private static Color PhoneScreenBackground()
	{
		return new Color(0.025f, 0.03f, 0.032f, 1f);
	}

	private static string GetPhoneAssetPath(string relativePath)
	{
		string normalizedPath = relativePath.Replace('/', Path.DirectorySeparatorChar);
		return Path.Combine(GetPluginDirectory(), PhoneAssetRoot.Replace('/', Path.DirectorySeparatorChar), normalizedPath);
	}

	private static string GetPluginDirectory()
	{
		if (!string.IsNullOrEmpty(s_pluginDirectory))
		{
			return s_pluginDirectory;
		}

		s_pluginDirectory = Path.GetDirectoryName(typeof(UavPhoneScreenRenderer).Assembly.Location) ?? string.Empty;
		return s_pluginDirectory;
	}

	private RectTransform CreateScreenRoot(string name, bool portrait = false)
	{
		GameObject rootObject = new(name);
		rootObject.layer = RenderLayer;
		rootObject.transform.SetParent(_canvas.transform, false);

		RectTransform rectTransform = rootObject.AddComponent<RectTransform>();
		rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
		rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
		rectTransform.pivot = new Vector2(0.5f, 0.5f);
		rectTransform.anchoredPosition = Vector2.zero;
		rectTransform.sizeDelta = portrait ? new Vector2(_texWidth, _texHeight) : GetRotatedRootSize();
		rectTransform.localRotation = portrait ? Quaternion.identity : Quaternion.Euler(0f, 0f, _canvasRotation);

		Image background = rootObject.AddComponent<Image>();
		background.sprite = WhiteSprite;
		background.color = PhoneScreenBackground();
		background.raycastTarget = false;

		return rectTransform;
	}

	private void BuildCommonChrome(RectTransform root, string brand, string subtitle)
	{
		AddText(root, brand, 28, FontStyle.Bold, new Color(0.82f, 0.86f, 0.84f), new Rect(40, 28, 245, 34), TextAnchor.MiddleLeft);
		AddText(root, subtitle, 14, FontStyle.Normal, new Color(0.55f, 0.6f, 0.58f), new Rect(42, 58, 220, 24), TextAnchor.MiddleLeft);
		AddText(root, DateTime.Now.ToString("HH:mm"), 18, FontStyle.Normal, new Color(0.82f, 0.86f, 0.84f), new Rect(336, 32, 96, 30), TextAnchor.MiddleCenter);
		AddText(root, "VERIFIED", 15, FontStyle.Bold, Teal(), new Rect(602, 32, 118, 28), TextAnchor.MiddleRight);
		AddLine(root, new Rect(36, 88, 694, 1), new Color(0.55f, 0.62f, 0.6f, 0.32f));
	}

	private void AddServiceCard(RectTransform root, ESupportType supportType, Rect rect)
	{
		bool selected = supportType == _context.SupportType;
		Color color = selected
			? new Color(0.085f, 0.115f, 0.11f, 0.96f)
			: new Color(0.055f, 0.07f, 0.072f, 0.9f);
		RectTransform card = AddPanel(root, rect, color);
		if (selected)
		{
			AddLine(card, new Rect(0, 0, rect.width, 2), Teal());
			AddLine(card, new Rect(0, rect.height - 2, rect.width, 2), Teal());
		}

		AddText(card, GetCategoryName(supportType), 19, FontStyle.Bold, selected ? Teal() : Color.white, new Rect(0, 20, rect.width, 30), TextAnchor.MiddleCenter);
		AddText(card, GetServiceTitle(supportType), 18, FontStyle.Bold, Color.white, new Rect(12, 60, rect.width - 24, 30), TextAnchor.MiddleCenter);
		AddText(card, FireSupportServiceAvailability.IsServiceEnabled(supportType)
				? FormatRoubles(FireSupportPayment.GetActiveCost(supportType))
				: "LOCKED",
			18,
			FontStyle.Bold,
			FireSupportServiceAvailability.IsServiceEnabled(supportType) ? Amber() : Muted(),
			new Rect(12, 96, rect.width - 24, 28),
			TextAnchor.MiddleCenter);
	}

	private void AddDetailRow(RectTransform parent, string label, string value, float y)
	{
		AddLine(parent, new Rect(18, y - 10, parent.sizeDelta.x - 36, 1), new Color(0.45f, 0.53f, 0.5f, 0.18f));
		AddText(parent, label, 16, FontStyle.Normal, Muted(), new Rect(18, y, 150, 28), TextAnchor.MiddleLeft);
		AddText(parent, value, 19, FontStyle.Bold, Teal(), new Rect(166, y, parent.sizeDelta.x - 184, 28), TextAnchor.MiddleRight);
	}

	private RectTransform AddPanel(RectTransform parent, Rect rect, Color color)
	{
		GameObject gameObject = new("Panel");
		gameObject.layer = RenderLayer;
		gameObject.transform.SetParent(parent, false);

		RectTransform rt = gameObject.AddComponent<RectTransform>();
		rt.anchorMin = new Vector2(0f, 1f);
		rt.anchorMax = new Vector2(0f, 1f);
		rt.pivot = new Vector2(0f, 1f);
		rt.anchoredPosition = new Vector2(rect.x, -rect.y);
		rt.sizeDelta = new Vector2(rect.width, rect.height);

		Image image = gameObject.AddComponent<Image>();
		image.sprite = WhiteSprite;
		image.color = color;
		image.raycastTarget = false;

		AddLine(rt, new Rect(0, 0, rect.width, 1), new Color(0.67f, 0.76f, 0.72f, 0.22f));
		AddLine(rt, new Rect(0, rect.height - 1, rect.width, 1), new Color(0.67f, 0.76f, 0.72f, 0.16f));
		AddLine(rt, new Rect(0, 0, 1, rect.height), new Color(0.67f, 0.76f, 0.72f, 0.16f));
		AddLine(rt, new Rect(rect.width - 1, 0, 1, rect.height), new Color(0.67f, 0.76f, 0.72f, 0.16f));

		return rt;
	}

	private Text AddText(
		RectTransform parent,
		string text,
		int fontSize,
		FontStyle style,
		Color color,
		Rect rect,
		TextAnchor alignment)
	{
		GameObject gameObject = new("Text");
		gameObject.layer = RenderLayer;
		gameObject.transform.SetParent(parent, false);

		RectTransform rt = gameObject.AddComponent<RectTransform>();
		rt.anchorMin = new Vector2(0f, 1f);
		rt.anchorMax = new Vector2(0f, 1f);
		rt.pivot = new Vector2(0f, 1f);
		rt.anchoredPosition = new Vector2(rect.x, -rect.y);
		rt.sizeDelta = new Vector2(rect.width, rect.height);

		Text label = gameObject.AddComponent<Text>();
		label.font = _font;
		label.text = text;
		label.fontSize = fontSize;
		label.fontStyle = style;
		label.color = color;
		label.alignment = alignment;
		label.horizontalOverflow = HorizontalWrapMode.Wrap;
		label.verticalOverflow = VerticalWrapMode.Truncate;
		label.raycastTarget = false;
		return label;
	}

	private void AddLine(RectTransform parent, Rect rect, Color color)
	{
		GameObject gameObject = new("Line");
		gameObject.layer = RenderLayer;
		gameObject.transform.SetParent(parent, false);

		RectTransform rt = gameObject.AddComponent<RectTransform>();
		rt.anchorMin = new Vector2(0f, 1f);
		rt.anchorMax = new Vector2(0f, 1f);
		rt.pivot = new Vector2(0f, 1f);
		rt.anchoredPosition = new Vector2(rect.x, -rect.y);
		rt.sizeDelta = new Vector2(rect.width, rect.height);

		Image image = gameObject.AddComponent<Image>();
		image.sprite = WhiteSprite;
		image.color = color;
		image.raycastTarget = false;
	}

	private void BuildScanlineOverlay(RectTransform root)
	{
		for (int y = 10; y < 430; y += 12)
		{
			AddLine(root, new Rect(0, y, 768, 1), new Color(1f, 1f, 1f, 0.018f));
		}

		for (int x = 50; x < 738; x += 78)
		{
			AddLine(root, new Rect(x, 92, 1, 520), new Color(0.3f, 0.9f, 0.78f, 0.035f));
		}
	}

	private void SetScreenState(CanvasGroup activeGroup)
	{
		SetGroup(_homeGroup, _homeGroup == activeGroup);
		SetGroup(_tacticalServicesGroup, _tacticalServicesGroup == activeGroup);
		SetGroup(_serviceCategoryGroup, _serviceCategoryGroup == activeGroup);
		SetGroup(_requestGroup, _requestGroup == activeGroup);
		SetGroup(_rotateGroup, _rotateGroup == activeGroup);
		SetGroup(_confirmPaymentGroup, _confirmPaymentGroup == activeGroup);
		SetGroup(_authorizingGroup, _authorizingGroup == activeGroup);
		SetGroup(_authorizedGroup, _authorizedGroup == activeGroup);
		SetGroup(_deniedGroup, _deniedGroup == activeGroup);
	}

	private IEnumerator FadeToStateCoroutine(TerraGroupPhoneState state, float durationSeconds)
	{
		CanvasGroup fromGroup = GetGroupForState(_currentState);
		CanvasGroup toGroup = GetGroupForState(state);
		if (toGroup == null || fromGroup == toGroup)
		{
			ShowState(state);
			yield break;
		}

		_currentState = state;
		CanvasGroup[] groups =
		{
			_homeGroup,
			_tacticalServicesGroup,
			_serviceCategoryGroup,
			_requestGroup,
			_rotateGroup,
			_confirmPaymentGroup,
			_authorizingGroup,
			_authorizedGroup,
			_deniedGroup
		};

		foreach (CanvasGroup group in groups)
		{
			if (group == null)
			{
				continue;
			}

			group.alpha = group == fromGroup ? 1f : 0f;
			group.interactable = false;
			group.blocksRaycasts = false;
		}

		float startedAt = Time.unscaledTime;
		while (Time.unscaledTime - startedAt < durationSeconds)
		{
			float t = Mathf.Clamp01((Time.unscaledTime - startedAt) / durationSeconds);
			t = Mathf.SmoothStep(0f, 1f, t);
			if (fromGroup != null)
			{
				fromGroup.alpha = 1f - t;
			}

			toGroup.alpha = t;
			yield return null;
		}

		SetScreenState(toGroup);
		HandleVisibleState(state);
		_stateFadeCoroutine = null;
	}

	private void HandleVisibleState(TerraGroupPhoneState state)
	{
		if (state != TerraGroupPhoneState.ConfirmPaymentPortrait)
		{
			StopSwipeAnimation();
		}

		if (state == TerraGroupPhoneState.Denied)
		{
			UpdateDeniedReasonText();
		}
	}

	private CanvasGroup GetGroupForState(TerraGroupPhoneState state)
	{
		return state switch
		{
			TerraGroupPhoneState.Home => _homeGroup,
			TerraGroupPhoneState.TacticalServices => _tacticalServicesGroup,
			TerraGroupPhoneState.ServiceCategory => _serviceCategoryGroup,
			TerraGroupPhoneState.ServiceReview => _requestGroup,
			TerraGroupPhoneState.RotateToConfirm => _rotateGroup,
			TerraGroupPhoneState.ConfirmPaymentPortrait => _confirmPaymentGroup,
			TerraGroupPhoneState.Authorizing => _authorizingGroup,
			TerraGroupPhoneState.Authorized => _authorizedGroup,
			TerraGroupPhoneState.Denied => _deniedGroup,
			_ => _homeGroup
		};
	}

	private static void SetGroup(CanvasGroup group, bool active)
	{
		if (group == null)
		{
			return;
		}

		group.alpha = active ? 1f : 0f;
		group.interactable = false;
		group.blocksRaycasts = false;
	}

	public void StartConfirmSwipeAnimation()
	{
		StartSwipeAnimation();
	}

	public void StopConfirmSwipeAnimation()
	{
		StopSwipeAnimation();
	}

	public void SetConfirmSwipeAnimationProgress(float progress)
	{
		if (_swipeArrowImage == null)
		{
			return;
		}

		StopSwipeAnimation();
		SetSwipeArrowProgress(progress);
	}

	private void StartSwipeAnimation()
	{
		if (_swipeArrowImage == null)
		{
			return;
		}

		StopSwipeAnimation();
		_swipeAnimationCoroutine = StartCoroutine(AnimateSwipeArrow());
	}

	private void StopSwipeAnimation()
	{
		if (_swipeAnimationCoroutine != null)
		{
			StopCoroutine(_swipeAnimationCoroutine);
			_swipeAnimationCoroutine = null;
		}

		if (_swipeArrowImage != null)
		{
			Color color = _swipeArrowImage.color;
			color.a = 0f;
			_swipeArrowImage.color = color;
		}
	}

	private IEnumerator AnimateSwipeArrow()
	{
		float startedAt = Time.unscaledTime;
		while (_swipeArrowImage != null &&
		       _currentState == TerraGroupPhoneState.ConfirmPaymentPortrait)
		{
			float t = Mathf.Clamp01((Time.unscaledTime - startedAt) / SwipeArrowAnimationSeconds);
			SetSwipeArrowProgress(t);
			if (t >= 1f)
			{
				break;
			}

			yield return null;
		}

		if (_swipeArrowImage != null)
		{
			SetSwipeArrowProgress(1f);
		}

		_swipeAnimationCoroutine = null;
	}

	private void SetSwipeArrowProgress(float progress)
	{
		if (_swipeArrowImage == null)
		{
			return;
		}

		progress = Mathf.Clamp01(progress);
		if (_swipeFrameSprites != null && _swipeFrameSprites.Length > 0)
		{
			int index = Mathf.Clamp(
				Mathf.FloorToInt(progress * _swipeFrameSprites.Length),
				0,
				_swipeFrameSprites.Length - 1);
			_swipeArrowImage.sprite = _swipeFrameSprites[index];
		}

		Color color = _swipeArrowImage.color;
		color.a = ComputeSwipeArrowAlpha(progress);
		_swipeArrowImage.color = color;
	}

	private static float ComputeSwipeArrowAlpha(float progress)
	{
		if (progress <= 0f || progress >= 1f)
		{
			return 0f;
		}

		float fadeIn = Mathf.Clamp01(progress / 0.18f);
		float fadeOut = Mathf.Clamp01((1f - progress) / 0.18f);
		return Mathf.SmoothStep(0f, 1f, Mathf.Min(fadeIn, fadeOut));
	}

	private Vector2 GetRotatedRootSize()
	{
		bool perpendicular = Mathf.Abs(((_canvasRotation % 180f) + 180f) % 180f - 90f) < 0.5f;
		return perpendicular
			? new Vector2(_texHeight, _texWidth)
			: new Vector2(_texWidth, _texHeight);
	}

	private static (int width, int height) ComputeRTDimensions(Renderer renderer, float canvasRotation)
	{
		float meshAspect = ComputeMeshAspect(renderer);
		bool perpendicular = Mathf.Abs(((canvasRotation % 180f) + 180f) % 180f - 90f) < 0.5f;
		float texAspect = perpendicular ? 1f / meshAspect : meshAspect;

		if (texAspect >= 1f)
		{
			return (LongSide, Mathf.Max(MinSide, Mathf.RoundToInt(LongSide / texAspect)));
		}

		return (Mathf.Max(MinSide, Mathf.RoundToInt(LongSide * texAspect)), LongSide);
	}

	private static float ComputeMeshAspect(Renderer renderer)
	{
		if (renderer == null)
		{
			return 1.6f;
		}

		Mesh mesh = null;
		MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
		if (meshFilter != null)
		{
			mesh = meshFilter.sharedMesh;
		}
		else if (renderer is SkinnedMeshRenderer skinnedMeshRenderer)
		{
			mesh = skinnedMeshRenderer.sharedMesh;
		}

		if (mesh == null)
		{
			return 1.6f;
		}

		Vector3 size = mesh.bounds.size;
		float[] dimensions = { size.x, size.y, size.z };
		Array.Sort(dimensions);
		return dimensions[1] < 0.0001f ? 1.6f : Mathf.Max(1f, dimensions[2] / dimensions[1]);
	}

	private static string FormatDuration(int seconds)
	{
		if (seconds <= 0)
		{
			return "UNKNOWN";
		}

		if (seconds < 60)
		{
			return $"{seconds} SEC";
		}

		int minutes = seconds / 60;
		int remainingSeconds = seconds % 60;
		return remainingSeconds == 0
			? $"{minutes} MIN"
			: $"{minutes}:{remainingSeconds:D2}";
	}

	private static string GetTacticalServicesAssetPath(ESupportType supportType)
	{
		return supportType switch
		{
			ESupportType.Extract => "landscape_1024x512/TG_01_TacticalServices_Extraction_Selected.png",
			ESupportType.PriorityExfil => "landscape_1024x512/TG_01_TacticalServices_Extraction_Selected.png",
			ESupportType.Strafe => "landscape_1024x512/TG_01_TacticalServices_Fire_Selected.png",
			ESupportType.DoubleStrafe => "landscape_1024x512/TG_01_TacticalServices_Fire_Selected.png",
			ESupportType.Uav => "landscape_1024x512/TG_01_TacticalServices_UAV_Selected.png",
			ESupportType.FocusedSweep => "landscape_1024x512/TG_01_TacticalServices_UAV_Selected.png",
			_ => "landscape_1024x512/TG_01_TacticalServices_UAV_Selected.png"
		};
	}

	private static string GetCategoryAssetPath(ESupportType supportType)
	{
		return supportType switch
		{
			ESupportType.Extract => "landscape_1024x512/TG_02_ExtractionCategory_1_Selected.png",
			ESupportType.PriorityExfil => "landscape_1024x512/TG_02_ExtractionCategory_2_Selected.png",
			ESupportType.Strafe => "landscape_1024x512/TG_02_FireSupportCategory_1_Selected.png",
			ESupportType.DoubleStrafe => "landscape_1024x512/TG_02_FireSupportCategory_2_Selected.png",
			ESupportType.Uav => "landscape_1024x512/TG_02_ReconCategory_1_Selected.png",
			ESupportType.FocusedSweep => "landscape_1024x512/TG_02_ReconCategory_2_Selected.png",
			_ => "landscape_1024x512/TG_02_ReconCategory_1_Selected.png"
		};
	}

	private static string GetReviewAssetPath(ESupportType supportType)
	{
		return supportType switch
		{
			ESupportType.Extract => "landscape_1024x512/TG_03_Extraction_Review_Rotate.png",
			ESupportType.PriorityExfil => "landscape_1024x512/TG_03_PriorityExfil_Review_Rotate.png",
			ESupportType.Strafe => "landscape_1024x512/TG_03_A10_Review_Rotate.png",
			ESupportType.DoubleStrafe => "landscape_1024x512/TG_03_DoublePass_Review_Rotate.png",
			ESupportType.Uav => "landscape_1024x512/TG_03_UAV_Review_Rotate.png",
			ESupportType.FocusedSweep => "landscape_1024x512/TG_03_FocusedSweep_Review_Rotate.png",
			_ => "landscape_1024x512/TG_03_UAV_Review_Rotate.png"
		};
	}

	private static string GetConfirmSwipeAssetPath(ESupportType supportType)
	{
		return supportType switch
		{
			ESupportType.Extract => "portrait_512x1024/TG_04_Extraction_ConfirmSwipe.png",
			ESupportType.PriorityExfil => "portrait_512x1024/TG_04_PriorityExfil_ConfirmSwipe.png",
			ESupportType.Strafe => "portrait_512x1024/TG_04_A10_ConfirmSwipe.png",
			ESupportType.DoubleStrafe => "portrait_512x1024/TG_04_DoublePass_ConfirmSwipe.png",
			ESupportType.Uav => "portrait_512x1024/TG_04_UAV_ConfirmSwipe.png",
			ESupportType.FocusedSweep => "portrait_512x1024/TG_04_FocusedSweep_ConfirmSwipe.png",
			_ => "portrait_512x1024/TG_04_UAV_ConfirmSwipe.png"
		};
	}

	private static string GetAuthorizedAssetPath(ESupportType supportType)
	{
		return supportType switch
		{
			ESupportType.Extract => "portrait_512x1024/TG_06_Extraction_Authorized.png",
			ESupportType.PriorityExfil => "portrait_512x1024/TG_06_PriorityExfil_Authorized.png",
			ESupportType.Strafe => "portrait_512x1024/TG_06_A10_Authorized.png",
			ESupportType.DoubleStrafe => "portrait_512x1024/TG_06_DoublePass_Authorized.png",
			ESupportType.Uav => "portrait_512x1024/TG_06_UAV_Authorized.png",
			ESupportType.FocusedSweep => "portrait_512x1024/TG_06_FocusedSweep_Authorized.png",
			_ => "portrait_512x1024/TG_06_UAV_Authorized.png"
		};
	}

	private static string GetCategoryName(ESupportType supportType)
	{
		return supportType switch
		{
			ESupportType.Extract => "EXTRACTION",
			ESupportType.PriorityExfil => "EXTRACTION",
			ESupportType.Strafe => "FIRE SUPPORT",
			ESupportType.DoubleStrafe => "FIRE SUPPORT",
			ESupportType.Uav => "RECON",
			ESupportType.FocusedSweep => "RECON",
			_ => "SERVICES"
		};
	}

	private static string GetServiceTitle(ESupportType supportType)
	{
		return supportType switch
		{
			ESupportType.Extract => "UH-60 EXTRACTION",
			ESupportType.PriorityExfil => "PRIORITY EXFIL",
			ESupportType.Strafe => "A-10 STRAFE",
			ESupportType.DoubleStrafe => "A-10 DOUBLE PASS",
			ESupportType.Uav => "UAV RECON",
			ESupportType.FocusedSweep => "FOCUSED SWEEP",
			_ => "FIRE SUPPORT"
		};
	}

	private static string GetServiceDescription(ESupportType supportType)
	{
		return supportType switch
		{
			ESupportType.Extract => "Authorize helicopter pickup. Target from rangefinder.",
			ESupportType.PriorityExfil => "Authorize expedited pickup. Target from rangefinder.",
			ESupportType.Strafe => "Authorize autocannon pass. Target from rangefinder.",
			ESupportType.DoubleStrafe => "Authorize two autocannon passes. Target from rangefinder.",
			ESupportType.Uav => "Authorize local recon scan from YY.",
			ESupportType.FocusedSweep => "Authorize narrow fast-refresh recon from YY.",
			_ => "Authorize tactical support."
		};
	}

	private string GetServiceDuration()
	{
		return _context.SupportType switch
		{
			ESupportType.Extract => "PICKUP",
			ESupportType.PriorityExfil => "EXPEDITED",
			ESupportType.Strafe => "ONE PASS",
			ESupportType.DoubleStrafe => "TWO PASSES",
			ESupportType.FocusedSweep => FormatDuration(_context.DurationSeconds),
			ESupportType.Uav => FormatDuration(_context.DurationSeconds),
			_ => "READY"
		};
	}

	private static string GetDeploymentMode(ESupportType supportType)
	{
		return supportType switch
		{
			ESupportType.Extract => "RANGEFINDER",
			ESupportType.PriorityExfil => "RANGEFINDER",
			ESupportType.Strafe => "RANGEFINDER",
			ESupportType.DoubleStrafe => "RANGEFINDER",
			ESupportType.FocusedSweep => "YY MENU",
			ESupportType.Uav => "YY MENU",
			_ => "YY MENU"
		};
	}

	private static string FormatRoubles(int amount)
	{
		if (amount < 0)
		{
			return "SYNC";
		}

		return $"{amount:N0} \u20BD";
	}

	private static Color Teal()
	{
		return new Color(0.32f, 0.86f, 0.78f, 1f);
	}

	private static Color Amber()
	{
		return new Color(0.95f, 0.68f, 0.34f, 1f);
	}

	private static Color Muted()
	{
		return new Color(0.66f, 0.72f, 0.69f, 1f);
	}

	private sealed class DynamicTextField
	{
		public string Name { get; private set; }
		public Rect Rect { get; private set; }
		public string Alignment { get; private set; }
		public int FontSize { get; private set; }
		public Color Color { get; private set; }
		public string Format { get; private set; }
		public string Source { get; private set; }

		public static DynamicTextField FromJson(string name, JObject json)
		{
			if (json == null)
			{
				return null;
			}

			JArray rectArray = json["rect_px"] as JArray;
			if (rectArray == null || rectArray.Count < 4)
			{
				return null;
			}

			float left = rectArray[0].Value<float>();
			float top = rectArray[1].Value<float>();
			float right = rectArray[2].Value<float>();
			float bottom = rectArray[3].Value<float>();

			string colorHex = json.Value<string>("color") ?? "#FFFFFF";
			if (!ColorUtility.TryParseHtmlString(colorHex, out Color color))
			{
				color = Color.white;
			}

			return new DynamicTextField
			{
				Name = name,
				Rect = new Rect(left, top, Mathf.Max(1f, right - left), Mathf.Max(1f, bottom - top)),
				Alignment = json.Value<string>("align") ?? "MiddleCenter",
				FontSize = Mathf.Max(8, json.Value<int?>("font_size") ?? 18),
				Color = color,
				Format = json.Value<string>("format") ?? "value",
				Source = json.Value<string>("source") ?? string.Empty
			};
		}
	}

	private static Sprite WhiteSprite
	{
		get
		{
			if (s_whiteSprite == null)
			{
				Texture2D texture = new(1, 1, TextureFormat.RGBA32, false)
				{
					hideFlags = HideFlags.HideAndDontSave,
					filterMode = FilterMode.Bilinear,
					wrapMode = TextureWrapMode.Clamp
				};
				texture.SetPixel(0, 0, Color.white);
				texture.Apply();

				s_whiteSprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
				s_whiteSprite.hideFlags = HideFlags.HideAndDontSave;
			}

			return s_whiteSprite;
		}
	}
}
