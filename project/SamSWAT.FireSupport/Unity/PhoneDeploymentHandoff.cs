namespace SamSWAT.FireSupport.ArysReloaded.Unity;

internal enum PhoneDeploymentHandoffState
{
	Waiting,
	Ready,
	Cancelled
}

/// <summary>Waits for phone camera restoration without outliving a newer phone session.</summary>
internal sealed class PhoneDeploymentHandoff
{
	private readonly int _phoneSessionGeneration;
	private readonly float _notBeforeUnscaledTime;
	private bool _finished;

	public PhoneDeploymentHandoff(int phoneSessionGeneration, float queuedAtUnscaledTime)
	{
		_phoneSessionGeneration = phoneSessionGeneration;
		_notBeforeUnscaledTime = queuedAtUnscaledTime + 0.35f;
	}

	public PhoneDeploymentHandoffState Advance(
		int currentPhoneSessionGeneration,
		float unscaledTime,
		bool zoomRestorePending,
		bool paused)
	{
		if (_finished || currentPhoneSessionGeneration != _phoneSessionGeneration ||
		    float.IsNaN(unscaledTime) || float.IsInfinity(unscaledTime))
		{
			_finished = true;
			return PhoneDeploymentHandoffState.Cancelled;
		}

		// The camera's native restore uses scaled time, so elapsed UI time alone
		// cannot prove that it finished while the game was paused.
		if (unscaledTime < _notBeforeUnscaledTime || zoomRestorePending || paused)
		{
			return PhoneDeploymentHandoffState.Waiting;
		}

		_finished = true;
		return PhoneDeploymentHandoffState.Ready;
	}
}
