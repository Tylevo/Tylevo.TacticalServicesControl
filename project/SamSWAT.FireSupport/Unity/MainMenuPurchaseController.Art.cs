using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public sealed partial class MainMenuPurchaseController
{
	private const float StoreAssetRetrySeconds = 5f;
	private const string StoreSurfaceName = "TSC Surface";
	private static readonly Dictionary<string, Texture2D> s_storeArtworkTextures = new(StringComparer.OrdinalIgnoreCase);
	private static readonly Dictionary<(string Key, int Aspect), Sprite> s_storeArtworkSprites = new();
	private static readonly Dictionary<string, float> s_storeArtworkRetryAfter = new(StringComparer.OrdinalIgnoreCase);
	private static readonly Dictionary<FontStyle, Font> s_storeNativeFonts = new();
	private static readonly Dictionary<FontStyle, float> s_storeFontRetryAfter = new();
	private static Font s_storeFallbackFont;
	private static Sprite s_storeSurfaceSprite;

	private static Image StoreArtwork(Transform parent, string name, string key,
		float x, float y, float width, float height, Color tint, bool preserveAspect = false)
	{
		GameObject node = new(name, typeof(RectTransform), typeof(Image));
		node.transform.SetParent(parent, false);
		SetStoreRect((RectTransform)node.transform, x, y, width, height);
		Image image = node.GetComponent<Image>();
		image.raycastTarget = false;
		image.preserveAspect = preserveAspect;
		image.sprite = LoadStoreArtwork(key, width, height, preserveAspect);
		image.color = image.sprite != null ? tint : Color.clear;
		return image;
	}

	private static void SetServiceDetailArtwork(Image image, ESupportType type)
	{
		if (image == null) return;
		bool recon = type is ESupportType.Uav or ESupportType.FocusedSweep;
		image.enabled = !recon;
		if (recon) return;
		string key = type switch
		{
			ESupportType.Strafe or ESupportType.DoubleStrafe => "a10-detail.png",
			ESupportType.Extract or ESupportType.PriorityExfil => "uh60-detail.png",
			_ => "a10-detail.png"
		};
		// Offline renders of the shipped models retain their complete silhouette.
		image.sprite = LoadStoreArtwork(key, 1f, 1f, true) ?? LoadStoreIcon(type, true);
		image.preserveAspect = true;
		image.raycastTarget = false;
		image.color = image.sprite != null ? Color.white : Color.clear;
	}

	private static Sprite LoadStoreArtwork(string key, float width, float height, bool preserveAspect)
	{
		if (string.IsNullOrWhiteSpace(key) || width <= 0f || height <= 0f) return null;
		key = key.ToLowerInvariant();
		if (key is not ("pilot-banner.png" or "pilot-portrait.png" or "a10-detail.png" or "uh60-detail.png")) return null;

		// Quantize only the cache key, so normal layout changes cannot accumulate
		// an unbounded collection of nearly identical cover sprites.
		float aspect = width / height;
		int aspectKey = preserveAspect ? 0 : Mathf.Clamp(Mathf.RoundToInt(aspect * 100f), 1, 10000);
		var spriteKey = (key, aspectKey);
		if (s_storeArtworkSprites.TryGetValue(spriteKey, out Sprite sprite) && sprite != null) return sprite;
		Texture2D texture = LoadStoreArtworkTexture(key);
		if (texture == null) return null;

		Rect source = new(0f, 0f, texture.width, texture.height);
		if (!preserveAspect && key == "pilot-portrait.png")
		{
			source = PilotPortraitFraming.GetSourceRect(source, aspect);
		}
		else if (!preserveAspect)
		{
			float sourceAspect = (float)texture.width / texture.height;
			if (sourceAspect > aspect)
			{
				source.width = texture.height * aspect;
				source.x = (texture.width - source.width) * 0.5f;
			}
			else
			{
				source.height = texture.width / aspect;
				// Unity source rectangles start at the bottom; keep the airfield
				// helicopter and horizon visible in the wide banner.
				float focusY = key == "pilot-banner.png" ? 0.44f : 0.5f;
				source.y = Mathf.Clamp(texture.height * focusY - source.height * 0.5f,
					0f, texture.height - source.height);
			}
		}
		sprite = Sprite.Create(texture, source, new Vector2(0.5f, 0.5f), 100f,
			0, SpriteMeshType.FullRect);
		sprite.name = $"TSC {key} ({aspectKey})";
		sprite.hideFlags = HideFlags.HideAndDontSave;
		s_storeArtworkSprites[spriteKey] = sprite;
		return sprite;
	}

	private static Texture2D LoadStoreArtworkTexture(string key)
	{
		if (s_storeArtworkTextures.TryGetValue(key, out Texture2D cached) && cached != null) return cached;
		if (s_storeArtworkRetryAfter.TryGetValue(key, out float retryAt) && Time.unscaledTime < retryAt) return null;
		Texture2D texture = null;
		try
		{
			string directory = FireSupportPlugin.Directory ??
				Path.GetDirectoryName(typeof(MainMenuPurchaseController).Assembly.Location) ?? string.Empty;
			string path = Path.Combine(directory, "assets", "content", "ui", "pilot-services", key);
			if (!File.Exists(path)) return null;
			texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
			{
				name = $"TSC {key}",
				hideFlags = HideFlags.HideAndDontSave,
				filterMode = FilterMode.Bilinear,
				wrapMode = TextureWrapMode.Clamp
			};
			if (!texture.LoadImage(File.ReadAllBytes(path), true)) return null;
			s_storeArtworkTextures[key] = texture;
			s_storeArtworkRetryAfter.Remove(key);
			return texture;
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource?.LogWarning($"TSC Pilot artwork unavailable: {key}. {ex.Message}");
			return null;
		}
		finally
		{
			if (!s_storeArtworkTextures.TryGetValue(key, out Texture2D loaded) || loaded == null)
			{
				if (texture != null) Destroy(texture);
				// Missing or temporarily unreadable assets can recover on a later
				// page build/selection; repeated redraws do not repeatedly hit disk.
				s_storeArtworkRetryAfter[key] = Time.unscaledTime + StoreAssetRetrySeconds;
			}
		}
	}

	private static Font ResolveStoreFont(FontStyle style, out FontStyle effectiveStyle)
	{
		Font font = LoadStoreNativeFont(style);
		if (font != null)
		{
			effectiveStyle = FontStyle.Normal;
			return font;
		}
		font = LoadStoreNativeFont(FontStyle.Normal);
		effectiveStyle = style;
		if (font != null) return font;
		if (s_storeFallbackFont == null)
			s_storeFallbackFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
		return s_storeFallbackFont;
	}

	private static Font LoadStoreNativeFont(FontStyle style)
	{
		if (s_storeNativeFonts.TryGetValue(style, out Font font) && font != null) return font;
		if (s_storeFontRetryAfter.TryGetValue(style, out float retryAt) && Time.unscaledTime < retryAt) return null;
		string suffix = style switch
		{
			FontStyle.Bold => " bold",
			FontStyle.Italic => " italic",
			FontStyle.BoldAndItalic => " bolditalic",
			_ => string.Empty
		};
		// These are native Unity Font resources, separate from EFT's TMP SDF
		// assets. Reuse them without modifying or redistributing game assets.
		font = Resources.Load<Font>("ui/fonts/source/jovanny lemonad - bender" + suffix);
		if (font != null)
		{
			s_storeNativeFonts[style] = font;
			s_storeFontRetryAfter.Remove(style);
			return font;
		}
		s_storeFontRetryAfter[style] = Time.unscaledTime + StoreAssetRetrySeconds;
		return null;
	}

	private static void AddStoreSurface(RectTransform rect, float strength = 1f)
	{
		if (rect == null) return;
		Transform existing = rect.Find(StoreSurfaceName);
		Image image = existing != null ? existing.GetComponent<Image>() : null;
		if (image == null)
		{
			GameObject node = new(StoreSurfaceName, typeof(RectTransform), typeof(Image));
			node.transform.SetParent(rect, false);
			Stretch((RectTransform)node.transform);
			image = node.GetComponent<Image>();
		}
		image.transform.SetAsFirstSibling();
		image.raycastTarget = false;
		image.sprite = GetStoreSurfaceSprite();
		image.color = new Color(1f, 1f, 1f, Mathf.Clamp01(strength));
	}

	private static Sprite GetStoreSurfaceSprite()
	{
		if (s_storeSurfaceSprite != null) return s_storeSurfaceSprite;
		const int width = 128;
		const int height = 64;
		Texture2D texture = new(width, height, TextureFormat.RGBA32, false)
		{
			name = "TSC Store Surface",
			hideFlags = HideFlags.HideAndDontSave,
			filterMode = FilterMode.Bilinear,
			wrapMode = TextureWrapMode.Clamp
		};
		Color32[] pixels = new Color32[width * height];
		for (int y = 0; y < height; y++)
		{
			float vertical = (float)y / (height - 1);
			for (int x = 0; x < width; x++)
			{
				// Deterministic fine grain leaves Unity's gameplay random state alone.
				uint hash = unchecked((uint)(x * 374761393 + y * 668265263));
				hash = unchecked((hash ^ (hash >> 13)) * 1274126177u);
				float grain = (hash & 255) / 255f;
				byte alpha = (byte)Mathf.RoundToInt(2f + 8f * vertical * vertical + 2f * grain);
				pixels[y * width + x] = new Color32(205, 194, 169, alpha);
			}
		}
		texture.SetPixels32(pixels);
		texture.Apply(false, true);
		s_storeSurfaceSprite = Sprite.Create(texture, new Rect(0, 0, width, height),
			new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
		s_storeSurfaceSprite.name = "TSC Store Surface";
		s_storeSurfaceSprite.hideFlags = HideFlags.HideAndDontSave;
		return s_storeSurfaceSprite;
	}
}
