namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public sealed class FireSupportAuthorizationUse
{
	public bool Ok { get; set; }
	public bool ConsumedAuthorization { get; set; }
	public ESupportType ConsumedAuthorizationType { get; set; }
	public string RequestId { get; set; } = string.Empty;
	public bool ServerBacked { get; set; }

	public static FireSupportAuthorizationUse Failed(ESupportType supportType)
	{
		return new FireSupportAuthorizationUse
		{
			Ok = false,
			ConsumedAuthorizationType = supportType
		};
	}
}
