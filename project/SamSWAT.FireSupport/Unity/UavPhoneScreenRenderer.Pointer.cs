using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public sealed partial class UavPhoneScreenRenderer
{
	private sealed class PhoneHitRegion
	{
		internal TerraGroupPhoneState State;
		internal Rect Bounds;
		internal PhonePointerAction Action;
		internal Func<bool> Enabled;
		internal Image Hover;
	}

	private readonly List<PhoneHitRegion> _phoneHitRegions = new();
	private readonly Dictionary<TerraGroupPhoneState, RectTransform> _phonePointerRoots = new();
	private readonly Dictionary<TerraGroupPhoneState, bool> _phonePointerPortrait = new();
	private readonly PhonePointerGesture _phonePointerGesture = new();
	private bool _phonePointerActive;
	private int _phoneViewGeneration;
	private Vector2 _phonePointerPosition = new(0.5f, 0.5f);
	private RectTransform _phonePointerVisual;
	private int _phoneHoveredRegion = -1;

	internal bool CanUsePointer
	{
		get
		{
			if (_shutdown || _radarHudMode || _canvas == null || _stateFadeCoroutine != null ||
			    !_phonePointerRoots.ContainsKey(_currentState)) return false;
			if (_currentState is not (TerraGroupPhoneState.Home or TerraGroupPhoneState.TacticalServices or
			    TerraGroupPhoneState.ServiceCategory or TerraGroupPhoneState.ServiceReview or
			    TerraGroupPhoneState.RotateToConfirm or TerraGroupPhoneState.DeploySelect)) return false;
			CanvasGroup group = GetGroupForState(_currentState);
			if (group == null || group.alpha < 0.999f) return false;
			return _currentState != TerraGroupPhoneState.DeploySelect ||
			       FireSupportController.Instance == null || FireSupportController.Instance.IsSupportAvailable();
		}
	}

	internal void SetPointerActive(bool active)
	{
		active &= CanUsePointer;
		if (_phonePointerActive != active)
		{
			_phonePointerGesture.Cancel();
			_phonePointerActive = active;
		}
		UpdatePointerVisual();
	}

	internal void MovePointer(float deltaX, float deltaY)
	{
		if (!_phonePointerActive || !CanUsePointer || float.IsNaN(deltaX) || float.IsNaN(deltaY) ||
		    float.IsInfinity(deltaX) || float.IsInfinity(deltaY)) return;
		RectTransform root = _phonePointerRoots[_currentState];
		bool portrait = _phonePointerPortrait[_currentState];
		float scale = Mathf.Min(root.sizeDelta.x / (portrait ? 576f : 1024f),
			root.sizeDelta.y / (portrait ? 1024f : 576f));
		// Pointer coordinates are top-left local UI coordinates. The visual inherits
		// the root's landscape rotation, keeping drawing and hit testing aligned.
		_phonePointerPosition.x = Mathf.Clamp01(_phonePointerPosition.x + deltaX * scale / root.sizeDelta.x);
		_phonePointerPosition.y = Mathf.Clamp01(_phonePointerPosition.y - deltaY * scale / root.sizeDelta.y);
		UpdatePointerVisual();
	}

	internal void BeginPointerPress()
	{
		_phonePointerGesture.BeginPress(_phonePointerActive && CanUsePointer ? FindPointerRegion() : -1,
			_phoneViewGeneration);
	}

	internal bool EndPointerPress(out PhonePointerAction action)
	{
		action = default;
		int regionId = _phonePointerActive && CanUsePointer ? FindPointerRegion() : -1;
		if (!_phonePointerGesture.EndPress(regionId, _phoneViewGeneration)) return false;
		action = _phoneHitRegions[regionId].Action;
		return action.Kind != PhonePointerActionKind.None;
	}

	private void RegisterPointerRoot(TerraGroupPhoneState state, RectTransform root, bool portrait)
	{
		_phonePointerRoots[state] = root;
		_phonePointerPortrait[state] = portrait;
	}

	private void AddPointerRegion(TerraGroupPhoneState state, RectTransform root, Rect bounds,
		PhonePointerAction action, Func<bool> enabled = null)
	{
		RectTransform overlay = NativeRectangle(root, bounds, Color.clear);
		overlay.gameObject.name = $"Pointer region: {action.Kind}";
		_phoneHitRegions.Add(new PhoneHitRegion
		{
			State = state, Bounds = bounds, Action = action, Enabled = enabled,
			Hover = overlay.GetComponent<Image>()
		});
	}

	private int FindPointerRegion()
	{
		if (!_phonePointerRoots.TryGetValue(_currentState, out RectTransform root) || root == null) return -1;
		Vector2 point = new(_phonePointerPosition.x * root.sizeDelta.x, _phonePointerPosition.y * root.sizeDelta.y);
		for (int i = _phoneHitRegions.Count - 1; i >= 0; i--)
		{
			PhoneHitRegion region = _phoneHitRegions[i];
			if (region.State == _currentState && region.Bounds.Contains(point) && (region.Enabled == null || region.Enabled())) return i;
		}
		return -1;
	}

	private void UpdatePointerVisual()
	{
		bool visible = _phonePointerActive && CanUsePointer;
		int hovered = visible ? FindPointerRegion() : -1;
		if (_phoneHoveredRegion != hovered)
		{
			if (_phoneHoveredRegion >= 0 && _phoneHoveredRegion < _phoneHitRegions.Count &&
			    _phoneHitRegions[_phoneHoveredRegion].Hover != null)
				_phoneHitRegions[_phoneHoveredRegion].Hover.color = Color.clear;
			_phoneHoveredRegion = hovered;
			if (hovered >= 0) _phoneHitRegions[hovered].Hover.color = new Color(0.91f, 0.73f, 0.41f, 0.13f);
		}
		if (!visible)
		{
			_phonePointerGesture.Cancel();
			if (_phonePointerVisual != null) _phonePointerVisual.gameObject.SetActive(false);
			return;
		}
		RectTransform root = _phonePointerRoots[_currentState];
		if (_phonePointerVisual == null || _phonePointerVisual.parent != root)
		{
			if (_phonePointerVisual != null) Destroy(_phonePointerVisual.gameObject);
			float scale = Mathf.Min(root.sizeDelta.x, root.sizeDelta.y) / 576f;
			_phonePointerVisual = NativeRectangle(root, new Rect(0, 0, 20 * scale, 20 * scale), Color.clear);
			_phonePointerVisual.gameObject.name = "Native phone cursor";
			// Bordered crosshair remains legible over icons, light buttons, and text.
			AddLine(_phonePointerVisual, new Rect(-9 * scale, -2 * scale, 19 * scale, 5 * scale), Color.black);
			AddLine(_phonePointerVisual, new Rect(-2 * scale, -9 * scale, 5 * scale, 19 * scale), Color.black);
			AddLine(_phonePointerVisual, new Rect(-8 * scale, -scale, 17 * scale, 2 * scale), NativeInk);
			AddLine(_phonePointerVisual, new Rect(-scale, -8 * scale, 2 * scale, 17 * scale), NativeInk);
		}
		_phonePointerVisual.gameObject.SetActive(true);
		_phonePointerVisual.SetAsLastSibling();
		_phonePointerVisual.anchoredPosition = new Vector2(
			_phonePointerPosition.x * root.sizeDelta.x, -_phonePointerPosition.y * root.sizeDelta.y);
	}

	private void ResetPhonePointer(bool clearRegions = false)
	{
		_phoneViewGeneration++;
		_phonePointerGesture.Cancel();
		_phonePointerActive = false;
		if (_phoneHoveredRegion >= 0 && _phoneHoveredRegion < _phoneHitRegions.Count &&
		    _phoneHitRegions[_phoneHoveredRegion].Hover != null)
			_phoneHitRegions[_phoneHoveredRegion].Hover.color = Color.clear;
		_phoneHoveredRegion = -1;
		if (_phonePointerVisual != null) _phonePointerVisual.gameObject.SetActive(false);
		if (clearRegions)
		{
			_phoneHitRegions.Clear();
			_phonePointerRoots.Clear();
			_phonePointerPortrait.Clear();
			_phonePointerVisual = null;
		}
	}
}
