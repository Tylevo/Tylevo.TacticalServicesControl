using Comfort.Common;
using EFT;
using EFT.UI;
using HarmonyLib;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public sealed partial class MainMenuPurchaseController
{
	private GameObject _taskBarRoot;
	private AnimatedToggle _taskBarButton;
	private bool _taskBarFailureLogged;

	// PreloaderUI persists beyond MenuScreen. The entry must not outlive the
	// active, authenticated menu owner or become a route into the store in raid.
	private bool CanUseTaskBar => !_destroyed && !_superseded && s_instance == this &&
		isActiveAndEnabled && _menuScreen != null && _menuScreen.isActiveAndEnabled &&
		_menuScreen.gameObject.activeInHierarchy && ShouldShowMenuButton &&
		Singleton<GameWorld>.Instance == null;

	private void EnsureMenuButton()
	{
		if (!CanUseTaskBar)
		{
			SetTaskBarVisible(false);
			return;
		}

		// The persistent footer is in PreloaderUI, a separate hierarchy from the
		// central MenuScreen canvas. Never fall back to inserting a center row.
		MenuTaskBar taskBar = PreloaderUI.Instance?.MenuTaskBar;
		AnimatedToggle template = FindTaskBarPlayerToggle(taskBar);
		Transform wrapper = template != null ? template.transform.parent : null;
		Transform tabs = wrapper != null ? wrapper.parent : null;
		if (taskBar == null || !taskBar.gameObject.activeInHierarchy || template == null ||
			!template.gameObject.activeInHierarchy || wrapper == null || tabs == null ||
			wrapper.GetComponent<ToggleGroup>() == null || tabs.GetComponent<HorizontalLayoutGroup>() == null)
		{
			SetTaskBarVisible(false);
			return; // Footer initialization can finish after MenuScreen.Show.
		}

		try
		{
			EnsureTaskBarButton(template, wrapper, tabs);
			_taskBarFailureLogged = false;
		}
		catch (Exception ex)
		{
			RetireTaskBarButton();
			if (!_taskBarFailureLogged)
			{
				_taskBarFailureLogged = true;
				FireSupportPlugin.LogSource.LogWarning($"TSC bottom-bar entry could not be initialized: {ex.Message}");
			}
		}
	}

	private void EnsureTaskBarButton(AnimatedToggle template, Transform wrapper, Transform tabs)
	{
		if (_taskBarRoot != null && (_taskBarRoot.transform.parent != tabs || _taskBarButton == null))
			RetireTaskBarButton();

		// Only TSC-owned wrappers are retired; native tabs and their listeners
		// remain owned by EFT. Retired wrappers leave layout immediately.
		for (int index = tabs.childCount - 1; index >= 0; index--)
		{
			Transform child = tabs.GetChild(index);
			if (child.name == ButtonName && child.gameObject != _taskBarRoot)
				RetireTaskBarRoot(child.gameObject);
		}

		if (_taskBarRoot == null)
		{
			GameObject staging = new("TSC_TaskBarStaging");
			staging.SetActive(false);
			staging.transform.SetParent(transform, false);
			try
			{
				// Clone the whole Character wrapper: its own ToggleGroup and native
				// layout must accompany the child AnimatedToggle. Cloning just the
				// button would register a second toggle in Character's native group.
				_taskBarRoot = Instantiate(wrapper.gameObject, staging.transform);
				_taskBarRoot.name = ButtonName;
				_taskBarRoot.SetActive(false);
				_taskBarButton = _taskBarRoot.GetComponentInChildren<AnimatedToggle>(true);
				ToggleGroup group = _taskBarRoot.GetComponent<ToggleGroup>();
				if (_taskBarButton == null || group == null)
					throw new InvalidOperationException("Character wrapper lacks its toggle or group.");
				group.allowSwitchOff = true;
				_taskBarButton.group = group;
				_taskBarButton.onValueChanged = new Toggle.ToggleEvent();
				_taskBarButton.ToggleSilent(false);
				_taskBarButton.interactable = true;
				SetTaskBarButtonPresentation(_taskBarRoot, _taskBarButton);
				LayoutElement layout = _taskBarRoot.GetComponent<LayoutElement>();
				if (layout != null) layout.ignoreLayout = false;
				_taskBarRoot.transform.SetParent(tabs, false);
				PositionTaskBarButton(wrapper);
				_taskBarRoot.SetActive(true);

				// AnimatedToggle.Awake appends ToggleSilent on first activation.
				// Establish its native listener FIRST, then the TSC action/reset.
				// Replacing the event also excludes any cloned persistent callbacks.
				_taskBarButton.onValueChanged = new Toggle.ToggleEvent();
				_taskBarButton.onValueChanged.AddListener(_taskBarButton.ToggleSilent);
				_taskBarButton.onValueChanged.AddListener(HandleTaskBarToggle);
				_taskBarButton.ToggleSilent(false);
			}
			finally
			{
				Destroy(staging);
			}
		}

		PositionTaskBarButton(wrapper);
		SetTaskBarVisible(CanUseTaskBar);
	}

	private void HandleTaskBarToggle(bool selected)
	{
		if (!selected) return;
		_taskBarButton?.ToggleSilent(false);
		if (!CanUseTaskBar || _taskBarRoot == null || !_taskBarRoot.activeInHierarchy) return;
		OpenPage();
	}

	private static void SetTaskBarButtonPresentation(GameObject root, AnimatedToggle button)
	{
		foreach (LocalizedText localized in root.GetComponentsInChildren<LocalizedText>(true))
		{
			localized.FormattedText = "TSC UPLINK";
			localized.enabled = false;
		}
		foreach (TMP_Text label in root.GetComponentsInChildren<TMP_Text>(true))
			label.text = "TSC UPLINK";
		foreach (HoverTooltipArea tooltip in root.GetComponentsInChildren<HoverTooltipArea>(true))
			tooltip.enabled = false;
		foreach (CanvasGroup canvasGroup in root.GetComponentsInChildren<CanvasGroup>(true))
		{
			canvasGroup.alpha = 1f;
			canvasGroup.interactable = true;
			canvasGroup.blocksRaycasts = true;
		}
		Image icon = button.transform.Find("Icon")?.GetComponent<Image>();
		Sprite logo = LoadStoreSprite("neutral_512/terragroup_logo.png");
		if (icon != null && logo != null)
		{
			// Image reports its sprite's natural pixel size to the native layout.
			// Retain the compact native slot before replacing its atlas sprite
			// with a 512px TSC logo, otherwise the footer expands by hundreds of px.
			RectTransform rect = icon.GetComponent<RectTransform>();
			float width = LayoutUtility.GetPreferredWidth(rect);
			float height = LayoutUtility.GetPreferredHeight(rect);
			LayoutElement size = icon.GetComponent<LayoutElement>() ?? icon.gameObject.AddComponent<LayoutElement>();
			size.minWidth = size.preferredWidth = width > 0f ? Mathf.Clamp(width, 16f, 32f) : 24f;
			size.minHeight = size.preferredHeight = height > 0f ? Mathf.Clamp(height, 16f, 32f) : 24f;
			size.flexibleWidth = size.flexibleHeight = 0f;
			icon.overrideSprite = null;
			icon.sprite = logo;
			icon.preserveAspect = true;
		}
	}

	private void PositionTaskBarButton(Transform characterWrapper)
	{
		if (_taskBarRoot == null || characterWrapper == null ||
			_taskBarRoot.transform.parent != characterWrapper.parent) return;
		Transform target = _taskBarRoot.transform;
		int current = target.GetSiblingIndex();
		int character = characterWrapper.GetSiblingIndex();
		int desired = character - (current < character ? 1 : 0);
		if (current != desired) target.SetSiblingIndex(desired);
		// The native Tabs layout owns positions and sizes. Its flexible Spacer
		// absorbs the new slot; no central-menu or native-tab rects are rewritten.
	}

	private void SetTaskBarVisible(bool visible)
	{
		if (_taskBarRoot == null || _taskBarRoot.activeSelf == visible) return;
		_taskBarRoot.SetActive(visible);
	}

	private void RetireAllMenuButtons() => RetireTaskBarButton();

	private void RetireTaskBarButton()
	{
		GameObject root = _taskBarRoot;
		_taskBarRoot = null;
		_taskBarButton = null;
		RetireTaskBarRoot(root);
	}

	private static void RetireTaskBarRoot(GameObject root)
	{
		if (root == null) return;
		foreach (AnimatedToggle toggle in root.GetComponentsInChildren<AnimatedToggle>(true))
			toggle.onValueChanged = new Toggle.ToggleEvent();
		root.name = $"{ButtonName}_Retired_{root.GetInstanceID()}";
		root.SetActive(false);
		Destroy(root);
	}

	private static AnimatedToggle FindTaskBarPlayerToggle(MenuTaskBar taskBar)
	{
		if (taskBar == null) return null;
		try
		{
			Dictionary<EMenuType, AnimatedToggle> toggles = Traverse.Create(taskBar)
				.Field("_toggleButtons").GetValue<Dictionary<EMenuType, AnimatedToggle>>();
			if (toggles != null && toggles.TryGetValue(EMenuType.Player, out AnimatedToggle toggle) && toggle != null)
				return toggle;
		}
		catch
		{
			// Scoped fallback for a delayed native dictionary, excluding TSC clones.
		}
		foreach (AnimatedToggle candidate in taskBar.GetComponentsInChildren<AnimatedToggle>(true))
		{
			if (candidate.name == "CharacterButton" && candidate.transform.parent?.name == "Character")
				return candidate;
		}
		return null;
	}
}
