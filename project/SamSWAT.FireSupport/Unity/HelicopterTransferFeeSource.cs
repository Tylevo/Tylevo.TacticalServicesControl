namespace SamSWAT.FireSupport.ArysReloaded;

/// <summary>
/// Selects only the payment source for EFT's native UH-60 cargo handling fee.
/// It is intentionally separate from TSC authorization pricing and currency.
/// </summary>
internal enum HelicopterTransferFeeSource
{
	Carried = 0,
	Stash = 1
}
