namespace SamSWAT.FireSupport.ArysReloaded.Unity;

/// <summary>
/// Bounds for the UH-60 cargo inventory grid. Each zero dimension preserves
/// the corresponding native dimension for existing configs and servers.
/// </summary>
public static class CargoGridPolicy
{
	public const int MaxWidth = 10;
	public const int MaxHeight = 30;

	public static bool IsValidWidth(int width) => width >= 0 && width <= MaxWidth;
	public static bool IsValidHeight(int height) => height >= 0 && height <= MaxHeight;

	public static bool TryValidate(int width, int height, string path, out string error)
	{
		if (!IsValidWidth(width))
		{
			error = $"{path}.gridWidth ({width}) must be between 0 and {MaxWidth}; 0 preserves the native cargo columns.";
			return false;
		}

		if (!IsValidHeight(height))
		{
			error = $"{path}.gridHeight ({height}) must be between 0 and {MaxHeight}; 0 preserves the native cargo rows.";
			return false;
		}

		error = string.Empty;
		return true;
	}
}
