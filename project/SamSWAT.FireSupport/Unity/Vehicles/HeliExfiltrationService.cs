namespace SamSWAT.FireSupport.ArysReloaded.Unity;

/// <summary>
/// Standard UH-60 extraction product. Cargo transfer has a separate concrete
/// service and cannot enter this extraction-specific acknowledgement path.
/// </summary>
public sealed class HeliExfiltrationService
	: HelicopterDispatchService
{
	public HeliExfiltrationService(
		FireSupportSpotter spotter,
		int maxRequests)
		: base(spotter, maxRequests)
	{
	}

	public override ESupportType SupportType => ESupportType.Extract;

	protected override void PlayRequestAcknowledgement()
	{
		FireSupportAudio.Instance.PlayVoiceover(
			EVoiceoverType.StationExtractionRequest);
	}
}
