using UnityEngine;
using UnityEngine.UI;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

/// <summary>Shared framing of the original Pilot portrait; no texture pixels are changed.</summary>
internal static class PilotPortraitFraming
{
	internal const float Size = 0.50f;
	internal const float CenterX = 0.62f;
	internal const float CenterY = 0.69f;

	internal static Rect GetSourceRect(Rect original, float targetAspect = 1f)
	{
		if (float.IsNaN(targetAspect) || float.IsInfinity(targetAspect) || targetAspect <= 0f)
			targetAspect = 1f;
		float height = original.height * Size;
		float width = height * targetAspect;
		if (width > original.width)
		{
			width = original.width;
			height = width / targetAspect;
		}
		return new Rect(
			Mathf.Clamp(original.x + original.width * CenterX - width * 0.5f, original.xMin, original.xMax - width),
			Mathf.Clamp(original.y + original.height * CenterY - height * 0.5f, original.yMin, original.yMax - height),
			width, height);
	}
}

/// <summary>Owns only a view sprite, never the native avatar sprite or shared texture.</summary>
public sealed class PilotTraderPortrait : MonoBehaviour
{
	private Image _image;
	private Sprite _original;
	private Sprite _cropped;
	private bool _originalPreserveAspect;
	private int _generation;
	private float _aspect;

	internal int BeginLoad(Image image)
	{
		ReleaseCrop();
		_image = image;
		return ++_generation;
	}

	internal bool IsCurrent(int generation) => generation == _generation;

	internal void Apply(int generation)
	{
		if (!IsCurrent(generation) || _image == null || _image.sprite == null) return;
		_original = _image.sprite;
		_originalPreserveAspect = _image.preserveAspect;
		Reframe();
	}

	private void Reframe()
	{
		if (_image == null || _original == null || _original.texture == null) return;
		Rect target = _image.rectTransform.rect;
		float aspect = target.width > 0f && target.height > 0f ? target.width / target.height : 1f;
		if (_cropped != null && Mathf.Approximately(_aspect, aspect)) return;
		Sprite previous = _cropped;
		_cropped = Sprite.Create(_original.texture,
			PilotPortraitFraming.GetSourceRect(_original.rect, aspect), new Vector2(0.5f, 0.5f),
			_original.pixelsPerUnit, 0, SpriteMeshType.FullRect);
		_cropped.name = "TSC Pilot portrait framing";
		_cropped.hideFlags = HideFlags.HideAndDontSave;
		_aspect = aspect;
		_image.sprite = _cropped;
		_image.preserveAspect = false;
		if (previous != null) Destroy(previous);
	}

	private void OnRectTransformDimensionsChange()
	{
		if (_cropped != null && _image != null && _image.sprite == _cropped) Reframe();
	}

	private void ReleaseCrop()
	{
		if (_image != null && _cropped != null && _image.sprite == _cropped)
		{
			_image.sprite = _original;
			_image.preserveAspect = _originalPreserveAspect;
		}
		if (_cropped != null) Destroy(_cropped);
		_cropped = null;
		_original = null;
	}

	private void OnDestroy()
	{
		_generation++;
		ReleaseCrop();
	}
}
