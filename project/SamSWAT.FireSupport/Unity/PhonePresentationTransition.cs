#nullable enable
using System;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

/// <summary>
/// A presentation clock shared by phone FOV and hand framing. Sampling depends
/// on elapsed time, not frame count, and a retired owner cannot resume writing.
/// </summary>
public sealed class PhonePresentationTransition
{
	private object? _owner;
	private float _startedAt;
	private float _duration;

	public bool IsActive => _owner != null;

	public void Begin(object owner, float startedAt, float durationSeconds)
	{
		_owner = owner;
		_startedAt = startedAt;
		_duration = IsFinite(durationSeconds) && durationSeconds > 0f ? durationSeconds : 0f;
	}

	public bool TrySample(float now, object? currentOwner, out float blend)
	{
		blend = 0f;
		if (_owner == null || !ReferenceEquals(_owner, currentOwner) ||
		    !IsFinite(now) || !IsFinite(_startedAt))
		{
			Cancel();
			return false;
		}

		float elapsed = now - _startedAt;
		float progress = elapsed <= 0f ? 0f : _duration <= 0f ? 1f : Math.Min(elapsed / _duration, 1f);
		blend = progress * progress * (3f - 2f * progress);
		return true;
	}

	public void Cancel()
	{
		_owner = null;
	}

	private static bool IsFinite(float value)
	{
		return !float.IsNaN(value) && !float.IsInfinity(value);
	}
}
