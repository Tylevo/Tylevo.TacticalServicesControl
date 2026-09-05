using Comfort.Common;
using Cysharp.Threading.Tasks;
using EFT;
using SamSWAT.FireSupport.ArysReloaded.Unity;
using System;
using System.Threading;
using UnityEngine;

namespace SamSWAT.FireSupport.ArysReloaded.Integration;

/// <summary>
/// Optional, reflection-friendly integration surface for Tylevo's Seasonal
/// Modifiers. TSC does not reference or require Seasonal Modifiers.
/// </summary>
public static class SeasonalModifiersBridge
{
	private const int CurrentApiVersion = 3;
	private const int MaxRequestIdLength = 96;
	private static readonly object s_dispatchGate = new();
	private static string s_reservedAmbientRequestId = string.Empty;

	static SeasonalModifiersBridge()
	{
		A10StrikeLifecycle.Completed += OnA10StrikeCompleted;
	}

	public static int ApiVersion => CurrentApiVersion;

	/// <summary>
	/// True only on a solo player or human Fika listen host that may originate
	/// the one authoritative environmental schedule. Fika clients remain visual
	/// consumers and dedicated-headless scheduling is intentionally unsupported.
	/// </summary>
	public static bool IsDangerCloseAuthority
	{
		get
		{
			GameWorld gameWorld = Singleton<GameWorld>.Instance;
			if (gameWorld == null)
			{
				return false;
			}

			if (!A10TracerNetworking.IsNetworkAuthorityActive)
			{
				return gameWorld.MainPlayer != null;
			}

			return string.Equals(
				A10TracerNetworking.CurrentAuthorityRole,
				A10AuthorityRole.FikaHost.ToString(),
				StringComparison.Ordinal);
		}
	}

	public static bool IsDangerCloseActive => DangerCloseLeaseRegistry.IsActive;

	/// <summary>
	/// Publishes an advance forecast for one automatic opportunity. Successful
	/// publication never depends on whether any peer currently has an eligible
	/// Uplink; device eligibility affects presentation only.
	/// </summary>
	public static bool TryPublishDangerCloseAdvanceWarning(
		string opportunityId,
		int secondsRemaining,
		string sourceId,
		out string reason)
	{
		return TryPublishDangerCloseWarning(
			DangerCloseWarningKind.Advance,
			opportunityId,
			secondsRemaining,
			sourceId,
			requireActiveLease: true,
			out reason);
	}

	/// <summary>
	/// Cancels a previously forecast opportunity. Cancellation is idempotent,
	/// including for a valid opportunity that was never presented locally.
	/// </summary>
	public static bool TryCancelDangerCloseAdvanceWarning(
		string opportunityId,
		string sourceId,
		out string reason)
	{
		return TryPublishDangerCloseWarning(
			DangerCloseWarningKind.Cancel,
			opportunityId,
			secondsRemaining: 0,
			sourceId,
			requireActiveLease: false,
			out reason);
	}

	/// <summary>
	/// Publishes the universal final safety alert after TSC has accepted the
	/// matching SeasonalAmbient A-10 request.
	/// </summary>
	public static bool TryPublishDangerCloseInboundWarning(
		string requestId,
		string sourceId,
		out string reason)
	{
		return TryPublishDangerCloseWarning(
			DangerCloseWarningKind.Inbound,
			requestId,
			secondsRemaining: 0,
			sourceId,
			requireActiveLease: true,
			out reason);
	}

	/// <summary>
	/// Acquires or releases this caller's manual-A-10 lock. Calls are idempotent,
	/// and releasing one source never releases another source's lease.
	/// </summary>
	public static bool TrySetDangerCloseActive(
		bool active,
		string sourceId,
		out string reason)
	{
		bool wasActive = DangerCloseLeaseRegistry.IsActive;
		if (!DangerCloseLeaseRegistry.TrySet(
			    active,
			    sourceId,
			    out bool changed,
			    out reason))
		{
			return false;
		}

		bool isActive = DangerCloseLeaseRegistry.IsActive;
		if (wasActive != isActive)
		{
			FireSupportPayment.NotifySettingsChanged(
				$"Danger Close {(isActive ? "activated" : "deactivated")}");
		}

		if (changed)
		{
			FireSupportPlugin.LogSource?.LogInfo(
				$"TSC Seasonal integration lease {(active ? "acquired" : "released")} source={sourceId} manualA10Locked={isActive}.");
		}

		return true;
	}

	/// <summary>
	/// Validates and queues one environmental A-10 pass. A true result
	/// means the request was reserved and queued, not that the later authority
	/// or aircraft lifecycle has completed.
	/// </summary>
	public static bool TryDispatchDangerCloseA10(
		Vector3 target,
		Vector3 direction,
		string requestId,
		out string reason)
	{
		return TryDispatchDangerCloseA10(
			target,
			direction,
			requestId,
			onProcessed: null,
			out reason);
	}

	/// <summary>
	/// Queues one neutral environmental pass and reports its later authority or
	/// executor acceptance exactly once. The synchronous result still means the
	/// request was reserved and queued; <paramref name="onProcessed"/> determines
	/// whether an aircraft pass was actually accepted.
	/// </summary>
	public static bool TryDispatchDangerCloseA10(
		Vector3 target,
		Vector3 direction,
		string requestId,
		Action<bool, string> onProcessed,
		out string reason)
	{
		if (!DangerCloseLeaseRegistry.IsActive)
		{
			reason = "ModifierInactive";
			return false;
		}

		if (!IsDangerCloseAuthority)
		{
			reason = "NotAuthority";
			return false;
		}

		GameWorld gameWorld = Singleton<GameWorld>.Instance;
		if (gameWorld == null || gameWorld.destroyCancellationToken.IsCancellationRequested)
		{
			reason = "RaidUnavailable";
			return false;
		}

		Player ballisticOwner = gameWorld.MainPlayer;
		string ballisticOwnerProfileId = ballisticOwner?.ProfileId?.Trim() ?? string.Empty;
		if (ballisticOwner == null ||
		    string.IsNullOrWhiteSpace(ballisticOwnerProfileId) ||
		    gameWorld.GetEverExistedBridgeByProfileID(ballisticOwnerProfileId) == null)
		{
			reason = "BallisticOwnerUnavailable";
			FireSupportPlugin.LogSource?.LogWarning(
				$"TSC Seasonal A-10 rejected requestId={A10AuthorityDiagnostics.ShortId(requestId)}; the authority player's ballistic bridge is unavailable.");
			return false;
		}

		if (!IsFinite(target) || !TryNormalizeDirection(direction, out Vector3 normalizedDirection))
		{
			reason = "InvalidGeometry";
			return false;
		}

		if (!IsValidRequestId(requestId))
		{
			reason = "InvalidRequestId";
			return false;
		}

		lock (s_dispatchGate)
		{
			if (!string.IsNullOrEmpty(s_reservedAmbientRequestId) ||
			    A10StrikeLifecycle.HasActivePasses)
			{
				reason = "StrikeAlreadyActive";
				return false;
			}

			s_reservedAmbientRequestId = requestId;
		}

		try
		{
			DispatchAmbientA10Async(
				gameWorld,
				target,
				normalizedDirection,
				requestId,
				ballisticOwnerProfileId,
				onProcessed,
				gameWorld.destroyCancellationToken)
				.Forget();
			reason = "Queued";
			return true;
		}
		catch (Exception ex)
		{
			ReleaseAmbientReservation(requestId);
			NotifyDispatchProcessed(onProcessed, accepted: false, "QueueFailed");
			FireSupportPlugin.LogSource?.LogWarning(
				$"TSC Seasonal A-10 request could not be queued requestId={A10AuthorityDiagnostics.ShortId(requestId)}. {ex}");
			reason = "QueueFailed";
			return false;
		}
	}

	internal static void ResetForRaidBoundary(string reason)
	{
		bool availabilityChanged = DangerCloseLeaseRegistry.Reset();
		DangerCloseWarningNetworking.ResetForRaidBoundary(reason);
		lock (s_dispatchGate)
		{
			s_reservedAmbientRequestId = string.Empty;
		}

		A10StrikeLifecycle.Reset();
		if (availabilityChanged)
		{
			FireSupportPayment.NotifySettingsChanged(
				$"Danger Close reset: {reason ?? "raid boundary"}");
		}
	}

	private static bool TryPublishDangerCloseWarning(
		DangerCloseWarningKind kind,
		string opportunityId,
		int secondsRemaining,
		string sourceId,
		bool requireActiveLease,
		out string reason)
	{
		if (!IsDangerCloseAuthority)
		{
			reason = "NotAuthority";
			return false;
		}

		if (requireActiveLease && !DangerCloseLeaseRegistry.IsActive)
		{
			reason = "ModifierInactive";
			return false;
		}

		return DangerCloseWarningNetworking.TryPublishAuthority(
			kind,
			opportunityId,
			secondsRemaining,
			sourceId,
			out reason);
	}

	private static async UniTaskVoid DispatchAmbientA10Async(
		GameWorld gameWorld,
		Vector3 target,
		Vector3 direction,
		string requestId,
		string ballisticOwnerProfileId,
		Action<bool, string> onProcessed,
		CancellationToken cancellationToken)
	{
		bool accepted = false;
		string processedReason = "QueueFailed";
		try
		{
			FireSupportNetworkRequestResult networkResult =
				await FireSupportNetworking.TryHandleSupportRequestAsync(
					ESupportType.Strafe,
					target,
					direction,
					Vector3.zero,
					cancellationToken,
					passIndex: 0,
					supportRequestId: requestId,
					requestOrigin: FireSupportRequestOrigin.SeasonalAmbient);
			if (networkResult.Handled)
			{
				accepted = networkResult.Accepted;
				processedReason = accepted
					? "Accepted"
					: string.IsNullOrWhiteSpace(networkResult.Reason)
						? "AuthorityRejected"
						: networkResult.Reason;
				if (!accepted)
				{
					FireSupportPlugin.LogSource?.LogWarning(
						$"TSC Seasonal A-10 authority rejected requestId={A10AuthorityDiagnostics.ShortId(requestId)} state={networkResult.State} reason={networkResult.Reason}.");
				}
				return;
			}

			cancellationToken.ThrowIfCancellationRequested();
			var request = new A10StrikeRequest
			{
				SupportRequestId = requestId,
				SupportType = ESupportType.Strafe,
				Position = target,
				Direction = direction,
				Rotation = Vector3.zero,
				VisualSeed = CreateStableSeed(requestId),
				PassIndex = 0,
				RequesterProfileId = ballisticOwnerProfileId,
				RequestOrigin = FireSupportRequestOrigin.SeasonalAmbient,
				ProjectileOwnerModeOverride = A10ProjectileOwnerMode.RequesterProfile,
				VisualOnly = false,
				Role = A10AuthorityRole.Singleplayer
			};

			accepted = await A10StrikeExecutorSelector.ExecuteAsync(
				request,
				cancellationToken);
			processedReason = accepted ? "Accepted" : "RuntimeRejected";
			if (!accepted)
			{
				FireSupportPlugin.LogSource?.LogWarning(
					$"TSC Seasonal A-10 local runtime rejected requestId={A10AuthorityDiagnostics.ShortId(requestId)}.");
			}
		}
		catch (OperationCanceledException)
		{
			processedReason = "RaidUnavailable";
		}
		catch (Exception ex)
		{
			processedReason = "QueueFailed";
			FireSupportPlugin.LogSource?.LogWarning(
				$"TSC Seasonal A-10 dispatch failed requestId={A10AuthorityDiagnostics.ShortId(requestId)}. {ex}");
		}
		finally
		{
			if (!accepted)
			{
				ReleaseAmbientReservation(requestId);
			}

			NotifyDispatchProcessed(onProcessed, accepted, processedReason);
		}
	}

	private static void NotifyDispatchProcessed(
		Action<bool, string> onProcessed,
		bool accepted,
		string reason)
	{
		if (onProcessed == null)
		{
			return;
		}

		try
		{
			onProcessed(accepted, reason ?? (accepted ? "Accepted" : "Rejected"));
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource?.LogWarning(
				$"TSC Seasonal dispatch callback failed. {ex}");
		}
	}

	private static void OnA10StrikeCompleted(string supportRequestId)
	{
		ReleaseAmbientReservation(supportRequestId);
	}

	private static void ReleaseAmbientReservation(string supportRequestId)
	{
		lock (s_dispatchGate)
		{
			if (string.Equals(
				    s_reservedAmbientRequestId,
				    supportRequestId,
				    StringComparison.Ordinal))
			{
				s_reservedAmbientRequestId = string.Empty;
			}
		}
	}

	private static bool TryNormalizeDirection(Vector3 direction, out Vector3 normalized)
	{
		normalized = default;
		if (!IsFinite(direction))
		{
			return false;
		}

		direction.y = 0f;
		if (direction.sqrMagnitude <= 0.0001f)
		{
			return false;
		}

		normalized = direction.normalized;
		return true;
	}

	private static bool IsFinite(Vector3 value)
	{
		return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
	}

	private static bool IsFinite(float value)
	{
		return !float.IsNaN(value) && !float.IsInfinity(value);
	}

	private static bool IsValidRequestId(string requestId)
	{
		if (string.IsNullOrWhiteSpace(requestId) ||
		    requestId.Length > MaxRequestIdLength ||
		    !string.Equals(requestId, requestId.Trim(), StringComparison.Ordinal))
		{
			return false;
		}

		foreach (char value in requestId)
		{
			if (!char.IsLetterOrDigit(value) &&
			    value != '-' &&
			    value != '_' &&
			    value != ':')
			{
				return false;
			}
		}

		return true;
	}

	private static int CreateStableSeed(string value)
	{
		unchecked
		{
			int hash = 17;
			foreach (char character in value)
			{
				hash = hash * 31 + character;
			}

			return hash == 0 ? 1 : hash;
		}
	}
}
