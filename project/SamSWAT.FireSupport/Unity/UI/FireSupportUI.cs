using Comfort.Common;
using Cysharp.Threading.Tasks;
using EFT;
using EFT.UI;
using EFT.UI.Gestures;
using SamSWAT.FireSupport.ArysReloaded.Utils;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public class FireSupportUI : UpdatableComponentBase, IPointerEnterHandler, IPointerExitHandler, IDisposable
{
	public GameObject SpotterNotice;
	public GameObject SpotterHeliNotice;
	public Text timerText;

	[SerializeField] private FireSupportUIElement[] supportOptions;
	[SerializeField] private HoverTooltipArea tooltip;

	private Player _player;
	private float _menuOffset;

	private FireSupportServiceMappings _services;

	private bool _isUnderPointer;
	private bool _rangefinderInHands;
	private readonly Color _enabledColor = new(1, 1, 1, 1);
	private readonly Color _disabledColor = new(1, 1, 1, 0.4f);
	private readonly HashSet<ESupportType> _missingOptionLogs = new(new SupportTypeComparer());

	public static FireSupportUI Instance { get; private set; }

	public event Action<ESupportType> SupportRequested;

	public static async UniTask<FireSupportUI> Load(
		FireSupportServiceMappings services,
		GesturesMenu gesturesMenu)
	{
		Instance = Instantiate(await AssetLoader.LoadAssetAsync("assets/content/ui/firesupport_ui.bundle"))
			.GetComponent<FireSupportUI>();
		Instance.Initialize(services, gesturesMenu);
		return Instance;
	}

	public void Dispose()
	{
		Instance = null;
		DestroyImmediate(gameObject);
	}

	public override void ManualUpdate()
	{
		if (!HasFinishedInitialization || _player == null) return;
		
		_rangefinderInHands = HasRangefinderEquipped();
		bool canUseFireSupport = _rangefinderInHands || HasUavRequestAvailable();
		
		tooltip.SetUnlockStatus(canUseFireSupport);
		
		if (canUseFireSupport)
		{
			RenderUI();
			HandleInput();
		}
		else
		{
			ClearSelection();
		}
	}

	private void RenderUI()
	{
		if (_services == null || _services.Count == 0)
		{
			return;
		}

		foreach (KeyValuePair<ESupportType, IFireSupportService> serviceMapping in _services)
		{
			RenderButton(serviceMapping);
		}
	}

	private void RenderButton(KeyValuePair<ESupportType, IFireSupportService> serviceMapping)
	{
		int optionIndex = (int)serviceMapping.Key;
		if (optionIndex < 0 || optionIndex >= supportOptions.Length)
		{
			LogMissingOption(serviceMapping.Key, $"index {optionIndex} is outside supportOptions length {supportOptions.Length}");
			return;
		}

		FireSupportUIElement uiElement = supportOptions[optionIndex];
		if (uiElement == null)
		{
			LogMissingOption(serviceMapping.Key, $"supportOptions[{optionIndex}] is null");
			return;
		}

		IFireSupportService service = serviceMapping.Value;
		
		if (HasSelectableRequest(serviceMapping.Key, service))
		{
			uiElement.AmountText.color = _enabledColor;
			uiElement.Icon.color = _enabledColor;
		}
		else
		{
			uiElement.IsUnderPointer = false;
			uiElement.AmountText.color = _disabledColor;
			uiElement.Icon.color = _disabledColor;
		}

		uiElement.AmountText.text = GetRequestDisplayText(serviceMapping.Key, service);
	}

	private void HandleInput()
	{
		if (!_isUnderPointer)
		{
			return;
		}

		float angle = CalculateAngle();
		var selectedSupportOption = ESupportType.None;

		for (var i = 0; i < supportOptions.Length; i++)
		{
			if (!IsValidButton(i)) continue;
			ESupportType supportType = (ESupportType)i;

			FireSupportUIElement uiElement = supportOptions[i];

			if (HasSelectableRequest(supportType) &&
				angle > i * 45 &&
				angle < (i + 1) * 45)
			{
				uiElement.IsUnderPointer = true;
				selectedSupportOption = supportType;
			}
			else
			{
				uiElement.IsUnderPointer = false;
			}
		}

		if (Input.GetMouseButtonDown(0) && selectedSupportOption != ESupportType.None)
		{
			SupportRequested?.Invoke(selectedSupportOption);
		}
	}

	private bool IsValidButton(int i)
	{
		if (i < 0 || i >= supportOptions.Length || supportOptions[i] == null)
		{
			return false;
		}

		var isValidButton = false;
			
		foreach (ESupportType supportType in _services.Keys)
		{
			if ((int)supportType == i)
			{
				isValidButton = true;
				break;
			}
		}

		return isValidButton;
	}

	private void LogMissingOption(ESupportType supportType, string reason)
	{
		if (_missingOptionLogs.Add(supportType))
		{
			FireSupportPlugin.LogSource.LogWarning(
				$"FireSupport radial option missing for {supportType}: {reason}");
		}
	}

	private bool HasUavRequestAvailable()
	{
		return _services != null &&
			_services.TryGetValue(ESupportType.Uav, out IFireSupportService uavService) &&
			HasSelectableRequest(ESupportType.Uav, uavService);
	}

	private bool HasSelectableRequest(ESupportType supportType)
	{
		return _services != null &&
			_services.TryGetValue(supportType, out IFireSupportService service) &&
			HasSelectableRequest(supportType, service);
	}

	private bool HasSelectableRequest(ESupportType supportType, IFireSupportService service)
	{
		return CanUseSupportOption(supportType) &&
			service.IsRequestAvailable() &&
			FireSupportController.Instance.IsSupportAvailable() &&
			CanPayOrUseAuthorization(supportType);
	}

	private bool CanUseSupportOption(ESupportType supportType)
	{
		return supportType == ESupportType.Uav || _rangefinderInHands;
	}

	private static bool CanPayOrUseAuthorization(ESupportType supportType)
	{
		PaymentMode paymentMode = FireSupportPayment.GetActivePaymentMode();
		return paymentMode switch
		{
			PaymentMode.PhoneAuthorizations => FireSupportAuthorizations.HasDeployable(supportType),
			PaymentMode.Hybrid => FireSupportAuthorizations.HasDeployable(supportType) ||
			                      FireSupportPayment.CanAfford(supportType),
			_ => FireSupportPayment.CanAfford(supportType)
		};
	}

	private static string GetRequestDisplayText(ESupportType supportType, IFireSupportService service)
	{
		PaymentMode paymentMode = FireSupportPayment.GetActivePaymentMode();
		int authorizationCount = FireSupportAuthorizations.GetDeployableCount(supportType);
		if (paymentMode == PaymentMode.PhoneAuthorizations)
		{
			return authorizationCount > 0
				? authorizationCount.ToString()
				: "AUTH REQ";
		}

		if (paymentMode == PaymentMode.Hybrid && authorizationCount > 0)
		{
			return authorizationCount.ToString();
		}

		return service.AvailableRequests.ToString();
	}

	private void ClearSelection()
	{
		foreach (FireSupportUIElement uiElement in supportOptions)
		{
			uiElement.IsUnderPointer = false;
		}
	}

	private void Initialize(FireSupportServiceMappings services, GesturesMenu gesturesMenu)
	{
		_player = Singleton<GameWorld>.Instance.MainPlayer;
		_services = services;

		Transform fireSupportUiT = transform;
		fireSupportUiT.parent = gesturesMenu.transform;
		fireSupportUiT.localPosition = new Vector3(0, -255, 0);
		fireSupportUiT.localScale = new Vector3(1.4f, 1.4f, 1);
		_menuOffset = Screen.height / 2f - fireSupportUiT.position.y;

		Transform infoPanelTransform = SpotterNotice.transform.parent;
		infoPanelTransform.parent = Singleton<GameUI>.Instance.transform;
		infoPanelTransform.localPosition = new Vector3(0, -370f, 0);
		infoPanelTransform.localScale = Vector3.one;

		HasFinishedInitialization = true;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private bool HasRangefinderEquipped()
	{
		if (_player.HandsController == null) return false;
		return _player.HandsController.Item?.TemplateId == ItemConstants.RANGEFINDER_TPL;
	}

	private float CalculateAngle()
	{
		Vector2 mouse;
		mouse.x = Input.mousePosition.x - (Screen.width / 2f);
		mouse.y = Input.mousePosition.y - (Screen.height / 2f) + _menuOffset;
		mouse.Normalize();

		if (mouse == Vector2.zero)
		{
			return 0;
		}

		float angle = Mathf.Atan2(mouse.y, -mouse.x) / Mathf.PI;
		angle *= 180;
		angle += 111;

		if (angle < 0)
		{
			angle += 360;
		}

		return angle;
	}

	void IPointerEnterHandler.OnPointerEnter(PointerEventData eventData)
	{
		_isUnderPointer = true;
	}

	void IPointerExitHandler.OnPointerExit(PointerEventData eventData)
	{
		_isUnderPointer = false;
		ClearSelection();
	}
}
