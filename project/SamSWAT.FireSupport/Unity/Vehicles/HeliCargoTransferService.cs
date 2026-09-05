namespace SamSWAT.FireSupport.ArysReloaded.Unity;

/// <summary>
/// UH-60 cargo-transfer product. The released PriorityExfil enum value remains
/// its wire/config/ledger key, but this service owns no extraction behavior.
/// </summary>
public sealed class HeliCargoTransferService
	: HelicopterDispatchService
{
	public HeliCargoTransferService(
		FireSupportSpotter spotter,
		int maxRequests)
		: base(spotter, maxRequests)
	{
	}

	public override ESupportType SupportType => ESupportType.PriorityExfil;
}
