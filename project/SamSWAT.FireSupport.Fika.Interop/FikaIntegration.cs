using BepInEx.Configuration;
using BepInEx.Logging;
using Comfort.Common;
using Cysharp.Threading.Tasks;
using EFT;
using EFT.Ballistics;
using Fika.Core.Main.GameMode;
using Fika.Core.Main.Players;
using Fika.Core.Main.Utils;
using Fika.Core.Modding;
using Fika.Core.Modding.Events;
using Fika.Core.Networking;
using Fika.Core.Networking.LiteNetLib;
using Fika.Core.Networking.Packets.Player.Common;
using Fika.Core.Networking.Packets.Player.Common.SubPackets;
using SamSWAT.FireSupport.ArysReloaded.Unity;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace SamSWAT.FireSupport.ArysReloaded.Fika;

/// <summary>
/// Holds every code path that references Fika.Core types. Must only be touched by
/// <see cref="FireSupportFikaPlugin"/> after it has confirmed com.fika.core is loaded,
/// so single-player installs without Fika.Core.dll never resolve these types.
/// </summary>
public static class FikaIntegration
{
	private const int SettingsBroadcastDebounceMs = 250;
	private const float ClientSettingsRetryDelaySeconds = 1.5f;
	private const float ClientRequestRetryDelaySeconds = 4f;
	private const float AuthorityRequestTimeoutSeconds = 20f;
	private const float ClientRequestTimeoutSeconds = 30f;
	private const float ClientCancelSettlementSeconds = 5f;
	private const int MaxPendingClientRequests = 8;
	private const int MaxInFlightAuthorityRequests = 128;
	private const int MaxAuthorityRequestEntries = 512;
	private const int MaxBufferedTracerBurstsPerRequest = 8;
	private const int MaxSupportRequestIdLength = 96;
	private const int MaxRequesterProfileIdLength = 128;
	private const int MaxA10TracerSegmentsPerPacket = 20;
	private const float MaxHelicopterDispatchDelaySeconds = 120f;
	private const int MaxHelicopterWaitTimeSeconds = 300;
	private const float MaxHelicopterExtractTimeSeconds = 60f;
	private const float MinHelicopterSpeedMultiplier = 0.5f;
	private const float MaxHelicopterSpeedMultiplier = 3f;
	private const float MinimumExtractionWindowMarginSeconds = 1f;
	private static readonly bool RemotePhoneVisualSyncEnabled = false;

	private static ManualLogSource s_logSource;
	private static bool s_enabled;
	private static FikaServer s_server;
	private static FikaClient s_client;
	private static readonly HashSet<object> s_registeredPacketManagers = new();
	private static readonly object s_networkRequestGate = new();
	private static readonly Dictionary<string, ClientPendingRequest> s_pendingClientRequests = new(StringComparer.Ordinal);
	private static readonly Dictionary<string, AuthorityRequestEntry> s_authorityRequests = new(StringComparer.Ordinal);
	private static readonly Dictionary<string, SupportRequestFingerprint> s_acceptedClientEvents = new(StringComparer.Ordinal);
	private static readonly HashSet<string> s_startedClientUavLoiterEvents = new(StringComparer.Ordinal);
	private static readonly Dictionary<string, UavAuthorityReservation> s_uavAuthorityReservations = new(StringComparer.Ordinal);
	private static int s_inFlightAuthorityRequestCount;
	private static int s_hostSettingsRevision;
	private static int s_currentHostSettingsRevision;
	private static bool s_hasHostSettingsOverride;
	private static CancellationTokenSource s_settingsBroadcastDebounceCts;

	public static void Enable(ManualLogSource logSource)
	{
		if (s_enabled)
		{
			s_logSource = logSource;
			return;
		}

		s_enabled = true;
		s_logSource = logSource;
		FireSupportNetworking.SupportRequested += OnLocalSupportRequested;
		A10TracerNetworking.TracerBurstCreated += OnA10TracerBurstCreated;
		A10HeadlessDamageCommandDispatcher.Handler = TrySendA10HeadlessDamageCommand;
		UavA10LoiterNetworking.StartRequested += OnLocalUavLoiterRequested;
		if (RemotePhoneVisualSyncEnabled)
		{
			UavPhoneVisualNetworkService.PhoneVisualRequested += OnLocalUavPhoneVisualRequested;
		}
		FireSupportPayment.SettingsChanged += OnEffectiveSettingsChanged;
		FireSupportExtraction.ExtractOverride = OnExtractOverride;
		FikaEventDispatcher.SubscribeEvent<FikaNetworkManagerCreatedEvent>(OnFikaNetworkManagerCreated);
		FikaEventDispatcher.SubscribeEvent<FikaGameEndedEvent>(OnFikaGameEnded);
		FikaEventDispatcher.SubscribeEvent<PeerDisconnectedEvent>(OnPeerDisconnected);
	}

	public static void Disable()
	{
		if (!s_enabled)
		{
			return;
		}

		FireSupportNetworking.SupportRequested -= OnLocalSupportRequested;
		A10TracerNetworking.TracerBurstCreated -= OnA10TracerBurstCreated;
		A10HeadlessDamageCommandDispatcher.Handler = null;
		UavA10LoiterNetworking.StartRequested -= OnLocalUavLoiterRequested;
		if (RemotePhoneVisualSyncEnabled)
		{
			UavPhoneVisualNetworkService.PhoneVisualRequested -= OnLocalUavPhoneVisualRequested;
		}
		FireSupportPayment.SettingsChanged -= OnEffectiveSettingsChanged;
		FikaEventDispatcher.UnsubscribeEvent<FikaNetworkManagerCreatedEvent>(OnFikaNetworkManagerCreated);
		FikaEventDispatcher.UnsubscribeEvent<FikaGameEndedEvent>(OnFikaGameEnded);
		FikaEventDispatcher.UnsubscribeEvent<PeerDisconnectedEvent>(OnPeerDisconnected);
		s_settingsBroadcastDebounceCts?.Cancel();
		s_settingsBroadcastDebounceCts?.Dispose();
		s_settingsBroadcastDebounceCts = null;
		FireSupportExtraction.ExtractOverride = null;
		s_registeredPacketManagers.Clear();
		s_server = null;
		s_client = null;
		ResetNetworkRequestState(
			"plugin destroyed",
			FireSupportNetworkRequestResult.Cancel("FikaIntegrationDisabled"),
			clearAuthorityOutcomes: true);
		FireSupportRuntime.Dispose();
		A10TracerNetworking.SetNetworkAuthorityActive(false, "plugin destroyed");
		A10TracerNetworking.SetAuthorityRole(A10AuthorityRole.Singleplayer.ToString());
		ClearHostAuthority("plugin destroyed");
		s_enabled = false;
	}

	public static void OnUpdate()
	{
		bool disconnected = false;
		try
		{
			if (A10TracerNetworking.IsNetworkAuthorityActive &&
			    !FikaBackendUtils.IsServer &&
			    !FikaBackendUtils.IsClient)
			{
				disconnected = true;
				A10TracerNetworking.SetNetworkAuthorityActive(false, "Fika session disconnected");
			}
		}
		catch
		{
			if (A10TracerNetworking.IsNetworkAuthorityActive)
			{
				disconnected = true;
				A10TracerNetworking.SetNetworkAuthorityActive(false, "Fika state unavailable");
			}
		}

		if (disconnected)
		{
			s_server = null;
			s_client = null;
			s_registeredPacketManagers.Clear();
			ResetNetworkRequestState(
				"Fika session disconnected",
				FireSupportNetworkRequestResult.Cancel("FikaSessionDisconnected"),
				clearAuthorityOutcomes: true);
			FireSupportRuntime.Dispose();
		}

		if (!s_hasHostSettingsOverride)
		{
			return;
		}

		try
		{
			if (!FikaBackendUtils.IsClient)
			{
				ClearHostAuthority("Fika client disconnected");
			}
		}
		catch
		{
			ClearHostAuthority("Fika state unavailable");
		}
	}

	private static void OnFikaNetworkManagerCreated(FikaNetworkManagerCreatedEvent @event)
	{
		switch (@event.Manager)
		{
			case FikaServer server:
				if (!ReferenceEquals(s_server, server) || s_client != null)
				{
					s_registeredPacketManagers.Clear();
					ResetNetworkRequestState(
						"hosting Fika session",
						FireSupportNetworkRequestResult.Cancel("FikaManagerChanged"),
						clearAuthorityOutcomes: true);
				}
				s_server = server;
				s_client = null;
				A10TracerNetworking.SetAuthorityRole(GetA10AuthorityRole().ToString());
				A10TracerNetworking.SetNetworkAuthorityActive(true, "hosting Fika session");
				A10AuthorityDiagnostics.LogOptionalVisualModsOnce();
				ClearHostAuthority("hosting Fika session");
				if (TryMarkPacketRegistration(server, "server"))
				{
					server.RegisterPacket<FireSupportRequestPacket, NetPeer>(OnServerSupportRequest);
					server.RegisterPacket<FireSupportCancelPacket, NetPeer>(OnServerSupportCancel);
					server.RegisterPacket<FireSupportSettingsPacket, NetPeer>(OnServerSettingsRequest);
					server.RegisterPacket<A10TracerBurstPacket, NetPeer>(OnServerA10TracerBurst);
					if (RemotePhoneVisualSyncEnabled)
					{
						server.RegisterPacket<UavPhoneVisualPacket, NetPeer>(OnServerUavPhoneVisual);
					}
					TscDiagnostics.LogFika("TSC Fika packets registered on server.");
				}
				BroadcastHostSettings("network manager created");
				break;
			case FikaClient client:
				if (!ReferenceEquals(s_client, client) || s_server != null)
				{
					s_registeredPacketManagers.Clear();
					ResetNetworkRequestState(
						"joining Fika host",
						FireSupportNetworkRequestResult.Cancel("FikaManagerChanged"),
						clearAuthorityOutcomes: true);
				}
				s_client = client;
				s_server = null;
				A10TracerNetworking.SetAuthorityRole(A10AuthorityRole.FikaClient.ToString());
				A10TracerNetworking.SetNetworkAuthorityActive(true, "joining Fika host");
				A10AuthorityDiagnostics.LogOptionalVisualModsOnce();
				ClearHostAuthority("joining Fika host");
				FireSupportServerConfigClient.SetFikaClientHostAuthorityActive(true, "joining Fika host");
				if (TryMarkPacketRegistration(client, "client"))
				{
					client.RegisterPacket<FireSupportRequestPacket>(OnClientSupportBroadcast);
					client.RegisterPacket<FireSupportAuthorityResultPacket>(OnClientAuthorityResult);
					client.RegisterPacket<FireSupportSettingsPacket>(OnClientSettingsResponse);
					client.RegisterPacket<StartUavLoiterPacket>(OnClientStartUavLoiter);
					client.RegisterPacket<A10TracerBurstPacket>(OnClientA10TracerBurst);
					if (RemotePhoneVisualSyncEnabled)
					{
						client.RegisterPacket<UavPhoneVisualPacket>(OnClientUavPhoneVisual);
					}
					TscDiagnostics.LogFika("TSC Fika packets registered on client.");
				}
				RequestHostSettings(client);
				RequestHostSettingsAfterDelay(client).Forget();
				break;
		}
	}

	private static bool TryMarkPacketRegistration(object manager, string role)
	{
		if (s_registeredPacketManagers.Contains(manager))
		{
			TscDiagnostics.LogFika($"TSC Fika settings: skipped duplicate {role} packet registration");
			return false;
		}

		s_registeredPacketManagers.Add(manager);
		return true;
	}

	private static async UniTask<FireSupportNetworkRequestResult> OnLocalSupportRequested(
		ESupportType supportType,
		Vector3 position,
		Vector3 direction,
		Vector3 rotation,
		int visualSeed,
		float durationSeconds,
		int passIndex,
		string supportRequestId,
		HelicopterTimingSnapshot? helicopterTimingSnapshot,
		CancellationToken cancellationToken)
	{
		if (!IsSupportedNetworkType(supportType))
		{
			return FireSupportNetworkRequestResult.NotHandled("UnsupportedNetworkType");
		}

		if (cancellationToken.IsCancellationRequested)
		{
			return FireSupportNetworkRequestResult.Cancel();
		}

		bool isServer = FikaBackendUtils.IsServer;
		bool isClient = FikaBackendUtils.IsClient;
		HelicopterTimingSnapshot? effectiveHelicopterTiming =
			IsExtractionType(supportType)
				? helicopterTimingSnapshot ??
				  FireSupportTuningSettings.CaptureHelicopterTiming(supportType)
				: null;
		int helicopterTimingRevision = isServer
			? s_hostSettingsRevision
			: s_currentHostSettingsRevision;
		var packet = new FireSupportRequestPacket(
			supportType,
			position,
			direction,
			rotation,
			visualSeed,
			durationSeconds,
			passIndex,
			GetLocalProfileId(),
			supportRequestId,
			IsUavType(supportType)
				? UavReconSettings.GetScanInterval(supportType)
				: 0f,
			IsUavType(supportType)
				? UavReconSettings.GetRangeMeters(supportType)
				: 0f,
			effectiveHelicopterTiming,
			helicopterTimingRevision);

		if (isServer)
		{
			return await ProcessAuthoritySupportRequestAsync(
				packet,
				peer: null,
				cancellationToken,
				playUavActivationVisual: true,
				source: "local host request");
		}

		if (isClient)
		{
			return await SendClientSupportRequestAsync(packet, cancellationToken);
		}

		return FireSupportNetworkRequestResult.NotHandled("NoFikaSession");
	}

	private static async UniTask<FireSupportNetworkRequestResult> SendClientSupportRequestAsync(
		FireSupportRequestPacket packet,
		CancellationToken cancellationToken)
	{
		if (!TryValidateSupportRequest(packet, out string validationReason))
		{
			return FireSupportNetworkRequestResult.Reject(validationReason);
		}

		var fingerprint = new SupportRequestFingerprint(packet);
		ClientPendingRequest pending;
		bool created = false;
		lock (s_networkRequestGate)
		{
			if (s_pendingClientRequests.TryGetValue(packet.SupportRequestId, out pending))
			{
				if (!pending.Fingerprint.Equals(fingerprint))
				{
					return FireSupportNetworkRequestResult.Reject("RequestIdPayloadMismatch");
				}
			}
			else
			{
				if (s_pendingClientRequests.Count >= MaxPendingClientRequests)
				{
					return FireSupportNetworkRequestResult.Reject("TooManyPendingClientRequests");
				}

				pending = new ClientPendingRequest(fingerprint);
				s_pendingClientRequests.Add(packet.SupportRequestId, pending);
				created = true;
			}
		}

		if (created)
		{
			if (!TrySendClientSupportPacket(packet, "initial request"))
			{
				pending.TrySetResult(
					FireSupportNetworkRequestResult.Reject("FikaClientUnavailable"));
			}
			else
			{
				RetryClientSupportRequestAsync(packet, pending, cancellationToken).Forget();
			}
		}

		using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		float authorityTimeoutSeconds = ClientRequestTimeoutSeconds +
			(IsExtractionType(packet.SupportType)
				? Math.Max(0f, packet.HelicopterDispatchDelaySeconds)
				: 0f);
		timeoutCts.CancelAfter(TimeSpan.FromSeconds(authorityTimeoutSeconds));
		try
		{
			FireSupportNetworkRequestResult result =
				await pending.Completion.Task.AttachExternalCancellation(timeoutCts.Token);
			return result;
		}
		catch (OperationCanceledException)
		{
			if (pending.TryGetResult(out FireSupportNetworkRequestResult completed))
			{
				return completed;
			}

			bool callerCancelled = cancellationToken.IsCancellationRequested;
			TrySendClientCancelPacket(
				packet,
				callerCancelled ? "caller cancellation" : "client timeout");

			// Cancellation is authority-arbitrated. Give the host a bounded chance
			// to either cancel the in-flight executor or replay an Accepted result
			// that won the race before the cancel packet arrived.
			using var settlementCts = new CancellationTokenSource(
				TimeSpan.FromSeconds(ClientCancelSettlementSeconds));
			try
			{
				return await pending.Completion.Task.AttachExternalCancellation(
					settlementCts.Token);
			}
			catch (OperationCanceledException)
			{
				if (pending.TryGetResult(out completed))
				{
					return completed;
				}
			}

			FireSupportNetworkRequestResult unsettled = callerCancelled
				? FireSupportNetworkRequestResult.Cancel("AuthorityCancelUnsettled")
				: FireSupportNetworkRequestResult.Timeout("AuthorityCancelUnsettled");
			pending.TrySetResult(unsettled);
			return pending.TryGetResult(out completed) ? completed : unsettled;
		}
		finally
		{
			lock (s_networkRequestGate)
			{
				if (s_pendingClientRequests.TryGetValue(packet.SupportRequestId, out ClientPendingRequest current) &&
				    ReferenceEquals(current, pending))
				{
					s_pendingClientRequests.Remove(packet.SupportRequestId);
				}
			}
		}
	}

	private static bool TrySendClientSupportPacket(FireSupportRequestPacket packet, string reason)
	{
		try
		{
			FikaClient client = s_client ?? Singleton<FikaClient>.Instance;
			if (client == null)
			{
				return false;
			}

			TscDiagnostics.LogFika(
				$"TSC Fika support request sent type={packet.SupportType} requestId={A10AuthorityDiagnostics.FormatRequestId(packet.SupportRequestId)} reason={reason}; waiting for host authority.");
			client.SendData(ref packet, DeliveryMethod.ReliableOrdered);
			return true;
		}
		catch (Exception ex)
		{
			s_logSource?.LogWarning(
				$"TSC Fika support request send failed type={packet?.SupportType} requestId={A10AuthorityDiagnostics.FormatRequestId(packet?.SupportRequestId)} reason={reason}. {ex}");
			return false;
		}
	}

	private static bool TrySendClientCancelPacket(
		FireSupportRequestPacket request,
		string reason)
	{
		try
		{
			FikaClient client = s_client ?? Singleton<FikaClient>.Instance;
			if (client == null)
			{
				return false;
			}

			var packet = new FireSupportCancelPacket(request);
			client.SendData(ref packet, DeliveryMethod.ReliableOrdered);
			TscDiagnostics.LogFika(
				$"TSC Fika support cancellation sent type={packet.SupportType} requestId={A10AuthorityDiagnostics.FormatRequestId(packet.SupportRequestId)} reason={reason}; awaiting authority settlement.");
			return true;
		}
		catch (Exception ex)
		{
			s_logSource?.LogWarning(
				$"TSC Fika support cancellation send failed type={request?.SupportType} requestId={A10AuthorityDiagnostics.FormatRequestId(request?.SupportRequestId)} reason={reason}. {ex}");
			return false;
		}
	}

	private static async UniTaskVoid RetryClientSupportRequestAsync(
		FireSupportRequestPacket packet,
		ClientPendingRequest pending,
		CancellationToken cancellationToken)
	{
		try
		{
			await UniTask.WaitForSeconds(
				ClientRequestRetryDelaySeconds,
				cancellationToken: cancellationToken);

			lock (s_networkRequestGate)
			{
				if (pending.IsCompleted ||
				    !s_pendingClientRequests.TryGetValue(packet.SupportRequestId, out ClientPendingRequest current) ||
				    !ReferenceEquals(current, pending))
				{
					return;
				}
			}

			TrySendClientSupportPacket(packet, "single retry");
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			s_logSource?.LogWarning(
				$"TSC Fika support request retry failed requestId={A10AuthorityDiagnostics.FormatRequestId(packet?.SupportRequestId)}. {ex}");
		}
	}

	private static async UniTask<FireSupportNetworkRequestResult> ProcessAuthoritySupportRequestAsync(
		FireSupportRequestPacket receivedPacket,
		NetPeer peer,
		CancellationToken cancellationToken,
		bool playUavActivationVisual,
		string source)
	{
		if (!TryValidateSupportRequest(receivedPacket, out string validationReason))
		{
			FireSupportNetworkRequestResult invalid =
				FireSupportNetworkRequestResult.Reject(validationReason);
			TrySendAuthorityResult(
				peer,
				new FireSupportAuthorityResultPacket(
					receivedPacket,
					accepted: false,
					validationReason),
				$"invalid {source}");
			return invalid;
		}

		FireSupportRequestPacket request = CloneSupportRequest(receivedPacket);
		var fingerprint = new SupportRequestFingerprint(request);
		AuthorityRequestEntry entry;
		bool created = false;
		string immediateRejectReason = string.Empty;

		lock (s_networkRequestGate)
		{
			if (s_authorityRequests.TryGetValue(request.SupportRequestId, out entry))
			{
				if (!entry.Fingerprint.Equals(fingerprint))
				{
					immediateRejectReason = "RequestIdPayloadMismatch";
				}
				else if (!entry.IsOwnedBy(peer))
				{
					immediateRejectReason = "RequestIdPeerMismatch";
				}
			}
			else if (s_authorityRequests.Count >= MaxAuthorityRequestEntries)
			{
				// Never evict a terminal request ID during the raid. Once the
				// bounded table is full, unknown IDs are rejected so a late replay
				// cannot be admitted as fresh work after its original rejection.
				immediateRejectReason = "AuthorityRequestCapacityReached";
				entry = null;
			}
			else if (s_inFlightAuthorityRequestCount >= MaxInFlightAuthorityRequests)
			{
				immediateRejectReason = "TooManyInFlightAuthorityRequests";
				// This rejection is transient at the authority level, but it is
				// terminal for this request ID. Cache it so a retry cannot execute
				// after the requester has already observed the rejection/refund.
				entry = new AuthorityRequestEntry(
					request,
					fingerprint,
					peer,
					cancellationToken);
				entry.TryComplete(
					AuthorityOutcome.Rejected(
						request,
						immediateRejectReason));
				entry.MarkAuthorityWorkFinished();
				s_authorityRequests.Add(request.SupportRequestId, entry);
			}
			else
			{
				entry = new AuthorityRequestEntry(
					request,
					fingerprint,
					peer,
					cancellationToken);
				s_authorityRequests.Add(request.SupportRequestId, entry);
				s_inFlightAuthorityRequestCount++;
				created = true;
			}
		}

		if (!string.IsNullOrEmpty(immediateRejectReason))
		{
			var rejectionPacket = new FireSupportAuthorityResultPacket(
				request,
				accepted: false,
				immediateRejectReason);
			TrySendAuthorityResult(peer, rejectionPacket, $"rejected {source}");
			return FireSupportNetworkRequestResult.Reject(immediateRejectReason);
		}

		if (!created)
		{
			AuthorityOutcome replay = await entry.Completion.Task;
			ReplayAuthorityOutcome(entry, replay, peer, source);
			return replay.Result;
		}

		try
		{
			AuthorityOutcome outcome = await ExecuteAuthorityRequestAsync(
				entry,
				playUavActivationVisual,
				source);
			if (TryPublishAuthorityOutcome(entry, outcome, source))
			{
				return outcome.Result;
			}

			return entry.TryGetOutcome(out AuthorityOutcome completed)
				? completed.Result
				: FireSupportNetworkRequestResult.Cancel("AuthorityRequestReset");
		}
		finally
		{
			entry.MarkAuthorityWorkFinished();
		}
	}

	private static async UniTask<AuthorityOutcome> ExecuteAuthorityRequestAsync(
		AuthorityRequestEntry entry,
		bool playUavActivationVisual,
		string source)
	{
		FireSupportRequestPacket request = CloneSupportRequest(entry.Request);

		try
		{
			// Cache requester-identity failures in the same authority entry as
			// every other admitted outcome. Otherwise a request rejected while
			// Fika is still binding peer.Player could be replayed later with the
			// same ID and execute after the requester has already refunded it.
			if (!TryValidateRequesterPeer(
				    request,
				    entry.OriginPeer,
				    out string requesterValidationReason))
			{
				return AuthorityOutcome.Rejected(
					request,
					requesterValidationReason);
			}

			if (!TryApplyHostAuthority(request, out string hostAuthorityReason))
			{
				return AuthorityOutcome.Rejected(
					request,
					hostAuthorityReason);
			}

			if (entry.CancellationToken.IsCancellationRequested)
			{
				return AuthorityOutcome.Rejected(request, entry.CancellationReason);
			}

			if (Singleton<GameWorld>.Instance == null)
			{
				return AuthorityOutcome.Rejected(request, "RaidUnavailable");
			}

			if (!FireSupportServiceAvailability.IsServiceEnabled(request.SupportType))
			{
				return AuthorityOutcome.Rejected(request, "ServiceDisabled");
			}

			if (IsExtractionType(request.SupportType))
			{
				await UniTask.WaitForSeconds(
					request.HelicopterDispatchDelaySeconds,
					cancellationToken: entry.CancellationToken);
				if (entry.CancellationToken.IsCancellationRequested)
				{
					return AuthorityOutcome.Rejected(
						request,
						entry.CancellationReason);
				}
			}

			if (IsA10Type(request.SupportType) &&
			    IsFikaHeadlessHost() &&
			    FireSupportTuningSettings.GetA10HeadlessFikaMode() == A10HeadlessFikaMode.Disabled)
			{
				return AuthorityOutcome.Rejected(request, "HeadlessA10Disabled");
			}

			if (IsA10Type(request.SupportType) && IsFikaHeadlessHost())
			{
				A10StrikeRequest strikeRequest = CreateA10StrikeRequest(
					request,
					visualOnly: false,
					A10AuthorityRole.FikaHeadlessHost);
				if (!A10HeadlessDamageExecutor.TryPreflight(
					    strikeRequest,
					    out string preflightReason))
				{
					return AuthorityOutcome.Rejected(
						request,
						string.IsNullOrWhiteSpace(preflightReason)
							? "HeadlessA10PreflightRejected"
							: preflightReason);
				}

				if (entry.CancellationToken.IsCancellationRequested)
				{
					return AuthorityOutcome.Rejected(
						request,
						entry.CancellationReason);
				}

				// The irreversible damage pass is launched only after
				// TryPublishAuthorityOutcome wins Accepted and publishes the
				// canonical start payload.
				return AuthorityOutcome.Accepted(request);
			}

			if (!IsUavType(request.SupportType) &&
			    !(IsExtractionType(request.SupportType) &&
			      IsFikaHeadlessHost()))
			{
				// Asset/pool initialization can yield, but it has no support
				// side effects. Keep the request cancellable during that work
				// and move the irreversible execution-start boundary to the
				// short ProcessRequest invocation that follows.
				await FireSupportRuntime.EnsureInitialized(entry.CancellationToken);
				if (entry.CancellationToken.IsCancellationRequested)
				{
					return AuthorityOutcome.Rejected(
						request,
						entry.CancellationReason);
				}
			}

			string executionStartRejectReason = string.Empty;
			lock (s_networkRequestGate)
			{
				if (IsUavType(request.SupportType) &&
				    !TryReserveUavAuthorityLinkNoLock(
					    request,
					    out executionStartRejectReason))
				{
					// The reservation is requester-scoped. Other clients can
					// hold independent recon links, but one requester cannot
					// race a second fresh UAV request past authority.
				}
				else if (!entry.TryBeginExecution())
				{
					if (IsUavType(request.SupportType))
					{
						ReleaseUavAuthorityReservationNoLock(request);
					}

					if (entry.TryGetOutcome(
						    out AuthorityOutcome terminalOutcome))
					{
						return terminalOutcome;
					}

					return AuthorityOutcome.Rejected(
						request,
						"AuthorityExecutionStartRejected");
				}
			}

			if (!string.IsNullOrEmpty(executionStartRejectReason))
			{
				return AuthorityOutcome.Rejected(
					request,
					executionStartRejectReason);
			}

			bool success;
			try
			{
				success = await ExecuteSupportCore(
					request,
					visualOnly: false,
					entry.CancellationToken,
					playUavActivationVisual);
			}
			finally
			{
				entry.MarkAuthorityWorkFinished();
			}

			if (success)
			{
				return AuthorityOutcome.Accepted(request);
			}

			return AuthorityOutcome.Rejected(
				request,
				entry.CancellationToken.IsCancellationRequested
					? entry.CancellationReason
					: "ExecutorRejected");
		}
		catch (OperationCanceledException)
		{
			return AuthorityOutcome.Rejected(request, entry.CancellationReason);
		}
		catch (Exception ex)
		{
			s_logSource?.LogWarning(
				$"TSC Fika authority execution failed type={request.SupportType} requestId={A10AuthorityDiagnostics.FormatRequestId(request.SupportRequestId)} source={source}. {ex}");
			return AuthorityOutcome.Rejected(request, "AuthorityExecutionFailed");
		}
	}

	private static bool TryPublishAuthorityOutcome(
		AuthorityRequestEntry entry,
		AuthorityOutcome outcome,
		string source)
	{
		bool startAcceptedHeadlessA10 = false;
		lock (s_networkRequestGate)
		{
			if (!entry.TryComplete(outcome))
			{
				if (IsUavType(entry.Request.SupportType))
				{
					ReleaseUavAuthorityReservationNoLock(entry.Request);
				}
				return false;
			}

			s_inFlightAuthorityRequestCount = Math.Max(0, s_inFlightAuthorityRequestCount - 1);
			if (IsUavType(entry.Request.SupportType))
			{
				if (outcome.Result.Accepted)
				{
					AcceptUavAuthorityReservationNoLock(outcome.AcceptedRequest);
				}
				else
				{
					ReleaseUavAuthorityReservationNoLock(entry.Request);
				}
			}

			if (outcome.Result.Accepted &&
			    IsA10Type(outcome.AcceptedRequest.SupportType) &&
			    IsFikaHeadlessHost())
			{
				startAcceptedHeadlessA10 =
					entry.TryReserveAcceptedBackgroundWork();
			}
		}

		TrySendAuthorityResult(entry.OriginPeer, outcome.ResultPacket, source);
		if (outcome.Result.Accepted)
		{
			BroadcastSupportPacket(
				outcome.AcceptedRequest,
				entry.OriginPeer,
				broadcastToAll: true,
				reason: $"authority accepted {source}");

			if (IsUavType(outcome.AcceptedRequest.SupportType))
			{
				PublishAcceptedUavLoiter(outcome.AcceptedRequest);
				ReleaseUavAuthorityReservationAfterExpiryAsync(
						outcome.AcceptedRequest.RequesterProfileId,
						outcome.AcceptedRequest.SupportRequestId,
						outcome.AcceptedRequest.DurationSeconds,
						GetRaidCancellationToken())
					.Forget();
			}
		}

		List<A10TracerBurst> bufferedTracerBursts =
			entry.MarkAcceptedDeliveryPublishedAndDrainTracerBursts();
		foreach (A10TracerBurst burst in bufferedTracerBursts)
		{
			BroadcastA10TracerBurst(burst, "after authority accepted delivery");
		}

		if (startAcceptedHeadlessA10)
		{
			ExecuteAcceptedHeadlessA10PassAsync(
					outcome.AcceptedRequest,
					entry)
				.Forget();
		}

		TscDiagnostics.LogFika(
			$"TSC Fika authority completed support request type={entry.Request.SupportType} requestId={A10AuthorityDiagnostics.FormatRequestId(entry.Request.SupportRequestId)} accepted={outcome.Result.Accepted} reason={outcome.Result.Reason} source={source}");
		return true;
	}

	private static void ReplayAuthorityOutcome(
		AuthorityRequestEntry entry,
		AuthorityOutcome outcome,
		NetPeer peer,
		string source)
	{
		TscDiagnostics.LogFika(
			$"TSC Fika duplicate support request converged type={entry.Request.SupportType} requestId={A10AuthorityDiagnostics.FormatRequestId(entry.Request.SupportRequestId)} accepted={outcome.Result.Accepted} source={source}");
		TrySendAuthorityResult(peer, outcome.ResultPacket, $"duplicate replay {source}");
		if (peer != null && outcome.Result.Accepted)
		{
			SendAcceptedSupportToPeer(
				outcome.AcceptedRequest,
				peer,
				$"duplicate replay {source}");
		}
	}

	private static bool TrySendAuthorityResult(
		NetPeer peer,
		FireSupportAuthorityResultPacket packet,
		string reason)
	{
		if (peer == null)
		{
			return false;
		}

		try
		{
			FikaServer server = GetServer();
			if (server == null)
			{
				return false;
			}

			server.SendData(
				ref packet,
				DeliveryMethod.ReliableOrdered,
				peer);
			TscDiagnostics.LogFika(
				$"TSC Fika authority result sent type={packet.SupportType} requestId={A10AuthorityDiagnostics.FormatRequestId(packet.SupportRequestId)} accepted={packet.Accepted} reason={packet.Reason} sendReason={reason}");
			return true;
		}
		catch (Exception ex)
		{
			s_logSource?.LogWarning(
				$"TSC Fika authority result send failed requestId={A10AuthorityDiagnostics.FormatRequestId(packet?.SupportRequestId)} reason={reason}. {ex}");
			return false;
		}
	}

	private static void SendAcceptedSupportToPeer(
		FireSupportRequestPacket packet,
		NetPeer peer,
		string reason)
	{
		try
		{
			FikaServer server = GetServer();
			if (server == null || peer == null)
			{
				return;
			}

			server.SendData(
				ref packet,
				DeliveryMethod.ReliableOrdered,
				peer);
			TscDiagnostics.LogFika(
				$"TSC Fika accepted support replay sent type={packet.SupportType} requestId={A10AuthorityDiagnostics.FormatRequestId(packet.SupportRequestId)} reason={reason}");
		}
		catch (Exception ex)
		{
			s_logSource?.LogWarning(
				$"TSC Fika accepted support replay failed requestId={A10AuthorityDiagnostics.FormatRequestId(packet?.SupportRequestId)} reason={reason}. {ex}");
		}
	}

	private static bool OnLocalUavLoiterRequested(
		UavA10LoiterRequest request,
		CancellationToken cancellationToken)
	{
		try
		{
			if (FikaBackendUtils.IsServer || FikaBackendUtils.IsClient)
			{
				// Fika loiter presentation is emitted only by the authority
				// outcome publisher. Suppress any downstream local attempt so a
				// requester cannot create a second, unauthenticated command.
				TscDiagnostics.LogFika(
					"TSC UAV local loiter request suppressed; awaiting accepted authority presentation.");
				return true;
			}
		}
		catch (Exception ex)
		{
			s_logSource?.LogWarning($"UAV loiter authority-state check failed; skipping aircraft visual. {ex}");
			return true;
		}

		return false;
	}

	private static void OnLocalUavPhoneVisualRequested(
		UavPhoneVisualEvent visualEvent,
		CancellationToken cancellationToken)
	{
		try
		{
			if (visualEvent == null || cancellationToken.IsCancellationRequested)
			{
				return;
			}

			var packet = new UavPhoneVisualPacket(visualEvent);
			TscDiagnostics.LogFika(
				$"sending phone visual packet phase={packet.Phase}, support={packet.SupportType}");

			if (FikaBackendUtils.IsServer)
			{
				FikaServer server = GetServer();
				if (server == null)
				{
					s_logSource?.LogWarning(
						$"phone visual packet skipped; server unavailable phase={packet.Phase}, support={packet.SupportType}");
					return;
				}

				server.SendData(
					ref packet,
					DeliveryMethod.ReliableOrdered,
					broadcast: true);
				return;
			}

			if (FikaBackendUtils.IsClient)
			{
				FikaClient client = s_client ?? Singleton<FikaClient>.Instance;
				if (client == null)
				{
					s_logSource?.LogWarning(
						$"phone visual packet skipped; client unavailable phase={packet.Phase}, support={packet.SupportType}");
					return;
				}

				client.SendData(ref packet, DeliveryMethod.ReliableOrdered);
			}
		}
		catch (Exception ex)
		{
			s_logSource?.LogWarning($"phone visual packet send failed; cosmetic skipped. {ex}");
		}
	}

	private static void OnServerSupportRequest(FireSupportRequestPacket packet, NetPeer peer)
	{
		if (packet == null)
		{
			return;
		}

		ProcessAuthoritySupportRequestAsync(
				packet,
				peer,
				GetRaidCancellationToken(),
				playUavActivationVisual: false,
				source: "client request")
			.Forget();
	}

	private static void OnServerSupportCancel(
		FireSupportCancelPacket packet,
		NetPeer peer)
	{
		ProcessAuthorityCancelAsync(packet, peer).Forget();
	}

	private static async UniTaskVoid ProcessAuthorityCancelAsync(
		FireSupportCancelPacket packet,
		NetPeer peer)
	{
		if (packet == null)
		{
			return;
		}

		AuthorityRequestEntry entry = null;
		AuthorityOutcome outcome = default;
		string rejectReason = string.Empty;
		bool cancelWon = false;
		bool awaitStartedExecution = false;
		lock (s_networkRequestGate)
		{
			if (string.IsNullOrWhiteSpace(packet.SupportRequestId) ||
			    !s_authorityRequests.TryGetValue(packet.SupportRequestId, out entry))
			{
				rejectReason = "CancelRequestUnknown";
			}
			else if (!entry.IsOwnedBy(peer))
			{
				rejectReason = "CancelPeerMismatch";
			}
			else if (!entry.Fingerprint.MatchesCancel(packet))
			{
				rejectReason = "CancelIdentityMismatch";
			}
			else if (entry.ExecutionStarted &&
			         !entry.TryGetOutcome(out outcome))
			{
				// Starting a runtime is the irreversible commit point for
				// human-host A-10, extraction, and UAV. Cancellation after this
				// point waits for the executor result and cannot manufacture a
				// refund while the effect is live.
				awaitStartedExecution = true;
			}
			else if (!entry.TryGetOutcome(out outcome))
			{
				outcome = AuthorityOutcome.FromResult(
					entry.Request,
					FireSupportNetworkRequestResult.Cancel("RequesterCancelled"));
				cancelWon = entry.TryCancelAndComplete(
					outcome,
					"RequesterCancelled");
				if (cancelWon)
				{
					s_inFlightAuthorityRequestCount =
						Math.Max(0, s_inFlightAuthorityRequestCount - 1);
				}
				else
				{
					entry.TryGetOutcome(out outcome);
				}
			}
		}

		if (!string.IsNullOrEmpty(rejectReason))
		{
			FireSupportRequestPacket cancelIdentity = CreateRequestFromCancel(packet);
			TrySendAuthorityResult(
				peer,
				new FireSupportAuthorityResultPacket(
					cancelIdentity,
					accepted: false,
					rejectReason),
				"cancel rejected");
			return;
		}

		if (awaitStartedExecution)
		{
			outcome = await entry.Completion.Task;
		}

		if (entry == null || outcome.Result.State == FireSupportNetworkRequestState.NotHandled)
		{
			TrySendAuthorityResult(
				peer,
				new FireSupportAuthorityResultPacket(
					CreateRequestFromCancel(packet),
					accepted: false,
					"CancelSettlementUnavailable"),
				"cancel settlement unavailable");
			return;
		}

		TscDiagnostics.LogFika(
			$"TSC Fika authority cancellation settled type={entry.Request.SupportType} requestId={A10AuthorityDiagnostics.FormatRequestId(entry.Request.SupportRequestId)} cancelWon={cancelWon} accepted={outcome.Result.Accepted} reason={outcome.Result.Reason}");
		ReplayAuthorityOutcome(
			entry,
			outcome,
			peer,
			cancelWon ? "client cancellation accepted" : "client cancellation lost terminal race");
	}

	private static void OnClientSupportBroadcast(FireSupportRequestPacket packet)
	{
		if (packet == null)
		{
			return;
		}

		if (!TryRegisterClientSupportEvent(
			    packet,
			    out bool shouldPlay,
			    out string rejectReason))
		{
			TscDiagnostics.LogFika(
				$"TSC Fika accepted support event ignored type={packet.SupportType} requestId={A10AuthorityDiagnostics.FormatRequestId(packet.SupportRequestId)} reason={rejectReason}");
			return;
		}

		CompleteClientRequestFromAcceptedEvent(packet);
		if (!shouldPlay)
		{
			TscDiagnostics.LogFika(
				$"TSC Fika duplicate accepted support event converged type={packet.SupportType} requestId={A10AuthorityDiagnostics.FormatRequestId(packet.SupportRequestId)}");
			return;
		}

		ExecuteClientSupportVisual(packet, GetRaidCancellationToken(), playUavActivationVisual: false).Forget();
	}

	private static void OnClientAuthorityResult(FireSupportAuthorityResultPacket packet)
	{
		if (packet == null || string.IsNullOrWhiteSpace(packet.SupportRequestId))
		{
			return;
		}

		ClientPendingRequest pending = null;
		lock (s_networkRequestGate)
		{
			if (s_pendingClientRequests.TryGetValue(packet.SupportRequestId, out pending) &&
			    !pending.Fingerprint.MatchesResult(packet))
			{
				s_logSource?.LogWarning(
					$"TSC Fika authority result identity mismatch requestId={A10AuthorityDiagnostics.FormatRequestId(packet.SupportRequestId)}");
				pending.TrySetResult(
					FireSupportNetworkRequestResult.Reject("AuthorityResultIdentityMismatch"));
				return;
			}
		}

		if (!packet.Accepted)
		{
			if (pending == null)
			{
				TscDiagnostics.LogFika(
					$"TSC Fika late rejected authority result ignored type={packet.SupportType} requestId={A10AuthorityDiagnostics.FormatRequestId(packet.SupportRequestId)} reason={packet.Reason}");
				return;
			}

			pending.TrySetResult(MapAuthorityRejection(packet.Reason));
			return;
		}

		FireSupportRequestPacket acceptedRequest = packet.ToSupportRequest();
		if (pending != null &&
		    !pending.Fingerprint.MatchesAcceptedRequest(acceptedRequest))
		{
			s_logSource?.LogWarning(
				$"TSC Fika accepted authority payload mismatch requestId={A10AuthorityDiagnostics.FormatRequestId(packet.SupportRequestId)}");
			pending.TrySetResult(
				FireSupportNetworkRequestResult.Reject(
					"AuthorityAcceptedPayloadMismatch"));
			return;
		}

		if (!TryRegisterClientSupportEvent(
			    acceptedRequest,
			    out bool shouldPlay,
			    out string rejectReason))
		{
			s_logSource?.LogWarning(
				$"TSC Fika accepted authority payload rejected requestId={A10AuthorityDiagnostics.FormatRequestId(packet.SupportRequestId)} reason={rejectReason}");
			pending?.TrySetResult(
				FireSupportNetworkRequestResult.Reject(
					"AuthorityAcceptedPayloadInvalid"));
			return;
		}

		// Register the canonical payload before completing the purchase waiter.
		// This result packet is sufficient to start the requester visual even if
		// the following accepted-event broadcast is lost.
		if (shouldPlay)
		{
			ExecuteClientSupportVisual(
					acceptedRequest,
					GetRaidCancellationToken(),
					playUavActivationVisual: false)
				.Forget();
		}

		if (pending == null)
		{
			TscDiagnostics.LogFika(
				$"TSC Fika late accepted authority result registered type={packet.SupportType} requestId={A10AuthorityDiagnostics.FormatRequestId(packet.SupportRequestId)} visualStarted={shouldPlay}");
			return;
		}

		pending.TrySetResult(
			FireSupportNetworkRequestResult.Accept(
				string.IsNullOrWhiteSpace(packet.Reason)
					? "AuthorityAccepted"
					: packet.Reason,
				packet.DurationSeconds,
				packet.ScanIntervalSeconds,
				packet.RangeMeters));
	}

	private static void PublishAcceptedUavLoiter(FireSupportRequestPacket acceptedSupport)
	{
		try
		{
			if (acceptedSupport == null ||
			    !IsUavType(acceptedSupport.SupportType) ||
			    !UavA10LoiterSettings.IsEnabled())
			{
				return;
			}

			UavA10LoiterRequest request =
				UavA10LoiterSettings.CreateConfiguredRequest(
					acceptedSupport.Position,
					acceptedSupport.DurationSeconds);
			var packet = new StartUavLoiterPacket(acceptedSupport, request);
			FikaServer server = GetServer();
			if (server == null)
			{
				s_logSource?.LogWarning(
					$"TSC UAV accepted loiter publish skipped: server unavailable requestId={A10AuthorityDiagnostics.FormatRequestId(acceptedSupport.SupportRequestId)}.");
				return;
			}

			server.SendData(
				ref packet,
				DeliveryMethod.ReliableOrdered,
				broadcast: true);

			if (!IsFikaHeadlessHost())
			{
				UavAircraftLoiterController.StartLocal(
					request,
					GetRaidCancellationToken(),
					acceptedSupport.SupportRequestId);
			}

			TscDiagnostics.LogFika(
				$"TSC UAV authoritative loiter published type={acceptedSupport.SupportType} requestId={A10AuthorityDiagnostics.FormatRequestId(acceptedSupport.SupportRequestId)} requester={A10AuthorityDiagnostics.ShortId(acceptedSupport.RequesterProfileId)} localVisual={!IsFikaHeadlessHost()}.");
		}
		catch (Exception ex)
		{
			s_logSource?.LogWarning($"TSC UAV accepted loiter publish failed; skipping aircraft visual. {ex}");
		}
	}

	private static void OnClientStartUavLoiter(StartUavLoiterPacket packet)
	{
		try
		{
			if (!TryAcceptClientUavLoiterPacket(packet, out string rejectReason))
			{
				TscDiagnostics.LogFika(
					$"TSC UAV loiter event ignored requestId={A10AuthorityDiagnostics.FormatRequestId(packet?.SupportRequestId)} reason={rejectReason}.");
				return;
			}

			UavAircraftLoiterController.StartLocal(
				packet.ToRequest(),
				GetRaidCancellationToken(),
				packet.SupportRequestId);
		}
		catch (Exception ex)
		{
			s_logSource?.LogWarning($"TSC UAV loiter client visual failed. {ex}");
		}
	}

	private static bool TryAcceptClientUavLoiterPacket(
		StartUavLoiterPacket packet,
		out string reason)
	{
		reason = string.Empty;
		if (packet == null ||
		    !IsUavType(packet.SupportType) ||
		    string.IsNullOrWhiteSpace(packet.SupportRequestId) ||
		    string.IsNullOrWhiteSpace(packet.RequesterProfileId))
		{
			reason = "InvalidLoiterIdentity";
			return false;
		}

		if (!IsFinite(packet.Center) ||
		    !IsFinite(packet.ModelRotationOffset) ||
		    !IsFinite(packet.DurationSeconds) ||
		    !IsFinite(packet.Radius) ||
		    !IsFinite(packet.Altitude) ||
		    !IsFinite(packet.OrbitPeriod) ||
		    !IsFinite(packet.IngressDuration) ||
		    !IsFinite(packet.IngressDistance) ||
		    !IsFinite(packet.EngineVolume) ||
		    !IsFinite(packet.StartAngle) ||
		    packet.DurationSeconds <= 0f ||
		    packet.Radius <= 0f ||
		    packet.Altitude <= 0f ||
		    packet.OrbitPeriod <= 0f ||
		    (packet.AircraftType != UavLoiterAircraftType.A10 &&
		     packet.AircraftType != UavLoiterAircraftType.Uh60) ||
		    (packet.Direction != -1 && packet.Direction != 1))
		{
			reason = "InvalidLoiterGeometry";
			return false;
		}

		lock (s_networkRequestGate)
		{
			if (!s_acceptedClientEvents.TryGetValue(
				    packet.SupportRequestId,
				    out SupportRequestFingerprint acceptedEvent))
			{
				reason = "AcceptedSupportEventUnavailable";
				return false;
			}

			if (!acceptedEvent.MatchesUavLoiter(packet))
			{
				reason = "AcceptedSupportPayloadMismatch";
				return false;
			}

			if (!s_startedClientUavLoiterEvents.Add(packet.SupportRequestId))
			{
				reason = "DuplicateLoiterEvent";
				return false;
			}
		}

		return true;
	}

	private static void OnServerUavPhoneVisual(UavPhoneVisualPacket packet, NetPeer peer)
	{
		try
		{
			TscDiagnostics.LogFika(
				$"received phone visual packet phase={packet?.Phase}, owner={packet?.ProfileId ?? string.Empty}");
			TryPlayRemoteUavPhoneVisual(packet);

			FikaServer server = GetServer();
			if (server == null || packet == null)
			{
				s_logSource?.LogWarning("phone visual relay skipped; server or packet unavailable.");
				return;
			}

			server.SendData(
				ref packet,
				DeliveryMethod.ReliableOrdered,
				broadcast: true);
		}
		catch (Exception ex)
		{
			s_logSource?.LogWarning($"phone visual server relay failed; cosmetic skipped. {ex}");
		}
	}

	private static void OnClientUavPhoneVisual(UavPhoneVisualPacket packet)
	{
		try
		{
			TscDiagnostics.LogFika(
				$"received phone visual packet phase={packet?.Phase}, owner={packet?.ProfileId ?? string.Empty}");
			TryPlayRemoteUavPhoneVisual(packet);
		}
		catch (Exception ex)
		{
			s_logSource?.LogWarning($"phone visual client playback failed; cosmetic skipped. {ex}");
		}
	}

	private static void TryPlayRemoteUavPhoneVisual(UavPhoneVisualPacket packet)
	{
		if (packet == null)
		{
			return;
		}

		RemoteUavPhoneVisualController.Play(
			packet.ProfileId,
			packet.AccountId,
			packet.SupportType,
			packet.Phase,
			packet.StartTime,
			packet.Duration,
			packet.Success);
	}

	private static void OnA10TracerBurstCreated(A10TracerBurst burst)
	{
		try
		{
			if (!FikaBackendUtils.IsServer || burst?.Segments == null || burst.Segments.Length == 0)
			{
				return;
			}

			if (!string.IsNullOrWhiteSpace(burst.SupportRequestId))
			{
				string disposition = string.Empty;
				bool handled = false;
				lock (s_networkRequestGate)
				{
					if (s_authorityRequests.TryGetValue(
						    burst.SupportRequestId,
						    out AuthorityRequestEntry entry))
					{
						handled = entry.TryBufferTracerBurst(
							burst,
							out disposition);
					}
				}

				if (handled)
				{
					TscDiagnostics.LogFika(
						$"A-10 tracer sync held requestId={A10AuthorityDiagnostics.FormatRequestId(burst.SupportRequestId)} burst={burst.BurstId} disposition={disposition}");
					return;
				}
			}

			BroadcastA10TracerBurst(burst, "authority already accepted");
		}
		catch (Exception ex)
		{
			s_logSource?.LogWarning($"A-10 tracer sync broadcast failed. {ex}");
		}
	}

	private static void BroadcastA10TracerBurst(
		A10TracerBurst burst,
		string reason)
	{
		try
		{
			if (burst?.Segments == null || burst.Segments.Length == 0)
			{
				return;
			}

			FikaServer server = GetServer();
			if (server == null)
			{
				s_logSource?.LogWarning(
					$"A-10 tracer sync skipped; server unavailable burst={burst.BurstId}");
				return;
			}

			int totalSegments = burst.Segments.Length;
			for (int offset = 0;
			     offset < totalSegments;
			     offset += MaxA10TracerSegmentsPerPacket)
			{
				int count = Math.Min(
					MaxA10TracerSegmentsPerPacket,
					totalSegments - offset);
				var chunk = new A10TracerSegment[count];
				Array.Copy(burst.Segments, offset, chunk, 0, count);
				var packet = new A10TracerBurstPacket(
					burst,
					offset,
					totalSegments,
					chunk);
				server.SendData(
					ref packet,
					DeliveryMethod.ReliableOrdered,
					broadcast: true);
			}

			TscDiagnostics.LogFika(
				$"A-10 tracer sync: broadcast burst={burst.BurstId} requestId={A10AuthorityDiagnostics.FormatRequestId(burst.SupportRequestId)} pass={burst.PassIndex} segments={totalSegments} reason={reason}");
		}
		catch (Exception ex)
		{
			s_logSource?.LogWarning(
				$"A-10 tracer sync broadcast failed burst={burst?.BurstId ?? 0} reason={reason}. {ex}");
		}
	}

	private static void OnServerA10TracerBurst(A10TracerBurstPacket packet, NetPeer peer)
	{
		s_logSource?.LogWarning(
			$"A-10 tracer sync: ignored non-host tracer burst packet burst={packet?.BurstId ?? 0}");
	}

	private static void OnClientA10TracerBurst(A10TracerBurstPacket packet)
	{
		try
		{
			if (packet?.Segments == null || packet.Segments.Length == 0)
			{
				return;
			}

			// Host/headless timestamps are not comparable with this client's Time.time.
			// Queue the burst by SupportRequestId and align playback to the local
			// visual A-10 pass instead of playing it immediately on packet receipt.
			A10TracerNetworking.QueueOrPlayHostBurst(
				packet.SupportRequestId,
				packet.VisualSeed,
				packet.PassIndex,
				packet.Segments,
				GetRaidCancellationToken(),
				spawnImpactEffects: true);
		}
		catch (Exception ex)
		{
			s_logSource?.LogWarning($"A-10 tracer sync playback failed. {ex}");
		}
	}

	private static void RequestHostSettings(FikaClient client)
	{
		TscDiagnostics.LogFika("TSC Fika settings: requesting host settings");
		var packet = FireSupportSettingsPacket.CreateRequest();
		client.SendData(ref packet, DeliveryMethod.ReliableOrdered);
	}

	private static async UniTaskVoid RequestHostSettingsAfterDelay(FikaClient client)
	{
		try
		{
			await UniTask.WaitForSeconds(ClientSettingsRetryDelaySeconds);
			if (s_client == client && FikaBackendUtils.IsClient)
			{
				RequestHostSettings(client);
			}
		}
		catch (Exception ex)
		{
			s_logSource?.LogWarning($"TSC Fika settings: delayed request failed. {ex}");
		}
	}

	private static void OnServerSettingsRequest(FireSupportSettingsPacket packet, NetPeer peer)
	{
		if (!packet.IsRequest)
		{
			s_logSource?.LogWarning(
				$"TSC Fika settings: ignored non-request settings packet from non-host revision={packet.Revision}");
			return;
		}

		TscDiagnostics.LogFika("TSC Fika settings: responding to client settings request");
		var response = BuildHostSettingsPacket(incrementRevision: false);
		FikaServer server = GetServer();
		if (server == null)
		{
			s_logSource?.LogWarning("TSC Fika settings: response skipped; server unavailable");
			return;
		}

		server.SendData(
			ref response,
			DeliveryMethod.ReliableOrdered,
			peer);
	}

	private static void OnClientSettingsResponse(FireSupportSettingsPacket packet)
	{
		if (packet.IsRequest)
		{
			return;
		}

		TscDiagnostics.LogFika($"TSC Fika settings: received revision={packet.Revision}");
		if (packet.Revision <= s_currentHostSettingsRevision)
		{
			TscDiagnostics.LogFika($"TSC Fika settings: ignored stale revision={packet.Revision} current={s_currentHostSettingsRevision}");
			return;
		}

		ApplyHostAuthority(packet);
	}

	private static void ApplyHostAuthority(FireSupportSettingsPacket packet)
	{
		s_hasHostSettingsOverride = true;
		s_currentHostSettingsRevision = packet.Revision;
		FireSupportPayment.SetSyncedCosts(
			packet.StrafeCostRoubles,
			packet.DoubleStrafeCostRoubles,
			packet.ExtractionCostRoubles,
			packet.PriorityExfilCostRoubles,
			packet.UavCostRoubles,
			packet.FocusedSweepCostRoubles);
		FireSupportPayment.SetSyncedPaymentMode(packet.PaymentMode);
		FireSupportPayment.SetSyncedPaymentSource(packet.PaymentSource);
		FireSupportServerConfigClient.SetHostPurchaseEndpoint(packet.ServerConfigUrl, packet.Revision);
		FireSupportServiceAvailability.SetSyncedAvailability(
			packet.EnablePriorityExfil,
			packet.EnableDoublePass,
			packet.EnableFocusedSweep);
		UavReconSettings.SetSyncedDuration(
			packet.UavDurationSeconds,
			packet.UavScanIntervalSeconds,
			packet.UavRangeMeters);
		UavReconSettings.SetSyncedFocusedSweep(
			packet.FocusedSweepDurationSeconds,
			packet.FocusedSweepScanIntervalSeconds,
			packet.FocusedSweepRangeMeters);
		FireSupportTuningSettings.SetSyncedTuning(
			packet.DoubleStrafeSecondPassDelaySeconds,
			packet.ExtractionDispatchDelaySeconds,
			packet.HelicopterWaitTimeSeconds,
			packet.ExtractionExtractTimeSeconds,
			packet.HelicopterSpeedMultiplier,
			packet.PriorityExfilDispatchDelaySeconds,
			packet.PriorityExfilHelicopterWaitTimeSeconds,
			packet.PriorityExfilExtractTimeSeconds,
			packet.PriorityExfilHelicopterSpeedMultiplier,
			packet.RequestCooldownSeconds);
		FireSupportPayment.NotifySettingsChanged(packet);
		s_logSource?.LogInfo($"TSC Fika settings applied revision {packet.Revision}.");
	}

	private static void OnEffectiveSettingsChanged(object sender, EventArgs args)
	{
		try
		{
			if (!FikaBackendUtils.IsServer)
			{
				return;
			}
		}
		catch
		{
			return;
		}

		string key = sender is ConfigEntryBase entry
			? $"{entry.Definition.Section}/{entry.Definition.Key}"
			: "<unknown>";
		s_hostSettingsRevision++;
		TscDiagnostics.LogFika($"TSC Fika settings: config changed key={key}");
		ScheduleBroadcastHostSettings($"config changed key={key}");
	}

	private static void ScheduleBroadcastHostSettings(string reason)
	{
		if (!s_enabled)
		{
			return;
		}

		s_settingsBroadcastDebounceCts?.Cancel();
		s_settingsBroadcastDebounceCts?.Dispose();
		s_settingsBroadcastDebounceCts = new CancellationTokenSource();
		DebouncedBroadcastHostSettings(reason, s_settingsBroadcastDebounceCts.Token).Forget();
	}

	private static async UniTaskVoid DebouncedBroadcastHostSettings(string reason, CancellationToken cancellationToken)
	{
		try
		{
			await UniTask.Delay(SettingsBroadcastDebounceMs, cancellationToken: cancellationToken);
			if (!cancellationToken.IsCancellationRequested && FikaBackendUtils.IsServer)
			{
				BroadcastHostSettings(reason);
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			s_logSource?.LogWarning($"TSC Fika settings: debounced broadcast failed. {ex}");
		}
	}

	private static void BroadcastHostSettings(string reason)
	{
		try
		{
			if (!FikaBackendUtils.IsServer)
			{
				return;
			}

			FikaServer server = GetServer();
			if (server == null)
			{
				s_logSource?.LogWarning(
					$"TSC Fika settings: broadcast skipped; server unavailable reason={reason}");
				return;
			}

			var packet = BuildHostSettingsPacket(incrementRevision: false);
			TscDiagnostics.LogFika($"TSC Fika settings: broadcasting reason={reason} revision={packet.Revision}");
			server.SendData(
				ref packet,
				DeliveryMethod.ReliableOrdered,
				broadcast: true);
		}
		catch (Exception ex)
		{
			s_logSource?.LogWarning($"TSC Fika settings: broadcast failed reason={reason}. {ex}");
		}
	}

	private static FireSupportSettingsPacket BuildHostSettingsPacket(bool incrementRevision)
	{
		if (incrementRevision || s_hostSettingsRevision <= 0)
		{
			s_hostSettingsRevision++;
		}

		HelicopterTimingSnapshot extractionTiming =
			FireSupportTuningSettings.CaptureHelicopterTiming(ESupportType.Extract);
		HelicopterTimingSnapshot priorityExfilTiming =
			FireSupportTuningSettings.CaptureHelicopterTiming(ESupportType.PriorityExfil);
		var packet = new FireSupportSettingsPacket
		{
			IsRequest = false,
			Revision = s_hostSettingsRevision,
			StrafeCostRoubles = FireSupportPayment.GetActiveCost(ESupportType.Strafe),
			DoubleStrafeCostRoubles = FireSupportPayment.GetActiveCost(ESupportType.DoubleStrafe),
			ExtractionCostRoubles = FireSupportPayment.GetActiveCost(ESupportType.Extract),
			PriorityExfilCostRoubles = FireSupportPayment.GetActiveCost(ESupportType.PriorityExfil),
			UavCostRoubles = FireSupportPayment.GetActiveCost(ESupportType.Uav),
			FocusedSweepCostRoubles = FireSupportPayment.GetActiveCost(ESupportType.FocusedSweep),
			EnablePriorityExfil = FireSupportServiceAvailability.IsServiceEnabled(ESupportType.PriorityExfil),
			EnableDoublePass = FireSupportServiceAvailability.IsServiceEnabled(ESupportType.DoubleStrafe),
			EnableFocusedSweep = FireSupportServiceAvailability.IsServiceEnabled(ESupportType.FocusedSweep),
			UavDurationSeconds = UavReconSettings.GetDurationSeconds(ESupportType.Uav),
			UavScanIntervalSeconds = UavReconSettings.GetScanInterval(ESupportType.Uav),
			UavRangeMeters = UavReconSettings.GetRangeMeters(ESupportType.Uav),
			FocusedSweepDurationSeconds = UavReconSettings.GetDurationSeconds(ESupportType.FocusedSweep),
			FocusedSweepScanIntervalSeconds = UavReconSettings.GetScanInterval(ESupportType.FocusedSweep),
			FocusedSweepRangeMeters = UavReconSettings.GetRangeMeters(ESupportType.FocusedSweep),
			DoubleStrafeSecondPassDelaySeconds = FireSupportTuningSettings.GetDoubleStrafeSecondPassDelay(),
			ExtractionDispatchDelaySeconds = extractionTiming.DispatchDelaySeconds,
			HelicopterWaitTimeSeconds = extractionTiming.WaitTimeSeconds,
			ExtractionExtractTimeSeconds = extractionTiming.ExtractTimeSeconds,
			HelicopterSpeedMultiplier = extractionTiming.SpeedMultiplier,
			PriorityExfilDispatchDelaySeconds = priorityExfilTiming.DispatchDelaySeconds,
			PriorityExfilHelicopterWaitTimeSeconds = priorityExfilTiming.WaitTimeSeconds,
			PriorityExfilExtractTimeSeconds = priorityExfilTiming.ExtractTimeSeconds,
			PriorityExfilHelicopterSpeedMultiplier = priorityExfilTiming.SpeedMultiplier,
			RequestCooldownSeconds = FireSupportTuningSettings.GetRequestCooldown(),
			PaymentMode = FireSupportPayment.GetActivePaymentMode(),
			PaymentSource = FireSupportPayment.GetActivePaymentSource(),
			ServerConfigUrl = FireSupportServerConfigClient.GetConfiguredServerConfigUrl()
		};

		TscDiagnostics.LogFika($"TSC Fika settings: host snapshot built revision={packet.Revision}");
		return packet;
	}

	private static FikaServer GetServer()
	{
		return s_server ?? Singleton<FikaServer>.Instance;
	}

	private static void ClearHostAuthority(string reason, bool notify = true)
	{
		bool hadHostAuthority =
			s_hasHostSettingsOverride ||
			s_currentHostSettingsRevision > 0 ||
			FireSupportPayment.HasSyncedCosts ||
			UavReconSettings.HasSyncedSettings ||
			FireSupportServiceAvailability.HasSyncedAvailability ||
			FireSupportTuningSettings.HasSyncedTuning;

		s_hasHostSettingsOverride = false;
		s_currentHostSettingsRevision = 0;
		FireSupportPayment.ClearSyncedCosts();
		FireSupportServiceAvailability.ClearSyncedAvailability();
		UavReconSettings.ClearSyncedDuration();
		FireSupportTuningSettings.ClearSyncedTuning();
		FireSupportServerConfigClient.ClearHostPurchaseEndpoint();
		FireSupportServerConfigClient.SetFikaClientHostAuthorityActive(false, reason);

		if (!hadHostAuthority)
		{
			return;
		}

		s_logSource?.LogInfo($"TSC Fika settings cleared host authority reason={reason}.");
		if (notify)
		{
			FireSupportPayment.NotifySettingsChanged(reason);
		}
	}

	private static async UniTaskVoid ExecuteClientSupportVisual(
		FireSupportRequestPacket packet,
		CancellationToken cancellationToken,
		bool playUavActivationVisual)
	{
		await ExecuteSupportCore(packet, visualOnly: true, cancellationToken, playUavActivationVisual);
	}

	private static async UniTaskVoid ExecuteAcceptedHeadlessA10PassAsync(
		FireSupportRequestPacket packet,
		AuthorityRequestEntry entry)
	{
		CancellationToken cancellationToken = entry.CancellationToken;
		try
		{
			A10StrikeRequest request = CreateA10StrikeRequest(
				packet,
				visualOnly: false,
				A10AuthorityRole.FikaHeadlessHost);
			bool success = await A10HeadlessDamageExecutor.ExecuteAcceptedAsync(
				request,
				cancellationToken);
			if (!success && !cancellationToken.IsCancellationRequested)
			{
				s_logSource?.LogWarning(
					$"TSC Fika accepted headless A-10 pass ended without firing type={packet.SupportType} requestId={A10AuthorityDiagnostics.FormatRequestId(packet.SupportRequestId)} pass={packet.PassIndex}.");
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			s_logSource?.LogWarning(
				$"TSC Fika accepted headless A-10 pass failed type={packet?.SupportType} requestId={A10AuthorityDiagnostics.FormatRequestId(packet?.SupportRequestId)}. {ex}");
		}
		finally
		{
			entry.MarkAuthorityWorkFinished();
		}
	}

	private static async UniTask<bool> ExecuteSupportCore(
		FireSupportRequestPacket packet,
		bool visualOnly,
		CancellationToken cancellationToken,
		bool playUavActivationVisual)
	{
		if (!IsSupportedNetworkType(packet.SupportType))
		{
			return false;
		}

		if (IsUavType(packet.SupportType))
		{
			if (IsFikaHeadlessHost())
			{
				TscDiagnostics.LogFika($"TSC UAV recon link skipped on Fika headless host requestId={A10AuthorityDiagnostics.FormatRequestId(packet.SupportRequestId)}; the requester renders it locally.");
				return true;
			}

			// The authority still executes and broadcasts every accepted UAV request,
			// but the live recon session belongs only to the requesting local player.
			// Without this guard a human Fika host also received the phone feed for a
			// client's UAV because the host runs the non-visual authority path.
			if (!IsLocalRequester(packet))
			{
				TscDiagnostics.LogFika(
					$"TSC UAV recon link skipped on non-requester peer type={packet.SupportType} requestId={A10AuthorityDiagnostics.FormatRequestId(packet.SupportRequestId)} requester={A10AuthorityDiagnostics.ShortId(packet.RequesterProfileId)} local={A10AuthorityDiagnostics.ShortId(GetLocalProfileId())} visualOnly={visualOnly}");
				return true;
			}

			if (visualOnly)
			{
				TscDiagnostics.LogFika(
					$"TSC UAV recon link accepted on requester client type={packet.SupportType} requestId={A10AuthorityDiagnostics.FormatRequestId(packet.SupportRequestId)} requester={A10AuthorityDiagnostics.ShortId(packet.RequesterProfileId)}");
			}

			UavReconOverlay.Activate(
				packet.DurationSeconds,
				cancellationToken,
				playActivationVisual: false,
				packet.ScanIntervalSeconds,
				packet.RangeMeters);

			return true;
		}

		bool success;
		if (IsA10Type(packet.SupportType))
		{
			A10AuthorityRole role = visualOnly ? A10AuthorityRole.FikaClient : GetA10AuthorityRole();
			A10StrikeRequest request = CreateA10StrikeRequest(
				packet,
				visualOnly,
				role);

			success = await A10StrikeExecutorSelector.ExecuteAsync(request, cancellationToken);
		}
		else
		{
			if (!visualOnly &&
			    IsExtractionType(packet.SupportType) &&
			    IsFikaHeadlessHost())
			{
				TscDiagnostics.LogFika(
					$"TSC Fika headless extraction authority accepted without local presentation type={packet.SupportType} requestId={A10AuthorityDiagnostics.FormatRequestId(packet.SupportRequestId)}.");
				return true;
			}

			HelicopterTimingSnapshot? helicopterTiming =
				IsExtractionType(packet.SupportType)
					? packet.GetHelicopterTiming()
					: null;
			bool allowLocalHelicopterExtraction =
				!IsExtractionType(packet.SupportType) ||
				IsLocalRequester(packet);
			success = await FireSupportRuntime.TryProcessRequest(
				packet.SupportType,
				packet.Position,
				packet.Direction,
				packet.Rotation,
				visualOnly,
				packet.VisualSeed,
				cancellationToken,
				packet.PassIndex,
				helicopterTiming,
				allowLocalHelicopterExtraction);
		}

		if (success && IsExtractionType(packet.SupportType) && !visualOnly)
		{
			try
			{
				FireSupportAudio.Instance?.PlayVoiceover(
					EVoiceoverType.SupportHeliArrivingToPickup);
			}
			catch (Exception ex)
			{
				// The helicopter runtime has already accepted the request. A
				// presentation failure must not convert live extraction into a
				// rejected/refunded authority outcome.
				s_logSource?.LogWarning(
					$"TSC Fika extraction arrival voiceover failed after executor acceptance. {ex}");
			}
		}

		return success;
	}

	private static A10StrikeRequest CreateA10StrikeRequest(
		FireSupportRequestPacket packet,
		bool visualOnly,
		A10AuthorityRole role)
	{
		return new A10StrikeRequest
		{
			SupportRequestId = packet.SupportRequestId,
			SupportType = packet.SupportType,
			Position = packet.Position,
			Direction = packet.Direction,
			Rotation = packet.Rotation,
			VisualSeed = packet.VisualSeed,
			PassIndex = packet.PassIndex,
			RequesterProfileId = packet.RequesterProfileId,
			VisualOnly = visualOnly,
			Role = role
		};
	}

	private static bool TrySendA10HeadlessDamageCommand(A10HeadlessDamageCommand command, out string reason)
	{
		reason = string.Empty;
		if (command == null)
		{
			reason = "CommandNull";
			return false;
		}

		if (command.TargetNetId <= 0)
		{
			reason = "MissingTargetNetId";
			return false;
		}

		try
		{
			if (!FikaBackendUtils.IsServer)
			{
				reason = "NotFikaServer";
				return false;
			}
		}
		catch (Exception ex)
		{
			reason = $"FikaServerStateUnavailable:{ex.GetType().Name}:{ex.Message}";
			return false;
		}

		FikaServer server = GetServer();
		if (server == null)
		{
			reason = "FikaServerUnavailable";
			return false;
		}

		DamageInfoStruct damageInfo = command.DamageInfo;
		DamagePacket damagePacket = DamagePacket.FromValue(
			command.TargetNetId,
			damageInfo,
			command.BodyPart,
			command.ColliderType,
			command.ArmorPlateCollider,
			command.MaterialType,
			command.Absorbed);

		var packet = new CommonPlayerPacket
		{
			NetId = command.TargetNetId,
			Type = ECommonSubPacketType.Damage,
			SubPacket = damagePacket
		};

		server.SendNetReusable(ref packet, DeliveryMethod.ReliableOrdered, true, null);
		reason = "BroadcastFikaDamagePacket";
		TscDiagnostics.LogFika(
			$"TSC A-10 headless damage command broadcast requestId={A10AuthorityDiagnostics.FormatRequestId(command.SupportRequestId)} target={A10AuthorityDiagnostics.ShortId(command.TargetProfileId)} netId={command.TargetNetId} damage={command.DamageInfo.Damage:0.0} bodyPart={command.BodyPart} collider={command.ColliderType}");
		return true;
	}

	private static void BroadcastSupportPacket(FireSupportRequestPacket packet, NetPeer peer, bool broadcastToAll, string reason)
	{
		try
		{
			FikaServer server = GetServer();
			if (server == null)
			{
				s_logSource?.LogWarning($"TSC Fika support broadcast skipped; server unavailable type={packet.SupportType} requestId={A10AuthorityDiagnostics.FormatRequestId(packet.SupportRequestId)} reason={reason}");
				return;
			}

			if (broadcastToAll)
			{
				TscDiagnostics.LogFika($"TSC Fika support broadcast to all clients type={packet.SupportType} requestId={A10AuthorityDiagnostics.FormatRequestId(packet.SupportRequestId)} reason={reason}");
				server.SendData(ref packet, DeliveryMethod.ReliableOrdered, broadcast: true);
				return;
			}

			if (peer == null)
			{
				s_logSource?.LogWarning($"TSC Fika support requester send skipped; peer unavailable type={packet.SupportType} requestId={A10AuthorityDiagnostics.FormatRequestId(packet.SupportRequestId)} reason={reason}");
				return;
			}

			TscDiagnostics.LogFika($"TSC Fika support sent to requester type={packet.SupportType} requestId={A10AuthorityDiagnostics.FormatRequestId(packet.SupportRequestId)} reason={reason}");
			server.SendData(ref packet, DeliveryMethod.ReliableOrdered, peer);
		}
		catch (Exception ex)
		{
			s_logSource?.LogWarning(
				$"TSC Fika support broadcast failed type={packet?.SupportType} requestId={A10AuthorityDiagnostics.FormatRequestId(packet?.SupportRequestId)} reason={reason}. {ex}");
		}
	}

	private static void CompleteClientRequestFromAcceptedEvent(FireSupportRequestPacket packet)
	{
		ClientPendingRequest pending;
		lock (s_networkRequestGate)
		{
			if (!s_pendingClientRequests.TryGetValue(packet.SupportRequestId, out pending) ||
			    !pending.Fingerprint.MatchesAcceptedRequest(packet))
			{
				return;
			}
		}

		pending.TrySetResult(
			FireSupportNetworkRequestResult.Accept(
				"AuthorityAcceptedBroadcast",
				packet.DurationSeconds,
				packet.ScanIntervalSeconds,
				packet.RangeMeters));
	}

	private static bool TryRegisterClientSupportEvent(
		FireSupportRequestPacket packet,
		out bool shouldPlay,
		out string rejectReason)
	{
		shouldPlay = false;
		rejectReason = string.Empty;
		if (!TryValidateSupportRequest(packet, out rejectReason))
		{
			return false;
		}

		var fingerprint = new SupportRequestFingerprint(packet);
		lock (s_networkRequestGate)
		{
			if (s_pendingClientRequests.TryGetValue(
				    packet.SupportRequestId,
				    out ClientPendingRequest pending) &&
			    !pending.Fingerprint.MatchesAcceptedRequest(packet))
			{
				rejectReason = "AcceptedEventPendingPayloadMismatch";
				return false;
			}

			if (s_acceptedClientEvents.TryGetValue(
				    packet.SupportRequestId,
				    out SupportRequestFingerprint existing))
			{
				if (existing.Equals(fingerprint))
				{
					rejectReason = "DuplicateAcceptedEvent";
					return true;
				}

				rejectReason = "AcceptedEventPayloadMismatch";
				return false;
			}

			s_acceptedClientEvents.Add(packet.SupportRequestId, fingerprint);
			shouldPlay = true;
		}

		return true;
	}

	private static FireSupportNetworkRequestResult MapAuthorityRejection(
		string reason)
	{
		string normalized = string.IsNullOrWhiteSpace(reason)
			? "AuthorityRejected"
			: reason;
		return normalized switch
		{
			"RequesterCancelled" => FireSupportNetworkRequestResult.Cancel(normalized),
			"RaidCancelled" => FireSupportNetworkRequestResult.Cancel(normalized),
			"AuthorityExecutionTimedOut" => FireSupportNetworkRequestResult.Timeout(normalized),
			_ => FireSupportNetworkRequestResult.Reject(normalized)
		};
	}

	private static bool TryValidateSupportRequest(
		FireSupportRequestPacket packet,
		out string reason)
	{
		reason = string.Empty;
		if (packet == null)
		{
			reason = "RequestNull";
			return false;
		}

		if (!IsSupportedNetworkType(packet.SupportType))
		{
			reason = "UnsupportedNetworkType";
			return false;
		}

		string requestId = packet.SupportRequestId ?? string.Empty;
		if (string.IsNullOrWhiteSpace(requestId))
		{
			reason = "MissingSupportRequestId";
			return false;
		}

		if (requestId.Length > MaxSupportRequestIdLength ||
		    !string.Equals(requestId, requestId.Trim(), StringComparison.Ordinal))
		{
			reason = "InvalidSupportRequestId";
			return false;
		}

		for (int i = 0; i < requestId.Length; i++)
		{
			char value = requestId[i];
			if (!char.IsLetterOrDigit(value) &&
			    value != '-' &&
			    value != '_' &&
			    value != ':')
			{
				reason = "InvalidSupportRequestId";
				return false;
			}
		}

		string requesterProfileId = packet.RequesterProfileId ?? string.Empty;
		if (string.IsNullOrWhiteSpace(requesterProfileId) ||
		    requesterProfileId.Length > MaxRequesterProfileIdLength ||
		    !string.Equals(requesterProfileId, requesterProfileId.Trim(), StringComparison.Ordinal))
		{
			reason = "InvalidRequesterProfileId";
			return false;
		}

		if (!IsFinite(packet.Position) ||
		    !IsFinite(packet.Direction) ||
		    !IsFinite(packet.Rotation) ||
		    !IsFinite(packet.DurationSeconds) ||
		    !IsFinite(packet.ScanIntervalSeconds) ||
		    !IsFinite(packet.RangeMeters))
		{
			reason = "InvalidRequestGeometry";
			return false;
		}

		if (IsUavType(packet.SupportType) &&
		    (packet.DurationSeconds <= 0f ||
		     packet.ScanIntervalSeconds <= 0f ||
		     packet.RangeMeters <= 0f))
		{
			reason = "InvalidUavContract";
			return false;
		}

		if (IsExtractionType(packet.SupportType) &&
		    (!IsFinite(packet.HelicopterDispatchDelaySeconds) ||
		     !IsFinite(packet.HelicopterExtractTimeSeconds) ||
		     !IsFinite(packet.HelicopterSpeedMultiplier) ||
		     packet.HelicopterTimingRevision < 0 ||
		     packet.HelicopterDispatchDelaySeconds < 0f ||
		     packet.HelicopterDispatchDelaySeconds > MaxHelicopterDispatchDelaySeconds ||
		     packet.HelicopterWaitTimeSeconds < 1 ||
		     packet.HelicopterWaitTimeSeconds > MaxHelicopterWaitTimeSeconds ||
		     packet.HelicopterExtractTimeSeconds < 0.1f ||
		     packet.HelicopterExtractTimeSeconds > MaxHelicopterExtractTimeSeconds ||
		     packet.HelicopterSpeedMultiplier < MinHelicopterSpeedMultiplier ||
		     packet.HelicopterSpeedMultiplier > MaxHelicopterSpeedMultiplier ||
		     packet.HelicopterWaitTimeSeconds <
		     packet.HelicopterExtractTimeSeconds + MinimumExtractionWindowMarginSeconds))
		{
			reason = "InvalidExtractionTimingContract";
			return false;
		}

		if (packet.PassIndex < 0 || packet.PassIndex > 1)
		{
			reason = "InvalidPassIndex";
			return false;
		}

		if (!IsA10Type(packet.SupportType) && packet.PassIndex != 0)
		{
			reason = "InvalidPassIndex";
			return false;
		}

		return true;
	}

	private static bool TryValidateRequesterPeer(
		FireSupportRequestPacket packet,
		NetPeer peer,
		out string reason)
	{
		reason = string.Empty;
		if (peer == null)
		{
			// Local human-host requests never traverse a remote peer.
			return true;
		}

		string peerProfileId = peer.Player?.ProfileId ?? string.Empty;
		if (string.IsNullOrWhiteSpace(peerProfileId))
		{
			reason = "RequesterPeerProfileUnavailable";
			return false;
		}

		if (!string.Equals(
			    packet.RequesterProfileId,
			    peerProfileId,
			    StringComparison.Ordinal))
		{
			reason = "RequesterProfilePeerMismatch";
			return false;
		}

		return true;
	}

	private static bool IsFinite(Vector3 value)
	{
		return !float.IsNaN(value.x) &&
		       !float.IsInfinity(value.x) &&
		       !float.IsNaN(value.y) &&
		       !float.IsInfinity(value.y) &&
		       !float.IsNaN(value.z) &&
		       !float.IsInfinity(value.z);
	}

	private static bool IsFinite(float value)
	{
		return !float.IsNaN(value) && !float.IsInfinity(value);
	}

	private static FireSupportRequestPacket CloneSupportRequest(
		FireSupportRequestPacket packet)
	{
		var clone = new FireSupportRequestPacket(
			packet.SupportType,
			packet.Position,
			packet.Direction,
			packet.Rotation,
			packet.VisualSeed,
			packet.DurationSeconds,
			packet.PassIndex,
			packet.RequesterProfileId,
			packet.SupportRequestId,
			packet.ScanIntervalSeconds,
			packet.RangeMeters);
		clone.HelicopterTimingRevision = packet.HelicopterTimingRevision;
		clone.HelicopterDispatchDelaySeconds = packet.HelicopterDispatchDelaySeconds;
		clone.HelicopterWaitTimeSeconds = packet.HelicopterWaitTimeSeconds;
		clone.HelicopterExtractTimeSeconds = packet.HelicopterExtractTimeSeconds;
		clone.HelicopterSpeedMultiplier = packet.HelicopterSpeedMultiplier;
		return clone;
	}

	private static FireSupportRequestPacket CreateRequestFromCancel(
		FireSupportCancelPacket packet)
	{
		return new FireSupportRequestPacket
		{
			SupportType = packet.SupportType,
			Position = Vector3.zero,
			Direction = Vector3.zero,
			Rotation = Vector3.zero,
			VisualSeed = 0,
			DurationSeconds = 0f,
			ScanIntervalSeconds = 0f,
			RangeMeters = 0f,
			PassIndex = packet.PassIndex,
			RequesterProfileId = packet.RequesterProfileId ?? string.Empty,
			SupportRequestId = packet.SupportRequestId ?? string.Empty
		};
	}

	private static bool TryReserveUavAuthorityLinkNoLock(
		FireSupportRequestPacket request,
		out string reason)
	{
		reason = string.Empty;
		PruneExpiredUavAuthorityReservationsNoLock();

		string requesterProfileId = request.RequesterProfileId ?? string.Empty;
		if (s_uavAuthorityReservations.TryGetValue(
			    requesterProfileId,
			    out UavAuthorityReservation existing))
		{
			if (string.Equals(
				    existing.SupportRequestId,
				    request.SupportRequestId,
				    StringComparison.Ordinal))
			{
				return true;
			}

			reason = "RequesterUavLinkAlreadyActive";
			return false;
		}

		s_uavAuthorityReservations.Add(
			requesterProfileId,
			new UavAuthorityReservation(
				request.SupportRequestId,
				float.PositiveInfinity));
		return true;
	}

	private static void AcceptUavAuthorityReservationNoLock(
		FireSupportRequestPacket request)
	{
		string requesterProfileId = request?.RequesterProfileId ?? string.Empty;
		if (!s_uavAuthorityReservations.TryGetValue(
			    requesterProfileId,
			    out UavAuthorityReservation reservation) ||
		    !string.Equals(
			    reservation.SupportRequestId,
			    request?.SupportRequestId,
			    StringComparison.Ordinal))
		{
			return;
		}

		reservation.ExpiresAt =
			Time.realtimeSinceStartup + Mathf.Max(1f, request.DurationSeconds);
	}

	private static void ReleaseUavAuthorityReservationNoLock(
		FireSupportRequestPacket request)
	{
		ReleaseUavAuthorityReservationNoLock(
			request?.RequesterProfileId,
			request?.SupportRequestId);
	}

	private static void ReleaseUavAuthorityReservationNoLock(
		string requesterProfileId,
		string supportRequestId)
	{
		requesterProfileId ??= string.Empty;
		if (s_uavAuthorityReservations.TryGetValue(
			    requesterProfileId,
			    out UavAuthorityReservation reservation) &&
		    string.Equals(
			    reservation.SupportRequestId,
			    supportRequestId ?? string.Empty,
			    StringComparison.Ordinal))
		{
			s_uavAuthorityReservations.Remove(requesterProfileId);
		}
	}

	private static void PruneExpiredUavAuthorityReservationsNoLock()
	{
		if (s_uavAuthorityReservations.Count == 0)
		{
			return;
		}

		float now = Time.realtimeSinceStartup;
		List<string> expiredProfiles = null;
		foreach (KeyValuePair<string, UavAuthorityReservation> pair in s_uavAuthorityReservations)
		{
			if (pair.Value.ExpiresAt <= now)
			{
				expiredProfiles ??= new List<string>();
				expiredProfiles.Add(pair.Key);
			}
		}

		if (expiredProfiles == null)
		{
			return;
		}

		foreach (string profileId in expiredProfiles)
		{
			s_uavAuthorityReservations.Remove(profileId);
		}
	}

	private static async UniTaskVoid ReleaseUavAuthorityReservationAfterExpiryAsync(
		string requesterProfileId,
		string supportRequestId,
		float durationSeconds,
		CancellationToken cancellationToken)
	{
		try
		{
			await UniTask.WaitForSeconds(
				Mathf.Max(1f, durationSeconds),
				cancellationToken: cancellationToken);
		}
		catch (OperationCanceledException)
		{
		}
		finally
		{
			lock (s_networkRequestGate)
			{
				ReleaseUavAuthorityReservationNoLock(
					requesterProfileId,
					supportRequestId);
			}
		}
	}

	private static void ResetNetworkRequestState(
		string reason,
		FireSupportNetworkRequestResult pendingResult,
		bool clearAuthorityOutcomes)
	{
		UavReconOverlay.Deactivate(reason);

		List<ClientPendingRequest> pendingClients;
		List<AuthorityRequestEntry> authorityEntries = null;
		lock (s_networkRequestGate)
		{
			pendingClients = new List<ClientPendingRequest>(s_pendingClientRequests.Values);
			s_pendingClientRequests.Clear();
			s_acceptedClientEvents.Clear();
			s_startedClientUavLoiterEvents.Clear();

			if (clearAuthorityOutcomes)
			{
				authorityEntries = new List<AuthorityRequestEntry>(s_authorityRequests.Values);
				s_authorityRequests.Clear();
				s_uavAuthorityReservations.Clear();
				s_inFlightAuthorityRequestCount = 0;
			}
		}

		foreach (ClientPendingRequest pending in pendingClients)
		{
			pending.TrySetResult(pendingResult);
		}

		if (authorityEntries != null)
		{
			foreach (AuthorityRequestEntry entry in authorityEntries)
			{
				entry.Abandon(pendingResult);
			}
		}

		UavAircraftLoiterController.ResetAll(reason);
		TscDiagnostics.LogFika(
			$"TSC Fika network request state cleared reason={reason} authority={clearAuthorityOutcomes}");
	}

	private static void OnFikaGameEnded(FikaGameEndedEvent @event)
	{
		UavReconOverlay.Deactivate("Fika game ended");
		ResetNetworkRequestState(
			"Fika game ended",
			FireSupportNetworkRequestResult.Cancel("RaidEnded"),
			clearAuthorityOutcomes: true);
		FireSupportRuntime.Dispose();
		A10TracerNetworking.SetNetworkAuthorityActive(false, "Fika game ended");
		ClearHostAuthority("Fika game ended");
	}

	private static void OnPeerDisconnected(PeerDisconnectedEvent @event)
	{
		try
		{
			if (FikaBackendUtils.IsClient)
			{
				UavReconOverlay.Deactivate("Fika host peer disconnected");
				ResetNetworkRequestState(
					"Fika host peer disconnected",
					FireSupportNetworkRequestResult.Cancel("FikaPeerDisconnected"),
					clearAuthorityOutcomes: true);
				return;
			}
		}
		catch
		{
			ResetNetworkRequestState(
				"Fika peer state unavailable",
				FireSupportNetworkRequestResult.Cancel("FikaPeerDisconnected"),
				clearAuthorityOutcomes: true);
			return;
		}

		NetPeer disconnectedPeer = @event.Peer;
		string disconnectedProfileId =
			disconnectedPeer?.Player?.ProfileId ?? string.Empty;
		List<AuthorityRequestEntry> disconnectedEntries = new();
		lock (s_networkRequestGate)
		{
			foreach (AuthorityRequestEntry entry in s_authorityRequests.Values)
			{
				if (!entry.IsCompleted && entry.IsOwnedBy(disconnectedPeer))
				{
					disconnectedEntries.Add(entry);
				}
			}

			foreach (AuthorityRequestEntry entry in disconnectedEntries)
			{
				AuthorityOutcome outcome = AuthorityOutcome.FromResult(
					entry.Request,
					FireSupportNetworkRequestResult.Reject("RequesterDisconnected"));
				if (entry.TryCancelAndComplete(
					    outcome,
					    "RequesterDisconnected"))
				{
					s_inFlightAuthorityRequestCount =
						Math.Max(0, s_inFlightAuthorityRequestCount - 1);
				}
			}

			if (!string.IsNullOrWhiteSpace(disconnectedProfileId))
			{
				s_uavAuthorityReservations.Remove(disconnectedProfileId);
			}
		}
	}

	private static CancellationToken GetRaidCancellationToken()
	{
		GameWorld gameWorld = Singleton<GameWorld>.Instance;
		return gameWorld != null ? gameWorld.destroyCancellationToken : CancellationToken.None;
	}

	private static A10AuthorityRole GetA10AuthorityRole()
	{
		if (IsFikaHeadlessHost())
		{
			return A10AuthorityRole.FikaHeadlessHost;
		}

		try
		{
			if (FikaBackendUtils.IsServer)
			{
				return A10AuthorityRole.FikaHost;
			}

			if (FikaBackendUtils.IsClient)
			{
				return A10AuthorityRole.FikaClient;
			}
		}
		catch
		{
		}

		return A10AuthorityRole.Singleplayer;
	}

	private static bool IsFikaHeadlessHost()
	{
		try
		{
			if (!FikaBackendUtils.IsServer)
			{
				return false;
			}
		}
		catch
		{
			return false;
		}

		GameWorld gameWorld = Singleton<GameWorld>.Instance;
		return gameWorld != null && gameWorld.MainPlayer == null;
	}

	private static string GetLocalProfileId()
	{
		try
		{
			return Singleton<GameWorld>.Instance?.MainPlayer?.ProfileId ?? string.Empty;
		}
		catch
		{
			return string.Empty;
		}
	}

	private static bool IsLocalRequester(FireSupportRequestPacket packet)
	{
		if (packet == null || string.IsNullOrWhiteSpace(packet.RequesterProfileId))
		{
			return false;
		}

		string localProfileId = GetLocalProfileId();
		return !string.IsNullOrWhiteSpace(localProfileId) &&
		       string.Equals(packet.RequesterProfileId, localProfileId, StringComparison.Ordinal);
	}


	private static bool TryApplyHostAuthority(
		FireSupportRequestPacket packet,
		out string reason)
	{
		reason = string.Empty;
		if (IsUavType(packet.SupportType))
		{
			packet.DurationSeconds = UavReconSettings.GetConfiguredDurationSeconds(packet.SupportType);
			packet.ScanIntervalSeconds = UavReconSettings.GetConfiguredScanInterval(packet.SupportType);
			packet.RangeMeters = UavReconSettings.GetConfiguredRangeMeters(packet.SupportType);
			TscDiagnostics.LogFika(
				$"TSC Fika host-authoritative UAV contract type={packet.SupportType} duration={packet.DurationSeconds:0.#}s scan={packet.ScanIntervalSeconds:0.##}s range={packet.RangeMeters:0.#}m requestId={A10AuthorityDiagnostics.FormatRequestId(packet.SupportRequestId)}");
		}

		if (!IsExtractionType(packet.SupportType))
		{
			return true;
		}

		HelicopterTimingSnapshot hostTiming =
			FireSupportTuningSettings.CaptureHelicopterTiming(packet.SupportType);
		HelicopterTimingSnapshot requestedTiming = packet.GetHelicopterTiming();
		if (!HelicopterTimingsEqual(requestedTiming, hostTiming))
		{
			reason = "ExtractionTimingContractChanged";
			TscDiagnostics.LogFika(
				$"TSC Fika extraction timing rejected type={packet.SupportType} " +
				$"requestId={A10AuthorityDiagnostics.FormatRequestId(packet.SupportRequestId)} " +
				$"clientRevision={packet.HelicopterTimingRevision} hostRevision={s_hostSettingsRevision}; " +
				"requester must retry with current host settings.");
			return false;
		}

		packet.SetHelicopterTiming(hostTiming, s_hostSettingsRevision);
		TscDiagnostics.LogFika(
			$"TSC Fika host-authoritative extraction timing type={packet.SupportType} " +
			$"revision={packet.HelicopterTimingRevision} " +
			$"dispatch={packet.HelicopterDispatchDelaySeconds:0.##}s " +
			$"wait={packet.HelicopterWaitTimeSeconds}s " +
			$"extract={packet.HelicopterExtractTimeSeconds:0.##}s " +
			$"speed={packet.HelicopterSpeedMultiplier:0.##} " +
			$"requestId={A10AuthorityDiagnostics.FormatRequestId(packet.SupportRequestId)}");
		return true;
	}

	private static bool HelicopterTimingsEqual(
		HelicopterTimingSnapshot left,
		HelicopterTimingSnapshot right)
	{
		return left.SupportType == right.SupportType &&
		       left.DispatchDelaySeconds.Equals(right.DispatchDelaySeconds) &&
		       left.WaitTimeSeconds == right.WaitTimeSeconds &&
		       left.ExtractTimeSeconds.Equals(right.ExtractTimeSeconds) &&
		       left.SpeedMultiplier.Equals(right.SpeedMultiplier);
	}

	// UH-60 extraction: in a Fika session the raid must end through Fika's
	// extract flow. The host stays to keep the session alive for remaining
	// players; stopping the session directly stranded the lobby.
	private static bool OnExtractOverride(Player player, string exitName)
	{
		try
		{
			if (!FikaBackendUtils.IsServer && !FikaBackendUtils.IsClient)
			{
				return false;
			}

			if (Singleton<AbstractGame>.Instance is not CoopGame coopGame ||
			    player is not FikaPlayer fikaPlayer)
			{
				return false;
			}

			coopGame.ExitStatus = ExitStatus.Survived;
			coopGame.ExitLocation = exitName;
			coopGame.Extract(fikaPlayer, null, null);
			TscDiagnostics.LogFika($"UH-60 extraction routed through Fika extract. exit={exitName}");
			return true;
		}
		catch (Exception ex)
		{
			s_logSource?.LogWarning($"Fika extract routing failed; falling back to session stop. {ex}");
			return false;
		}
	}

	private static bool IsA10Type(ESupportType supportType)
	{
		return supportType == ESupportType.Strafe ||
		       supportType == ESupportType.DoubleStrafe;
	}

	private static bool IsSupportedNetworkType(ESupportType supportType)

	{
		return supportType == ESupportType.Strafe ||
		       supportType == ESupportType.DoubleStrafe ||
		       supportType == ESupportType.Extract ||
		       supportType == ESupportType.PriorityExfil ||
		       supportType == ESupportType.Uav ||
		       supportType == ESupportType.FocusedSweep;
	}

	private static bool IsExtractionType(ESupportType supportType)
	{
		return supportType == ESupportType.Extract ||
		       supportType == ESupportType.PriorityExfil;
	}

	private static bool IsUavType(ESupportType supportType)
	{
		return supportType == ESupportType.Uav ||
		       supportType == ESupportType.FocusedSweep;
	}

	private sealed class ClientPendingRequest
	{
		private readonly object _gate = new();
		private bool _completed;
		private FireSupportNetworkRequestResult _result;

		public ClientPendingRequest(SupportRequestFingerprint fingerprint)
		{
			Fingerprint = fingerprint;
		}

		public SupportRequestFingerprint Fingerprint { get; }
		public UniTaskCompletionSource<FireSupportNetworkRequestResult> Completion { get; } = new();

		public bool IsCompleted
		{
			get
			{
				lock (_gate)
				{
					return _completed;
				}
			}
		}

		public bool TrySetResult(FireSupportNetworkRequestResult result)
		{
			lock (_gate)
			{
				if (_completed)
				{
					return false;
				}

				_completed = true;
				_result = result;
			}

			Completion.TrySetResult(result);
			return true;
		}

		public bool TryGetResult(out FireSupportNetworkRequestResult result)
		{
			lock (_gate)
			{
				result = _result;
				return _completed;
			}
		}
	}

	private sealed class AuthorityRequestEntry : IDisposable
	{
		private readonly object _gate = new();
		private readonly CancellationTokenSource _cancellationTokenSource;
		private readonly CancellationToken _raidCancellationToken;
		private readonly List<A10TracerBurst> _bufferedTracerBursts = new();
		private AuthorityOutcome _outcome;
		private string _explicitCancellationReason = string.Empty;
		private bool _acceptedDeliveryPublished;
		private int _activeAuthorityWorkCount;
		private bool _disposeRequested;
		private bool _disposed;

		public AuthorityRequestEntry(
			FireSupportRequestPacket request,
			SupportRequestFingerprint fingerprint,
			NetPeer originPeer,
			CancellationToken cancellationToken)
		{
			Request = CloneSupportRequest(request);
			Fingerprint = fingerprint;
			OriginPeer = originPeer;
			_raidCancellationToken = cancellationToken;
			_cancellationTokenSource =
				CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			// Hold the CTS for the lifetime of the original authority handler.
			// Reset/eviction can request disposal, but it is finalized only after
			// this handler and any launched executor work have unwound.
			_activeAuthorityWorkCount = 1;
			float authorityTimeoutSeconds = AuthorityRequestTimeoutSeconds +
				(IsExtractionType(request.SupportType)
					? Math.Max(0f, request.HelicopterDispatchDelaySeconds)
					: 0f);
			_cancellationTokenSource.CancelAfter(
				TimeSpan.FromSeconds(authorityTimeoutSeconds));
		}

		public FireSupportRequestPacket Request { get; }
		public SupportRequestFingerprint Fingerprint { get; }
		public NetPeer OriginPeer { get; }
		public CancellationToken CancellationToken => _cancellationTokenSource.Token;
		public string CancellationReason
		{
			get
			{
				lock (_gate)
				{
					if (!string.IsNullOrEmpty(_explicitCancellationReason))
					{
						return _explicitCancellationReason;
					}

					return _raidCancellationToken.IsCancellationRequested
						? "RaidCancelled"
						: "AuthorityExecutionTimedOut";
				}
			}
		}
		public UniTaskCompletionSource<AuthorityOutcome> Completion { get; } = new();
		public bool IsCompleted { get; private set; }
		public bool IsAbandoned { get; private set; }
		public bool ExecutionStarted { get; private set; }

		public bool IsOwnedBy(NetPeer peer)
		{
			return ReferenceEquals(OriginPeer, peer);
		}

		public bool TryBeginExecution()
		{
			lock (_gate)
			{
				if (IsCompleted || IsAbandoned || ExecutionStarted)
				{
					return false;
				}

				ExecutionStarted = true;
				_activeAuthorityWorkCount++;
			}

			// The 20-second authority deadline governs request admission. Once
			// an irreversible runtime start is committed, a client cancel must
			// wait for that executor's true result rather than interrupting it
			// and refunding a partially-started effect.
			try
			{
				_cancellationTokenSource.CancelAfter(Timeout.InfiniteTimeSpan);
			}
			catch
			{
			}

			return true;
		}

		public bool TryReserveAcceptedBackgroundWork()
		{
			lock (_gate)
			{
				if (IsAbandoned ||
				    _disposed ||
				    !IsCompleted ||
				    !_outcome.Result.Accepted)
				{
					return false;
				}

				_activeAuthorityWorkCount++;
				return true;
			}
		}

		public void MarkAuthorityWorkFinished()
		{
			bool disposeNow;
			lock (_gate)
			{
				_activeAuthorityWorkCount =
					Math.Max(0, _activeAuthorityWorkCount - 1);
				disposeNow =
					_disposeRequested &&
					_activeAuthorityWorkCount == 0 &&
					!_disposed;
				if (disposeNow)
				{
					_disposed = true;
				}
			}

			if (disposeNow)
			{
				_cancellationTokenSource.Dispose();
			}
		}

		public bool TryComplete(AuthorityOutcome outcome)
		{
			lock (_gate)
			{
				if (IsCompleted || IsAbandoned)
				{
					return false;
				}

				IsCompleted = true;
				_outcome = outcome;
				if (!outcome.Result.Accepted)
				{
					_bufferedTracerBursts.Clear();
				}
			}

			try
			{
				_cancellationTokenSource.CancelAfter(Timeout.InfiniteTimeSpan);
			}
			catch
			{
			}

			Completion.TrySetResult(outcome);
			return true;
		}

		public bool TryCancelAndComplete(
			AuthorityOutcome outcome,
			string cancellationReason)
		{
			lock (_gate)
			{
				if (IsCompleted || IsAbandoned || ExecutionStarted)
				{
					return false;
				}

				IsCompleted = true;
				_outcome = outcome;
				_explicitCancellationReason = cancellationReason ?? string.Empty;
				_bufferedTracerBursts.Clear();
			}

			try
			{
				_cancellationTokenSource.Cancel();
			}
			catch
			{
			}

			Completion.TrySetResult(outcome);
			return true;
		}

		public bool TryBufferTracerBurst(
			A10TracerBurst burst,
			out string disposition)
		{
			disposition = string.Empty;
			lock (_gate)
			{
				if (IsAbandoned)
				{
					disposition = "authority request abandoned";
					return true;
				}

				if (IsCompleted && !_outcome.Result.Accepted)
				{
					disposition = "authority request rejected";
					return true;
				}

				if (!IsCompleted ||
				    (_outcome.Result.Accepted && !_acceptedDeliveryPublished))
				{
					if (_bufferedTracerBursts.Count <
					    MaxBufferedTracerBurstsPerRequest)
					{
						_bufferedTracerBursts.Add(burst);
						disposition = "buffered pending accepted delivery";
					}
					else
					{
						disposition = "buffer full; tracer dropped";
					}

					return true;
				}

				return false;
			}
		}

		public List<A10TracerBurst> MarkAcceptedDeliveryPublishedAndDrainTracerBursts()
		{
			lock (_gate)
			{
				if (!IsCompleted || !_outcome.Result.Accepted)
				{
					_bufferedTracerBursts.Clear();
					return new List<A10TracerBurst>();
				}

				_acceptedDeliveryPublished = true;
				var buffered = new List<A10TracerBurst>(_bufferedTracerBursts);
				_bufferedTracerBursts.Clear();
				return buffered;
			}
		}

		public void Abandon(FireSupportNetworkRequestResult result)
		{
			AuthorityOutcome outcome = default;
			bool completeWaiters = false;
			lock (_gate)
			{
				if (IsAbandoned)
				{
					return;
				}

				IsAbandoned = true;
				_bufferedTracerBursts.Clear();
				if (!IsCompleted)
				{
					IsCompleted = true;
					outcome = AuthorityOutcome.FromResult(Request, result);
					_outcome = outcome;
					_explicitCancellationReason = result.Reason ?? string.Empty;
					completeWaiters = true;
				}
			}

			try
			{
				_cancellationTokenSource.Cancel();
			}
			catch
			{
			}

			if (completeWaiters)
			{
				Completion.TrySetResult(outcome);
			}

			RequestDisposeWhenAuthorityWorkFinishes();
		}

		public bool TryGetOutcome(out AuthorityOutcome outcome)
		{
			lock (_gate)
			{
				outcome = _outcome;
				return IsCompleted;
			}
		}

		public void Dispose()
		{
			lock (_gate)
			{
				_bufferedTracerBursts.Clear();
			}
			RequestDisposeWhenAuthorityWorkFinishes();
		}

		private void RequestDisposeWhenAuthorityWorkFinishes()
		{
			bool disposeNow;
			lock (_gate)
			{
				_disposeRequested = true;
				disposeNow =
					_activeAuthorityWorkCount == 0 &&
					!_disposed;
				if (disposeNow)
				{
					_disposed = true;
				}
			}

			if (disposeNow)
			{
				_cancellationTokenSource.Dispose();
			}
		}
	}

	private sealed class UavAuthorityReservation
	{
		public UavAuthorityReservation(
			string supportRequestId,
			float expiresAt)
		{
			SupportRequestId = supportRequestId ?? string.Empty;
			ExpiresAt = expiresAt;
		}

		public string SupportRequestId { get; }
		public float ExpiresAt { get; set; }
	}

	private readonly struct AuthorityOutcome
	{
		private AuthorityOutcome(
			FireSupportNetworkRequestResult result,
			FireSupportAuthorityResultPacket resultPacket,
			FireSupportRequestPacket acceptedRequest)
		{
			Result = result;
			ResultPacket = resultPacket;
			AcceptedRequest = acceptedRequest;
		}

		public FireSupportNetworkRequestResult Result { get; }
		public FireSupportAuthorityResultPacket ResultPacket { get; }
		public FireSupportRequestPacket AcceptedRequest { get; }

		public static AuthorityOutcome Accepted(FireSupportRequestPacket request)
		{
			FireSupportRequestPacket acceptedRequest = CloneSupportRequest(request);
			var result = FireSupportNetworkRequestResult.Accept(
				"AuthorityAccepted",
				acceptedRequest.DurationSeconds,
				acceptedRequest.ScanIntervalSeconds,
				acceptedRequest.RangeMeters);
			return new AuthorityOutcome(
				result,
				new FireSupportAuthorityResultPacket(
					acceptedRequest,
					accepted: true,
					result.Reason),
				acceptedRequest);
		}

		public static AuthorityOutcome Rejected(
			FireSupportRequestPacket request,
			string reason)
		{
			return FromResult(
				request,
				FireSupportNetworkRequestResult.Reject(reason));
		}

		public static AuthorityOutcome FromResult(
			FireSupportRequestPacket request,
			FireSupportNetworkRequestResult result)
		{
			FireSupportRequestPacket snapshot = CloneSupportRequest(request);
			if (result.Accepted)
			{
				result = FireSupportNetworkRequestResult.Accept(
					result.Reason,
					snapshot.DurationSeconds,
					snapshot.ScanIntervalSeconds,
					snapshot.RangeMeters);
			}
			return new AuthorityOutcome(
				result,
				new FireSupportAuthorityResultPacket(
					snapshot,
					result.Accepted,
					result.Reason),
				result.Accepted ? snapshot : null);
		}
	}

	private readonly struct SupportRequestFingerprint : IEquatable<SupportRequestFingerprint>
	{
		private readonly ESupportType _supportType;
		private readonly Vector3 _position;
		private readonly Vector3 _direction;
		private readonly Vector3 _rotation;
		private readonly int _visualSeed;
		private readonly float _durationSeconds;
		private readonly float _scanIntervalSeconds;
		private readonly float _rangeMeters;
		private readonly float _helicopterDispatchDelaySeconds;
		private readonly int _helicopterWaitTimeSeconds;
		private readonly float _helicopterExtractTimeSeconds;
		private readonly float _helicopterSpeedMultiplier;
		private readonly int _passIndex;
		private readonly string _requesterProfileId;

		public SupportRequestFingerprint(FireSupportRequestPacket packet)
		{
			_supportType = packet.SupportType;
			_position = packet.Position;
			_direction = packet.Direction;
			_rotation = packet.Rotation;
			_visualSeed = packet.VisualSeed;
			_durationSeconds = packet.DurationSeconds;
			_scanIntervalSeconds = packet.ScanIntervalSeconds;
			_rangeMeters = packet.RangeMeters;
			_helicopterDispatchDelaySeconds = packet.HelicopterDispatchDelaySeconds;
			_helicopterWaitTimeSeconds = packet.HelicopterWaitTimeSeconds;
			_helicopterExtractTimeSeconds = packet.HelicopterExtractTimeSeconds;
			_helicopterSpeedMultiplier = packet.HelicopterSpeedMultiplier;
			_passIndex = packet.PassIndex;
			_requesterProfileId = packet.RequesterProfileId ?? string.Empty;
		}

		public bool MatchesResult(FireSupportAuthorityResultPacket packet)
		{
			return packet != null &&
			       _supportType == packet.SupportType &&
			       _passIndex == packet.PassIndex &&
			       string.Equals(
				       _requesterProfileId,
				       packet.RequesterProfileId,
				       StringComparison.Ordinal);
		}

		public bool MatchesCancel(FireSupportCancelPacket packet)
		{
			return packet != null &&
			       _supportType == packet.SupportType &&
			       _passIndex == packet.PassIndex &&
			       string.Equals(
				       _requesterProfileId,
				       packet.RequesterProfileId,
				       StringComparison.Ordinal);
		}

		public bool MatchesAcceptedRequest(FireSupportRequestPacket packet)
		{
			return packet != null &&
			       _supportType == packet.SupportType &&
			       _position.Equals(packet.Position) &&
			       _direction.Equals(packet.Direction) &&
			       _rotation.Equals(packet.Rotation) &&
			       _visualSeed == packet.VisualSeed &&
			       (IsUavType(_supportType) ||
			        (_durationSeconds.Equals(packet.DurationSeconds) &&
			         _scanIntervalSeconds.Equals(packet.ScanIntervalSeconds) &&
			         _rangeMeters.Equals(packet.RangeMeters))) &&
			       MatchesHelicopterTiming(packet) &&
			       _passIndex == packet.PassIndex &&
			       string.Equals(
				       _requesterProfileId,
				       packet.RequesterProfileId,
				       StringComparison.Ordinal);
		}

		public bool MatchesUavLoiter(StartUavLoiterPacket packet)
		{
			return packet != null &&
			       IsUavType(_supportType) &&
			       _supportType == packet.SupportType &&
			       _position.Equals(packet.Center) &&
			       _durationSeconds.Equals(packet.DurationSeconds) &&
			       string.Equals(
				       _requesterProfileId,
				       packet.RequesterProfileId,
				       StringComparison.Ordinal);
		}

		public bool Equals(SupportRequestFingerprint other)
		{
			return _supportType == other._supportType &&
			       _position.Equals(other._position) &&
			       _direction.Equals(other._direction) &&
			       _rotation.Equals(other._rotation) &&
			       _visualSeed == other._visualSeed &&
			       _durationSeconds.Equals(other._durationSeconds) &&
			       _scanIntervalSeconds.Equals(other._scanIntervalSeconds) &&
			       _rangeMeters.Equals(other._rangeMeters) &&
			       _helicopterDispatchDelaySeconds.Equals(other._helicopterDispatchDelaySeconds) &&
			       _helicopterWaitTimeSeconds == other._helicopterWaitTimeSeconds &&
			       _helicopterExtractTimeSeconds.Equals(other._helicopterExtractTimeSeconds) &&
			       _helicopterSpeedMultiplier.Equals(other._helicopterSpeedMultiplier) &&
			       _passIndex == other._passIndex &&
			       string.Equals(
				       _requesterProfileId,
				       other._requesterProfileId,
				       StringComparison.Ordinal);
		}

		public override bool Equals(object obj)
		{
			return obj is SupportRequestFingerprint other && Equals(other);
		}

		public override int GetHashCode()
		{
			unchecked
			{
				int hash = (int)_supportType;
				hash = hash * 397 ^ _position.GetHashCode();
				hash = hash * 397 ^ _direction.GetHashCode();
				hash = hash * 397 ^ _rotation.GetHashCode();
				hash = hash * 397 ^ _visualSeed;
				hash = hash * 397 ^ _durationSeconds.GetHashCode();
				hash = hash * 397 ^ _scanIntervalSeconds.GetHashCode();
				hash = hash * 397 ^ _rangeMeters.GetHashCode();
				hash = hash * 397 ^ _helicopterDispatchDelaySeconds.GetHashCode();
				hash = hash * 397 ^ _helicopterWaitTimeSeconds;
				hash = hash * 397 ^ _helicopterExtractTimeSeconds.GetHashCode();
				hash = hash * 397 ^ _helicopterSpeedMultiplier.GetHashCode();
				hash = hash * 397 ^ _passIndex;
				hash = hash * 397 ^ StringComparer.Ordinal.GetHashCode(
					_requesterProfileId ?? string.Empty);
				return hash;
			}
		}

		private bool MatchesHelicopterTiming(FireSupportRequestPacket packet)
		{
			return !IsExtractionType(_supportType) ||
			       (_helicopterDispatchDelaySeconds.Equals(
				        packet.HelicopterDispatchDelaySeconds) &&
			        _helicopterWaitTimeSeconds == packet.HelicopterWaitTimeSeconds &&
			        _helicopterExtractTimeSeconds.Equals(
				        packet.HelicopterExtractTimeSeconds) &&
			        _helicopterSpeedMultiplier.Equals(
				        packet.HelicopterSpeedMultiplier));
		}
	}
}
