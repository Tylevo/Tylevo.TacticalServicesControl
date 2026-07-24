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

	// Restrained subset of the dashboard palette. The storefront intentionally
	// keeps its original simple layout and built-in Unity font.
	private static readonly Color s_background = new Color32(3, 6, 7, 250);
	private static readonly Color s_panel = new Color32(12, 17, 17, 255);
	private static readonly Color s_row = new Color32(17, 23, 22, 255);
	private static readonly Color s_line = new Color32(220, 216, 200, 41);
	private static readonly Color s_lineStrong = new Color32(232, 185, 103, 117);
	private static readonly Color s_text = new Color32(220, 216, 200, 255);
	private static readonly Color s_muted = new Color32(150, 146, 132, 255);
	private static readonly Color s_amberHigh = new Color32(232, 185, 103, 255);
	private static readonly Color s_green = new Color32(113, 157, 70, 255);
	private static readonly Color s_greenHigh = new Color32(145, 200, 90, 255);
	private static readonly Color s_red = new Color32(198, 72, 61, 255);

	private static readonly ServiceDescriptor[] s_services =
	[
		new(ESupportType.Strafe, "A10", "A-10 STRAFE"),
		new(ESupportType.DoubleStrafe, "DoublePass", "A-10 DOUBLE PASS"),
		new(ESupportType.Extract, "Extraction", "UH-60 EXTRACTION"),
		new(ESupportType.PriorityExfil, "PriorityExfil", "UH-60 PRIORITY EXFIL"),
		new(ESupportType.Uav, "Uav", "UAV RECON"),
		new(ESupportType.FocusedSweep, "FocusedSweep", "UAV FOCUSED SWEEP")
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

	private readonly Dictionary<ESupportType, RowView> _rows = new();
	private MenuScreen _menuScreen;
	private DefaultUIButton _menuButton;
	private GameObject _pageRoot;
	private Text _statusText;
	private Text _balanceText;
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
		_pageRoot.GetComponent<Image>().color = s_background;

		GameObject panel = CreateBorderedPanel(_pageRoot.transform, "Panel", s_panel, s_lineStrong);
		SetRect(panel.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(1180f, 780f), Vector2.zero);

		CreateText(panel.transform, "Title", "TERRAGROUP // TSC UPLINK", 34, FontStyle.Bold,
			s_amberHigh, TextAnchor.MiddleLeft,
			new Vector2(0.5f, 1f), new Vector2(800f, 52f), new Vector2(-145f, -42f));
		CreateText(panel.transform, "Subtitle", "PRE-RAID PERSISTENT AUTHORIZATION STORE", 17, FontStyle.Normal,
			s_muted, TextAnchor.MiddleLeft,
			new Vector2(0.5f, 1f), new Vector2(800f, 30f), new Vector2(-145f, -82f));

		_balanceText = CreateText(panel.transform, "Balance", "STASH: --", 22, FontStyle.Bold,
			s_text, TextAnchor.MiddleRight,
			new Vector2(1f, 1f), new Vector2(330f, 42f), new Vector2(-210f, -52f));
		_statusText = CreateText(panel.transform, "Status", "Open the page to synchronize.", 17, FontStyle.Normal,
			s_muted, TextAnchor.MiddleLeft,
			new Vector2(0.5f, 1f), new Vector2(1090f, 46f), new Vector2(0f, -124f));

		_refreshButton = CreateButton(panel.transform, "Refresh", "REFRESH", new Vector2(0.5f, 1f),
			new Vector2(150f, 42f), new Vector2(330f, -84f), StartRefresh, ButtonVisual.Neutral);
		CreateButton(panel.transform, "Close", "CLOSE", new Vector2(0.5f, 1f),
			new Vector2(130f, 42f), new Vector2(480f, -84f), ClosePage, ButtonVisual.Neutral);

		_rows.Clear();
		for (int index = 0; index < s_services.Length; index++)
		{
			ServiceDescriptor service = s_services[index];
			float y = -190f - index * 82f;
			GameObject row = CreateBorderedPanel(panel.transform, $"Row_{service.Type}", s_row, s_line);
			SetRect(row.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(1090f, 68f), new Vector2(0f, y));

			Text name = CreateText(row.transform, "Name", service.DisplayName, 20, FontStyle.Bold,
				s_text, TextAnchor.MiddleLeft, new Vector2(0f, 0.5f),
				new Vector2(300f, 54f), new Vector2(165f, 0f));
			Text state = CreateText(row.transform, "State", "--", 15, FontStyle.Bold,
				s_muted, TextAnchor.MiddleLeft, new Vector2(0f, 0.5f),
				new Vector2(150f, 54f), new Vector2(385f, 0f));
			Text price = CreateText(row.transform, "Price", "--", 19, FontStyle.Bold,
				s_amberHigh, TextAnchor.MiddleRight, new Vector2(0f, 0.5f),
				new Vector2(190f, 54f), new Vector2(610f, 0f));
			Text owned = CreateText(row.transform, "Owned", "-- / --", 18, FontStyle.Bold,
				s_text, TextAnchor.MiddleCenter, new Vector2(0f, 0.5f),
				new Vector2(150f, 54f), new Vector2(790f, 0f));
			Button buy = CreateButton(row.transform, "Buy", "BUY", new Vector2(1f, 0.5f),
				new Vector2(140f, 42f), new Vector2(-88f, 0f), () => BeginPurchase(service.Type));
			_rows[service.Type] = new RowView(name, state, price, owned, buy);
		}

		CreateText(panel.transform, "Footer",
			"Purchases debit the authenticated PMC stash and must be returned by the persistent server ledger.",
			14, FontStyle.Normal, s_muted, TextAnchor.MiddleLeft,
			new Vector2(0.5f, 0f), new Vector2(1090f, 32f), new Vector2(0f, 26f));

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
			? $"STASH: ₽{balance:N0}"
			: "STASH: --";
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
				: !hasSnapshot ? "--" : enabled ? "AVAILABLE" : "DISABLED";
			row.State.color = retryAmbiguousPurchase
				? s_amberHigh
				: enabled
				? s_greenHigh
				: s_red;
			row.Price.text = price >= 0 ? $"₽{price:N0}" : "--";
			row.Price.color = price >= 0 ? s_amberHigh : s_muted;
			row.Owned.text = hasSnapshot ? $"{owned} / {maximum}" : "-- / --";
			row.Buy.interactable =
				_ready && !_refreshPending && !_purchasePending &&
				(retryAmbiguousPurchase ||
				 (!hasAmbiguousPurchase && enabled && !atLimit));
			row.Buy.GetComponentInChildren<Text>().text =
				pending
					? "WAIT"
					: retryAmbiguousPurchase
						? "RETRY"
						: atLimit ? "MAX" : enabled ? "BUY" : "LOCKED";
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

		_statusText.text = message ?? string.Empty;
		_statusText.color = healthy
			? s_greenHigh
			: new Color32(255, 182, 173, 255);
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
		Vector2 position)
	{
		GameObject textObject = new(name, typeof(RectTransform), typeof(Text));
		textObject.transform.SetParent(parent, false);
		Text text = textObject.GetComponent<Text>();
		text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
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
		Color border;
		Color labelColor;
		if (visual == ButtonVisual.Neutral)
		{
			image.color = new Color32(21, 23, 20, 255);
			border = s_line;
			labelColor = s_text;
		}
		else
		{
			image.color = new Color32(20, 43, 27, 255);
			border = new Color(s_green.r, s_green.g, s_green.b, 0.55f);
			labelColor = s_greenHigh;
		}
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
			: new Color(1f, 0.9f, 0.72f, 1f);
		colors.pressedColor = new Color(0.72f, 0.72f, 0.68f, 1f);
		colors.disabledColor = new Color(0.42f, 0.42f, 0.42f, 0.52f);
		button.colors = colors;
		button.onClick.AddListener(() => onClick?.Invoke());
		CreateText(buttonObject.transform, "Label", label, 17, FontStyle.Bold, labelColor,
			TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), size, Vector2.zero);
		return button;
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

	private static void Stretch(RectTransform rect)
	{
		rect.anchorMin = Vector2.zero;
		rect.anchorMax = Vector2.one;
		rect.offsetMin = Vector2.zero;
		rect.offsetMax = Vector2.zero;
	}

	private readonly struct ServiceDescriptor
	{
		public ServiceDescriptor(ESupportType type, string configKey, string displayName)
		{
			Type = type;
			ConfigKey = configKey;
			DisplayName = displayName;
		}

		public ESupportType Type { get; }
		public string ConfigKey { get; }
		public string DisplayName { get; }
	}

	private enum ButtonVisual
	{
		Primary,
		Neutral
	}

	private sealed class RowView
	{
		public RowView(Text name, Text state, Text price, Text owned, Button buy)
		{
			Name = name;
			State = state;
			Price = price;
			Owned = owned;
			Buy = buy;
		}

		public Text Name { get; }
		public Text State { get; }
		public Text Price { get; }
		public Text Owned { get; }
		public Button Buy { get; }
	}
}
