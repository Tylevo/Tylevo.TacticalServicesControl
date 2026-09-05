using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public sealed partial class MainMenuPurchaseController
{
	private const float StoreWidth = 1280f;
	private const float StoreHeight = 820f;
	private const float ConfirmationWidth = 760f;
	private const float ConfirmationHeight = 620f;
	private static readonly Dictionary<string, Sprite> s_storeIcons = new();
	private RectTransform _storePanel;
	private RectTransform _storeDialog;
	private ESupportType _selectedService = ESupportType.Strafe;
	private Image _detailArt;
	private Text _detailTitle;
	private Text _detailDescription;
	private Text _detailPrice;
	private Text _detailPriceCaption;
	private Text _detailOwned;
	private Text _detailState;
	private Button _detailReview;
	private Image _confirmationArt;
	private Text _confirmationService;
	private Text _confirmationPriceText;
	private Text _confirmationPriceCaption;
	private Text _confirmationBefore;
	private Text _confirmationAfter;
	private Text _confirmationAfterCaption;

	private void BuildPage()
	{
		if (_pageRoot != null) return;
		Canvas parentCanvas = _menuScreen?.GetComponentInParent<Canvas>();
		if (parentCanvas == null)
		{
			FireSupportPlugin.LogSource.LogWarning("TSC main-menu purchase page could not find the EFT menu Canvas.");
			return;
		}
		_pageRoot = new GameObject(PageName, typeof(RectTransform), typeof(Canvas),
			typeof(GraphicRaycaster), typeof(CanvasGroup), typeof(Image));
		_pageRoot.transform.SetParent(parentCanvas.transform, false);
		Stretch(_pageRoot.GetComponent<RectTransform>());
		Canvas pageCanvas = _pageRoot.GetComponent<Canvas>();
		pageCanvas.overrideSorting = true;
		pageCanvas.sortingOrder = parentCanvas.sortingOrder + 500;
		_pageRoot.GetComponent<Image>().color = s_background;
		_pageRoot.GetComponent<Image>().raycastTarget = true;

		GameObject panel = CreateBorderedPanel(_pageRoot.transform, "TSC Storefront", s_panel, s_lineStrong);
		_storePanel = panel.GetComponent<RectTransform>();
		SetRect(_storePanel, new Vector2(0.5f, 0.5f), new Vector2(StoreWidth, StoreHeight), Vector2.zero);
		StoreText(_storePanel, "Brand", "TERRAGROUP / TACTICAL SERVICES", 15, s_muted, 32, 22, 630, 24, true);
		StoreText(_storePanel, "Title", "TSC UPLINK", 36, s_text, 32, 51, 660, 47, true);
		StoreText(_storePanel, "Subtitle", "Prepare your support. Deploy when it matters.", 18, s_muted, 34, 105, 650, 29);
		StoreText(_storePanel, "Stash label", "STASH BALANCE", 13, s_muted, 760, 23, 488, 22, true, TextAnchor.MiddleRight);
		_balanceText = StoreText(_storePanel, "Balance", "SYNC", 28, s_text, 760, 47, 488, 41, true, TextAnchor.MiddleRight);
		_refreshButton = StoreButton(_storePanel, "Refresh", "REFRESH", 1004, 103, 116, 36, StartRefresh, false);
		StoreButton(_storePanel, "Close", "CLOSE", 1132, 103, 116, 36, ClosePage, false);
		StoreBox(_storePanel, "Header divider", 32, 151, 1216, 1, s_line, s_line);
		_statusText = StoreText(_storePanel, "Status", "Refresh to load your services and stash balance.", 17, s_muted, 32, 168, 1216, 40);

		_rows.Clear();
		for (int index = 0; index < s_services.Length; index++)
		{
			ServiceDescriptor service = s_services[index];
			float x = 32 + index % 2 * 358;
			float y = 224 + index / 2 * 178;
			RectTransform card = StoreBox(_storePanel, $"Service {service.Type}", x, y, 340, 160, s_row, s_line);
			Image cardImage = card.GetComponent<Image>();
			cardImage.raycastTarget = true;
			Button select = card.gameObject.AddComponent<Button>();
			select.targetGraphic = cardImage;
			ColorBlock colors = select.colors;
			colors.normalColor = Color.white;
			colors.highlightedColor = new Color(1.25f, 1.20f, 1.08f, 1f);
			colors.pressedColor = new Color(0.85f, 0.80f, 0.70f, 1f);
			select.colors = colors;
			select.onClick.AddListener(() => SelectStoreService(service.Type));
			StoreIcon(card, "Service art", service.Type, 16, 16, 66, false);
			Text state = StoreText(card, "State", "SYNC", 13, s_muted, 96, 21, 218, 40, true);
			Text name = StoreText(card, "Name", service.DisplayName, 21, s_text, 16, 82, 308, 32, true);
			Text price = StoreText(card, "Price", "--", 22, s_amberHigh, 16, 121, 184, 31, true);
			Text owned = StoreText(card, "Held", "-- / --", 14, s_muted, 204, 124, 120, 25, true, TextAnchor.MiddleRight);
			_rows[service.Type] = new RowView(name, state, price, owned, select, cardImage, card.GetComponent<Outline>());
		}

		RectTransform detail = StoreBox(_storePanel, "Service detail", 760, 224, 488, 516, s_row, s_line);
		_detailArt = StoreIcon(detail, "Selected art", _selectedService, 176, 15, 136, true);
		_detailTitle = StoreText(detail, "Selected service", "", 28, s_text, 24, 161, 440, 67, true, TextAnchor.MiddleCenter);
		_detailDescription = StoreText(detail, "Description", "", 18, s_muted, 24, 236, 440, 72);
		_detailPriceCaption = StoreText(detail, "Price caption", "AUTHORIZATION COST", 13, s_muted, 24, 313, 277, 22, true);
		_detailPrice = StoreText(detail, "Selected price", "--", 34, s_amberHigh, 24, 339, 277, 47, true);
		StoreText(detail, "Held caption", "HELD / LIMIT", 13, s_muted, 314, 313, 150, 22, true, TextAnchor.MiddleRight);
		_detailOwned = StoreText(detail, "Selected held", "-- / --", 23, s_text, 314, 344, 150, 36, true, TextAnchor.MiddleRight);
		_detailState = StoreText(detail, "Availability", "", 16, s_greenHigh, 24, 395, 440, 53);
		_detailReview = StoreButton(detail, "Review purchase", "REVIEW PURCHASE", 24, 455, 440, 43,
			() => ShowPurchaseConfirmation(_selectedService), true);

		StoreBox(_storePanel, "Footer divider", 32, 759, 1216, 1, s_line, s_line);
		StoreText(_storePanel, "Footer", "Paid from your stash. Purchased authorizations are ready for your next raid.", 15, s_muted, 32, 776, 1034, 28);
		StoreButton(_storePanel, "Dashboard", "DASHBOARD", 1094, 775, 154, 32, OpenDashboard, false);
		BuildPurchaseConfirmation();
		UpdateStorefrontScale();
		_pageRoot.SetActive(false);
		Redraw();
	}

	private void BuildPurchaseConfirmation()
	{
		_purchaseConfirmationRoot = CreatePanel(_pageRoot.transform, "PurchaseConfirmation", new Color32(0, 0, 0, 215));
		Stretch(_purchaseConfirmationRoot.GetComponent<RectTransform>());
		_purchaseConfirmationRoot.GetComponent<Image>().raycastTarget = true;
		GameObject dialog = CreateBorderedPanel(_purchaseConfirmationRoot.transform, "Review authorization", s_panel, s_lineStrong);
		_storeDialog = dialog.GetComponent<RectTransform>();
		SetRect(_storeDialog, new Vector2(0.5f, 0.5f), new Vector2(ConfirmationWidth, ConfirmationHeight), Vector2.zero);
		StoreText(_storeDialog, "Brand", "TERRAGROUP / TSC UPLINK", 14, s_muted, 32, 20, 696, 24, true);
		_purchaseConfirmationTitle = StoreText(_storeDialog, "Title", "CONFIRM PURCHASE", 28, s_text, 32, 54, 696, 40, true);
		_confirmationArt = StoreIcon(_storeDialog, "Service art", ESupportType.Strafe, 32, 111, 112, true);
		_confirmationService = StoreText(_storeDialog, "Service", "", 28, s_text, 167, 119, 561, 83, true);
		_confirmationPriceCaption = StoreText(_storeDialog, "Price caption", "AUTHORIZATION COST", 13, s_muted, 32, 232, 696, 24, true);
		_confirmationPriceText = StoreText(_storeDialog, "Price", "", 38, s_amberHigh, 32, 264, 696, 51, true);
		RectTransform terms = StoreBox(_storeDialog, "Stash terms", 32, 329, 696, 80, s_row, s_line);
		StoreText(terms, "Before caption", "STASH BALANCE", 12, s_muted, 16, 9, 320, 22, true);
		_confirmationBefore = StoreText(terms, "Before", "", 22, s_text, 16, 35, 320, 32, true);
		_confirmationAfterCaption = StoreText(terms, "After caption", "AFTER PURCHASE", 12, s_muted, 358, 9, 320, 22, true);
		_confirmationAfter = StoreText(terms, "After", "", 22, s_text, 358, 35, 320, 32, true);
		_purchaseConfirmationBody = StoreText(_storeDialog, "Terms", "", 17, s_muted, 32, 428, 696, 112);
		_purchaseConfirmationBody.alignment = TextAnchor.UpperLeft;
		_purchaseConfirmationBody.lineSpacing = 1.05f;
		StoreButton(_storeDialog, "Cancel", "CANCEL", 32, 561, 194, 43, HidePurchaseConfirmation, false);
		_purchaseConfirmationConfirmButton = StoreButton(_storeDialog, "Confirm", "CONFIRM PURCHASE", 474, 561, 254, 43, ConfirmPurchase, true);
		_purchaseConfirmationRoot.SetActive(false);
	}

	private void SelectStoreService(ESupportType type)
	{
		if (IsPurchaseConfirmationOpen || !_rows.ContainsKey(type)) return;
		_selectedService = type;
		Redraw();
	}

	private void RedrawStoreDetail()
	{
		if (_detailTitle == null || !_rows.TryGetValue(_selectedService, out RowView row)) return;
		ServiceDescriptor service = GetDescriptor(_selectedService);
		_detailTitle.text = service.DisplayName;
		_detailDescription.text = StoreDescription(_selectedService);
		SetStoreIcon(_detailArt, _selectedService, true);
		_detailOwned.text = row.Owned.text;
		bool retry = !string.IsNullOrWhiteSpace(_ambiguousRequestId) && _ambiguousType == _selectedService;
		bool resolved = TryResolvePurchaseTerms(_snapshot, service, retry ? _ambiguousRequestId : string.Empty,
			out int price, out PaymentCurrency currency, out bool recoveredQuote);
		_detailPrice.text = resolved ? PaymentCurrencyInfo.Format(price, currency) : "--";
		_detailPriceCaption.text = retry && recoveredQuote ? "RECOVERY PRICE" :
			_selectedService == ESupportType.PriorityExfil ? "DISPATCH AUTHORIZATION" : "AUTHORIZATION COST";
		_detailReview.interactable = row.CanPurchase;
		_detailReview.GetComponentInChildren<Text>().text = row.ActionLabel;
		_detailState.text = StoreAvailabilityMessage(service, row, retry);
		bool insufficientFunds = !retry && resolved &&
			FireSupportServerConfigClient.GetSnapshotStashBalance(_snapshot, currency) is int balance && balance < price;
		_detailState.color = row.CanPurchase && !insufficientFunds ? s_greenHigh : s_amberHigh;
		foreach (KeyValuePair<ESupportType, RowView> entry in _rows)
		{
			bool selected = entry.Key == _selectedService;
			entry.Value.Background.color = selected ? new Color32(28, 29, 22, 255) : s_row;
			entry.Value.Border.effectColor = selected ? s_green : s_line;
			entry.Value.Name.color = selected ? s_amberHigh : s_text;
		}
	}

	private string StoreAvailabilityMessage(ServiceDescriptor service, RowView row, bool retry)
	{
		if (_purchasePending) return _pendingType == service.Type ? "Processing your purchase. Please wait." : "Another purchase is being processed.";
		if (_refreshPending) return "Refreshing your stash and authorizations...";
		if (!_ready || _snapshot == null) return "Refresh to load current prices and availability.";
		if (!FireSupportServiceAvailability.IsLocalUseAllowed(service.Type))
			return FireSupportServiceAvailability.GetLocalRestrictionReason(service.Type);
		if (retry) return "A previous purchase needs recovery. Review it to continue without a second completed charge.";
		if (!string.IsNullOrWhiteSpace(_ambiguousRequestId))
			return $"Recover the interrupted {GetDescriptor(_ambiguousType).DisplayName} purchase first.";
		if (!GetEnabled(_snapshot, service.ConfigKey)) return "This service is currently unavailable.";
		if (GetOwned(_snapshot, service.ConfigKey) >= GetMaximum(_snapshot)) return "Authorization limit reached for this service.";
		PaymentCurrency currency = FireSupportServerConfigClient.GetSnapshotCurrency(_snapshot);
		if (FireSupportServerConfigClient.GetSnapshotStashBalance(_snapshot, currency) is int balance &&
		    balance < GetPrice(_snapshot, service.ConfigKey)) return "Not enough stash funds for this authorization.";
		return service.Type == ESupportType.PriorityExfil
			? "Cargo only. No PMC extraction. A separate RUB handling fee applies when cargo is loaded."
			: "Adds one authorization. Deploy it from your Uplink menu in raid.";
	}

	private string StoreDescription(ESupportType type)
	{
		string description = type switch
		{
			ESupportType.Strafe => "Call in one A-10 autocannon pass over your designated target.",
			ESupportType.DoubleStrafe => "Two A-10 autocannon passes over your designated target.",
			ESupportType.Extract => "Request a UH-60 pickup at your marked landing zone.",
			ESupportType.PriorityExfil => "Send cargo out of the raid by helicopter while you stay on the ground.",
			ESupportType.FocusedSweep => "Request a focused sweep for nearby contacts.",
			_ => "Scan the surrounding area for nearby contacts."
		};
		RaidOpsFireSupportServerConfig.UavSettings settings = type == ESupportType.Uav ? _snapshot?.Uav :
			type == ESupportType.FocusedSweep ? _snapshot?.FocusedSweep : null;
		if (settings != null)
			description += $"\n{settings.DurationSeconds}s / {settings.RangeMeters:0.#}m radius / scan {settings.ScanIntervalSeconds:0.#}s";
		return description;
	}

	private void SetConfirmationPresentation(ServiceDescriptor service, int price, PaymentCurrency currency,
		int? balance, bool retry, bool recoveredQuote)
	{
		_purchaseConfirmationTitle.text = retry ? "REVIEW PURCHASE RECOVERY" : "CONFIRM PURCHASE";
		_confirmationService.text = service.DisplayName;
		SetStoreIcon(_confirmationArt, service.Type, true);
		_confirmationPriceCaption.text = retry ? (recoveredQuote ? "ORIGINAL PURCHASE PRICE" : "CURRENT LIST PRICE") :
			service.Type == ESupportType.PriorityExfil ? "DISPATCH AUTHORIZATION" : "AUTHORIZATION COST";
		_confirmationPriceText.text = PaymentCurrencyInfo.Format(price, currency);
		_confirmationBefore.text = balance.HasValue ? PaymentCurrencyInfo.Format(balance.Value, currency) : "UNAVAILABLE";
		_confirmationAfterCaption.text = retry ? "PAYMENT RECOVERY" : "AFTER PURCHASE";
		_confirmationAfter.text = retry ? "NO DUPLICATE CHARGE" : balance.HasValue ? FormatProjectedBalance(balance.Value, price, currency) : "UNAVAILABLE";
		_confirmationAfter.fontSize = retry ? 18 : 22;
		_purchaseConfirmationBody.text = retry
			? "Continue the interrupted purchase. Retrying cannot create a second completed charge."
			: "Purchase one authorization for a future raid. Payment is taken from your stash.";
		if (service.Type == ESupportType.PriorityExfil)
			_purchaseConfirmationBody.text += "\n\nCARGO ONLY: This service does not extract your PMC. A separate RUB handling fee is calculated when cargo is loaded.";
		_purchaseConfirmationConfirmButton.GetComponentInChildren<Text>().text = retry ? "CONFIRM RETRY" : "CONFIRM PURCHASE";
	}

	private void UpdateStorefrontScale()
	{
		if (_pageRoot == null || _storePanel == null) return;
		RectTransform parent = _pageRoot.transform.parent as RectTransform;
		if (parent == null) return;
		Vector2 size = parent.rect.size;
		if (size.x <= 0f || size.y <= 0f) return;
		float width = Mathf.Max(1f, size.x - 48f);
		float height = Mathf.Max(1f, size.y - 48f);
		float pageScale = Mathf.Min(1f, Mathf.Min(width / StoreWidth, height / StoreHeight));
		_storePanel.localScale = new Vector3(pageScale, pageScale, 1f);
		if (_storeDialog != null)
		{
			float dialogScale = Mathf.Min(1f, Mathf.Min(width / ConfirmationWidth, height / ConfirmationHeight));
			_storeDialog.localScale = new Vector3(dialogScale, dialogScale, 1f);
		}
	}

	private static RectTransform StoreBox(Transform parent, string name, float x, float y, float width, float height, Color fill, Color border)
	{
		RectTransform rect = CreateBorderedPanel(parent, name, fill, border).GetComponent<RectTransform>();
		SetStoreRect(rect, x, y, width, height);
		return rect;
	}
	private static Text StoreText(Transform parent, string name, string value, int fontSize, Color color,
		float x, float y, float width, float height, bool bold = false, TextAnchor alignment = TextAnchor.MiddleLeft)
	{
		Text text = CreateText(parent, name, value, fontSize, bold ? FontStyle.Bold : FontStyle.Normal, color,
			alignment, new Vector2(0f, 1f), new Vector2(width, height), new Vector2(x + width / 2f, -y - height / 2f));
		text.horizontalOverflow = HorizontalWrapMode.Wrap;
		text.verticalOverflow = VerticalWrapMode.Truncate;
		return text;
	}
	private static Button StoreButton(Transform parent, string name, string label, float x, float y, float width, float height, Action click, bool primary)
	{
		Button button = CreateButton(parent, name, label, new Vector2(0f, 1f), new Vector2(width, height),
			new Vector2(x + width / 2f, -y - height / 2f), click, primary ? ButtonVisual.Primary : ButtonVisual.Neutral);
		if (primary)
		{
			button.GetComponent<Image>().color = new Color32(205, 158, 84, 255);
			button.GetComponent<Outline>().effectColor = s_amberHigh;
			button.GetComponentInChildren<Text>().color = new Color32(10, 13, 13, 255);
		}
		return button;
	}
	private static void SetStoreRect(RectTransform rect, float x, float y, float width, float height) =>
		SetRect(rect, new Vector2(0f, 1f), new Vector2(width, height), new Vector2(x + width / 2f, -y - height / 2f));
	private static Image StoreIcon(Transform parent, string name, ESupportType type, float x, float y, float size, bool amber)
	{
		GameObject node = CreatePanel(parent, name, Color.white);
		SetStoreRect(node.GetComponent<RectTransform>(), x, y, size, size);
		Image image = node.GetComponent<Image>();
		image.preserveAspect = true;
		SetStoreIcon(image, type, amber);
		return image;
	}
	private static void SetStoreIcon(Image image, ESupportType type, bool amber)
	{
		image.sprite = LoadStoreIcon(type, amber);
		image.color = image.sprite == null ? Color.clear : Color.white;
	}
	private static Sprite LoadStoreIcon(ESupportType type, bool amber)
	{
		string icon = type switch
		{
			ESupportType.Extract => "extraction",
			ESupportType.PriorityExfil => "priority_exfil",
			ESupportType.Strafe => "a10_strafe",
			ESupportType.DoubleStrafe => "double_pass",
			ESupportType.FocusedSweep => "focused_sweep",
			_ => "uav_recon"
		};
		string key = $"{(amber ? "amber_512" : "neutral_512")}/{icon}.png";
		if (s_storeIcons.TryGetValue(key, out Sprite cached)) return cached;
		Sprite sprite = null;
		Texture2D texture = null;
		try
		{
			string directory = Path.GetDirectoryName(typeof(MainMenuPurchaseController).Assembly.Location) ?? string.Empty;
			string path = Path.Combine(directory, "assets", "content", "ui", "phone", "icons", key);
			if (File.Exists(path))
			{
				texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
				texture.hideFlags = HideFlags.HideAndDontSave;
				if (texture.LoadImage(File.ReadAllBytes(path), true))
				{
					texture.filterMode = FilterMode.Bilinear;
					texture.wrapMode = TextureWrapMode.Clamp;
					sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
					sprite.hideFlags = HideFlags.HideAndDontSave;
				}
			}
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource.LogWarning($"TSC storefront icon unavailable: {icon}. {ex.Message}");
		}
		if (sprite == null && texture != null) Destroy(texture);
		s_storeIcons[key] = sprite;
		return sprite;
	}
}
