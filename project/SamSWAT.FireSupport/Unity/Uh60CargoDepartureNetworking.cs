using System;
using System.Collections.Generic;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

/// <summary>
/// Keeps the core UH-60 runtime independent from Fika while exposing the one
/// cargo lifecycle event that remote visual-only helicopters must mirror.
/// </summary>
public static class Uh60CargoDepartureNetworking
{
	private static readonly object s_gate = new();
	private static readonly Dictionary<string, bool> s_remoteDepartures =
		new(StringComparer.Ordinal);

	public delegate bool DepartureHandler(
		string supportRequestId,
		string requesterProfileId,
		bool successfulTransfer);

	public delegate void RemoteDepartureHandler(
		string supportRequestId,
		bool successfulTransfer);

	public static event DepartureHandler DeparturePublished =
		delegate { return true; };
	public static event RemoteDepartureHandler RemoteDepartureReceived =
		delegate { };

	public static bool TryPublishDeparture(
		string supportRequestId,
		string requesterProfileId,
		bool successfulTransfer)
	{
		if (string.IsNullOrWhiteSpace(supportRequestId) ||
		    string.IsNullOrWhiteSpace(requesterProfileId))
		{
			return false;
		}

		bool published = true;
		DepartureHandler handlers = DeparturePublished;
		foreach (DepartureHandler handler in handlers.GetInvocationList())
		{
			try
			{
				published &=
					handler(
						supportRequestId,
						requesterProfileId,
						successfulTransfer);
			}
			catch
			{
				published = false;
			}
		}

		return published;
	}

	public static void ApplyRemoteDeparture(
		string supportRequestId,
		bool successfulTransfer)
	{
		if (string.IsNullOrWhiteSpace(supportRequestId))
		{
			return;
		}

		lock (s_gate)
		{
			s_remoteDepartures[supportRequestId] =
				successfulTransfer;
		}

		RemoteDepartureReceived?.Invoke(
			supportRequestId,
			successfulTransfer);
	}

	public static bool TryGetRemoteDeparture(
		string supportRequestId,
		out bool successfulTransfer)
	{
		lock (s_gate)
		{
			return s_remoteDepartures.TryGetValue(
				supportRequestId ?? string.Empty,
				out successfulTransfer);
		}
	}

	public static void ResetRemoteDepartures()
	{
		lock (s_gate)
		{
			s_remoteDepartures.Clear();
		}
	}
}
