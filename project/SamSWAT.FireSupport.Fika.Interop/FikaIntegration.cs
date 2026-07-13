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
	private const int MaxA10TracerSegmentsPerPacket = 20;
	private static readonly bool RemotePhoneVisualSyncEnabled = false;

	private static ManualLogSource s_logSource;
	private static bool s_enabled;
	private static FikaServer s_server;
	private static FikaClient s_client;
	private static readonly HashSet<object> s_registeredPacketManagers = new();
	private static readonly object s_supportRequestGate = new();
	private static readonly HashSet<string> s_inFlightSupportRequests = new(StringComparer.Ordinal);
	private static readonly HashSet<string> s_completedSupportRequests = new(StringComparer.Ordinal);
	private static readonly Queue<string> s_completedSupportRequestOrder = new();
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
		s_settingsBroadcastDebounceCts?.Cancel();
		s_settingsBroadcastDebounceCts?.Dispose();
		s_settingsBroadcastDebounceCts = null;
		FireSupportExtraction.ExtractOverride = null;
		s_registeredPacketManagers.Clear();
		s_server = null;
		s_client = null;
		ClearSupportRequestGates("plugin destroyed");
		A10TracerNetworking.SetNetworkAuthorityActive(false, "plugin destroyed");
		A10TracerNetworking.SetAuthorityRole(A10AuthorityRole.Singleplayer.ToString());
		ClearHostAuthority("plugin destroyed");
		s_enabled = false;
	}

	public static void OnUpdate()
	{
		try
		{
			if (A10TracerNetworking.IsNetworkAuthorityActive &&
			    !FikaBackendUtils.IsServer &&
			    !FikaBackendUtils.IsClient)
			{
				A10TracerNetworking.SetNetworkAuthorityActive(false, "Fika session disconnected");
			}
		}
		catch
		{
			if (A10TracerNetworking.IsNetworkAuthorityActive)
			{
				A10TracerNetworking.SetNetworkAuthorityActive(false, "Fika state unavailable");
			}
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
				s_server = server;
				s_client = null;
				A10TracerNetworking.SetAuthorityRole(GetA10AuthorityRole().ToString());
				A10TracerNetworking.SetNetworkAuthorityActive(true, "hosting Fika session");
				A10AuthorityDiagnostics.LogOptionalVisualModsOnce();
				ClearSupportRequestGates("hosting Fika session");
				ClearHostAuthority("hosting Fika session");
				if (TryMarkPacketRegistration(server, "server"))
				{
					server.RegisterPacket<FireSupportRequestPacket, NetPeer>(OnServerSupportRequest);
					server.RegisterPacket<FireSupportSettingsPacket, NetPeer>(OnServerSettingsRequest);
					server.RegisterPacket<StartUavLoiterPacket, NetPeer>(OnServerStartUavLoiter);
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
				s_client = client;
				s_server = null;
				A10TracerNetworking.SetAuthorityRole(A10AuthorityRole.FikaClient.ToString());
				A10TracerNetworking.SetNetworkAuthorityActive(true, "joining Fika host");
				A10AuthorityDiagnostics.LogOptionalVisualModsOnce();
				ClearSupportRequestGates("joining Fika host");
				ClearHostAuthority("joining Fika host");
				FireSupportServerConfigClient.SetFikaClientHostAuthorityActive(true, "joining Fika host");
				if (TryMarkPacketRegistration(client, "client"))
				{
					client.RegisterPacket<FireSupportRequestPacket>(OnClientSupportBroadcast);
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

	private static bool OnLocalSupportRequested(
		ESupportType supportType,
		Vector3 position,
		Vector3 direction,
		Vector3 rotation,
		int visualSeed,
		float durationSeconds,
		int passIndex,
		CancellationToken cancellationToken)
	{
		if (!IsSupportedNetworkType(supportType))
		{
			return false;
		}

		var packet = new FireSupportRequestPacket(
			supportType,
			position,
			direction,
			rotation,
			visualSeed,
			durationSeconds,
			passIndex,
			GetLocalProfileId(),
			Guid.NewGuid().ToString("N"));

		if (FikaBackendUtils.IsServer)
		{
			ApplyHostAuthority(packet);
			if (!TryBeginAuthorityRequest(packet, "local host request"))
			{
				return true;
			}

			ExecuteAuthoritySupport(packet, cancellationToken, playUavActivationVisual: true).Forget();
			BroadcastSupportPacket(packet, peer: null, broadcastToAll: true, reason: "local host request");
			return true;
		}

		if (FikaBackendUtils.IsClient)
		{
			// Clients never create authoritative damage. They wait for the host/headless
			// accepted/broadcast packet, then run local visual/UI playback only. Optional
			// A-10 prediction remains behind a hidden diagnostics switch.
			if (IsA10Type(packet.SupportType) &&
			    FireSupportTuningSettings.IsA10ClientVisualPredictionEnabled())
			{
				A10TracerNetworking.TrackLocalVisualPrediction(
					packet.SupportRequestId,
					packet.SupportType,
					packet.VisualSeed,
					packet.PassIndex,
					packet.Position,
					cancellationToken);
				ExecuteClientSupportVisual(packet, cancellationToken, playUavActivationVisual: true).Forget();
			}

			TscDiagnostics.LogFika(
				$"TSC Fika support request sent type={packet.SupportType} requestId={A10AuthorityDiagnostics.FormatRequestId(packet.SupportRequestId)}; waiting for host authority.");
			Singleton<FikaClient>.Instance.SendData(ref packet, DeliveryMethod.ReliableOrdered);
			return true;
		}

		return false;
	}

	private static bool OnLocalUavLoiterRequested(
		UavA10LoiterRequest request,
		CancellationToken cancellationToken)
	{
		try
		{
			if (FikaBackendUtils.IsServer)
			{
				UavA10LoiterRequest authoritativeRequest = UavA10LoiterSettings.ApplyHostAuthority(request);
				var packet = new StartUavLoiterPacket(authoritativeRequest);
				Singleton<FikaServer>.Instance.SendData(
					ref packet,
					DeliveryMethod.ReliableOrdered,
					broadcast: true);
				UavAircraftLoiterController.StartLocal(authoritativeRequest, cancellationToken);
				return true;
			}

			if (FikaBackendUtils.IsClient)
			{
				var packet = new StartUavLoiterPacket(request);
				Singleton<FikaClient>.Instance.SendData(ref packet, DeliveryMethod.ReliableOrdered);
				return true;
			}
		}
		catch (Exception ex)
		{
			s_logSource?.LogWarning($"UAV A-10 loiter packet failed; skipping aircraft visual. {ex}");
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
		CancellationToken cancellationToken = GetRaidCancellationToken();

		packet?.EnsureRequestId();
		if (packet == null)
		{
			return;
		}

		ApplyHostAuthority(packet);
		if (!FireSupportServiceAvailability.IsServiceEnabled(packet.SupportType))
		{
			s_logSource?.LogWarning(
				$"TSC Fika settings: ignored disabled support request type={packet.SupportType} requestId={A10AuthorityDiagnostics.FormatRequestId(packet.SupportRequestId)}");
			return;
		}

		if (!TryBeginAuthorityRequest(packet, "client request"))
		{
			return;
		}

		ExecuteAuthoritySupport(packet, cancellationToken, playUavActivationVisual: false).Forget();
		bool broadcastToAll = IsA10Type(packet.SupportType) ||
		                      IsExtractionType(packet.SupportType) ||
		                      IsUavType(packet.SupportType);
		BroadcastSupportPacket(packet, peer, broadcastToAll, "host accepted client request");
	}

	private static void OnClientSupportBroadcast(FireSupportRequestPacket packet)
	{
		packet?.EnsureRequestId();
		if (packet == null)
		{
			return;
		}

		ExecuteClientSupportVisual(packet, GetRaidCancellationToken(), playUavActivationVisual: false).Forget();
	}

	private static void OnServerStartUavLoiter(StartUavLoiterPacket packet, NetPeer peer)
	{
		try
		{
			UavA10LoiterRequest request = UavA10LoiterSettings.ApplyHostAuthority(packet.ToRequest());
			packet = new StartUavLoiterPacket(request);
			Singleton<FikaServer>.Instance.SendData(
				ref packet,
				DeliveryMethod.ReliableOrdered,
				broadcast: true);
			UavAircraftLoiterController.StartLocal(request, GetRaidCancellationToken());
		}
		catch (Exception ex)
		{
			s_logSource?.LogWarning($"UAV A-10 loiter server broadcast failed; skipping aircraft visual. {ex}");
		}
	}

	private static void OnClientStartUavLoiter(StartUavLoiterPacket packet)
	{
		try
		{
			UavAircraftLoiterController.StartLocal(packet.ToRequest(), GetRaidCancellationToken());
		}
		catch (Exception ex)
		{
			s_logSource?.LogWarning($"UAV A-10 loiter client visual failed. {ex}");
		}
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

			FikaServer server = GetServer();
			if (server == null)
			{
				s_logSource?.LogWarning(
					$"A-10 tracer sync skipped; server unavailable burst={burst?.BurstId ?? 0}");
				return;
			}

			int totalSegments = burst.Segments.Length;
			for (int offset = 0; offset < totalSegments; offset += MaxA10TracerSegmentsPerPacket)
			{
				int count = Math.Min(MaxA10TracerSegmentsPerPacket, totalSegments - offset);
				var chunk = new A10TracerSegment[count];
				Array.Copy(burst.Segments, offset, chunk, 0, count);
				var packet = new A10TracerBurstPacket(burst, offset, totalSegments, chunk);
				server.SendData(
					ref packet,
					DeliveryMethod.ReliableOrdered,
					broadcast: true);
			}

			TscDiagnostics.LogFika(
				$"A-10 tracer sync: broadcast burst={burst.BurstId} pass={burst.PassIndex} segments={totalSegments}");
		}
		catch (Exception ex)
		{
			s_logSource?.LogWarning($"A-10 tracer sync broadcast failed. {ex}");
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
			packet.HelicopterWaitTimeSeconds,
			packet.PriorityExfilHelicopterWaitTimeSeconds,
			packet.PriorityExfilDispatchDelaySeconds,
			packet.HelicopterExtractTimeSeconds,
			packet.HelicopterSpeedMultiplier,
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

			var packet = BuildHostSettingsPacket(incrementRevision: true);
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
			HelicopterWaitTimeSeconds = FireSupportTuningSettings.GetHelicopterWaitTime(ESupportType.Extract),
			PriorityExfilHelicopterWaitTimeSeconds = FireSupportTuningSettings.GetHelicopterWaitTime(ESupportType.PriorityExfil),
			PriorityExfilDispatchDelaySeconds = FireSupportTuningSettings.GetPriorityExfilDispatchDelay(),
			HelicopterExtractTimeSeconds = FireSupportTuningSettings.GetHelicopterExtractTime(),
			HelicopterSpeedMultiplier = FireSupportTuningSettings.GetHelicopterSpeedMultiplier(ESupportType.Extract),
			PriorityExfilHelicopterSpeedMultiplier = FireSupportTuningSettings.GetHelicopterSpeedMultiplier(ESupportType.PriorityExfil),
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

	private static async UniTaskVoid ExecuteAuthoritySupport(
		FireSupportRequestPacket packet,
		CancellationToken cancellationToken,
		bool playUavActivationVisual)
	{
		bool success = false;
		try
		{
			success = await ExecuteSupportCore(packet, visualOnly: false, cancellationToken, playUavActivationVisual);
		}
		finally
		{
			MarkAuthorityRequestComplete(packet, success);
		}
	}

	private static async UniTaskVoid ExecuteClientSupportVisual(
		FireSupportRequestPacket packet,
		CancellationToken cancellationToken,
		bool playUavActivationVisual)
	{
		await ExecuteSupportCore(packet, visualOnly: true, cancellationToken, playUavActivationVisual);
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
			if (visualOnly && !IsLocalRequester(packet))
			{
				TscDiagnostics.LogFika(
					$"TSC UAV HUD ignored on non-requester client type={packet.SupportType} requestId={A10AuthorityDiagnostics.FormatRequestId(packet.SupportRequestId)} requester={A10AuthorityDiagnostics.ShortId(packet.RequesterProfileId)} local={A10AuthorityDiagnostics.ShortId(GetLocalProfileId())}");
				return true;
			}

			if (!IsFikaHeadlessHost())
			{
				if (visualOnly)
				{
					TscDiagnostics.LogFika(
						$"TSC UAV HUD accepted on requester client type={packet.SupportType} requestId={A10AuthorityDiagnostics.FormatRequestId(packet.SupportRequestId)} requester={A10AuthorityDiagnostics.ShortId(packet.RequesterProfileId)}");
				}

				UavReconOverlay.Activate(
					packet.DurationSeconds,
					cancellationToken,
					playActivationVisual: false,
					UavReconSettings.GetScanInterval(packet.SupportType),
					UavReconSettings.GetRangeMeters(packet.SupportType));
			}
			else
			{
				TscDiagnostics.LogFika($"TSC UAV HUD skipped on Fika headless host requestId={A10AuthorityDiagnostics.FormatRequestId(packet.SupportRequestId)}; clients render their own overlays.");
			}

			return true;
		}

		bool success;
		if (IsA10Type(packet.SupportType))
		{
			A10AuthorityRole role = visualOnly ? A10AuthorityRole.FikaClient : GetA10AuthorityRole();
			var request = new A10StrikeRequest
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

			success = await A10StrikeExecutorSelector.ExecuteAsync(request, cancellationToken);
		}
		else
		{
			success = await FireSupportRuntime.TryProcessRequest(
				packet.SupportType,
				packet.Position,
				packet.Direction,
				packet.Rotation,
				visualOnly,
				packet.VisualSeed,
				cancellationToken,
				packet.PassIndex);
		}

		if (success && IsExtractionType(packet.SupportType) && !visualOnly)
		{
			FireSupportAudio.Instance.PlayVoiceover(EVoiceoverType.SupportHeliArrivingToPickup);
		}

		return success;
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

	private static bool TryBeginAuthorityRequest(FireSupportRequestPacket packet, string source)
	{
		packet.EnsureRequestId();
		lock (s_supportRequestGate)
		{
			if (s_completedSupportRequests.Contains(packet.SupportRequestId) ||
			    s_inFlightSupportRequests.Contains(packet.SupportRequestId))
			{
				TscDiagnostics.LogFika($"TSC Fika duplicate support request ignored type={packet.SupportType} requestId={A10AuthorityDiagnostics.FormatRequestId(packet.SupportRequestId)} source={source}");
				return false;
			}

			s_inFlightSupportRequests.Add(packet.SupportRequestId);
		}

		TscDiagnostics.LogFika($"TSC Fika authority accepted support request type={packet.SupportType} requestId={A10AuthorityDiagnostics.FormatRequestId(packet.SupportRequestId)} requester={packet.RequesterProfileId} source={source}");
		return true;
	}

	private static void MarkAuthorityRequestComplete(FireSupportRequestPacket packet, bool success)
	{
		if (packet == null || string.IsNullOrWhiteSpace(packet.SupportRequestId))
		{
			return;
		}

		lock (s_supportRequestGate)
		{
			s_inFlightSupportRequests.Remove(packet.SupportRequestId);
			if (s_completedSupportRequests.Add(packet.SupportRequestId))
			{
				s_completedSupportRequestOrder.Enqueue(packet.SupportRequestId);
			}

			while (s_completedSupportRequestOrder.Count > 256)
			{
				s_completedSupportRequests.Remove(s_completedSupportRequestOrder.Dequeue());
			}
		}

		TscDiagnostics.LogFika($"TSC Fika authority completed support request type={packet.SupportType} requestId={A10AuthorityDiagnostics.FormatRequestId(packet.SupportRequestId)} success={success}");
	}

	private static void ClearSupportRequestGates(string reason)
	{
		lock (s_supportRequestGate)
		{
			s_inFlightSupportRequests.Clear();
			s_completedSupportRequests.Clear();
			s_completedSupportRequestOrder.Clear();
		}

		TscDiagnostics.LogFika($"TSC Fika support request gates cleared reason={reason}");
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


	private static void ApplyHostAuthority(FireSupportRequestPacket packet)
	{
		if (IsUavType(packet.SupportType))
		{
			packet.DurationSeconds = UavReconSettings.GetConfiguredDurationSeconds(packet.SupportType);
		}
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
}
