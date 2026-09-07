using UnityEngine;
using UnityEngine.UI;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

/// <summary>A native canvas ribbon for the selected service category.</summary>
internal sealed class PilotServicesRibbonGraphic : MaskableGraphic
{
	protected override void OnPopulateMesh(VertexHelper mesh)
	{
		mesh.Clear();
		Rect rect = rectTransform.rect;
		Color32 top = new(213, 202, 167, 255);
		Color32 bottom = new(155, 143, 109, 255);
		mesh.AddVert(new Vector3(rect.xMin, rect.yMin), bottom, Vector2.zero);
		mesh.AddVert(new Vector3(rect.xMin, rect.yMax), top, Vector2.up);
		mesh.AddVert(new Vector3(rect.xMax - 24f, rect.yMax), top, Vector2.one);
		mesh.AddVert(new Vector3(rect.xMax, rect.yMin), bottom, Vector2.right);
		mesh.AddTriangle(0, 1, 2);
		mesh.AddTriangle(2, 3, 0);
	}
}
