using System;
using System.Collections.Generic;

namespace SamSWAT.FireSupport.ArysReloaded.Integration;

/// <summary>
/// Tracks independent opt-in integrations that temporarily reserve A-10
/// tasking. A caller can only release its own lease, so one integration cannot
/// accidentally re-enable manual strikes while another still needs the lock.
/// </summary>
internal static class DangerCloseLeaseRegistry
{
	private const int MaxSourceIdLength = 128;
	private static readonly object s_gate = new();
	private static readonly HashSet<string> s_sources = new(StringComparer.Ordinal);

	public static bool IsActive
	{
		get
		{
			lock (s_gate)
			{
				return s_sources.Count > 0;
			}
		}
	}

	public static bool TrySet(
		bool active,
		string sourceId,
		out bool changed,
		out string reason)
	{
		changed = false;
		if (!TryNormalizeSourceId(sourceId, out string normalized))
		{
			reason = "InvalidSourceId";
			return false;
		}

		lock (s_gate)
		{
			changed = active
				? s_sources.Add(normalized)
				: s_sources.Remove(normalized);
		}

		reason = active
			? changed ? "Activated" : "AlreadyActive"
			: changed ? "Deactivated" : "AlreadyInactive";
		return true;
	}

	public static bool Reset()
	{
		lock (s_gate)
		{
			if (s_sources.Count == 0)
			{
				return false;
			}

			s_sources.Clear();
			return true;
		}
	}

	private static bool TryNormalizeSourceId(string sourceId, out string normalized)
	{
		normalized = sourceId?.Trim() ?? string.Empty;
		if (normalized.Length == 0 ||
		    normalized.Length > MaxSourceIdLength ||
		    !string.Equals(normalized, sourceId, StringComparison.Ordinal))
		{
			return false;
		}

		foreach (char value in normalized)
		{
			if (!char.IsLetterOrDigit(value) &&
			    value != '.' &&
			    value != '-' &&
			    value != '_' &&
			    value != ':')
			{
				return false;
			}
		}

		return true;
	}
}
