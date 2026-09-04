using Comfort.Common;
using EFT;
using EFT.Communications;
using SamSWAT.FireSupport.ArysReloaded.Unity;
using System;

namespace SamSWAT.FireSupport.ArysReloaded.Integration;

/// <summary>
/// Applies warning presentation locally and exposes only authority-originated
/// publications to the optional Fika transport assembly.
/// </summary>
public static class DangerCloseWarningNetworking
{
	private static readonly DangerCloseWarningRegistry s_registry = new();
	private static readonly object s_presentationGate = new();

	/// <summary>
	/// Raised after an authority publication has been accepted and applied to
	/// the local peer. Fika subscribes on a listen host and broadcasts it.
	/// </summary>
	public static event Action<DangerCloseWarningPublication> AuthorityPublished;

	internal static bool TryPublishAuthority(
		DangerCloseWarningKind kind,
		string opportunityId,
		int secondsRemaining,
		string sourceId,
		out string reason)
	{
		if (!s_registry.TryRegisterAuthority(
			    kind,
			    opportunityId,
			    secondsRemaining,
			    sourceId,
			    out bool shouldPublish,
			    out reason))
		{
			return false;
		}

		if (!shouldPublish)
		{
			return true;
		}

		var publication = new DangerCloseWarningPublication(
			kind,
			opportunityId,
			secondsRemaining);
		if (!ApplyReceived(publication, out string localReason))
		{
			FireSupportPlugin.LogSource?.LogWarning(
				$"TSC could not apply its local Danger Close warning opportunity={A10AuthorityDiagnostics.ShortId(opportunityId)} reason={localReason}.");
		}

		PublishToOptionalTransports(publication);
		reason = "Published";
		return true;
	}

	/// <summary>
	/// Applies one host-authenticated Fika publication without re-publishing it.
	/// Fika clients register only a server-to-client handler for this payload.
	/// </summary>
	public static bool ApplyRemote(
		DangerCloseWarningPublication publication,
		out string reason)
	{
		return ApplyReceived(publication, out reason);
	}

	/// <summary>
	/// Clears replay and presentation state when Fika changes network/raid state.
	/// </summary>
	public static void ResetForNetworkBoundary(string reason)
	{
		lock (s_presentationGate)
		{
			s_registry.Reset();
			TryResetDangerClosePresentation(reason ?? "network boundary");
		}
		TscDiagnostics.LogFika(
			$"TSC Danger Close warning state cleared reason={reason ?? "network boundary"}.");
	}

	internal static void ResetForRaidBoundary(string reason)
	{
		lock (s_presentationGate)
		{
			s_registry.Reset();
			TryResetDangerClosePresentation(reason ?? "raid boundary");
		}
		FireSupportPlugin.LogSource?.LogInfo(
			$"TSC Danger Close warning state cleared reason={reason ?? "raid boundary"}.");
	}

	private static bool ApplyReceived(
		DangerCloseWarningPublication publication,
		out string reason)
	{
		lock (s_presentationGate)
		{
			return ApplyReceivedSerialized(publication, out reason);
		}
	}

	private static bool ApplyReceivedSerialized(
		DangerCloseWarningPublication publication,
		out string reason)
	{
		if (!s_registry.TryRegisterReceived(
			    publication.Kind,
			    publication.OpportunityId,
			    publication.SecondsRemaining,
			    out bool shouldPresent,
			    out reason))
		{
			return false;
		}

		if (!shouldPresent)
		{
			return true;
		}

		if (publication.Kind == DangerCloseWarningKind.Cancel ||
		    publication.Kind == DangerCloseWarningKind.Inbound)
		{
			TryApplyDangerCloseTerminal(publication);
		}

		if (publication.Kind == DangerCloseWarningKind.Advance &&
		    !LocalPlayerHasWarningUplink())
		{
			reason = "ReceivedWithoutEquippedUplink";
			return true;
		}

		bool incomingCallStarted =
			publication.Kind == DangerCloseWarningKind.Advance &&
			TryStartDangerCloseIncomingCall(publication);
		if (incomingCallStarted)
		{
			// Starting the visible/audible call counts as presentation even if
			// EFT's stock notification surface later fails.
			s_registry.MarkAdvancePresented(publication.OpportunityId);
		}

		try
		{
			NotificationManager.DisplayWarningNotification(
				CreateMessage(publication, incomingCallStarted),
				ENotificationDurationType.Long);
			if (publication.Kind == DangerCloseWarningKind.Advance &&
			    !incomingCallStarted)
			{
				s_registry.MarkAdvancePresented(publication.OpportunityId);
			}
			reason = "Presented";
			return true;
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource?.LogWarning(
				$"TSC Danger Close notification failed kind={publication.Kind} opportunity={A10AuthorityDiagnostics.ShortId(publication.OpportunityId)}. {ex}");
			if (incomingCallStarted)
			{
				reason = "IncomingCallPresented";
				return true;
			}

			reason = "PresentationFailed";
			return false;
		}
	}

	private static bool LocalPlayerHasWarningUplink()
	{
		GameWorld gameWorld = Singleton<GameWorld>.Instance;
		return UavDeviceInventory.HasUplinkInDedicatedWarningSlot(
			gameWorld?.MainPlayer);
	}

	private static bool TryStartDangerCloseIncomingCall(
		DangerCloseWarningPublication publication)
	{
		try
		{
			return UavPhoneHotkeyController.TryPresentDangerCloseAdvance(publication);
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource?.LogWarning(
				$"TSC Danger Close incoming call failed; using the stock warning opportunity={A10AuthorityDiagnostics.ShortId(publication.OpportunityId)}. {ex}");
			TryResetDangerClosePresentation("incoming call startup failed");
			return false;
		}
	}

	private static void TryApplyDangerCloseTerminal(
		DangerCloseWarningPublication publication)
	{
		try
		{
			UavPhoneHotkeyController.ApplyDangerCloseTerminal(publication);
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource?.LogWarning(
				$"TSC Danger Close phone teardown failed kind={publication.Kind} opportunity={A10AuthorityDiagnostics.ShortId(publication.OpportunityId)}. {ex}");
		}
	}

	private static void TryResetDangerClosePresentation(string reason)
	{
		try
		{
			UavPhoneHotkeyController.ResetDangerClosePresentation(reason);
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource?.LogWarning(
				$"TSC Danger Close phone reset failed reason={reason}. {ex}");
		}
	}

	private static string CreateMessage(
		DangerCloseWarningPublication publication,
		bool incomingCallStarted)
	{
		return publication.Kind switch
		{
			DangerCloseWarningKind.Advance when incomingCallStarted =>
				$"TSC UPLINK: PHONE RINGING. Press [{GetAnswerKeyLabel()}] to answer.",
			DangerCloseWarningKind.Advance =>
				$"TSC UPLINK: A-10 strafe expected in approximately {publication.SecondsRemaining} seconds. Seek cover.",
			DangerCloseWarningKind.Cancel =>
				"TSC UPLINK: A-10 tasking cancelled. Stand down.",
			DangerCloseWarningKind.Inbound =>
				"A-10 STRAFE INBOUND - SEEK COVER NOW!",
			_ => "A-10 warning received."
		};
	}

	private static string GetAnswerKeyLabel()
	{
		return PluginSettings.OpenUavRadarKey != null
			? PluginSettings.OpenUavRadarKey.Value.ToString().ToUpperInvariant()
			: "J";
	}

	private static void PublishToOptionalTransports(
		DangerCloseWarningPublication publication)
	{
		Action<DangerCloseWarningPublication> handlers = AuthorityPublished;
		if (handlers == null)
		{
			return;
		}

		foreach (Delegate handler in handlers.GetInvocationList())
		{
			try
			{
				((Action<DangerCloseWarningPublication>)handler)(publication);
			}
			catch (Exception ex)
			{
				// A missing or broken optional transport must never change the
				// accepted warning/scheduler result.
				FireSupportPlugin.LogSource?.LogWarning(
					$"TSC optional Danger Close warning transport failed kind={publication.Kind} opportunity={A10AuthorityDiagnostics.ShortId(publication.OpportunityId)}. {ex}");
			}
		}
	}
}
