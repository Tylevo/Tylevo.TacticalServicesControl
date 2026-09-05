namespace SamSWAT.FireSupport.ArysReloaded.Integration;

/// <summary>
/// Reflection-only capability marker consumed by an optional Seasonal
/// Modifiers server installation. Neither mod has a hard dependency on the
/// other; type presence plus this exact API version proves compatibility.
/// </summary>
public static class SeasonalModifiersServerBridge
{
	public static int ApiVersion => 3;
}
