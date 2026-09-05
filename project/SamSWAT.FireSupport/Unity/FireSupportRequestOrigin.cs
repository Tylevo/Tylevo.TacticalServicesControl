namespace SamSWAT.FireSupport.ArysReloaded.Unity;

/// <summary>
/// Identifies whether a networked support request came from TSC's normal
/// player workflow or from a trusted host-side environmental integration.
/// </summary>
public enum FireSupportRequestOrigin
{
	Manual = 0,
	SeasonalAmbient = 1
}
