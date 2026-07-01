namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public sealed class UavPhoneVisualEvent
{
	public string ProfileId { get; set; } = string.Empty;
	public string AccountId { get; set; } = string.Empty;
	public ESupportType SupportType { get; set; }
	public UavPhoneVisualPhase Phase { get; set; }
	public double StartTime { get; set; }
	public float Duration { get; set; }
	public bool Success { get; set; }
}
