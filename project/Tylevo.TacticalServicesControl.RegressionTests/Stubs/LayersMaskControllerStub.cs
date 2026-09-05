// Test layer indices are arbitrary; assertions verify callers consistently use
// the designator's terrain/low-poly mask rather than an all-layer query.
public static class LayersMaskController
{
	public const int TerrainLowPoly = (1 << 8) | (1 << 9);
}
