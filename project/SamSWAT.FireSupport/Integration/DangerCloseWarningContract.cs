namespace SamSWAT.FireSupport.ArysReloaded.Integration;

/// <summary>
/// The authenticated lifecycle messages that TSC can present for one
/// Seasonal Danger Close opportunity.
/// </summary>
public enum DangerCloseWarningKind
{
	Advance = 1,
	Cancel = 2,
	Inbound = 3
}

/// <summary>
/// Dependency-free warning payload shared with TSC's optional Fika transport.
/// The transport deliberately carries no target coordinates.
/// </summary>
public readonly struct DangerCloseWarningPublication
{
	public DangerCloseWarningPublication(
		DangerCloseWarningKind kind,
		string opportunityId,
		int secondsRemaining)
	{
		Kind = kind;
		OpportunityId = opportunityId ?? string.Empty;
		SecondsRemaining = secondsRemaining;
	}

	public DangerCloseWarningKind Kind { get; }
	public string OpportunityId { get; }
	public int SecondsRemaining { get; }
}
