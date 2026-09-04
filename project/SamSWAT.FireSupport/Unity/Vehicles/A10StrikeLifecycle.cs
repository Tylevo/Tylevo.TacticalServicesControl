using System;
using System.Collections.Generic;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

/// <summary>
/// Tracks the visual or headless lifetime of accepted A-10 passes. Authority
/// acceptance starts a pass but does not mean the aircraft has left the raid.
/// </summary>
public static class A10StrikeLifecycle
{
	private static readonly object s_gate = new();
	private static readonly HashSet<string> s_activeRequestIds = new(StringComparer.Ordinal);

	public static event Action<string> Completed;

	public static bool HasActivePasses
	{
		get
		{
			lock (s_gate)
			{
				return s_activeRequestIds.Count > 0;
			}
		}
	}

	public static void Begin(string supportRequestId)
	{
		if (string.IsNullOrWhiteSpace(supportRequestId))
		{
			return;
		}

		lock (s_gate)
		{
			s_activeRequestIds.Add(supportRequestId);
		}
	}

	public static void Complete(string supportRequestId)
	{
		if (string.IsNullOrWhiteSpace(supportRequestId))
		{
			return;
		}

		bool removed;
		lock (s_gate)
		{
			removed = s_activeRequestIds.Remove(supportRequestId);
		}

		if (removed)
		{
			Completed?.Invoke(supportRequestId);
		}
	}

	public static void Reset()
	{
		string[] active;
		lock (s_gate)
		{
			active = new string[s_activeRequestIds.Count];
			s_activeRequestIds.CopyTo(active);
			s_activeRequestIds.Clear();
		}

		foreach (string supportRequestId in active)
		{
			Completed?.Invoke(supportRequestId);
		}
	}
}
