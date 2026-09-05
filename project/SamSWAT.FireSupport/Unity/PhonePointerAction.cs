namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public enum PhonePointerActionKind
{
	None,
	OpenServices,
	OpenCategory,
	SelectService,
	ReviewService,
	ConfirmPurchase,
	Back,
	Close,
	SelectDeployment,
	DeploySelected
}

public readonly struct PhonePointerAction
{
	public PhonePointerActionKind Kind { get; }
	public ESupportType SupportType { get; }
	public int Index { get; }

	public PhonePointerAction(
		PhonePointerActionKind kind,
		ESupportType supportType = ESupportType.None,
		int index = -1)
	{
		Kind = kind;
		SupportType = supportType;
		Index = index;
	}
}
