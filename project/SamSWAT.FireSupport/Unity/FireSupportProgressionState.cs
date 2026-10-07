using System;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

/// <summary>Permission is bound to one authenticated session, never to raid globals.</summary>
public sealed class FireSupportProgressionState
{
	private string _sessionKey = string.Empty;
	private string _permit = string.Empty;
	private bool _unlocked;

	public void Clear()
	{
		_sessionKey = string.Empty;
		_permit = string.Empty;
		_unlocked = false;
	}

	public void Apply(string sessionKey, bool playerStateIncluded, bool? unlocked, string permit)
	{
		Clear();
		if (string.IsNullOrWhiteSpace(sessionKey) || !playerStateIncluded ||
		    unlocked != true || !IsValidPermit(permit))
		{
			return;
		}
		_sessionKey = sessionKey;
		_permit = permit;
		_unlocked = true;
	}

	public bool IsUnlocked(string sessionKey) =>
		_unlocked && !string.IsNullOrWhiteSpace(sessionKey) &&
		string.Equals(_sessionKey, sessionKey, StringComparison.Ordinal);

	public string GetPermit(string sessionKey) => IsUnlocked(sessionKey) ? _permit : string.Empty;

	public static bool IsValidPermit(string permit)
	{
		if (permit == null || permit.Length != 64) return false;
		foreach (char value in permit)
		{
			if (!(value >= '0' && value <= '9') && !(value >= 'a' && value <= 'f')) return false;
		}
		return true;
	}
}
