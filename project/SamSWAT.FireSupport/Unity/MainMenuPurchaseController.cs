using Comfort.Common;
using Cysharp.Threading.Tasks;
using EFT;
using EFT.UI;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

/// <summary>
/// Main-menu-only authorization storefront. It owns no player, hands controller,
/// inventory item, or raid runtime object.
/// </summary>
public sealed class MainMenuPurchaseController : MonoBehaviour
{
	private const string ButtonName = "TSC_MainMenuUplinkButton";
	private const string PageName = "TSC_MainMenuPurchasePage";
	private const int LayoutScanPasses = 12;
	private const float LayoutScanIntervalSeconds = 0.5f;
	private const float ButtonSlotHeight = 60f;
	private const float SidebarWidth = 220f;
	private const float TopbarHeight = 60f;
	private const float BottombarHeight = 30f;

	// Mirrors the dashboard tokens in Server/CopyToOutput/web/styles.css.
	private static readonly Color s_background = new Color32(3, 6, 7, 255);
	private static readonly Color s_background2 = new Color32(7, 16, 17, 255);
	private static readonly Color s_panel = new Color32(12, 17, 17, 245);
	private static readonly Color s_panel2 = new Color32(17, 23, 22, 235);
	private static readonly Color s_panel3 = new Color32(21, 23, 20, 245);
	private static readonly Color s_line = new Color32(220, 216, 200, 41);
	private static readonly Color s_lineStrong = new Color32(232, 185, 103, 117);
	private static readonly Color s_text = new Color32(220, 216, 200, 255);
	private static readonly Color s_muted = new Color32(150, 146, 132, 255);
	private static readonly Color s_soft = new Color32(104, 107, 97, 255);
	private static readonly Color s_amber = new Color32(205, 158, 84, 255);
	private static readonly Color s_amberHigh = new Color32(232, 185, 103, 255);
	private static readonly Color s_green = new Color32(113, 157, 70, 255);
	private static readonly Color s_greenHigh = new Color32(145, 200, 90, 255);
	private static readonly Color s_red = new Color32(198, 72, 61, 255);

	private static readonly ServiceDescriptor[] s_services =
	[
		new(ESupportType.Strafe, "A10", "A-10 STRAFE", "CAS", "Autocannon strike"),
		new(ESupportType.DoubleStrafe, "DoublePass", "A-10 DOUBLE PASS", "CAS+", "Second A-10 pass"),
		new(ESupportType.Extract, "Extraction", "UH-60 EXTRACTION", "EXT", "Combat pickup"),
		new(ESupportType.PriorityExfil, "PriorityExfil", "UH-60 PRIORITY EXFIL", "EXT+", "Expedited pickup"),
		new(ESupportType.Uav, "Uav", "UAV RECON", "REC", "Wide-area scan"),
		new(ESupportType.FocusedSweep, "FocusedSweep", "UAV FOCUSED SWEEP", "REC+", "Tighter scan radius")
	];

	private static readonly string[] s_stackButtonNames =
	[
		"PlayButton",
		"CharacterButton",
		"RecordsButton",
		"TradeButton",
		"HideoutButton",
		"ExitButtonGroup",
		"SSS_OverclockButton"
	];

	private static MainMenuPurchaseController s_instance;
	private static string s_boundSessionKey;
	private static Font s_sansFont;
	private static Font s_monoFont;

	private readonly Dictionary<ESupportType, RowView> _rows = new();
	private MenuScreen _menuScreen;
	private DefaultUIButton _menuButton;
	private GameObject _pageRoot;
	private Text _statusText;
	private Text _balanceText;
	private Text _syncText;
	private Text _routeStatusText;
	private Text _revisionPillText;
	private Text _paymentPillText;
	private Image _statusPanelImage;
	private Outline _statusPanelOutline;
	private Button _refreshButton;
	private RaidOpsFireSupportServerConfig _snapshot;
	private CancellationTokenSource _refreshCts;
	private string _profileId = string.Empty;
	private string _sessionKey = string.Empty;
	private string _ambiguousRequestId = string.Empty;
	private ESupportType _ambiguousType = ESupportType.None;
	private int _generation;
	private int _layoutScansRemaining;
	private float _nextLayoutScanAt;
	private bool _ready;
	private bool _refreshPending;
	private bool _purchasePending;
	private ESupportType _pendingType = ESupportType.None;
	private bool _destroyed;

	public static void Attach(MenuScreen menuScreen, Profile profile)
	{
		if (menuScreen == null)
		{
			return;
		}

		MainMenuPurchaseController controller =
			menuScreen.GetComponent<MainMenuPurchaseController>() ??
			menuScreen.gameObject.AddComponent<MainMenuPurchaseController>();
		controller.Bind(menuScreen, profile);
	}

	public static void CloseForRaidStart()
	{
		s_instance?.ClosePage();
	}

	private void Bind(MenuScreen menuScreen, Profile profile)
	{
		if (s_instance != null && s_instance != this)
		{
			s_instance.ClosePage();
		}

		s_instance = this;
		_menuScreen = menuScreen;
		_profileId = profile?.Id?.Trim() ?? string.Empty;
		string authenticatedKey = FireSupportServerConfigClient.GetAuthenticatedSessionKey();
		string nextBoundKey = $"{authenticatedKey}|menu-profile:{_profileId}";
		if (!string.Equals(s_boundSessionKey, nextBoundKey, StringComparison.Ordinal))
		{
			s_boundSessionKey = nextBoundKey;
			ResetPageState();
			FireSupportServerConfigClient.ClearPreRaidSessionState();
		}

		_sessionKey = authenticatedKey;
		EnsureMenuButton();
		_layoutScansRemaining = LayoutScanPasses;
		_nextLayoutScanAt = Time.unscaledTime + LayoutScanIntervalSeconds;
	}

	private void Update()
	{
		if (_menuButton != null)
		{
			_menuButton.gameObject.SetActive(PluginSettings.Enabled?.Value == true);
		}

		if (_layoutScansRemaining <= 0 || Time.unscaledTime < _nextLayoutScanAt)
		{
			return;
		}

		_layoutScansRemaining--;
		_nextLayoutScanAt = Time.unscaledTime + LayoutScanIntervalSeconds;
		EnsureMenuButton();
	}

	private void EnsureMenuButton()
	{
		if (_menuScreen == null)
		{
			return;
		}

		DefaultUIButton template = FindCharacterButton(_menuScreen);
		if (template == null || template.transform.parent == null)
		{
			FireSupportPlugin.LogSource.LogWarning(
				"TSC main-menu purchase button could not find EFT's CharacterButton template.");
			return;
		}

		Transform parent = template.transform.parent;
		if (_menuButton != null && _menuButton.transform.parent != parent)
		{
			DefaultUIButton scopedExisting =
				parent.Find(ButtonName)?.GetComponent<DefaultUIButton>();
			if (scopedExisting != null && scopedExisting != _menuButton)
			{
				Destroy(_menuButton.gameObject);
				_menuButton = scopedExisting;
			}
			else
			{
				_menuButton.transform.SetParent(parent, false);
			}
		}
		if (_menuButton == null)
		{
			Transform existing = parent.Find(ButtonName);
			_menuButton = existing?.GetComponent<DefaultUIButton>();
			if (_menuButton == null)
			{
				GameObject clone = Instantiate(template.gameObject, parent);
				clone.name = ButtonName;
				_menuButton = clone.GetComponent<DefaultUIButton>();
			}
		}

		if (_menuButton == null)
		{
			return;
		}

		_menuButton.gameObject.SetActive(PluginSettings.Enabled?.Value == true);
		_menuButton.SetRawText("TSC UPLINK", ResolveFontSize(template));
		_menuButton.Interactable = true;
		_menuButton.OnClick.RemoveAllListeners();
		_menuButton.OnClick.AddListener(OpenPage);
		_menuButton.OnMouseOver?.RemoveAllListeners();
		_menuButton.OnMouseOut?.RemoveAllListeners();
		EnsureUsable(_menuButton.gameObject);
		PositionMenuButton(template);
	}

	private void PositionMenuButton(DefaultUIButton template)
	{
		RectTransform target = _menuButton?.GetComponent<RectTransform>();
		RectTransform templateRect = template?.GetComponent<RectTransform>();
		Transform parent = templateRect?.parent;
		if (target == null || templateRect == null || parent == null)
		{
			return;
		}

		RectTransform bottom = null;
		float minimumY = float.MaxValue;
		foreach (string name in s_stackButtonNames)
		{
			RectTransform candidate = FindSiblingRect(parent, name, target);
			if (candidate == null || !candidate.gameObject.activeSelf)
			{
				continue;
			}

			if (candidate.anchoredPosition.y < minimumY)
			{
				minimumY = candidate.anchoredPosition.y;
				bottom = candidate;
			}
		}

		bottom ??= templateRect;
		CopyRect(target, bottom);
		target.anchoredPosition =
			new Vector2(bottom.anchoredPosition.x, bottom.anchoredPosition.y - ButtonSlotHeight);
		target.SetSiblingIndex(Mathf.Min(bottom.GetSiblingIndex() + 1, parent.childCount - 1));
	}

	private void OpenPage()
	{
		if (PluginSettings.Enabled?.Value != true)
		{
			return;
		}

		if (Singleton<GameWorld>.Instance != null)
		{
			FireSupportPlugin.LogSource.LogWarning(
				"TSC pre-raid purchase page refused to open while GameWorld was active.");
			return;
		}

		BuildPage();
		if (_pageRoot == null)
		{
			return;
		}

		_pageRoot.SetActive(true);
		_pageRoot.transform.SetAsLastSibling();
		StartRefresh();
	}

	private void BuildPage()
	{
		if (_pageRoot != null)
		{
			return;
		}

		Canvas parentCanvas = _menuScreen?.GetComponentInParent<Canvas>();
		if (parentCanvas == null)
		{
			FireSupportPlugin.LogSource.LogWarning(
				"TSC main-menu purchase page could not find the EFT menu Canvas.");
			return;
		}

		_pageRoot = new GameObject(
			PageName,
			typeof(RectTransform),
			typeof(Canvas),
			typeof(GraphicRaycaster),
			typeof(CanvasGroup),
			typeof(Image));
		_pageRoot.transform.SetParent(parentCanvas.transform, false);
		Stretch(_pageRoot.GetComponent<RectTransform>());
		Canvas pageCanvas = _pageRoot.GetComponent<Canvas>();
		pageCanvas.overrideSorting = true;
		pageCanvas.sortingOrder = parentCanvas.sortingOrder + 500;
		Image pageBackground = _pageRoot.GetComponent<Image>();
		pageBackground.color = s_background;
		pageBackground.raycastTarget = true;
		CreateGridBackdrop(_pageRoot.transform);
		CreateSidebar(_pageRoot.transform);
		CreateTopbar(_pageRoot.transform);
		CreateBottomBar(_pageRoot.transform);

		GameObject workspace = new("Workspace", typeof(RectTransform));
		workspace.transform.SetParent(_pageRoot.transform, false);
		SetStretchRect(
			workspace.GetComponent<RectTransform>(),
			Vector2.zero,
			Vector2.one,
			new Vector2(SidebarWidth + 28f, BottombarHeight + 48f),
			new Vector2(-28f, -TopbarHeight - 22f));

		CreateMetricCard(
			workspace.transform,
			0,
			"AUTHORITY",
			"AUTHENTICATED PMC",
			out _);
		CreateMetricCard(
			workspace.transform,
			1,
			"WALLET",
			"₽--",
			out _balanceText);
		CreateMetricCard(
			workspace.transform,
			2,
			"SYNC",
			"LEDGER --",
			out _syncText);

		GameObject section = CreateBorderedPanel(workspace.transform, "AuthorizationStore", s_panel, s_line);
		SetStretchRect(
			section.GetComponent<RectTransform>(),
			Vector2.zero,
			Vector2.one,
			Vector2.zero,
			new Vector2(0f, -100f));

		GameObject sectionHeading = CreatePanel(section.transform, "SectionHeading", s_panel3);
		SetStretchRect(
			sectionHeading.GetComponent<RectTransform>(),
			new Vector2(0f, 1f),
			Vector2.one,
			new Vector2(0f, -84f),
			Vector2.zero);
		CreatePanelLine(
			sectionHeading.transform,
			"HeadingAccent",
			s_amber,
			new Vector2(0f, 0f),
			new Vector2(0f, 1f),
			new Vector2(0f, 0f),
			new Vector2(3f, 0f));
		CreatePanelLine(
			sectionHeading.transform,
			"HeadingRule",
			new Color(s_amberHigh.r, s_amberHigh.g, s_amberHigh.b, 0.14f),
			new Vector2(0f, 0f),
			new Vector2(1f, 0f),
			Vector2.zero,
			new Vector2(0f, 1f));
		CreateText(sectionHeading.transform, "Kicker", "PURCHASE PERSISTENCE", 10, FontStyle.Bold,
			s_amberHigh, TextAnchor.MiddleLeft, new Vector2(0f, 1f),
			new Vector2(360f, 22f), new Vector2(200f, -17f), mono: true);
		CreateText(sectionHeading.transform, "Title", "AUTHORIZATION STORE", 23, FontStyle.Bold,
			s_text, TextAnchor.MiddleLeft, new Vector2(0f, 1f),
			new Vector2(500f, 32f), new Vector2(270f, -42f));
		Text sectionIntro = CreateText(sectionHeading.transform, "Intro",
			"Server-authoritative pre-raid credits. Purchases debit the authenticated PMC stash.",
			13, FontStyle.Normal, s_muted, TextAnchor.MiddleLeft, new Vector2(0f, 1f),
			new Vector2(100f, 24f), new Vector2(68f, -68f));
		SetStretchRect(
			sectionIntro.rectTransform,
			new Vector2(0f, 0f),
			new Vector2(0.68f, 0f),
			new Vector2(18f, 5f),
			new Vector2(-8f, 29f));
		Text catalogMeta = CreateText(sectionHeading.transform, "CatalogMeta", "6 SERVICES // PERSISTENT LEDGER",
			10, FontStyle.Bold, s_muted, TextAnchor.MiddleRight, new Vector2(1f, 0.5f),
			new Vector2(100f, 24f), new Vector2(-68f, 0f), mono: true);
		SetStretchRect(
			catalogMeta.rectTransform,
			new Vector2(0.68f, 0f),
			new Vector2(1f, 0f),
			new Vector2(8f, 5f),
			new Vector2(-18f, 29f));

		GameObject serviceDeck = new("ServiceDeck", typeof(RectTransform));
		serviceDeck.transform.SetParent(section.transform, false);
		SetStretchRect(
			serviceDeck.GetComponent<RectTransform>(),
			Vector2.zero,
			Vector2.one,
			new Vector2(18f, 18f),
			new Vector2(-18f, -98f));

		_rows.Clear();
		for (int index = 0; index < s_services.Length; index++)
		{
			ServiceDescriptor service = s_services[index];
			int column = index % 3;
			int deckRow = index / 3;
			float columnMin = column / 3f;
			float columnMax = (column + 1) / 3f;
			float rowMax = 1f - deckRow / 2f;
			float rowMin = 1f - (deckRow + 1) / 2f;
			Vector2 offsetMin = new(column == 0 ? 0f : 7f, deckRow == 1 ? 0f : 7f);
			Vector2 offsetMax = new(column == 2 ? 0f : -7f, deckRow == 0 ? 0f : -7f);
			_rows[service.Type] = CreateServiceCard(
				serviceDeck.transform,
				service,
				new Vector2(columnMin, rowMin),
				new Vector2(columnMax, rowMax),
				offsetMin,
				offsetMax);
		}

		GameObject statusPanel = CreateBorderedPanel(
			_pageRoot.transform,
			"StatusToast",
			new Color(0.035f, 0.063f, 0.047f, 0.97f),
			new Color(s_green.r, s_green.g, s_green.b, 0.55f));
		SetRect(
			statusPanel.GetComponent<RectTransform>(),
			new Vector2(1f, 0f),
			new Vector2(620f, 40f),
			new Vector2(-330f, BottombarHeight + 20f));
		_statusPanelImage = statusPanel.GetComponent<Image>();
		_statusPanelOutline = statusPanel.GetComponent<Outline>();
		_statusText = CreateText(statusPanel.transform, "Status", "WAITING FOR AUTHENTICATED SNAPSHOT", 11,
			FontStyle.Bold, s_greenHigh, TextAnchor.MiddleLeft, new Vector2(0.5f, 0.5f),
			new Vector2(590f, 28f), Vector2.zero, mono: true);
		_statusText.resizeTextForBestFit = true;
		_statusText.resizeTextMinSize = 9;
		_statusText.resizeTextMaxSize = 11;

		_pageRoot.SetActive(false);
		Redraw();
	}

	private void StartRefresh()
	{
		StartRefresh(afterMutation: false);
	}

	private void StartRefresh(bool afterMutation)
	{
		if (_refreshPending || _purchasePending)
		{
			return;
		}

		string authenticatedSessionKey =
			FireSupportServerConfigClient.GetAuthenticatedSessionKey();
		if (string.IsNullOrWhiteSpace(authenticatedSessionKey) ||
		    string.IsNullOrWhiteSpace(_profileId) ||
		    !FireSupportServerConfigClient.IsAuthenticatedProfile(_profileId))
		{
			FailClosedForSessionChange(
				authenticatedSessionKey,
				"Authenticated PMC session is not available. Reopen TSC UPLINK from the current main menu.");
			return;
		}
		if (!string.IsNullOrWhiteSpace(_sessionKey) &&
		    !string.Equals(_sessionKey, authenticatedSessionKey, StringComparison.Ordinal))
		{
			FailClosedForSessionChange(
				authenticatedSessionKey,
				"Authenticated PMC session changed. Reopen TSC UPLINK from the current main menu.");
			return;
		}

		_sessionKey = authenticatedSessionKey;

		_refreshCts?.Cancel();
		_refreshCts?.Dispose();
		_refreshCts = new CancellationTokenSource();
		// Keep the correlated purchase response visible while the required
		// post-mutation verification GET is in flight. A normal/manual refresh
		// still clears stale state until its authenticated snapshot arrives.
		if (!afterMutation)
		{
			_snapshot = null;
		}
		_ready = false;
		_refreshPending = true;
		int generation = ++_generation;
		SetStatus("Synchronizing authoritative stash and authorization ledger...", true);
		Redraw();
		RefreshAsync(generation, _sessionKey, afterMutation, _refreshCts.Token).Forget();
	}

	private async UniTaskVoid RefreshAsync(
		int generation,
		string expectedSessionKey,
		bool afterMutation,
		CancellationToken cancellationToken)
	{
		try
		{
			RaidOpsFireSupportServerConfig snapshot =
				await FireSupportServerConfigClient.FetchPreRaidSnapshotOnceAsync(
					expectedSessionKey,
					cancellationToken);
			if (_destroyed || generation != _generation)
			{
				return;
			}

			if (!ValidateSnapshot(snapshot, out string reason))
			{
				_snapshot = snapshot;
				_ready = false;
				SetStatus(reason, false);
				return;
			}

			_snapshot = snapshot;
			_ready = true;
			bool recoveredPreparedPurchase = AdoptPreparedPurchase(snapshot);
			if (!string.IsNullOrWhiteSpace(_ambiguousRequestId))
			{
				SetStatus(
					recoveredPreparedPurchase
						? "INTERRUPTED PURCHASE RECOVERED // SELECT RETRY TO FINISH WITHOUT A SECOND CHARGE"
						: "PURCHASE RECOVERY PENDING // SELECT RETRY WITH THE ORIGINAL REQUEST ID",
					false);
			}
			else
			{
				SetStatus(
					afterMutation
						? $"PURCHASE CONFIRMED // SERVER REVISION {Math.Max(0, snapshot.Revision)}"
						: $"CONNECTED // SERVER REVISION {Math.Max(0, snapshot.Revision)}",
					true);
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception ex)
		{
			if (!_destroyed && generation == _generation)
			{
				string authenticatedSessionKey =
					FireSupportServerConfigClient.GetAuthenticatedSessionKey();
				if (!string.Equals(
					    expectedSessionKey,
					    authenticatedSessionKey,
					    StringComparison.Ordinal) ||
				    !FireSupportServerConfigClient.IsAuthenticatedProfile(_profileId))
				{
					FailClosedForSessionChange(
						authenticatedSessionKey,
						"Authenticated PMC session changed during synchronization. Reopen TSC UPLINK.");
					return;
				}

				_ready = false;
				SetStatus($"Synchronization failed: {ex.Message}", false);
			}
		}
		finally
		{
			if (!_destroyed && generation == _generation)
			{
				_refreshPending = false;
				Redraw();
			}
		}
	}

	private void BeginPurchase(ESupportType supportType)
	{
		if (!_ready || _snapshot == null || _refreshPending || _purchasePending)
		{
			return;
		}
		string authenticatedSessionKey =
			FireSupportServerConfigClient.GetAuthenticatedSessionKey();
		if (string.IsNullOrWhiteSpace(authenticatedSessionKey) ||
		    !string.Equals(_sessionKey, authenticatedSessionKey, StringComparison.Ordinal) ||
		    !FireSupportServerConfigClient.IsAuthenticatedProfile(_profileId))
		{
			FailClosedForSessionChange(
				authenticatedSessionKey,
				"Authenticated PMC session changed. Reopen TSC UPLINK from the current main menu.");
			return;
		}

		ServiceDescriptor descriptor = GetDescriptor(supportType);
		bool hasAmbiguousPurchase = !string.IsNullOrWhiteSpace(_ambiguousRequestId);
		bool retryAmbiguousPurchase =
			hasAmbiguousPurchase && _ambiguousType == supportType;
		if ((hasAmbiguousPurchase && !retryAmbiguousPurchase) ||
		    (!retryAmbiguousPurchase &&
		     (!GetEnabled(_snapshot, descriptor.ConfigKey) ||
		      GetPrice(_snapshot, descriptor.ConfigKey) < 0 ||
		      GetOwned(_snapshot, descriptor.ConfigKey) >= GetMaximum(_snapshot))))
		{
			return;
		}

		string requestId = retryAmbiguousPurchase
			? _ambiguousRequestId
			: Guid.NewGuid().ToString("N");
		_purchasePending = true;
		_pendingType = supportType;
		SetStatus(
			retryAmbiguousPurchase
				? $"Retrying {descriptor.DisplayName} with the original request ID..."
				: $"Submitting {descriptor.DisplayName} purchase...",
			true);
		Redraw();
		PurchaseAsync(supportType, requestId, _sessionKey, _generation).Forget();
	}

	private async UniTaskVoid PurchaseAsync(
		ESupportType supportType,
		string requestId,
		string expectedSessionKey,
		int generation)
	{
		bool refreshAfterMutation = false;
		try
		{
			FireSupportPurchaseResponse response =
				await FireSupportPayment.PurchasePersistentAuthorizationAsync(
					supportType,
					requestId,
					expectedSessionKey,
					_profileId);
			if (_destroyed || generation != _generation)
			{
				return;
			}
			string authenticatedSessionKey =
				FireSupportServerConfigClient.GetAuthenticatedSessionKey();
			if (!string.Equals(
				    expectedSessionKey,
				    authenticatedSessionKey,
				    StringComparison.Ordinal) ||
			    !FireSupportServerConfigClient.IsAuthenticatedProfile(_profileId))
			{
				FailClosedForSessionChange(
					authenticatedSessionKey,
					"Authenticated PMC session changed during purchase. Reopen TSC UPLINK.");
				return;
			}

			if (response == null ||
			    !string.Equals(response.RequestId, requestId, StringComparison.Ordinal))
			{
				_ambiguousRequestId = requestId;
				_ambiguousType = supportType;
				_ready = false;
				SetStatus(
					"Purchase response could not be correlated. REFRESH, then RETRY with the original request ID.",
					false);
				return;
			}

			ApplyResponseToView(supportType, response);
			if (response?.Ok == true)
			{
				ClearAmbiguousPurchase(requestId, supportType);
				SetStatus($"{GetDescriptor(supportType).DisplayName} authorization purchased.", true);
				refreshAfterMutation = true;
			}
			else
			{
				string reason = response?.Reason ?? "InvalidServerResponse";
				if (IsAmbiguousPurchaseFailure(reason))
				{
					// The server may have committed before the response was lost.
					// Retain the click's ID and require an authoritative refresh
					// before a new ID can be generated.
					_ambiguousRequestId = requestId;
					_ambiguousType = supportType;
					_ready = false;
					SetStatus(
						"Purchase outcome is uncertain. REFRESH, then RETRY with the original request ID.",
						false);
				}
				else
				{
					ClearAmbiguousPurchase(requestId, supportType);
					SetStatus(FormatPurchaseFailure(reason), false);
				}
			}
		}
		catch (Exception ex)
		{
			if (!_destroyed && generation == _generation)
			{
				_ambiguousRequestId = requestId;
				_ambiguousType = supportType;
				_ready = false;
				SetStatus(
					$"Purchase outcome is uncertain ({ex.Message}). REFRESH, then RETRY with the original request ID.",
					false);
			}
		}
		finally
		{
			if (!_destroyed && generation == _generation)
			{
				_purchasePending = false;
				_pendingType = ESupportType.None;
				Redraw();
				if (refreshAfterMutation && _pageRoot?.activeSelf == true)
				{
					StartRefresh(afterMutation: true);
				}
			}
		}
	}

	private void ApplyResponseToView(ESupportType supportType, FireSupportPurchaseResponse response)
	{
		if (_snapshot == null || response == null)
		{
			return;
		}

		if (response.NewBalance >= 0)
		{
			_snapshot.StashRoubleBalance = response.NewBalance;
		}
		if (response.ServerRevision > 0)
		{
			_snapshot.Revision = response.ServerRevision;
		}
		if (response.AuthorizationsIncluded && response.Authorizations != null)
		{
			_snapshot.Authorizations =
				new Dictionary<string, int>(response.Authorizations, StringComparer.OrdinalIgnoreCase);
		}
		if (response.Cost >= 0 && _snapshot.Prices != null)
		{
			_snapshot.Prices[GetDescriptor(supportType).ConfigKey] = response.Cost;
		}
	}

	private void Redraw()
	{
		if (_balanceText == null)
		{
			return;
		}

		_balanceText.text = _snapshot?.StashRoubleBalance is int balance
			? $"₽{balance:N0}"
			: "₽--";
		if (_syncText != null)
		{
			_syncText.text = _snapshot != null
				? $"LEDGER READY // R{Math.Max(0, _snapshot.Revision)}"
				: "LEDGER --";
		}
		if (_revisionPillText != null)
		{
			_revisionPillText.text = _snapshot != null
				? $"REVISION {Math.Max(0, _snapshot.Revision)}"
				: "REVISION --";
		}
		if (_paymentPillText != null)
		{
			_paymentPillText.text = string.IsNullOrWhiteSpace(_snapshot?.PaymentSource)
				? "PAYMENT --"
				: _snapshot.PaymentSource.ToUpperInvariant();
		}
		if (_refreshButton != null)
		{
			_refreshButton.interactable = !_refreshPending && !_purchasePending;
		}

		int maximum = GetMaximum(_snapshot);
		foreach (ServiceDescriptor service in s_services)
		{
			if (!_rows.TryGetValue(service.Type, out RowView row))
			{
				continue;
			}

			bool hasSnapshot = _snapshot != null;
			bool enabled = hasSnapshot && GetEnabled(_snapshot, service.ConfigKey);
			int owned = hasSnapshot ? GetOwned(_snapshot, service.ConfigKey) : 0;
			int price = hasSnapshot ? GetPrice(_snapshot, service.ConfigKey) : -1;
			bool atLimit = hasSnapshot && maximum > 0 && owned >= maximum;
			bool pending = _purchasePending && _pendingType == service.Type;
			bool hasAmbiguousPurchase = !string.IsNullOrWhiteSpace(_ambiguousRequestId);
			bool retryAmbiguousPurchase =
				hasAmbiguousPurchase && _ambiguousType == service.Type;

			row.State.text = retryAmbiguousPurchase
				? "OUTCOME UNKNOWN"
				: !hasSnapshot
					? "--"
					: !enabled
						? "DISABLED"
						: atLimit
							? "AT LIMIT"
							: "AVAILABLE";
			row.State.color = retryAmbiguousPurchase
				? s_amberHigh
				: !enabled
					? s_red
					: atLimit
						? s_amberHigh
						: s_greenHigh;
			row.Price.text = price >= 0 ? $"₽{price:N0}" : "--";
			row.Price.color = price >= 0 ? s_amberHigh : s_muted;
			row.Owned.text = hasSnapshot ? $"{owned} / {maximum}" : "-- / --";
			row.Owned.color = atLimit ? s_amberHigh : hasSnapshot ? s_text : s_muted;
			row.Buy.interactable =
				_ready && !_refreshPending && !_purchasePending &&
				(retryAmbiguousPurchase ||
				 (!hasAmbiguousPurchase && enabled && !atLimit));
			row.BuyLabel.text =
				pending
					? "WAIT"
					: retryAmbiguousPurchase
						? "RETRY"
						: atLimit ? "MAX" : enabled ? "BUY" : "LOCKED";
			row.BuyLabel.color = retryAmbiguousPurchase
				? s_amberHigh
				: row.Buy.interactable
					? s_greenHigh
					: s_soft;
		}
	}

	private static bool ValidateSnapshot(
		RaidOpsFireSupportServerConfig snapshot,
		out string reason)
	{
		if (snapshot == null || !snapshot.PlayerStateIncluded)
		{
			reason = "Server did not return authoritative player state.";
			return false;
		}
		if (snapshot.PreparedPurchases == null)
		{
			reason = "Server omitted the persistent-purchase recovery journal.";
			return false;
		}
		if (snapshot.PurchasePersistence == null)
		{
			reason = "Server omitted purchase-persistence settings.";
			return false;
		}

		bool hasPreparedPurchase = snapshot.PreparedPurchases.Count > 0;
		if (!snapshot.PurchasePersistence.Enabled && !hasPreparedPurchase)
		{
			reason = "Pre-raid buying requires Purchase Persistence on the TSC server.";
			return false;
		}
		if (!snapshot.StashRoubleBalance.HasValue || snapshot.Authorizations == null)
		{
			reason = "Server omitted the authoritative stash balance or authorization ledger.";
			return false;
		}
		if ((!Enum.TryParse(snapshot.PaymentSource, true, out PaymentSource source) ||
		     source == PaymentSource.CarriedRoubles) &&
		    !hasPreparedPurchase)
		{
			reason = "Pre-raid buying requires a server-backed stash payment source.";
			return false;
		}
		if (snapshot.PurchasePersistence.MaxStoredAuthorizationsPerService <= 0 &&
		    !hasPreparedPurchase)
		{
			reason = "The server authorization limit is disabled.";
			return false;
		}
		foreach (ServiceDescriptor service in s_services)
		{
			if (snapshot.Prices == null ||
			    !snapshot.Prices.TryGetValue(service.ConfigKey, out int price) ||
			    price < 0)
			{
				reason = $"Server omitted a valid price for {service.DisplayName}.";
				return false;
			}
			if (snapshot.Enabled == null ||
			    !snapshot.Enabled.ContainsKey(service.ConfigKey))
			{
				reason = $"Server omitted availability for {service.DisplayName}.";
				return false;
			}
		}

		reason = string.Empty;
		return true;
	}

	private bool AdoptPreparedPurchase(RaidOpsFireSupportServerConfig snapshot)
	{
		if (!string.IsNullOrWhiteSpace(_ambiguousRequestId) ||
		    snapshot?.PreparedPurchases == null ||
		    snapshot.PreparedPurchases.Count == 0)
		{
			return false;
		}

		// The server emits entries from oldest to newest. Dictionary insertion
		// order is retained by the JSON client, so adopt the first valid record
		// and recover additional interrupted purchases on subsequent refreshes.
		foreach (KeyValuePair<string, string> pending in snapshot.PreparedPurchases)
		{
			string requestId = pending.Value?.Trim() ?? string.Empty;
			if (string.IsNullOrWhiteSpace(requestId))
			{
				continue;
			}

			foreach (ServiceDescriptor service in s_services)
			{
				if (!string.Equals(
					    service.ConfigKey,
					    pending.Key,
					    StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				_ambiguousRequestId = requestId;
				_ambiguousType = service.Type;
				return true;
			}
		}

		return false;
	}

	private void SetStatus(string message, bool healthy)
	{
		if (_statusText == null)
		{
			return;
		}

		_statusText.text = (message ?? string.Empty).ToUpperInvariant();
		_statusText.color = healthy ? s_greenHigh : new Color32(255, 182, 173, 255);
		if (_statusPanelImage != null)
		{
			_statusPanelImage.color = healthy
				? new Color(0.035f, 0.063f, 0.047f, 0.97f)
				: new Color(34f / 255f, 10f / 255f, 8f / 255f, 0.97f);
		}
		if (_statusPanelOutline != null)
		{
			_statusPanelOutline.effectColor = healthy
				? new Color(s_green.r, s_green.g, s_green.b, 0.55f)
				: new Color(s_red.r, s_red.g, s_red.b, 0.60f);
		}
		if (_routeStatusText != null)
		{
			bool busy = _refreshPending || _purchasePending;
			_routeStatusText.text = healthy
				? busy ? "SYNCING" : "ONLINE"
				: "ATTENTION";
			_routeStatusText.color = healthy ? s_greenHigh : new Color32(255, 182, 173, 255);
			Outline routeOutline = _routeStatusText.transform.parent?.GetComponent<Outline>();
			if (routeOutline != null)
			{
				routeOutline.effectColor = healthy
					? new Color(s_green.r, s_green.g, s_green.b, 0.5f)
					: new Color(s_red.r, s_red.g, s_red.b, 0.60f);
			}
		}
	}

	private void ClosePage()
	{
		_refreshCts?.Cancel();
		if (_pageRoot != null)
		{
			_pageRoot.SetActive(false);
		}
	}

	private void ResetPageState()
	{
		_generation++;
		_refreshCts?.Cancel();
		_refreshCts?.Dispose();
		_refreshCts = null;
		_snapshot = null;
		_ready = false;
		_refreshPending = false;
		_purchasePending = false;
		_pendingType = ESupportType.None;
		_ambiguousRequestId = string.Empty;
		_ambiguousType = ESupportType.None;
		if (_pageRoot != null)
		{
			_pageRoot.SetActive(false);
		}
		Redraw();
	}

	private void FailClosedForSessionChange(string authenticatedSessionKey, string message)
	{
		ResetPageState();
		FireSupportServerConfigClient.ClearPreRaidSessionState();
		_sessionKey = authenticatedSessionKey ?? string.Empty;
		SetStatus(message, false);
	}

	private void OnDisable()
	{
		ClosePage();
	}

	private void OnDestroy()
	{
		_destroyed = true;
		_generation++;
		_refreshCts?.Cancel();
		_refreshCts?.Dispose();
		_refreshCts = null;
		_menuButton?.OnClick.RemoveAllListeners();
		if (_pageRoot != null)
		{
			Destroy(_pageRoot);
		}
		if (s_instance == this)
		{
			s_instance = null;
		}
	}

	private static bool IsAmbiguousPurchaseFailure(string reason)
	{
		return reason is "RequestFailed" or "InvalidServerResponse" or "InternalServerError" or
			"AuthoritativeLedgerMissing" or "ResponseRequestIdMismatch" or
			"PersistentPurchasePending";
	}

	private void ClearAmbiguousPurchase(string requestId, ESupportType supportType)
	{
		if (_ambiguousType == supportType &&
		    string.Equals(_ambiguousRequestId, requestId, StringComparison.Ordinal))
		{
			_ambiguousRequestId = string.Empty;
			_ambiguousType = ESupportType.None;
		}
	}

	private static string FormatPurchaseFailure(string reason)
	{
		return reason switch
		{
			"AuthorizationLimitReached" => "Authorization limit reached for this service.",
			"InsufficientRoubles" => "Insufficient stash roubles.",
			"RateLimited" => "Purchase rate-limited. Wait briefly and refresh.",
			"ServiceUnavailable" => "This service is disabled by the server.",
			"PaymentSourceNotServerBacked" => "Server payment source is not stash-backed.",
			"PurchasePersistenceDisabled" => "Server purchase persistence is disabled.",
			"ProfileSessionChanged" => "Backend profile changed; reopen the page.",
			_ => $"Purchase denied: {reason}"
		};
	}

	private static ServiceDescriptor GetDescriptor(ESupportType type)
	{
		foreach (ServiceDescriptor descriptor in s_services)
		{
			if (descriptor.Type == type)
			{
				return descriptor;
			}
		}

		return s_services[0];
	}

	private static int GetPrice(RaidOpsFireSupportServerConfig snapshot, string key)
	{
		return snapshot?.Prices != null && snapshot.Prices.TryGetValue(key, out int value)
			? Math.Max(0, value)
			: -1;
	}

	private static bool GetEnabled(RaidOpsFireSupportServerConfig snapshot, string key)
	{
		return snapshot?.Enabled != null &&
		       snapshot.Enabled.TryGetValue(key, out bool enabled) &&
		       enabled;
	}

	private static int GetOwned(RaidOpsFireSupportServerConfig snapshot, string key)
	{
		return snapshot?.Authorizations != null &&
		       snapshot.Authorizations.TryGetValue(key, out int count)
			? Math.Max(0, count)
			: 0;
	}

	private static int GetMaximum(RaidOpsFireSupportServerConfig snapshot)
	{
		return Math.Max(0, snapshot?.PurchasePersistence?.MaxStoredAuthorizationsPerService ?? 0);
	}

	private static DefaultUIButton FindCharacterButton(MenuScreen menuScreen)
	{
		foreach (Transform child in menuScreen.GetComponentsInChildren<Transform>(true))
		{
			if (child != null && child.name == "CharacterButton")
			{
				DefaultUIButton found = child.GetComponent<DefaultUIButton>();
				if (found != null)
				{
					return found;
				}
			}
		}

		try
		{
			return Traverse.Create(menuScreen).Field("_playerButton").GetValue<DefaultUIButton>();
		}
		catch
		{
			return null;
		}
	}

	private static RectTransform FindSiblingRect(
		Transform parent,
		string name,
		RectTransform self)
	{
		for (int index = 0; index < parent.childCount; index++)
		{
			Transform child = parent.GetChild(index);
			if (child != null && child != self.transform && child.name == name)
			{
				return child as RectTransform;
			}
		}

		return null;
	}

	private static void CopyRect(RectTransform target, RectTransform source)
	{
		target.anchorMin = source.anchorMin;
		target.anchorMax = source.anchorMax;
		target.pivot = source.pivot;
		target.sizeDelta = source.sizeDelta;
		target.localScale = source.localScale;
	}

	private static int ResolveFontSize(DefaultUIButton template)
	{
		try
		{
			return Mathf.Max(28, (int)template.HeaderSize);
		}
		catch
		{
			return 32;
		}
	}

	private static void EnsureUsable(GameObject root)
	{
		CanvasGroup group = root.GetComponent<CanvasGroup>();
		if (group != null)
		{
			group.alpha = 1f;
			group.interactable = true;
			group.blocksRaycasts = true;
		}
	}

	private static void CreateGridBackdrop(Transform parent)
	{
		GameObject grid = new("DashboardGrid", typeof(RectTransform));
		grid.transform.SetParent(parent, false);
		Stretch(grid.GetComponent<RectTransform>());

		Color vertical = new(1f, 1f, 1f, 0.018f);
		for (int index = 1; index < 28; index++)
		{
			float x = index / 28f;
			CreatePanelLine(
				grid.transform,
				$"V{index:00}",
				vertical,
				new Vector2(x, 0f),
				new Vector2(x, 1f),
				Vector2.zero,
				new Vector2(1f, 0f));
		}

		Color horizontal = new(1f, 1f, 1f, 0.014f);
		for (int index = 1; index < 44; index++)
		{
			float y = index / 44f;
			CreatePanelLine(
				grid.transform,
				$"H{index:00}",
				horizontal,
				new Vector2(0f, y),
				new Vector2(1f, y),
				Vector2.zero,
				new Vector2(0f, 1f));
		}
	}

	private void CreateSidebar(Transform parent)
	{
		GameObject sidebar = CreatePanel(parent, "Sidebar", new Color(s_background2.r, s_background2.g, s_background2.b, 0.97f));
		SetStretchRect(
			sidebar.GetComponent<RectTransform>(),
			Vector2.zero,
			new Vector2(0f, 1f),
			new Vector2(0f, BottombarHeight),
			new Vector2(SidebarWidth, 0f));
		CreatePanelLine(
			sidebar.transform,
			"RightRule",
			new Color(s_amberHigh.r, s_amberHigh.g, s_amberHigh.b, 0.14f),
			new Vector2(1f, 0f),
			Vector2.one,
			new Vector2(-1f, 0f),
			Vector2.zero);

		GameObject brandMark = CreateBorderedPanel(
			sidebar.transform,
			"BrandMark",
			new Color(s_amber.r, s_amber.g, s_amber.b, 0.035f),
			new Color(s_text.r, s_text.g, s_text.b, 0.62f));
		SetRect(
			brandMark.GetComponent<RectTransform>(),
			new Vector2(0f, 1f),
			new Vector2(34f, 34f),
			new Vector2(34f, -34f));
		brandMark.transform.localEulerAngles = new Vector3(0f, 0f, 45f);
		GameObject brandCore = CreateBorderedPanel(
			brandMark.transform,
			"Core",
			new Color(s_amber.r, s_amber.g, s_amber.b, 0.06f),
			new Color(s_amberHigh.r, s_amberHigh.g, s_amberHigh.b, 0.36f));
		SetRect(
			brandCore.GetComponent<RectTransform>(),
			new Vector2(0.5f, 0.5f),
			new Vector2(15f, 15f),
			Vector2.zero);

		CreateText(sidebar.transform, "Brand", "TERRAGROUP", 16, FontStyle.Bold,
			s_amberHigh, TextAnchor.MiddleLeft, new Vector2(0f, 1f),
			new Vector2(145f, 24f), new Vector2(137f, -25f));
		CreateText(sidebar.transform, "BrandSubtitle", "TACTICAL SERVICES\nCONTROL", 10, FontStyle.Normal,
			s_muted, TextAnchor.UpperLeft, new Vector2(0f, 1f),
			new Vector2(145f, 35f), new Vector2(137f, -52f), mono: true);

		string[] navigation =
		[
			"MAIN",
			"AUTHORIZATION STORE",
			"PURCHASE PERSISTENCE",
			"PAYMENT",
			"SERVICE CATALOG",
			"RECON SERVICES",
			"EXTRACTION SERVICES",
			"FIRE SUPPORT"
		];
		for (int index = 0; index < navigation.Length; index++)
		{
			bool active = index == 1;
			float top = -105f - index * 34f;
			GameObject navItem = CreatePanel(
				sidebar.transform,
				$"Nav_{index:00}",
				active
					? new Color(s_amberHigh.r, s_amberHigh.g, s_amberHigh.b, 0.08f)
					: Color.clear);
			SetStretchRect(
				navItem.GetComponent<RectTransform>(),
				new Vector2(0f, 1f),
				Vector2.one,
				new Vector2(14f, top - 30f),
				new Vector2(-14f, top));
			if (active)
			{
				CreatePanelLine(
					navItem.transform,
					"ActiveRule",
					s_amber,
					Vector2.zero,
					new Vector2(0f, 1f),
					Vector2.zero,
					new Vector2(2f, 0f));
			}

			CreateText(navItem.transform, "Label", navigation[index], 10, FontStyle.Bold,
				active ? s_text : s_muted, TextAnchor.MiddleLeft, new Vector2(0.5f, 0.5f),
				new Vector2(174f, 24f), new Vector2(7f, 0f), mono: true);
		}

		CreateText(sidebar.transform, "TerminalState", "CLIENT TERMINAL\nPROFILE BOUND", 9,
			FontStyle.Normal, s_soft, TextAnchor.LowerLeft, new Vector2(0f, 0f),
			new Vector2(180f, 34f), new Vector2(106f, 22f), mono: true);
	}

	private void CreateTopbar(Transform parent)
	{
		GameObject topbar = CreatePanel(parent, "Topbar", new Color(4f / 255f, 8f / 255f, 8f / 255f, 0.96f));
		SetStretchRect(
			topbar.GetComponent<RectTransform>(),
			new Vector2(0f, 1f),
			Vector2.one,
			new Vector2(SidebarWidth, -TopbarHeight),
			Vector2.zero);
		CreatePanelLine(
			topbar.transform,
			"BottomRule",
			new Color(s_amberHigh.r, s_amberHigh.g, s_amberHigh.b, 0.18f),
			Vector2.zero,
			new Vector2(1f, 0f),
			Vector2.zero,
			new Vector2(0f, 1f));

		CreateText(topbar.transform, "Title", "TSC UPLINK", 11, FontStyle.Bold,
			s_text, TextAnchor.MiddleLeft, new Vector2(0f, 0.5f),
			new Vector2(260f, 20f), new Vector2(150f, 10f), mono: true);
		CreateText(topbar.transform, "Subtitle", "PRE-RAID AUTHORIZATION TERMINAL", 11,
			FontStyle.Normal, s_muted, TextAnchor.MiddleLeft, new Vector2(0f, 0.5f),
			new Vector2(320f, 20f), new Vector2(180f, -10f));

		CreatePill(
			topbar.transform,
			"RouteStatus",
			"LINKING",
			new Vector2(1f, 0.5f),
			new Vector2(82f, 28f),
			new Vector2(-548f, 0f),
			new Color(s_green.r, s_green.g, s_green.b, 0.5f),
			s_greenHigh,
			out _routeStatusText);
		CreatePill(
			topbar.transform,
			"Revision",
			"REVISION --",
			new Vector2(1f, 0.5f),
			new Vector2(106f, 28f),
			new Vector2(-446f, 0f),
			s_line,
			s_muted,
			out _revisionPillText);
		CreatePill(
			topbar.transform,
			"Payment",
			"STASHROUBLES",
			new Vector2(1f, 0.5f),
			new Vector2(126f, 28f),
			new Vector2(-320f, 0f),
			s_line,
			s_muted,
			out _paymentPillText);

		_refreshButton = CreateButton(
			topbar.transform,
			"Refresh",
			"REFRESH",
			new Vector2(1f, 0.5f),
			new Vector2(112f, 34f),
			new Vector2(-190f, 0f),
			StartRefresh,
			ButtonVisual.Neutral);
		CreateButton(
			topbar.transform,
			"Close",
			"CLOSE",
			new Vector2(1f, 0.5f),
			new Vector2(100f, 34f),
			new Vector2(-66f, 0f),
			ClosePage,
			ButtonVisual.Neutral);
	}

	private static void CreateBottomBar(Transform parent)
	{
		GameObject bottom = CreatePanel(parent, "BottomBar", new Color(s_background.r, s_background.g, s_background.b, 0.97f));
		SetStretchRect(
			bottom.GetComponent<RectTransform>(),
			Vector2.zero,
			new Vector2(1f, 0f),
			Vector2.zero,
			new Vector2(0f, BottombarHeight));
		CreatePanelLine(
			bottom.transform,
			"TopRule",
			new Color(s_amberHigh.r, s_amberHigh.g, s_amberHigh.b, 0.14f),
			new Vector2(0f, 1f),
			Vector2.one,
			new Vector2(0f, -1f),
			Vector2.zero);
		CreateText(bottom.transform, "Left", "PROFILE-BOUND CLIENT TERMINAL", 9, FontStyle.Normal,
			s_muted, TextAnchor.MiddleLeft, new Vector2(0f, 0.5f),
			new Vector2(300f, 20f), new Vector2(160f, 0f), mono: true);
		CreateText(bottom.transform, "Right", "SERVER-AUTHORITATIVE // PRE-RAID", 9, FontStyle.Normal,
			s_muted, TextAnchor.MiddleRight, new Vector2(1f, 0.5f),
			new Vector2(350f, 20f), new Vector2(-185f, 0f), mono: true);
	}

	private static void CreateMetricCard(
		Transform parent,
		int index,
		string label,
		string value,
		out Text valueText)
	{
		float minimum = index / 3f;
		float maximum = (index + 1) / 3f;
		GameObject card = CreateBorderedPanel(parent, $"Metric_{label}", new Color(11f / 255f, 16f / 255f, 16f / 255f, 0.88f), s_line);
		SetStretchRect(
			card.GetComponent<RectTransform>(),
			new Vector2(minimum, 1f),
			new Vector2(maximum, 1f),
			new Vector2(index == 0 ? 0f : 7f, -78f),
			new Vector2(index == 2 ? 0f : -7f, 0f));
		CreatePanelLine(
			card.transform,
			"AmberGlint",
			new Color(s_amberHigh.r, s_amberHigh.g, s_amberHigh.b, 0.22f),
			new Vector2(0f, 1f),
			new Vector2(0f, 1f),
			Vector2.zero,
			new Vector2(72f, 1f));
		CreateText(card.transform, "Label", label, 10, FontStyle.Bold,
			s_muted, TextAnchor.MiddleLeft, new Vector2(0f, 1f),
			new Vector2(250f, 20f), new Vector2(143f, -20f), mono: true);
		valueText = CreateText(card.transform, "Value", value, 21, FontStyle.Bold,
			s_text, TextAnchor.MiddleLeft, new Vector2(0f, 1f),
			new Vector2(100f, 34f), new Vector2(68f, -49f));
		SetStretchRect(
			valueText.rectTransform,
			new Vector2(0f, 1f),
			Vector2.one,
			new Vector2(18f, -68f),
			new Vector2(-18f, -30f));
		valueText.resizeTextForBestFit = true;
		valueText.resizeTextMinSize = 14;
		valueText.resizeTextMaxSize = 21;
	}

	private RowView CreateServiceCard(
		Transform parent,
		ServiceDescriptor service,
		Vector2 anchorMin,
		Vector2 anchorMax,
		Vector2 offsetMin,
		Vector2 offsetMax)
	{
		GameObject card = CreateBorderedPanel(
			parent,
			$"Card_{service.Type}",
			new Color(13f / 255f, 18f / 255f, 17f / 255f, 0.9f),
			new Color(s_text.r, s_text.g, s_text.b, 0.10f));
		SetStretchRect(card.GetComponent<RectTransform>(), anchorMin, anchorMax, offsetMin, offsetMax);
		CreatePanelLine(
			card.transform,
			"BottomGlint",
			new Color(s_amberHigh.r, s_amberHigh.g, s_amberHigh.b, 0.12f),
			Vector2.zero,
			new Vector2(1f, 0f),
			new Vector2(18f, 0f),
			new Vector2(-18f, 1f));

		GameObject codeBox = CreateBorderedPanel(
			card.transform,
			"ServiceCode",
			new Color(s_amber.r, s_amber.g, s_amber.b, 0.07f),
			new Color(s_amberHigh.r, s_amberHigh.g, s_amberHigh.b, 0.42f));
		SetRect(
			codeBox.GetComponent<RectTransform>(),
			new Vector2(0f, 1f),
			new Vector2(42f, 42f),
			new Vector2(31f, -31f));
		CreateText(codeBox.transform, "Code", service.Code, 11, FontStyle.Bold,
			s_amberHigh, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f),
			new Vector2(38f, 30f), Vector2.zero, mono: true);

		Text name = CreateText(card.transform, "Name", service.DisplayName, 17, FontStyle.Bold,
			s_text, TextAnchor.MiddleLeft, new Vector2(0f, 1f),
			new Vector2(100f, 28f), new Vector2(118f, -23f));
		SetStretchRect(
			name.rectTransform,
			new Vector2(0f, 1f),
			Vector2.one,
			new Vector2(64f, -38f),
			new Vector2(-18f, -8f));
		name.resizeTextForBestFit = true;
		name.resizeTextMinSize = 13;
		name.resizeTextMaxSize = 17;
		Text summary = CreateText(card.transform, "Summary", service.Summary, 11, FontStyle.Normal,
			s_muted, TextAnchor.MiddleLeft, new Vector2(0f, 1f),
			new Vector2(100f, 20f), new Vector2(118f, -48f));
		SetStretchRect(
			summary.rectTransform,
			new Vector2(0f, 1f),
			Vector2.one,
			new Vector2(64f, -62f),
			new Vector2(-18f, -40f));

		Text statusLabel = CreateText(card.transform, "StatusLabel", "STATUS", 9, FontStyle.Bold,
			s_muted, TextAnchor.MiddleLeft, new Vector2(0f, 0f),
			new Vector2(105f, 16f), new Vector2(70f, 82f), mono: true);
		SetStretchRect(
			statusLabel.rectTransform,
			Vector2.zero,
			new Vector2(0.34f, 0f),
			new Vector2(18f, 80f),
			new Vector2(-4f, 96f));
		Text state = CreateText(card.transform, "State", "--", 11, FontStyle.Bold,
			s_muted, TextAnchor.MiddleLeft, new Vector2(0f, 0f),
			new Vector2(125f, 22f), new Vector2(80f, 64f), mono: true);
		SetStretchRect(
			state.rectTransform,
			Vector2.zero,
			new Vector2(0.34f, 0f),
			new Vector2(18f, 54f),
			new Vector2(-4f, 78f));
		state.resizeTextForBestFit = true;
		state.resizeTextMinSize = 8;
		state.resizeTextMaxSize = 11;

		Text priceLabel = CreateText(card.transform, "PriceLabel", "UNIT PRICE", 9, FontStyle.Bold,
			s_muted, TextAnchor.MiddleCenter, new Vector2(0.5f, 0f),
			new Vector2(120f, 16f), new Vector2(-12f, 82f), mono: true);
		SetStretchRect(
			priceLabel.rectTransform,
			new Vector2(0.34f, 0f),
			new Vector2(0.72f, 0f),
			new Vector2(4f, 80f),
			new Vector2(-4f, 96f));
		Text price = CreateText(card.transform, "Price", "--", 13, FontStyle.Bold,
			s_amberHigh, TextAnchor.MiddleCenter, new Vector2(0.5f, 0f),
			new Vector2(150f, 22f), new Vector2(-12f, 64f), mono: true);
		SetStretchRect(
			price.rectTransform,
			new Vector2(0.34f, 0f),
			new Vector2(0.72f, 0f),
			new Vector2(4f, 54f),
			new Vector2(-4f, 78f));
		price.resizeTextForBestFit = true;
		price.resizeTextMinSize = 9;
		price.resizeTextMaxSize = 13;

		Text ownedLabel = CreateText(card.transform, "OwnedLabel", "OWNED", 9, FontStyle.Bold,
			s_muted, TextAnchor.MiddleRight, new Vector2(1f, 0f),
			new Vector2(95f, 16f), new Vector2(-58f, 82f), mono: true);
		SetStretchRect(
			ownedLabel.rectTransform,
			new Vector2(0.72f, 0f),
			new Vector2(1f, 0f),
			new Vector2(4f, 80f),
			new Vector2(-18f, 96f));
		Text owned = CreateText(card.transform, "Owned", "-- / --", 12, FontStyle.Bold,
			s_text, TextAnchor.MiddleRight, new Vector2(1f, 0f),
			new Vector2(95f, 22f), new Vector2(-58f, 64f), mono: true);
		SetStretchRect(
			owned.rectTransform,
			new Vector2(0.72f, 0f),
			new Vector2(1f, 0f),
			new Vector2(4f, 54f),
			new Vector2(-18f, 78f));

		CreateText(card.transform, "CreditType", "PERSISTENT CREDIT", 9, FontStyle.Normal,
			s_soft, TextAnchor.MiddleLeft, new Vector2(0f, 0f),
			new Vector2(150f, 20f), new Vector2(93f, 23f), mono: true);
		Button buy = CreateButton(
			card.transform,
			"Buy",
			"BUY",
			new Vector2(1f, 0f),
			new Vector2(112f, 34f),
			new Vector2(-74f, 23f),
			() => BeginPurchase(service.Type),
			ButtonVisual.Primary);
		Text buyLabel = buy.GetComponentInChildren<Text>();
		return new RowView(name, state, price, owned, buy, buyLabel);
	}

	private static GameObject CreatePill(
		Transform parent,
		string name,
		string label,
		Vector2 anchor,
		Vector2 size,
		Vector2 position,
		Color border,
		Color textColor,
		out Text text)
	{
		GameObject pill = CreateBorderedPanel(
			parent,
			name,
			new Color(11f / 255f, 16f / 255f, 16f / 255f, 0.72f),
			border);
		SetRect(pill.GetComponent<RectTransform>(), anchor, size, position);
		text = CreateText(pill.transform, "Label", label, 9, FontStyle.Bold,
			textColor, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f),
			new Vector2(size.x - 8f, size.y - 4f), Vector2.zero, mono: true);
		text.resizeTextForBestFit = true;
		text.resizeTextMinSize = 8;
		text.resizeTextMaxSize = 9;
		return pill;
	}

	private static GameObject CreatePanel(Transform parent, string name, Color color)
	{
		GameObject panel = new(name, typeof(RectTransform), typeof(Image));
		panel.transform.SetParent(parent, false);
		Image image = panel.GetComponent<Image>();
		image.color = color;
		image.raycastTarget = false;
		return panel;
	}

	private static GameObject CreateBorderedPanel(
		Transform parent,
		string name,
		Color color,
		Color border)
	{
		GameObject panel = CreatePanel(parent, name, color);
		Outline outline = panel.AddComponent<Outline>();
		outline.effectColor = border;
		outline.effectDistance = new Vector2(1f, -1f);
		outline.useGraphicAlpha = false;
		return panel;
	}

	private static void CreatePanelLine(
		Transform parent,
		string name,
		Color color,
		Vector2 anchorMin,
		Vector2 anchorMax,
		Vector2 offsetMin,
		Vector2 offsetMax)
	{
		GameObject line = CreatePanel(parent, name, color);
		SetStretchRect(line.GetComponent<RectTransform>(), anchorMin, anchorMax, offsetMin, offsetMax);
	}

	private static Text CreateText(
		Transform parent,
		string name,
		string value,
		int fontSize,
		FontStyle style,
		Color color,
		TextAnchor alignment,
		Vector2 anchor,
		Vector2 size,
		Vector2 position,
		bool mono = false)
	{
		GameObject textObject = new(name, typeof(RectTransform), typeof(Text));
		textObject.transform.SetParent(parent, false);
		Text text = textObject.GetComponent<Text>();
		text.font = GetUiFont(mono);
		text.fontSize = fontSize;
		text.fontStyle = style;
		text.color = color;
		text.alignment = alignment;
		text.text = value;
		text.raycastTarget = false;
		SetRect(textObject.GetComponent<RectTransform>(), anchor, size, position);
		return text;
	}

	private static Button CreateButton(
		Transform parent,
		string name,
		string label,
		Vector2 anchor,
		Vector2 size,
		Vector2 position,
		Action onClick,
		ButtonVisual visual = ButtonVisual.Primary)
	{
		GameObject buttonObject = new(name, typeof(RectTransform), typeof(Image), typeof(Button));
		buttonObject.transform.SetParent(parent, false);
		SetRect(buttonObject.GetComponent<RectTransform>(), anchor, size, position);
		Image image = buttonObject.GetComponent<Image>();
		Color fill;
		Color border;
		Color labelColor;
		switch (visual)
		{
			case ButtonVisual.Danger:
				fill = new Color(34f / 255f, 10f / 255f, 8f / 255f, 0.92f);
				border = new Color(s_red.r, s_red.g, s_red.b, 0.60f);
				labelColor = new Color32(255, 182, 173, 255);
				break;
			case ButtonVisual.Neutral:
				fill = s_panel3;
				border = s_line;
				labelColor = s_text;
				break;
			default:
				fill = new Color(20f / 255f, 43f / 255f, 27f / 255f, 0.92f);
				border = new Color(s_green.r, s_green.g, s_green.b, 0.55f);
				labelColor = s_greenHigh;
				break;
		}

		image.color = fill;
		Outline outline = buttonObject.AddComponent<Outline>();
		outline.effectColor = border;
		outline.effectDistance = new Vector2(1f, -1f);
		outline.useGraphicAlpha = false;
		Button button = buttonObject.GetComponent<Button>();
		button.targetGraphic = image;
		ColorBlock colors = button.colors;
		colors.normalColor = Color.white;
		colors.highlightedColor = visual == ButtonVisual.Primary
			? new Color(1.12f, 1.12f, 1.12f, 1f)
			: new Color(1f, 0.86f, 0.64f, 1f);
		colors.pressedColor = new Color(0.72f, 0.72f, 0.68f, 1f);
		colors.disabledColor = new Color(0.42f, 0.42f, 0.42f, 0.52f);
		button.colors = colors;
		button.onClick.AddListener(() => onClick?.Invoke());
		CreateText(buttonObject.transform, "Label", label, 10, FontStyle.Bold, labelColor,
			TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), size, Vector2.zero, mono: true);
		return button;
	}

	private static Font GetUiFont(bool mono)
	{
		if (mono && s_monoFont != null)
		{
			return s_monoFont;
		}
		if (!mono && s_sansFont != null)
		{
			return s_sansFont;
		}

		string[] preferred = mono
			? ["Cascadia Mono", "Consolas"]
			: ["Bahnschrift", "Segoe UI Semibold", "Arial"];
		foreach (string family in preferred)
		{
			try
			{
				Font candidate = Font.CreateDynamicFontFromOSFont(family, 18);
				if (candidate == null)
				{
					continue;
				}

				if (mono)
				{
					s_monoFont = candidate;
				}
				else
				{
					s_sansFont = candidate;
				}
				return candidate;
			}
			catch
			{
			}
		}

		Font fallback = Resources.GetBuiltinResource<Font>("Arial.ttf");
		if (mono)
		{
			s_monoFont = fallback;
		}
		else
		{
			s_sansFont = fallback;
		}
		return fallback;
	}

	private static void SetRect(
		RectTransform rect,
		Vector2 anchor,
		Vector2 size,
		Vector2 position)
	{
		rect.anchorMin = anchor;
		rect.anchorMax = anchor;
		rect.pivot = new Vector2(0.5f, 0.5f);
		rect.sizeDelta = size;
		rect.anchoredPosition = position;
	}

	private static void SetStretchRect(
		RectTransform rect,
		Vector2 anchorMin,
		Vector2 anchorMax,
		Vector2 offsetMin,
		Vector2 offsetMax)
	{
		rect.anchorMin = anchorMin;
		rect.anchorMax = anchorMax;
		rect.pivot = new Vector2(0.5f, 0.5f);
		rect.offsetMin = offsetMin;
		rect.offsetMax = offsetMax;
		rect.localScale = Vector3.one;
	}

	private static void Stretch(RectTransform rect)
	{
		rect.anchorMin = Vector2.zero;
		rect.anchorMax = Vector2.one;
		rect.offsetMin = Vector2.zero;
		rect.offsetMax = Vector2.zero;
	}

	private readonly struct ServiceDescriptor
	{
		public ServiceDescriptor(
			ESupportType type,
			string configKey,
			string displayName,
			string code,
			string summary)
		{
			Type = type;
			ConfigKey = configKey;
			DisplayName = displayName;
			Code = code;
			Summary = summary;
		}

		public ESupportType Type { get; }
		public string ConfigKey { get; }
		public string DisplayName { get; }
		public string Code { get; }
		public string Summary { get; }
	}

	private enum ButtonVisual
	{
		Primary,
		Neutral,
		Danger
	}

	private sealed class RowView
	{
		public RowView(
			Text name,
			Text state,
			Text price,
			Text owned,
			Button buy,
			Text buyLabel)
		{
			Name = name;
			State = state;
			Price = price;
			Owned = owned;
			Buy = buy;
			BuyLabel = buyLabel;
		}

		public Text Name { get; }
		public Text State { get; }
		public Text Price { get; }
		public Text Owned { get; }
		public Button Buy { get; }
		public Text BuyLabel { get; }
	}
}
