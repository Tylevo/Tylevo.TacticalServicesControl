using System.Threading.Tasks;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public sealed class FireSupportAuthorizationUse
{
	internal enum FinalizationIntent
	{
		None,
		Commit,
		Refund
	}

	private readonly object _finalizationGate = new();
	private FinalizationIntent _finalizationIntent;
	private TaskCompletionSource<bool> _finalizationCompletion;

	public bool Ok { get; set; }
	public bool ConsumedAuthorization { get; set; }
	public ESupportType ConsumedAuthorizationType { get; set; }
	public string RequestId { get; set; } = string.Empty;
	public bool ServerBacked { get; set; }
	public string ServerSessionKey { get; set; } = string.Empty;
	public string ServerProfileId { get; set; } = string.Empty;
	public bool IsCommitted => GetIntent() == FinalizationIntent.Commit;
	public bool IsRefunded => GetIntent() == FinalizationIntent.Refund;
	public bool IsFinalized => GetIntent() != FinalizationIntent.None;

	internal bool TrySelectFinalization(
		FinalizationIntent requestedIntent,
		out bool ownsFinalization,
		out Task<bool> completion)
	{
		lock (_finalizationGate)
		{
			if (_finalizationIntent == FinalizationIntent.None)
			{
				_finalizationIntent = requestedIntent;
				_finalizationCompletion = new TaskCompletionSource<bool>(
					TaskCreationOptions.RunContinuationsAsynchronously);
				ownsFinalization = true;
			}
			else
			{
				ownsFinalization = false;
			}

			completion = _finalizationCompletion?.Task ?? Task.FromResult(false);
			return _finalizationIntent == requestedIntent;
		}
	}

	internal void CompleteFinalization(bool success)
	{
		TaskCompletionSource<bool> completion;
		lock (_finalizationGate)
		{
			completion = _finalizationCompletion;
		}

		completion?.TrySetResult(success);
	}

	private FinalizationIntent GetIntent()
	{
		lock (_finalizationGate)
		{
			return _finalizationIntent;
		}
	}

	public static FireSupportAuthorizationUse Failed(ESupportType supportType)
	{
		return new FireSupportAuthorizationUse
		{
			Ok = false,
			ConsumedAuthorizationType = supportType
		};
	}
}
