using UnityEngine;
using UnityEngine.UI;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

/// <summary>Static service artwork, independent of live raid contacts or scan state.</summary>
internal sealed class PilotServicesReconGraphic : MaskableGraphic
{
	private bool _focused;

	internal void SetFocused(bool focused)
	{
		if (_focused == focused) return;
		_focused = focused;
		SetVerticesDirty();
	}

	protected override void OnPopulateMesh(VertexHelper mesh)
	{
		mesh.Clear();
		Rect rect = rectTransform.rect;
		Vector2 center = rect.center;
		float radius = Mathf.Min(rect.width * 0.18f, rect.height * 0.46f);
		if (radius <= 0f) return;
		Color32 grid = new(160, 166, 155, 35);
		Color32 line = new(209, 210, 192, 150);
		Color32 accent = new(200, 178, 119, 230);
		float spacing = radius / 2f;
		for (int i = -4; i <= 4; i++)
		{
			float x = center.x + i * spacing;
			Line(mesh, new Vector2(x, center.y - radius), new Vector2(x, center.y + radius), 0.7f, grid);
		}
		for (int i = -2; i <= 2; i++)
		{
			float y = center.y + i * spacing;
			Line(mesh, new Vector2(center.x - radius * 2f, y), new Vector2(center.x + radius * 2f, y), 0.7f, grid);
		}
		for (int ring = 1; ring <= 3; ring++)
		{
			float r = radius * ring / 3f;
			for (int step = 0; step < 96; step++)
			{
				float a = step * Mathf.PI * 2f / 96f;
				float b = (step + 1) * Mathf.PI * 2f / 96f;
				Line(mesh, center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r,
					center + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * r, 0.8f, line);
			}
		}
		Line(mesh, center, center + new Vector2(0.62f, 0.78f) * radius, 1.2f, accent);
		Contact(mesh, center + new Vector2(0.27f, 0.44f) * radius, accent);
		if (_focused)
		{
			Vector2 target = center + new Vector2(0.27f, 0.44f) * radius;
			for (int x = -1; x <= 1; x += 2)
			for (int y = -1; y <= 1; y += 2)
			{
				Vector2 corner = target + new Vector2(x, y) * 9f;
				Line(mesh, corner, corner - new Vector2(x * 5f, 0f), 1.2f, accent);
				Line(mesh, corner, corner - new Vector2(0f, y * 5f), 1.2f, accent);
			}
		}
		else
		{
			Contact(mesh, center + new Vector2(-0.58f, 0.19f) * radius, line);
			Contact(mesh, center + new Vector2(0.32f, -0.57f) * radius, line);
		}
	}

	private static void Contact(VertexHelper mesh, Vector2 at, Color32 tint)
	{
		Line(mesh, at - Vector2.right * 2f, at + Vector2.right * 2f, 4f, tint);
	}

	private static void Line(VertexHelper mesh, Vector2 from, Vector2 to, float width, Color32 tint)
	{
		Vector2 direction = (to - from).normalized;
		Vector2 offset = new Vector2(-direction.y, direction.x) * (width * 0.5f);
		int start = mesh.currentVertCount;
		mesh.AddVert(from - offset, tint, Vector2.zero);
		mesh.AddVert(from + offset, tint, Vector2.zero);
		mesh.AddVert(to + offset, tint, Vector2.zero);
		mesh.AddVert(to - offset, tint, Vector2.zero);
		mesh.AddTriangle(start, start + 1, start + 2);
		mesh.AddTriangle(start + 2, start + 3, start);
	}
}
