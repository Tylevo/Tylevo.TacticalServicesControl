using SamSWAT.FireSupport.ArysReloaded.Unity;

namespace SamSWAT.FireSupport.ArysReloaded.Fika;

/// <summary>
/// Versions service meanings that cannot be inferred safely from the stable
/// numeric support-type wire values alone.
/// </summary>
public static class FireSupportServiceSemantics
{
	/// <summary>
	/// Legacy peers interpret wire value 10 as Priority Exfil.
	/// </summary>
	public const int LegacyVersion = 0;

	/// <summary>
	/// Wire value 10 is the Cargo service.
	/// </summary>
	public const int CargoVersion = 1;

	public const int CurrentVersion = CargoVersion;

	public static bool SupportsCargo(int version)
	{
		return version >= CargoVersion;
	}

	public static bool IsCargoAvailable(bool advertised, int version)
	{
		return advertised && SupportsCargo(version);
	}

	public static bool CanExecute(ESupportType supportType, int version)
	{
		return supportType != ESupportType.PriorityExfil || SupportsCargo(version);
	}
}
