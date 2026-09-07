using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public sealed partial class MainMenuPurchaseController
{
	private const float StoreWidth = 1580f;
	private const float StoreHeight = 650f;
	private const float ConfirmationWidth = 760f;
	private const float ConfirmationHeight = 620f;
	private static readonly Dictionary<string, Sprite> s_storeIcons = new();
	private RectTransform _storePanel;
	private RectTransform _storeDialog;
	private ESupportType _selectedService = ESupportType.Strafe;
	private Image _detailArt;
	private PilotServicesReconGraphic _detailRecon;
	private Text _detailTitle;
	private Text _detailDesignation;
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
		if (_storeHost == null)
		{
			FireSupportPlugin.LogSource.LogWarning("TSC purchase page could not find the Pilot Services content area.");
			return;
		}
		// The native Services screen already supplies the blurred environment.
		// Keep its camera, navigation and input ownership; only draw our content.
		_pageRoot = new GameObject(PageName, typeof(RectTransform), typeof(CanvasGroup),
			typeof(Image), typeof(RectMask2D));
		_pageRoot.transform.SetParent(_storeHost, false);
		Stretch(_pageRoot.GetComponent<RectTransform>());
		_pageRoot.GetComponent<Image>().color = s_background;
		_pageRoot.GetComponent<Image>().raycastTarget = true;

		GameObject panel = CreateBorderedPanel(_pageRoot.transform, "TSC Storefront", Color.clear, s_line);
		_storePanel = panel.GetComponent<RectTransform>();
		SetRect(_storePanel, new Vector2(0.5f, 0.5f), new Vector2(StoreWidth, StoreHeight), Vector2.zero);
		RectTransform header = StoreBox(_storePanel, "Pilot services header", 16, 12, 1548, 132, s_panel, s_line);
		StoreBox(header, "Portrait frame", 10, 8, 114, 114, s_panel, s_lineStrong);
		StoreArtwork(header, "Pilot portrait", "pilot-portrait.png", 12, 10, 110, 110, Color.white);
		StoreText(header, "Pilot name", "Pilot", 28, s_text, 148, 7, 500, 34, true);
		StoreText(header, "Pilot designation", "TERRAGROUP", 12, s_muted, 150, 41, 500, 17);
		const float headerControlTop = 63f;
		const float headerControlHeight = 38f;
		_servicesTab = StoreButton(header, "Services tab", "SERVICES", 148, headerControlTop, 214, headerControlHeight,
			() => SelectStoreTab(false), false);
		_historyTab = StoreButton(header, "History tab", "HISTORY", 362, headerControlTop, 200, headerControlHeight,
			() => SelectStoreTab(true), false);
		StoreText(header, "Stash label", "STASH BALANCE", 12, s_muted, 990, 40, 360, 20, false, TextAnchor.MiddleRight);
		_balanceText = StoreMoneyText(header, "Balance", "SYNC", 25, s_text, 970, 64, 380, 35, TextAnchor.MiddleRight);
		_refreshButton = StoreButton(header, "Refresh", "REFRESH", 1380, headerControlTop, 150, headerControlHeight, StartRefresh, false);

		_servicesContent = CreatePanel(_storePanel, "Services", Color.clear).GetComponent<RectTransform>();
		SetStoreRect(_servicesContent, 16, 152, 1548, 450);
		StoreText(_servicesContent, "Service column", "SERVICE / AVAILABILITY", 12, s_muted, 12, 0, 410, 24);
		StoreText(_servicesContent, "Price column", "COST", 12, s_muted, 552, 0, 205, 24, false, TextAnchor.MiddleRight);
		StoreText(_servicesContent, "Held column", "HELD / LIMIT", 12, s_muted, 775, 0, 104, 24, false, TextAnchor.MiddleRight);

		_rows.Clear();
		for (int index = 0; index < s_services.Length; index++)
		{
			ServiceDescriptor service = s_services[index];
			float y = 30 + index * 68;
			RectTransform card = StoreBox(_servicesContent, $"Service {service.Type}", 0, y, 928, 62, s_row, s_line);
			Image cardImage = card.GetComponent<Image>();
			cardImage.raycastTarget = true;
			Button select = card.gameObject.AddComponent<Button>();
			select.targetGraphic = cardImage;
			ColorBlock colors = select.colors;
			colors.normalColor = Color.white;
			colors.highlightedColor = new Color(1.16f, 1.16f, 1.12f, 1f);
			colors.pressedColor = new Color(0.84f, 0.84f, 0.80f, 1f);
			colors.fadeDuration = 0.12f;
			select.colors = colors;
			select.onClick.AddListener(() => SelectStoreService(service.Type));
			StoreIcon(card, "Service art", service.Type, 16, 5, 52, false);
			Text name = StoreText(card, "Name", service.DisplayName, 20, s_text, 106, 5, 438, 28, true);
			Text state = StoreText(card, "State", "SYNC", 13, s_muted, 106, 34, 438, 20);
			Text price = StoreMoneyText(card, "Price", "--", 20, s_amberHigh, 552, 15, 205, 32, TextAnchor.MiddleRight);
			Text owned = StoreText(card, "Held", "-- / --", 18, s_text, 775, 16, 104, 30, false, TextAnchor.MiddleRight);
			StoreText(card, "Open service", ">", 24, s_muted, 899, 17, 19, 26);
			_rows[service.Type] = new RowView(name, state, price, owned, select, cardImage, card.GetComponent<PilotServicesBorder>());
		}

		RectTransform detail = StoreBox(_servicesContent, "Service detail", 950, 0, 598, 432, s_panel, s_line);
		StoreBox(detail, "Detail title backing", 1, 1, 596, 42, s_row, Color.clear);
		_detailTitle = StoreText(detail, "Selected service", "", 26, s_text, 14, 4, 570, 34, true);
		_detailArt = StoreArtwork(detail, "Selected aircraft", "a10-detail.png", 18, 54, 562, 132, Color.white, true);
		GameObject recon = new("Recon preview", typeof(RectTransform), typeof(PilotServicesReconGraphic));
		recon.transform.SetParent(detail, false);
		SetStoreRect(recon.GetComponent<RectTransform>(), 18, 67, 562, 108);
		_detailRecon = recon.GetComponent<PilotServicesReconGraphic>();
		_detailRecon.raycastTarget = false;
		_detailRecon.gameObject.SetActive(false);
		_detailDesignation = StoreText(detail, "Aircraft designation", "", 11, s_muted, 24, 188, 550, 19);
		_detailDescription = StoreText(detail, "Description", "", 16, s_muted, 24, 213, 550, 47);
		StoreBox(detail, "Brief divider", 24, 273, 550, 1, s_line, Color.clear);
		_detailPriceCaption = StoreText(detail, "Price caption", "COST", 12, s_muted, 24, 281, 360, 20);
		_detailPrice = StoreMoneyText(detail, "Selected price", "--", 27, s_text, 24, 305, 360, 35);
		StoreText(detail, "Held caption", "HELD / LIMIT", 12, s_muted, 420, 281, 154, 20, false, TextAnchor.MiddleRight);
		_detailOwned = StoreText(detail, "Selected held", "-- / --", 23, s_text, 420, 307, 154, 31, false, TextAnchor.MiddleRight);
		_detailState = StoreText(detail, "Availability", "", 13, s_muted, 24, 345, 550, 32);
		_detailReview = StoreButton(detail, "Review purchase", "REVIEW PURCHASE", 24, 383, 550, 40,
			() => ShowPurchaseConfirmation(_selectedService), true);

		_statusText = StoreText(_storePanel, "Status", "Refresh to load your services and stash balance.", 13, s_muted, 28, 610, 1524, 28);
		BuildStoreHistory();
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
		GameObject dialog = CreateBorderedPanel(_purchaseConfirmationRoot.transform, "Review authorization", new Color32(20, 23, 22, 250), s_lineStrong);
		_storeDialog = dialog.GetComponent<RectTransform>();
		SetRect(_storeDialog, new Vector2(0.5f, 0.5f), new Vector2(ConfirmationWidth, ConfirmationHeight), Vector2.zero);
		StoreText(_storeDialog, "Brand", "PILOT / SERVICES", 14, s_muted, 32, 20, 696, 24, true);
		_purchaseConfirmationTitle = StoreText(_storeDialog, "Title", "CONFIRM PURCHASE", 28, s_text, 32, 54, 696, 40, true);
		_confirmationArt = StoreIcon(_storeDialog, "Service art", ESupportType.Strafe, 32, 111, 112, true);
		_confirmationService = StoreText(_storeDialog, "Service", "", 28, s_text, 167, 119, 561, 83, true);
		_confirmationPriceCaption = StoreText(_storeDialog, "Price caption", "AUTHORIZATION COST", 13, s_muted, 32, 232, 696, 24, true);
		_confirmationPriceText = StoreMoneyText(_storeDialog, "Price", "", 38, s_amberHigh, 32, 264, 696, 51);
		RectTransform terms = StoreBox(_storeDialog, "Stash terms", 32, 329, 696, 80, s_row, s_line);
		StoreText(terms, "Before caption", "STASH BALANCE", 12, s_muted, 16, 9, 320, 22, true);
		_confirmationBefore = StoreMoneyText(terms, "Before", "", 22, s_text, 16, 35, 320, 32);
		_confirmationAfterCaption = StoreText(terms, "After caption", "AFTER PURCHASE", 12, s_muted, 358, 9, 320, 22, true);
		_confirmationAfter = StoreMoneyText(terms, "After", "", 22, s_text, 358, 35, 320, 32);
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
		SetServiceDetailArtwork(_detailArt, _selectedService);
		if (_detailRecon != null)
		{
			_detailRecon.SetFocused(_selectedService == ESupportType.FocusedSweep);
			_detailRecon.gameObject.SetActive(_selectedService is ESupportType.Uav or ESupportType.FocusedSweep);
		}
		_detailDesignation.text = _selectedService switch
		{
			ESupportType.Strafe or ESupportType.DoubleStrafe => "CAS / A-10 THUNDERBOLT II",
			ESupportType.Extract => "AIRLIFT / UH-60 BLACK HAWK",
			ESupportType.PriorityExfil => "LOGISTICS / UH-60 BLACK HAWK",
			ESupportType.FocusedSweep => "ISR / FOCUSED SWEEP",
			_ => "ISR / UAV RECONNAISSANCE"
		};
		_detailOwned.text = row.Owned.text;
		bool retry = !string.IsNullOrWhiteSpace(_ambiguousRequestId) && _ambiguousType == _selectedService;
		bool resolved = TryResolvePurchaseTerms(_snapshot, service, retry ? _ambiguousRequestId : string.Empty,
			out int price, out PaymentCurrency currency, out bool recoveredQuote);
		_detailPrice.text = resolved ? PaymentCurrencyInfo.Format(price, currency) : "--";
		_detailPriceCaption.text = retry && recoveredQuote ? "RECOVERY PRICE" :
			_selectedService == ESupportType.PriorityExfil ? "DISPATCH COST" : "COST";
		_detailReview.interactable = row.CanPurchase;
		_detailReview.GetComponentInChildren<Text>().text = row.ActionLabel;
		_detailState.text = StoreAvailabilityMessage(service, row, retry);
		bool insufficientFunds = !retry && resolved &&
			FireSupportServerConfigClient.GetSnapshotStashBalance(_snapshot, currency) is int balance && balance < price;
		_detailState.color = row.CanPurchase && !insufficientFunds ? s_muted : s_red;
		foreach (KeyValuePair<ESupportType, RowView> entry in _rows)
		{
			bool selected = entry.Key == _selectedService;
			entry.Value.Background.color = selected ? new Color32(62, 60, 49, 185) : s_row;
			entry.Value.Border.effectColor = selected ? s_lineStrong : s_line;
			entry.Value.Name.color = s_text;
		}
	}

	private string StoreAvailabilityMessage(ServiceDescriptor service, RowView row, bool retry)
	{
		if (_purchasePending) return _pendingType == service.Type ? "Processing your purchase. Please wait." : "Another purchase is being processed.";
		if (_refreshPending) return "Refreshing your stash and authorizations...";
		if (!_ready || _snapshot == null) return "Refresh to load current prices and availability.";
		if (retry) return "A previous purchase needs recovery. Review it to continue without a second completed charge.";
		if (!FireSupportServiceAvailability.IsLocalUseAllowed(service.Type))
			return FireSupportServiceAvailability.GetLocalRestrictionReason(service.Type);
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
			service.Type == ESupportType.PriorityExfil ? "DISPATCH COST" : "COST";
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
		if (_pageRoot == null || _storePanel == null || _storeHost == null) return;
		Vector2 size = _storeHost.rect.size;
		if (size.x <= 0f || size.y <= 0f) return;
		float width = Mathf.Max(1f, size.x - 16f);
		float height = Mathf.Max(1f, size.y - 16f);
		float pageScale = Mathf.Min(width / StoreWidth, height / StoreHeight);
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
	private static Text StoreMoneyText(Transform parent, string name, string value, int fontSize, Color color,
		float x, float y, float width, float height, TextAnchor alignment = TextAnchor.MiddleLeft)
	{
		Text text = StoreText(parent, name, value, fontSize, color, x, y, width, height, true, alignment);
		// Bender has no ruble glyph. Keep every amount in a font that supports
		// the configured currency, including fields populated after creation.
		text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
		text.fontStyle = FontStyle.Bold;
		return text;
	}
	private static Button StoreButton(Transform parent, string name, string label, float x, float y, float width, float height, Action click, bool primary)
	{
		Button button = CreateButton(parent, name, label, new Vector2(0f, 1f), new Vector2(width, height),
			new Vector2(x + width / 2f, -y - height / 2f), click, primary ? ButtonVisual.Primary : ButtonVisual.Neutral);
		if (primary)
		{
			button.GetComponent<Image>().color = new Color32(172, 167, 146, 255);
			button.GetComponent<PilotServicesBorder>().effectColor = new Color32(184, 180, 158, 170);
			button.GetComponentInChildren<Text>().color = new Color32(10, 13, 13, 255);
		}
		AddStoreSurface(button.GetComponent<RectTransform>(), primary ? 0.35f : 0.15f);
		return button;
	}
	private static void StoreRibbon(Transform parent, float x, float y, float width, float height)
	{
		GameObject node = new("Selected service section", typeof(RectTransform), typeof(PilotServicesRibbonGraphic));
		node.transform.SetParent(parent, false);
		SetStoreRect(node.GetComponent<RectTransform>(), x, y, width, height);
		node.GetComponent<PilotServicesRibbonGraphic>().raycastTarget = false;
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
		image.color = image.sprite == null ? Color.clear : s_text;
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
		string key = $"mask_512/{icon}.png";
		return LoadStoreSprite(key);
	}
	private static Sprite LoadStoreSprite(string key)
	{
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
			FireSupportPlugin.LogSource.LogWarning($"TSC storefront icon unavailable: {key}. {ex.Message}");
		}
		if (sprite == null && texture != null) Destroy(texture);
		s_storeIcons[key] = sprite;
		return sprite;
	}
}
