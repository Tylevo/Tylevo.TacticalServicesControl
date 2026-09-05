using System;

namespace SamSWAT.FireSupport.ArysReloaded.Integration;

internal enum DangerCloseIncomingCallPhase
{
	Idle,
	Ringing,
	Answering,
	Reopening,
	Answered,
	AnsweredStowed,
	TimedOut,
	Cancelled,
	Inbound,
	Completed
}

internal enum DangerCloseIncomingCallTickResult
{
	None,
	RingTimedOut,
	AnswerEquipTimedOut,
	ReopenEquipTimedOut,
	AdvanceExpired
}

/// <summary>
/// Owns the local-only lifecycle of one received Danger Close phone call.
/// It deliberately has no network or scheduler behavior: answering never
/// delays, authorizes, or cancels the authority-owned A-10 request.
/// </summary>
internal sealed class DangerCloseIncomingCallState
{
	private string _opportunityId = string.Empty;
	private double _advanceDeadline;
	private double _ringDeadline;
	private double _phaseDeadline;

	public DangerCloseIncomingCallPhase Phase { get; private set; }

	public bool IsRinging => Phase == DangerCloseIncomingCallPhase.Ringing;

	public bool IsAnswering => Phase == DangerCloseIncomingCallPhase.Answering;

	public bool IsReopening => Phase == DangerCloseIncomingCallPhase.Reopening;

	public bool IsAnswered => Phase == DangerCloseIncomingCallPhase.Answered;

	public bool IsAnsweredStowed => Phase == DangerCloseIncomingCallPhase.AnsweredStowed;

	public bool IsAnswerActive => IsAnswering || IsReopening || IsAnswered;

	public bool IsActive => IsRinging || IsAnswerActive || IsAnsweredStowed;

	public bool TryBeginAdvance(
		string opportunityId,
		int secondsRemaining,
		double now,
		double ringDurationSeconds)
	{
		if (string.IsNullOrWhiteSpace(opportunityId) ||
		    secondsRemaining <= 0 ||
		    ringDurationSeconds <= 0d)
		{
			return false;
		}

		Tick(now);

		// A replay must never restart the audio or extend its answer window.
		if (string.Equals(_opportunityId, opportunityId, StringComparison.Ordinal) ||
		    IsActive)
		{
			return false;
		}

		_opportunityId = opportunityId;
		_advanceDeadline = now + secondsRemaining;
		_ringDeadline = Math.Min(now + ringDurationSeconds, _advanceDeadline);
		_phaseDeadline = _ringDeadline;
		Phase = DangerCloseIncomingCallPhase.Ringing;
		return true;
	}

	public void ResumeRingingAfterFailedAnswer(double now)
	{
		if (!IsAnswering)
		{
			return;
		}

		if (now >= _advanceDeadline)
		{
			Phase = DangerCloseIncomingCallPhase.Completed;
			_phaseDeadline = 0d;
			return;
		}

		if (now >= _ringDeadline)
		{
			Phase = DangerCloseIncomingCallPhase.TimedOut;
			_phaseDeadline = 0d;
			return;
		}

		Phase = DangerCloseIncomingCallPhase.Ringing;
		_phaseDeadline = _ringDeadline;
	}

	public bool TryBeginAnswer(
		double now,
		double equipTimeoutSeconds,
		out int secondsRemaining)
	{
		Tick(now);
		secondsRemaining = GetSecondsRemaining(now);
		if (!IsRinging || equipTimeoutSeconds <= 0d)
		{
			return false;
		}

		Phase = DangerCloseIncomingCallPhase.Answering;
		_phaseDeadline = Math.Min(now + equipTimeoutSeconds, _advanceDeadline);
		return true;
	}

	public bool TryBeginReopen(
		double now,
		double equipTimeoutSeconds,
		out int secondsRemaining)
	{
		Tick(now);
		secondsRemaining = GetSecondsRemaining(now);
		if (!IsAnsweredStowed || equipTimeoutSeconds <= 0d)
		{
			return false;
		}

		Phase = DangerCloseIncomingCallPhase.Reopening;
		_phaseDeadline = Math.Min(now + equipTimeoutSeconds, _advanceDeadline);
		return true;
	}

	public bool TryMarkAnswerPresented(double now)
	{
		Tick(now);
		if (!IsAnswering && !IsReopening)
		{
			return false;
		}

		Phase = DangerCloseIncomingCallPhase.Answered;
		_phaseDeadline = _advanceDeadline;
		return true;
	}

	public void MarkAnswerStowed()
	{
		if (!IsAnswered)
		{
			return;
		}

		Phase = DangerCloseIncomingCallPhase.AnsweredStowed;
		_phaseDeadline = _advanceDeadline;
	}

	public void ResumeStowedAfterFailedReopen(double now)
	{
		if (!IsReopening)
		{
			return;
		}

		if (now >= _advanceDeadline)
		{
			Phase = DangerCloseIncomingCallPhase.Completed;
			_phaseDeadline = 0d;
			return;
		}

		Phase = DangerCloseIncomingCallPhase.AnsweredStowed;
		_phaseDeadline = _advanceDeadline;
	}

	public DangerCloseIncomingCallTickResult Tick(double now)
	{
		if (IsActive && now >= _advanceDeadline)
		{
			Phase = DangerCloseIncomingCallPhase.Completed;
			_phaseDeadline = 0d;
			return DangerCloseIncomingCallTickResult.AdvanceExpired;
		}

		if (now < _phaseDeadline)
		{
			return DangerCloseIncomingCallTickResult.None;
		}

		if (IsRinging)
		{
			Phase = DangerCloseIncomingCallPhase.TimedOut;
			_phaseDeadline = 0d;
			return DangerCloseIncomingCallTickResult.RingTimedOut;
		}

		if (IsAnswering)
		{
			Phase = DangerCloseIncomingCallPhase.TimedOut;
			_phaseDeadline = 0d;
			return DangerCloseIncomingCallTickResult.AnswerEquipTimedOut;
		}

		if (IsReopening)
		{
			Phase = DangerCloseIncomingCallPhase.AnsweredStowed;
			_phaseDeadline = _advanceDeadline;
			return DangerCloseIncomingCallTickResult.ReopenEquipTimedOut;
		}

		return DangerCloseIncomingCallTickResult.None;
	}

	public bool TryCancel(string opportunityId)
	{
		if (!IsActive ||
		    string.IsNullOrWhiteSpace(opportunityId) ||
		    !string.Equals(_opportunityId, opportunityId, StringComparison.Ordinal))
		{
			return false;
		}

		Phase = DangerCloseIncomingCallPhase.Cancelled;
		_phaseDeadline = 0d;
		return true;
	}

	public bool TryMarkInbound(string opportunityId)
	{
		if (!IsActive ||
		    string.IsNullOrWhiteSpace(opportunityId) ||
		    !string.Equals(_opportunityId, opportunityId, StringComparison.Ordinal))
		{
			return false;
		}

		Phase = DangerCloseIncomingCallPhase.Inbound;
		_phaseDeadline = 0d;
		return true;
	}

	public int GetSecondsRemaining(double now)
	{
		return Math.Max(0, (int)Math.Ceiling(_advanceDeadline - now));
	}

	public void Reset()
	{
		_opportunityId = string.Empty;
		_advanceDeadline = 0d;
		_ringDeadline = 0d;
		_phaseDeadline = 0d;
		Phase = DangerCloseIncomingCallPhase.Idle;
	}
}
