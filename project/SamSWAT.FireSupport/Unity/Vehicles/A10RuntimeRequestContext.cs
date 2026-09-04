namespace SamSWAT.FireSupport.ArysReloaded.Unity;

/// <summary>
/// Immutable request identity carried with the leased A-10 behaviour. Multiple
/// network deliveries may initialize concurrently, so this must not live in a
/// process-wide push/pop context.
/// </summary>
public sealed class A10RuntimeRequestContext
{
	public A10RuntimeRequestContext(
		string supportRequestId,
		string requesterProfileId,
		string projectileOwnerProfileId)
	{
		SupportRequestId = supportRequestId ?? string.Empty;
		RequesterProfileId = requesterProfileId ?? string.Empty;
		ProjectileOwnerProfileId = projectileOwnerProfileId ?? string.Empty;
	}

	public string SupportRequestId { get; }
	public string RequesterProfileId { get; }
	public string ProjectileOwnerProfileId { get; }
}
