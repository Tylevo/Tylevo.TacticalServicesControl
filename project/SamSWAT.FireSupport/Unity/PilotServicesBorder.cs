using UnityEngine;
using UnityEngine.UI;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

/// <summary>A one-pixel frame that does not duplicate a translucent panel's fill.</summary>
internal sealed class PilotServicesBorder : MonoBehaviour
{
	private readonly Image[] _edges = new Image[4];
	private Color _color;

	internal Color effectColor
	{
		get => _color;
		set
		{
			_color = value;
			EnsureEdges();
			foreach (Image edge in _edges) edge.color = value;
		}
	}

	private void EnsureEdges()
	{
		if (_edges[0] != null) return;
		for (int index = 0; index < _edges.Length; index++)
		{
			GameObject edge = new("Panel edge", typeof(RectTransform), typeof(Image));
			edge.transform.SetParent(transform, false);
			RectTransform rect = (RectTransform)edge.transform;
			bool horizontal = index < 2;
			float side = index % 2;
			rect.anchorMin = horizontal ? new Vector2(0f, side) : new Vector2(side, 0f);
			rect.anchorMax = horizontal ? new Vector2(1f, side) : new Vector2(side, 1f);
			rect.pivot = horizontal ? new Vector2(0.5f, side) : new Vector2(side, 0.5f);
			rect.sizeDelta = horizontal ? new Vector2(0f, 1f) : new Vector2(1f, 0f);
			rect.anchoredPosition = Vector2.zero;
			_edges[index] = edge.GetComponent<Image>();
			_edges[index].raycastTarget = false;
		}
	}
}
