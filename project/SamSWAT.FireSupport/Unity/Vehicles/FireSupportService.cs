using Cysharp.Threading.Tasks;
using System.Runtime.CompilerServices;
using System.Threading;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public abstract class FireSupportService(int maxRequests) : IFireSupportService
{
	protected int availableRequests = maxRequests;
	protected bool requestAvailable = true;

	public abstract ESupportType SupportType { get; }
	public int AvailableRequests => availableRequests;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool IsRequestAvailable()
	{
		return requestAvailable && HasRequestBudget();
	}

	private bool HasRequestBudget()
	{
		PaymentMode paymentMode = FireSupportPayment.GetActivePaymentMode();
		return paymentMode switch
		{
			PaymentMode.PhoneAuthorizations => FireSupportAuthorizations.HasDeployable(SupportType),
			PaymentMode.Hybrid => availableRequests > 0 || FireSupportAuthorizations.HasDeployable(SupportType),
			_ => availableRequests > 0
		};
	}

	public abstract UniTaskVoid PlanRequest(CancellationToken cancellationToken);
}
