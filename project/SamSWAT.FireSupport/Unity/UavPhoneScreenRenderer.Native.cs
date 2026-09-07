using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public sealed partial class UavPhoneScreenRenderer
{
	// Purchase screens use the same restrained palette as the native deploy menu.
	// Images are optional transparent service icons; every panel and label is native UI.
	private static readonly Color NativeInk = new(0.863f, 0.847f, 0.784f, 1f);
	private static readonly Color NativeMuted = new(0.616f, 0.600f, 0.549f, 1f);
	private static readonly Color NativeAmber = new(0.804f, 0.620f, 0.329f, 1f);
	private static readonly Color NativeGreen = new(0.443f, 0.616f, 0.275f, 1f);
	private static readonly Color NativeRed = new(0.78f, 0.38f, 0.30f, 1f);
	private static readonly Color NativePanel = new(0.071f, 0.082f, 0.082f, 1f);
	private static readonly Color NativeLine = new(0.282f, 0.294f, 0.278f, 0.65f);
	private readonly Dictionary<TerraGroupPhoneState, List<Action>> _nativeBindings = new();
	private RectTransform _nativeSwipeArrow;
	private CanvasGroup _nativeSwipeVisual;
	private RectTransform _nativeSwipeFill;
	private NativeLayout _nativeSwipeLayout;
	private RectTransform _nativePendingMarker;
	private NativeLayout _nativePendingLayout;
	private float _nativeNextRefreshAt;

	// A shared design grid is fitted to both dimensions, rather than assuming a
	// particular mesh aspect ratio. Hit rectangles use these exact same transforms.
	private readonly struct NativeLayout
	{
		internal readonly RectTransform Root;
		internal readonly TerraGroupPhoneState State;
		internal readonly float Scale;
		private readonly Vector2 _offset;
		internal NativeLayout(RectTransform root, TerraGroupPhoneState state, bool portrait)
		{
			Root = root;
			State = state;
			Vector2 basis = portrait ? new Vector2(576f, 1024f) : new Vector2(1024f, 576f);
			Scale = Mathf.Min(root.sizeDelta.x / basis.x, root.sizeDelta.y / basis.y);
			_offset = (root.sizeDelta - basis * Scale) * 0.5f;
		}
		internal Rect R(float x, float y, float width, float height) =>
			new(_offset.x + x * Scale, _offset.y + y * Scale, width * Scale, height * Scale);
		internal int F(int size) => Mathf.Max(8, Mathf.RoundToInt(size * Scale));
	}

	private NativeLayout NativeScreen(string name, TerraGroupPhoneState state, out CanvasGroup group, bool portrait = false)
	{
		RectTransform root = CreateScreenRoot(name, portrait);
		root.GetComponent<Image>().color = new Color(0.020f, 0.027f, 0.027f, 1f);
		group = root.gameObject.AddComponent<CanvasGroup>();
		_nativeBindings[state] = new List<Action>();
		RegisterPointerRoot(state, root, portrait);
		return new NativeLayout(root, state, portrait);
	}

	private Text NativeText(NativeLayout layout, string value, int size, Color color,
		float x, float y, float width, float height, bool bold = false,
		TextAnchor alignment = TextAnchor.MiddleLeft, Func<string> live = null)
	{
		Text text = AddText(layout.Root, value, layout.F(size), bold ? FontStyle.Bold : FontStyle.Normal,
			color, layout.R(x, y, width, height), alignment);
		if (live != null)
		{
			Action update = () => { if (text != null) text.text = live(); };
			_nativeBindings[layout.State].Add(update);
			update();
		}
		return text;
	}

	private RectTransform NativeBox(NativeLayout layout, float x, float y, float width, float height,
		Color? fill = null, Color? border = null)
	{
		RectTransform box = NativeRectangle(layout.Root, layout.R(x, y, width, height), fill ?? NativePanel);
		Color edge = border ?? NativeLine;
		float thickness = Mathf.Max(1f, layout.Scale);
		AddLine(box, new Rect(0, 0, box.sizeDelta.x, thickness), edge);
		AddLine(box, new Rect(0, box.sizeDelta.y - thickness, box.sizeDelta.x, thickness), edge);
		AddLine(box, new Rect(0, 0, thickness, box.sizeDelta.y), edge);
		AddLine(box, new Rect(box.sizeDelta.x - thickness, 0, thickness, box.sizeDelta.y), edge);
		return box;
	}

	private RectTransform NativeRectangle(RectTransform parent, Rect rect, Color color)
	{
		GameObject node = new("Native phone panel");
		node.layer = RenderLayer;
		node.transform.SetParent(parent, false);
		RectTransform transform = node.AddComponent<RectTransform>();
		transform.anchorMin = transform.anchorMax = transform.pivot = new Vector2(0f, 1f);
		transform.anchoredPosition = new Vector2(rect.x, -rect.y);
		transform.sizeDelta = new Vector2(rect.width, rect.height);
		Image image = node.AddComponent<Image>();
		image.sprite = WhiteSprite;
		image.color = color;
		image.raycastTarget = false;
		return transform;
	}

	private void NativeIcon(NativeLayout layout, string icon, float x, float y, float size, bool amber = false)
	{
		Sprite sprite = LoadOverlaySprite($"icons/{(amber ? "amber_512" : "neutral_512")}/{icon}.png");
		if (sprite == null) return;
		RectTransform node = NativeRectangle(layout.Root, layout.R(x, y, size, size), Color.white);
		Image image = node.GetComponent<Image>();
		image.sprite = sprite;
		image.preserveAspect = true;
	}

	private void NativeButton(NativeLayout layout, string label, float x, float y, float width, float height,
		PhonePointerAction action, bool primary = false, Func<bool> enabled = null, Func<string> live = null)
	{
		RectTransform box = NativeBox(layout, x, y, width, height,
			primary ? NativeAmber : NativePanel, primary ? NativeAmber : NativeLine);
		Text text = NativeText(layout, label, 20, primary ? new Color(0.04f, 0.05f, 0.05f, 1f) : NativeInk,
			x + 10, y + 4, width - 20, height - 8, true, TextAnchor.MiddleCenter, live);
		if (enabled != null)
		{
			_nativeBindings[layout.State].Add(() =>
			{
				bool available = enabled();
				box.GetComponent<Image>().color = available && primary ? NativeAmber : NativePanel;
				text.color = available ? (primary ? new Color(0.04f, 0.05f, 0.05f, 1f) : NativeInk) : NativeMuted;
			});
		}
		AddPointerRegion(layout.State, layout.Root, layout.R(x, y, width, height), action, enabled);
	}

	private void NativeChrome(NativeLayout layout, bool portrait = false)
	{
		float width = portrait ? 576 : 1024;
		NativeText(layout, "TERRAGROUP", 25, NativeInk, 32, 20, 300, 32, true);
		NativeText(layout, "TACTICAL SERVICES", 13, NativeMuted, 33, 51, 290, 22, true);
		NativeText(layout, "SECURE LINK", 14, NativeGreen, width - 215, 25, 180, 26, true, TextAnchor.MiddleRight);
		NativeText(layout, "", 13, NativeMuted, width - 287, 51, 252, 22, false, TextAnchor.MiddleRight,
			() => portrait ? DateTime.Now.ToString("HH:mm") : NativeInputHint());
		AddLine(layout.Root, layout.R(32, 87, width - 64, 1), NativeLine);
	}

	private void NativeWallet(NativeLayout layout, float x = 32, float y = 491, float width = 460)
	{
		NativeText(layout, "", 14, NativeMuted, x, y, width, 21, true, live:
			() => FireSupportPayment.GetEffectiveBalanceLabel().ToUpperInvariant());
		NativeText(layout, "", 25, NativeInk, x, y + 23, width, 33, true, live: NativeBalance);
	}

	private static string NativeMoney(int amount) => FormatCurrency(amount, FireSupportPayment.GetActivePaymentCurrency());
	private static string NativeBalance() => NativeMoney(FireSupportPayment.GetEffectiveBalance());
	private static string NativeInputHint()
	{
		if (!(PluginSettings.PhoneMouseEnabled?.Value ?? true)) return "KEYBOARD CONTROLS";
		KeyCode key = PluginSettings.PhoneMouseModifier?.Value ?? KeyCode.LeftAlt;
		string label = key == KeyCode.LeftAlt || key == KeyCode.RightAlt ? "ALT" : key.ToString().ToUpperInvariant();
		return key == KeyCode.None ? "KEYBOARD CONTROLS" : $"HOLD {label} + MOUSE";
	}
	private static string NativePrice(ESupportType type) => NativeMoney(FireSupportPayment.GetActiveCost(type));
	private static string NativeServiceIcon(ESupportType type) => type switch
	{
		ESupportType.Extract => "extraction",
		ESupportType.PriorityExfil => "priority_exfil",
		ESupportType.Strafe => "a10_strafe",
		ESupportType.DoubleStrafe => "double_pass",
		ESupportType.FocusedSweep => "focused_sweep",
		_ => "uav_recon"
	};
	private static ESupportType NativeCategoryBase(ESupportType type) => type switch
	{
		ESupportType.Extract or ESupportType.PriorityExfil => ESupportType.Extract,
		ESupportType.Strafe or ESupportType.DoubleStrafe => ESupportType.Strafe,
		_ => ESupportType.Uav
	};
	private static ESupportType NativeVariant(ESupportType type) => NativeCategoryBase(type) switch
	{
		ESupportType.Extract => ESupportType.PriorityExfil,
		ESupportType.Strafe => ESupportType.DoubleStrafe,
		_ => ESupportType.FocusedSweep
	};
	private static bool NativeAvailable(ESupportType type) => FireSupportServiceAvailability.IsServiceEnabled(type);
	private static string NativeHeld(ESupportType type) => $"{FireSupportAuthorizations.Get(type)} HELD";
	private string NativeParameters() => _context.SupportType switch
	{
		ESupportType.Uav or ESupportType.FocusedSweep =>
			$"{UavReconSettings.GetDurationSeconds(_context.SupportType)} SEC  /  {Mathf.RoundToInt(UavReconSettings.GetRangeMeters(_context.SupportType))} M RADIUS  /  SCAN {UavReconSettings.GetScanInterval(_context.SupportType):0.#}S",
		ESupportType.Strafe => "ONE AUTOCANNON PASS",
		ESupportType.DoubleStrafe => "TWO AUTOCANNON PASSES",
		ESupportType.PriorityExfil => "CARGO PICKUP ONLY",
		_ => "HELICOPTER EXTRACTION"
	};
	private static string NativeRestriction(ESupportType type)
	{
		string reason = FireSupportServiceAvailability.GetLocalRestrictionReason(type);
		return string.IsNullOrEmpty(reason) ? "Disabled by service settings" : reason;
	}
	private string NativePurchaseNote() => !NativeAvailable(_context.SupportType)
		? NativeRestriction(_context.SupportType)
		: _context.SupportType == ESupportType.PriorityExfil
			? "Dispatch authorization only. RUB handling fee is charged separately when cargo is loaded."
			: "Adds one authorization. Deploy it when you are ready.";
	private bool NativeCanConfirm() => NativeAvailable(_context.SupportType) && FireSupportPayment.CanAfford(_context.SupportType);
	private string NativeConfirmLabel()
	{
		if (!NativeAvailable(_context.SupportType)) return "SERVICE LOCKED";
		if (FireSupportPayment.GetEffectiveBalance() < 0 && FireSupportPayment.GetActiveCost(_context.SupportType) > 0) return "BALANCE SYNCING";
		return NativeCanConfirm() ? "CONFIRM PURCHASE  >" : "INSUFFICIENT FUNDS";
	}

	private void BuildHomeScreen()
	{
		NativeLayout layout = NativeScreen("Native phone Home", TerraGroupPhoneState.Home, out _homeGroup);
		NativeChrome(layout);
		NativeText(layout, "FIELD SUPPORT", 43, NativeInk, 40, 125, 650, 58, true);
		NativeText(layout, "Authorization terminal", 23, NativeAmber, 42, 191, 650, 36);
		NativeText(layout, "Air support, extraction, and reconnaissance.\nPurchase an authorization. Deploy on your terms.",
			23, NativeMuted, 42, 250, 585, 100);
		NativeIcon(layout, "terragroup_logo", 715, 150, 225);
		NativeBox(layout, 40, 382, 944, 64);
		NativeText(layout, "", 17, NativeGreen, 58, 392, 575, 42, true, live:
			() => string.IsNullOrEmpty(FireSupportProgression.RestrictionReason)
				? "SERVICES ONLINE" : FireSupportProgression.RestrictionReason);
		NativeText(layout, "", 17, NativeInk, 640, 392, 325, 42, true, TextAnchor.MiddleRight,
			() => $"{NativeTotalHeld()} AUTHORIZATIONS HELD");
		NativeWallet(layout, 40);
		NativeButton(layout, "CLOSE", 558, 491, 128, 56, new PhonePointerAction(PhonePointerActionKind.Close));
		NativeButton(layout, "OPEN SERVICES  >", 704, 491, 280, 56, new PhonePointerAction(PhonePointerActionKind.OpenServices), true);
	}

	private static int NativeTotalHeld() => FireSupportAuthorizations.Get(ESupportType.Extract) +
		FireSupportAuthorizations.Get(ESupportType.PriorityExfil) + FireSupportAuthorizations.Get(ESupportType.Strafe) +
		FireSupportAuthorizations.Get(ESupportType.DoubleStrafe) + FireSupportAuthorizations.Get(ESupportType.Uav) +
		FireSupportAuthorizations.Get(ESupportType.FocusedSweep);

	private void BuildTacticalServicesScreen()
	{
		NativeLayout layout = NativeScreen("Native phone Services", TerraGroupPhoneState.TacticalServices, out _tacticalServicesGroup);
		NativeChrome(layout);
		NativeText(layout, "SELECT CATEGORY", 34, NativeInk, 32, 111, 900, 49, true);
		NativeText(layout, "", 19, NativeMuted, 34, 164, 920, 29, live:
			() => string.IsNullOrEmpty(FireSupportProgression.RestrictionReason)
				? "Choose a service family to view its authorizations." : FireSupportProgression.RestrictionReason);
		ESupportType[] types = { ESupportType.Extract, ESupportType.Strafe, ESupportType.Uav };
		string[] titles = { "EXTRACTION", "FIRE SUPPORT", "RECON" };
		string[] descriptions = { "Helicopter pickup\n& cargo transfer", "A-10 autocannon\nsingle or double pass", "Local reconnaissance\n& focused sweeps" };
		for (int i = 0; i < types.Length; i++)
		{
			ESupportType type = types[i];
			float x = 32 + i * 326;
			NativeBox(layout, x, 217, 308, 240);
			NativeIcon(layout, NativeServiceIcon(type), x + 17, 231, 74);
			NativeText(layout, $"0{i + 1}", 16, NativeAmber, x + 237, 231, 50, 29, true, TextAnchor.MiddleRight);
			NativeText(layout, titles[i], 24, NativeInk, x + 18, 312, 272, 35, true);
			NativeText(layout, descriptions[i], 18, NativeMuted, x + 18, 350, 272, 53);
			NativeText(layout, "", 15, NativeGreen, x + 18, 416, 272, 24, true, live:
				() => $"{FireSupportAuthorizations.Get(type) + FireSupportAuthorizations.Get(NativeVariant(type))} HELD   /   VIEW SERVICES  >");
			AddPointerRegion(layout.State, layout.Root, layout.R(x, 217, 308, 240),
				new PhonePointerAction(PhonePointerActionKind.OpenCategory, type));
		}
		NativeWallet(layout);
		NativeButton(layout, "<  BACK", 808, 491, 184, 56, new PhonePointerAction(PhonePointerActionKind.Back));
	}

	private void BuildServiceCategoryScreen()
	{
		NativeLayout layout = NativeScreen("Native phone Category", TerraGroupPhoneState.ServiceCategory, out _serviceCategoryGroup);
		NativeChrome(layout);
		NativeText(layout, "SELECT SERVICE", 33, NativeInk, 32, 105, 700, 47, true);
		ESupportType[] tabs = { ESupportType.Extract, ESupportType.Strafe, ESupportType.Uav };
		string[] labels = { "EXTRACTION", "FIRE SUPPORT", "RECON" };
		for (int i = 0; i < tabs.Length; i++)
		{
			NativeButton(layout, labels[i], 32 + i * 218, 162, 204, 44,
				new PhonePointerAction(PhonePointerActionKind.OpenCategory, tabs[i]), NativeCategoryBase(_context.SupportType) == tabs[i]);
		}
		ESupportType[] types = { NativeCategoryBase(_context.SupportType), NativeVariant(_context.SupportType) };
		for (int i = 0; i < types.Length; i++)
		{
			ESupportType type = types[i];
			bool selected = type == _context.SupportType;
			float y = 229 + i * 110;
			NativeBox(layout, 32, y, 465, 96, selected ? new Color(0.090f, 0.090f, 0.075f, 1f) : NativePanel,
				selected ? NativeGreen : NativeLine);
			NativeIcon(layout, NativeServiceIcon(type), 45, y + 15, 64, selected);
			NativeText(layout, $"{i + 1}  {GetServiceTitle(type)}", 20, selected ? NativeAmber : NativeInk, 122, y + 10, 354, 31, true);
			NativeText(layout, "", 22, NativeInk, 122, y + 46, 214, 35, true, live: () => NativePrice(type));
			NativeText(layout, "", 14, NativeGreen, 338, y + 46, 138, 35, true, TextAnchor.MiddleRight,
				() => NativeAvailable(type) ? NativeHeld(type) : "LOCKED");
			// Locked rows remain inspectable so their price and details are visible.
			AddPointerRegion(layout.State, layout.Root, layout.R(32, y, 465, 96),
				new PhonePointerAction(PhonePointerActionKind.SelectService, type));
		}
		NativeBox(layout, 518, 229, 474, 206);
		NativeText(layout, GetServiceTitle(_context.SupportType), 24, NativeInk, 540, 244, 430, 38, true);
		NativeText(layout, "", 19, NativeMuted, 540, 288, 430, 72, live:
			() => NativeAvailable(_context.SupportType)
				? GetServiceDescription(_context.SupportType) : NativeRestriction(_context.SupportType));
		NativeText(layout, "", 17, NativeAmber, 540, 369, 430, 27, true, live: NativeParameters);
		NativeText(layout, "", 14, NativeGreen, 540, 400, 430, 24, true, live:
			() => NativeAvailable(_context.SupportType) ? $"{NativeHeld(_context.SupportType)}   /   DEPLOY VIA UPLINK" : "SERVICE LOCKED");
		NativeWallet(layout);
		NativeButton(layout, "<  BACK", 524, 491, 154, 56, new PhonePointerAction(PhonePointerActionKind.Back));
		NativeButton(layout, "REVIEW AUTHORIZATION  >", 694, 491, 298, 56,
			new PhonePointerAction(PhonePointerActionKind.ReviewService, _context.SupportType), true,
			() => NativeAvailable(_context.SupportType));
	}

	private void BuildRequestScreen() => _requestGroup = BuildNativeReview(TerraGroupPhoneState.ServiceReview);
	private void BuildRotateToConfirmScreen() => _rotateGroup = BuildNativeReview(TerraGroupPhoneState.RotateToConfirm);
	private CanvasGroup BuildNativeReview(TerraGroupPhoneState state)
	{
		NativeLayout layout = NativeScreen($"Native phone {state}", state, out CanvasGroup group);
		NativeChrome(layout);
		NativeText(layout, "REVIEW AUTHORIZATION", 33, NativeInk, 32, 106, 910, 47, true);
		NativeText(layout, "One authorization will be added after payment approval.", 19, NativeMuted, 34, 157, 915, 32);
		NativeBox(layout, 32, 213, 526, 251);
		NativeIcon(layout, NativeServiceIcon(_context.SupportType), 50, 228, 80, true);
		NativeText(layout, GetServiceTitle(_context.SupportType), 26, NativeInk, 149, 229, 387, 63, true);
		NativeText(layout, GetServiceDescription(_context.SupportType), 20, NativeMuted, 52, 311, 486, 62);
		NativeText(layout, "", 17, NativeAmber, 52, 382, 486, 28, true, live: NativeParameters);
		NativeText(layout, "", 15, NativeGreen, 52, 421, 486, 25, true, live: () => NativeHeld(_context.SupportType));
		NativeBox(layout, 577, 213, 415, 251);
		NativeText(layout, _context.SupportType == ESupportType.PriorityExfil ? "DISPATCH AUTHORIZATION" : "AUTHORIZATION COST", 14, NativeMuted, 599, 230, 371, 28, true);
		NativeText(layout, "", 38, NativeAmber, 599, 265, 371, 53, true, live: () => NativePrice(_context.SupportType));
		AddLine(layout.Root, layout.R(599, 329, 371, 1), NativeLine);
		NativeText(layout, "", 13, NativeMuted, 599, 343, 371, 25, true, live: () => FireSupportPayment.GetEffectiveBalanceLabel().ToUpperInvariant());
		NativeText(layout, "", 25, NativeInk, 599, 372, 371, 34, true, live: NativeBalance);
		NativeText(layout, "", 15, NativeGreen, 599, 423, 371, 25, true, live: () => NativeCanConfirm() ? "PAYMENT AVAILABLE" : NativeConfirmLabel());
		NativeText(layout, "", 14, NativeMuted, 32, 482, 489, 66, live: NativePurchaseNote);
		NativeText(layout, "ENTER  CONFIRM", 12, NativeMuted, 698, 550, 294, 19, false, TextAnchor.MiddleCenter);
		NativeButton(layout, "<  BACK", 542, 491, 141, 56, new PhonePointerAction(PhonePointerActionKind.Back));
		NativeButton(layout, "", 698, 491, 294, 56,
			new PhonePointerAction(PhonePointerActionKind.ConfirmPurchase, _context.SupportType), true,
			NativeCanConfirm, NativeConfirmLabel);
		return group;
	}

	private NativeLayout NativePortraitInvoice(TerraGroupPhoneState state, string title, string subtitle,
		string icon, Color accent, out CanvasGroup group)
	{
		NativeLayout layout = NativeScreen($"Native phone {state}", state, out group, true);
		NativeChrome(layout, true);
		NativeText(layout, title, 32, NativeInk, 32, 112, 512, 56, true, TextAnchor.MiddleCenter);
		NativeText(layout, subtitle, 17, accent, 40, 173, 496, 36, true, TextAnchor.MiddleCenter);
		NativeIcon(layout, icon, 223, 225, 130, true);
		NativeText(layout, GetServiceTitle(_context.SupportType), 27, NativeInk, 42, 373, 492, 68, true, TextAnchor.MiddleCenter);
		NativeText(layout, _context.SupportType == ESupportType.PriorityExfil ? "DISPATCH AUTHORIZATION" : "AUTHORIZATION COST", 14, NativeMuted, 42, 457, 492, 27, true, TextAnchor.MiddleCenter);
		NativeText(layout, "", 42, NativeAmber, 42, 491, 492, 60, true, TextAnchor.MiddleCenter, () => NativePrice(_context.SupportType));
		NativeBox(layout, 42, 581, 492, 95);
		NativeText(layout, "", 13, NativeMuted, 60, 592, 456, 26, true, live: () => FireSupportPayment.GetEffectiveBalanceLabel().ToUpperInvariant());
		NativeText(layout, "", 24, NativeInk, 60, 624, 296, 35, true, live: NativeBalance);
		NativeText(layout, "", 14, NativeGreen, 358, 624, 158, 35, true, TextAnchor.MiddleRight, () => NativeHeld(_context.SupportType));
		return layout;
	}

	private void BuildConfirmPaymentPortraitScreen()
	{
		NativeLayout layout = NativePortraitInvoice(TerraGroupPhoneState.ConfirmPaymentPortrait, "CONFIRMING PURCHASE", "SECURE AUTHORIZATION", NativeServiceIcon(_context.SupportType), NativeAmber, out _confirmPaymentGroup);
		NativeBox(layout, 42, 708, 492, 206);
		NativeText(layout, "SWIPE TO AUTHORIZE", 17, NativeAmber, 62, 722, 452, 26, true, TextAnchor.MiddleCenter);
		NativeText(layout, "SECURE TRANSFER", 15, NativeMuted, 60, 880, 456, 22, false, TextAnchor.MiddleCenter);
		_nativeSwipeLayout = layout;
		RectTransform visual = NativeRectangle(layout.Root, layout.R(0, 0, 576, 1024), Color.clear);
		_nativeSwipeVisual = visual.gameObject.AddComponent<CanvasGroup>();
		_nativeSwipeVisual.alpha = 0;
		_nativeSwipeVisual.blocksRaycasts = false;
		_nativeSwipeVisual.interactable = false;
		_nativeSwipeArrow = NativeRectangle(visual, new Rect(0, 0, 47 * layout.Scale, 84 * layout.Scale), Color.white);
		Image arrow = _nativeSwipeArrow.GetComponent<Image>();
		arrow.sprite = LoadOverlaySprite(SwipeArrowRelativePath);
		arrow.preserveAspect = true;
		if (arrow.sprite == null)
		{
			arrow.color = Color.clear;
			AddText(_nativeSwipeArrow, "^", layout.F(42), FontStyle.Bold, NativeAmber,
				new Rect(0, 0, 47 * layout.Scale, 84 * layout.Scale), TextAnchor.MiddleCenter);
		}
		_nativeSwipeFill = NativeRectangle(layout.Root, layout.R(64, 920, 0, 4), NativeAmber);
		NativeText(layout, "", 14, NativeMuted, 42, 942, 492, 57, false, TextAnchor.MiddleCenter, NativePurchaseNote);
	}

	private void BuildAuthorizingScreen()
	{
		NativeLayout layout = NativePortraitInvoice(TerraGroupPhoneState.Authorizing, "AUTHORIZING", "AWAITING PAYMENT APPROVAL", "lock", NativeAmber, out _authorizingGroup);
		NativeBox(layout, 42, 718, 492, 181);
		NativeText(layout, "PROCESSING REQUEST", 21, NativeAmber, 62, 744, 452, 34, true, TextAnchor.MiddleCenter);
		NativeText(layout, "The service will be available once\nthe transaction is confirmed.", 19, NativeMuted, 62, 800, 452, 68, false, TextAnchor.MiddleCenter);
		NativeRectangle(layout.Root, layout.R(64, 920, 448, 4), NativeLine);
		_nativePendingLayout = layout;
		_nativePendingMarker = NativeRectangle(layout.Root, layout.R(64, 920, 112, 4), NativeAmber);
	}

	private void BuildAuthorizedScreen()
	{
		NativeLayout layout = NativePortraitInvoice(TerraGroupPhoneState.Authorized, "AUTHORIZED", "PURCHASE APPROVED", "success", NativeGreen, out _authorizedGroup);
		NativeBox(layout, 42, 718, 492, 181, border: NativeGreen);
		NativeText(layout, "READY TO DEPLOY", 24, NativeGreen, 62, 742, 452, 36, true, TextAnchor.MiddleCenter);
		NativeText(layout, "", 20, NativeInk, 62, 793, 452, 69, false, TextAnchor.MiddleCenter, () =>
			$"{FireSupportAuthorizations.Get(_context.SupportType)} authorization(s) held.\nDeploy from your Uplink menu.");
		NativeText(layout, "Payment confirmed. Closing secure terminal.", 16, NativeMuted, 42, 932, 492, 46, false, TextAnchor.MiddleCenter);
	}

	private void BuildDeniedScreen()
	{
		NativeLayout layout = NativePortraitInvoice(TerraGroupPhoneState.Denied, "REQUEST DENIED", "AUTHORIZATION UNAVAILABLE", "warning", NativeRed, out _deniedGroup);
		NativeBox(layout, 42, 708, 492, 220, border: NativeRed);
		_deniedReasonText = NativeText(layout, "", 22, NativeRed, 62, 727, 452, 57, true, TextAnchor.MiddleCenter,
			() => FireSupportPayment.GetLastPurchaseDenialTitle(_context.SupportType));
		_deniedDetailText = NativeText(layout, "", 18, NativeMuted, 62, 797, 452, 108, false, TextAnchor.MiddleCenter,
			() => FireSupportPayment.GetLastPurchaseDenialDetail(_context.SupportType));
		NativeText(layout, "Review the result before trying again.", 16, NativeMuted, 42, 953, 492, 42, false, TextAnchor.MiddleCenter);
	}

	private void RefreshNativeState(TerraGroupPhoneState state)
	{
		if (_nativeBindings.TryGetValue(state, out List<Action> bindings))
			foreach (Action update in bindings) update();
		_nativeNextRefreshAt = Time.unscaledTime + 0.25f;
	}

	private void UpdateNativeScreen()
	{
		if (_shutdown || _radarHudMode) return;
		if (Time.unscaledTime >= _nativeNextRefreshAt) RefreshNativeState(_currentState);
		if (_currentState == TerraGroupPhoneState.Authorizing && _nativePendingMarker != null)
		{
			Rect rect = _nativePendingLayout.R(64 + Mathf.PingPong(Time.unscaledTime * 170f, 336f), 920, 112, 4);
			_nativePendingMarker.anchoredPosition = new Vector2(rect.x, -rect.y);
		}
		UpdatePointerVisual();
	}

	private void SetNativeSwipeProgress(float progress)
	{
		if (_nativeSwipeArrow == null || _nativeSwipeVisual == null) return;
		progress = Mathf.Clamp01(progress);
		_nativeSwipeVisual.alpha = ComputeSwipeArrowAlpha(progress);
		// The arrow is under a full design-grid container, so it uses local scaled coordinates.
		_nativeSwipeArrow.anchoredPosition = new Vector2(264.5f * _nativeSwipeLayout.Scale, -(785f - progress * 32f) * _nativeSwipeLayout.Scale);
		_nativeSwipeFill.sizeDelta = new Vector2(448f * _nativeSwipeLayout.Scale * progress, 4f * _nativeSwipeLayout.Scale);
	}

	private void ResetNativeScreens()
	{
		ResetPhonePointer(clearRegions: true);
		_nativeBindings.Clear();
		_nativeSwipeArrow = null;
		_nativeSwipeVisual = null;
		_nativeSwipeFill = null;
		_nativePendingMarker = null;
		_deniedReasonText = null;
		_deniedDetailText = null;
	}
}
