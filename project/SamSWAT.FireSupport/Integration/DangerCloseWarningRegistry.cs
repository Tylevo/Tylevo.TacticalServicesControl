#nullable enable

using System;
using System.Collections.Generic;

namespace SamSWAT.FireSupport.ArysReloaded.Integration;

/// <summary>
/// Raid-scoped validation, ownership, transition, and replay protection for
/// Danger Close warning messages. Presentation and transport are intentionally
/// kept outside this dependency-free state machine.
/// </summary>
internal sealed class DangerCloseWarningRegistry
{
	internal const int MaxOpportunityIdLength = 96;
	internal const int MaxSourceIdLength = 128;
	internal const int MaxAdvanceSeconds = 600;
	private const int MaxEntries = 512;

	private readonly object _gate = new();
	private readonly Dictionary<string, AuthorityEntry> _authorityEntries =
		new(StringComparer.Ordinal);
	private readonly Dictionary<string, ReceivedEntry> _receivedEntries =
		new(StringComparer.Ordinal);

	public bool TryRegisterAuthority(
		DangerCloseWarningKind kind,
		string opportunityId,
		int secondsRemaining,
		string sourceId,
		out bool shouldPublish,
		out string reason)
	{
		shouldPublish = false;
		if (!TryValidatePayload(kind, opportunityId, secondsRemaining, out reason))
		{
			return false;
		}

		if (!TryValidateSourceId(sourceId))
		{
			reason = "InvalidSourceId";
			return false;
		}

		lock (_gate)
		{
			if (!_authorityEntries.TryGetValue(opportunityId, out AuthorityEntry? existingEntry))
			{
				if (_authorityEntries.Count >= MaxEntries)
				{
					reason = "WarningCapacityReached";
					return false;
				}

				_authorityEntries.Add(
					opportunityId,
					new AuthorityEntry(sourceId, kind, secondsRemaining));
				shouldPublish = true;
				reason = "Published";
				return true;
			}

			AuthorityEntry entry = existingEntry!;

			if (!string.Equals(entry.SourceId, sourceId, StringComparison.Ordinal))
			{
				reason = "OpportunityOwnedByAnotherSource";
				return false;
			}

			switch (kind)
			{
				case DangerCloseWarningKind.Advance:
					if (entry.Kind != DangerCloseWarningKind.Advance)
					{
						reason = "AlreadyTerminal";
						return true;
					}

					if (entry.SecondsRemaining != secondsRemaining)
					{
						reason = "WarningPayloadMismatch";
						return false;
					}

					reason = "AlreadyPublished";
					return true;

				case DangerCloseWarningKind.Cancel:
					if (entry.Kind == DangerCloseWarningKind.Inbound)
					{
						reason = "AlreadyInbound";
						return true;
					}

					if (entry.Kind == DangerCloseWarningKind.Cancel)
					{
						reason = "AlreadyCancelled";
						return true;
					}

					entry.Kind = DangerCloseWarningKind.Cancel;
					entry.SecondsRemaining = 0;
					shouldPublish = true;
					reason = "Published";
					return true;

				case DangerCloseWarningKind.Inbound:
					if (entry.Kind == DangerCloseWarningKind.Inbound)
					{
						reason = "AlreadyInbound";
						return true;
					}

					// A physical pass that won a late cancellation race must still
					// produce the universal final safety alert.
					entry.Kind = DangerCloseWarningKind.Inbound;
					entry.SecondsRemaining = 0;
					shouldPublish = true;
					reason = "Published";
					return true;

				default:
					reason = "InvalidWarningKind";
					return false;
			}
		}
	}

	public bool TryRegisterReceived(
		DangerCloseWarningKind kind,
		string opportunityId,
		int secondsRemaining,
		out bool shouldPresent,
		out string reason)
	{
		shouldPresent = false;
		if (!TryValidatePayload(kind, opportunityId, secondsRemaining, out reason))
		{
			return false;
		}

		lock (_gate)
		{
			if (!_receivedEntries.TryGetValue(opportunityId, out ReceivedEntry? existingEntry))
			{
				if (_receivedEntries.Count >= MaxEntries)
				{
					reason = "WarningCapacityReached";
					return false;
				}

				var newEntry = new ReceivedEntry(kind, secondsRemaining);
				_receivedEntries.Add(opportunityId, newEntry);
				shouldPresent = kind != DangerCloseWarningKind.Cancel;
				reason = "Received";
				return true;
			}

			ReceivedEntry entry = existingEntry!;

			switch (kind)
			{
				case DangerCloseWarningKind.Advance:
					if (entry.Kind != DangerCloseWarningKind.Advance)
					{
						reason = "AlreadyTerminal";
						return true;
					}

					if (entry.SecondsRemaining != secondsRemaining)
					{
						reason = "WarningPayloadMismatch";
						return false;
					}

					reason = "Duplicate";
					return true;

				case DangerCloseWarningKind.Cancel:
					if (entry.Kind == DangerCloseWarningKind.Inbound)
					{
						reason = "AlreadyInbound";
						return true;
					}

					if (entry.Kind == DangerCloseWarningKind.Cancel)
					{
						reason = "Duplicate";
						return true;
					}

					shouldPresent = entry.AdvancePresented;
					entry.Kind = DangerCloseWarningKind.Cancel;
					entry.SecondsRemaining = 0;
					reason = "Received";
					return true;

				case DangerCloseWarningKind.Inbound:
					if (entry.Kind == DangerCloseWarningKind.Inbound)
					{
						reason = "Duplicate";
						return true;
					}

					entry.Kind = DangerCloseWarningKind.Inbound;
					entry.SecondsRemaining = 0;
					shouldPresent = true;
					reason = "Received";
					return true;

				default:
					reason = "InvalidWarningKind";
					return false;
			}
		}
	}

	public void MarkAdvancePresented(string opportunityId)
	{
		lock (_gate)
		{
			if (_receivedEntries.TryGetValue(opportunityId, out ReceivedEntry? entry) &&
			    entry.Kind == DangerCloseWarningKind.Advance)
			{
				entry.AdvancePresented = true;
			}
		}
	}

	public void Reset()
	{
		lock (_gate)
		{
			_authorityEntries.Clear();
			_receivedEntries.Clear();
		}
	}

	private static bool TryValidatePayload(
		DangerCloseWarningKind kind,
		string opportunityId,
		int secondsRemaining,
		out string reason)
	{
		if (!Enum.IsDefined(typeof(DangerCloseWarningKind), kind))
		{
			reason = "InvalidWarningKind";
			return false;
		}

		if (!TryValidateToken(opportunityId, MaxOpportunityIdLength, allowDot: false))
		{
			reason = "InvalidOpportunityId";
			return false;
		}

		if ((kind == DangerCloseWarningKind.Advance &&
		     (secondsRemaining < 1 || secondsRemaining > MaxAdvanceSeconds)) ||
		    (kind != DangerCloseWarningKind.Advance && secondsRemaining != 0))
		{
			reason = "InvalidSecondsRemaining";
			return false;
		}

		reason = string.Empty;
		return true;
	}

	private static bool TryValidateSourceId(string sourceId)
	{
		return TryValidateToken(sourceId, MaxSourceIdLength, allowDot: true);
	}

	private static bool TryValidateToken(string value, int maxLength, bool allowDot)
	{
		if (string.IsNullOrWhiteSpace(value) ||
		    value.Length > maxLength ||
		    !string.Equals(value, value.Trim(), StringComparison.Ordinal))
		{
			return false;
		}

		foreach (char character in value)
		{
			if (!char.IsLetterOrDigit(character) &&
			    character != '-' &&
			    character != '_' &&
			    character != ':' &&
			    (!allowDot || character != '.'))
			{
				return false;
			}
		}

		return true;
	}

	private sealed class AuthorityEntry
	{
		public AuthorityEntry(
			string sourceId,
			DangerCloseWarningKind kind,
			int secondsRemaining)
		{
			SourceId = sourceId;
			Kind = kind;
			SecondsRemaining = secondsRemaining;
		}

		public string SourceId { get; }
		public DangerCloseWarningKind Kind { get; set; }
		public int SecondsRemaining { get; set; }
	}

	private sealed class ReceivedEntry
	{
		public ReceivedEntry(DangerCloseWarningKind kind, int secondsRemaining)
		{
			Kind = kind;
			SecondsRemaining = secondsRemaining;
		}

		public DangerCloseWarningKind Kind { get; set; }
		public int SecondsRemaining { get; set; }
		public bool AdvancePresented { get; set; }
	}
}
