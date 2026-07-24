using BepInEx.Configuration;
using Comfort.Common;
using Cysharp.Threading.Tasks;
using EFT;
using Newtonsoft.Json;
using SPT.Common.Http;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Text;
using System.Threading;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public static class FireSupportServerConfigClient
{
	private static readonly object s_profileMutationGate = new();
	private static CancellationTokenSource s_refreshCts;
	private static bool s_initialized;
	private static bool s_globalSettingsSuppressedByFikaClient;
	private static bool s_raidActive;
	private static string s_hostPurchaseBaseUrl;
	private static int s_hostPurchaseRevision;
	private static long s_profileMutationEpoch;
	private static int s_profileMutationsInFlight;

	public static void Initialize()
	{
		if (s_initialized)
		{
			return;
		}

		s_initialized = true;
		SubscribeSetting(PluginSettings.UseServerConfigUrl);
		SubscribeSetting(PluginSettings.ServerConfigUrl);
		SubscribeSetting(PluginSettings.ServerConfigAuthToken);
		SubscribeSetting(PluginSettings.RequireServerConfigInFika);
		SubscribeSetting(PluginSettings.ServerConfigRefreshSeconds);
		// No poll here: config is only consumed in raid, so polling only runs
		// between OnRaidStarted and OnRaidEnded. This stops the mod from hitting
		// the server (and logging a request) every few seconds in the menu and
		// hideout, which was the main source of log spam and server load.
	}

	public static void OnRaidStarted()
	{
		s_raidActive = true;
		RestartRefresh("raid started");
	}

	public static void OnRaidEnded()
	{
		s_raidActive = false;
		StopRefresh();
	}

	public static void SetFikaClientHostAuthorityActive(bool active, string reason)
	{
		if (s_globalSettingsSuppressedByFikaClient == active)
		{
			return;
		}

		s_globalSettingsSuppressedByFikaClient = active;
		TscDiagnostics.LogPayment(
			$"TSC server global settings {(active ? "suppressed by Fika host authority" : "resumed after Fika host authority cleared")}; per-profile sync remains active: {reason}");
		if (active)
		{
			ClearServerGlobalOverrides(notify: true);
		}

		RestartRefresh(reason);
	}

	public static void SetHostPurchaseEndpoint(string baseUrl, int revision)
	{
		s_hostPurchaseBaseUrl = baseUrl;
		s_hostPurchaseRevision = revision;
		TscDiagnostics.LogPayment($"TSC Fika host purchase endpoint received revision={revision} url={baseUrl}");
	}

	public static void ClearHostPurchaseEndpoint()
	{
		s_hostPurchaseBaseUrl = null;
		s_hostPurchaseRevision = 0;
	}

	public static string GetConfiguredServerConfigUrl()
	{
		return PluginSettings.UseServerConfigUrl?.Value == true
			? PluginSettings.ServerConfigUrl?.Value ?? string.Empty
			: string.Empty;
	}

	public static async UniTask<FireSupportPurchaseResponse> PurchaseAuthorizationAsync(
		ESupportType supportType,
		int clientKnownRevision)
	{
		var fallback = new FireSupportPurchaseResponse
		{
			Ok = false,
			Reason = "ServerConfigUnavailable",
			SupportType = supportType.ToString(),
			Cost = FireSupportPayment.GetActiveCost(supportType),
			PaymentSource = nameof(PaymentSource.StashRoubles),
			NewBalance = FireSupportPayment.GetEffectiveBalance(),
			AuthorizationGranted = false,
			ServerRevision = Math.Max(clientKnownRevision, s_hostPurchaseRevision)
		};

		BeginProfileMutation();
		try
		{
			var body = new FireSupportPurchaseRequest
			{
				Action = "BuyAuthorization",
				SessionId = GetLocalProfileId(),
				ProfileId = GetLocalProfileId(),
				SupportType = supportType.ToString(),
				ClientKnownRevision = clientKnownRevision,
				Quantity = 1
			};
			string responseBody = await SendServerRequestAsync(
				HttpMethod.Post, "purchase", JsonConvert.SerializeObject(body), CancellationToken.None);
			FireSupportPurchaseResponse result = JsonConvert.DeserializeObject<FireSupportPurchaseResponse>(responseBody);
			if (result == null)
			{
				fallback.Reason = "InvalidServerResponse";
				return fallback;
			}

			return result;
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource.LogWarning($"FireSupport purchase request failed. {ex}");
			fallback.Reason = "RequestFailed";
			return fallback;
		}
		finally
		{
			EndProfileMutation();
		}
	}

	public static UniTask<FireSupportPurchaseResponse> ConsumeAuthorizationAsync(
		ESupportType supportType,
		string requestId,
		int clientKnownRevision)
	{
		return SendAuthorizationActionAsync("ConsumeAuthorization", supportType, requestId, clientKnownRevision);
	}

	public static UniTask<FireSupportPurchaseResponse> RefundAuthorizationAsync(
		ESupportType supportType,
		string requestId,
		int clientKnownRevision)
	{
		return SendAuthorizationActionAsync("RefundAuthorization", supportType, requestId, clientKnownRevision);
	}

	public static UniTask<FireSupportPurchaseResponse> CommitAuthorizationAsync(
		ESupportType supportType,
		string requestId,
		int clientKnownRevision)
	{
		return SendAuthorizationActionAsync("CommitAuthorization", supportType, requestId, clientKnownRevision);
	}

	private static async UniTask<FireSupportPurchaseResponse> SendAuthorizationActionAsync(
		string action,
		ESupportType supportType,
		string requestId,
		int clientKnownRevision)
	{
		var fallback = new FireSupportPurchaseResponse
		{
			Ok = false,
			Reason = "ServerConfigUnavailable",
			SupportType = supportType.ToString(),
			RequestId = requestId,
			ServerRevision = Math.Max(clientKnownRevision, s_hostPurchaseRevision)
		};

		BeginProfileMutation();
		try
		{
			var body = new FireSupportPurchaseRequest
			{
				Action = action,
				SessionId = GetLocalProfileId(),
				ProfileId = GetLocalProfileId(),
				SupportType = supportType.ToString(),
				RequestId = requestId,
				ClientKnownRevision = clientKnownRevision,
				Quantity = 1
			};
			string responseBody = await SendServerRequestAsync(
				HttpMethod.Post, "purchase", JsonConvert.SerializeObject(body), CancellationToken.None);
			FireSupportPurchaseResponse result = JsonConvert.DeserializeObject<FireSupportPurchaseResponse>(responseBody);
			if (result == null)
			{
				fallback.Reason = "InvalidServerResponse";
				return fallback;
			}

			return result;
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource.LogWarning($"FireSupport authorization {action} failed. {ex}");
			fallback.Reason = "RequestFailed";
			return fallback;
		}
		finally
		{
			EndProfileMutation();
		}
	}

	private static void SubscribeSetting<T>(ConfigEntry<T> entry)
	{
		if (entry != null)
		{
			entry.SettingChanged += OnServerConfigSettingChanged;
		}
	}

	private static void OnServerConfigSettingChanged(object sender, EventArgs args)
	{
		string key = sender is ConfigEntryBase entry
			? $"{entry.Definition.Section}/{entry.Definition.Key}"
			: "<unknown>";
		RestartRefresh($"setting changed {key}");
	}

	private static void RestartRefresh(string reason)
	{
		StopRefresh();
		if (!ShouldFetchPlayerState())
		{
			return;
		}

		if (!ShouldApplyLocalGlobalSettings())
		{
			ClearServerGlobalOverrides(notify: false);
		}

		s_refreshCts = new CancellationTokenSource();
		RefreshLoop(reason, s_refreshCts.Token).Forget();
	}

	private static void StopRefresh()
	{
		s_refreshCts?.Cancel();
		s_refreshCts?.Dispose();
		s_refreshCts = null;
	}

	private static async UniTaskVoid RefreshLoop(string reason, CancellationToken cancellationToken)
	{
		TscDiagnostics.LogPayment($"TSC authenticated player-state refresh started: {reason}");
		while (!cancellationToken.IsCancellationRequested && ShouldFetchPlayerState())
		{
			await FetchConfigOnce(cancellationToken);

			int seconds = Math.Max(0, PluginSettings.ServerConfigRefreshSeconds?.Value ?? 10);
			if (seconds <= 0)
			{
				break;
			}

			await UniTask.Delay(TimeSpan.FromSeconds(seconds), cancellationToken: cancellationToken);
		}
	}

	private static async UniTask FetchConfigOnce(CancellationToken cancellationToken)
	{
		try
		{
			string route = BuildConfigRoute();
			TscDiagnostics.LogPayment($"TSC server config requested: {route}");
			long mutationEpochAtRequest = CaptureProfileMutationEpoch();
			string body = await SendServerRequestAsync(HttpMethod.Get, route, null, cancellationToken);
			RaidOpsFireSupportServerConfig snapshot = JsonConvert.DeserializeObject<RaidOpsFireSupportServerConfig>(body);
			if (snapshot == null)
			{
				HandleConfigFailure("empty or invalid JSON snapshot");
				return;
			}

			// Close the final gap between an uncancellable RequestHandler task
			// completing and installing its snapshot into the active raid.
			cancellationToken.ThrowIfCancellationRequested();
			ApplySnapshot(snapshot, mutationEpochAtRequest);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception ex)
		{
			HandleConfigFailure(ex.Message);
		}
	}

	private static void ApplySnapshot(
		RaidOpsFireSupportServerConfig snapshot,
		long mutationEpochAtRequest)
	{
		int revision = Math.Max(0, snapshot.Revision);
		bool playerStateIncluded = snapshot.PlayerStateIncluded || snapshot.StashRoubleBalance.HasValue;
		bool playerStateApplied = false;
		if (playerStateIncluded)
		{
			playerStateApplied = TryApplyPlayerState(
				snapshot,
				revision,
				mutationEpochAtRequest);
			if (!playerStateApplied)
			{
				TscDiagnostics.LogPayment(
					$"TSC player-state snapshot skipped as potentially stale; mutation overlapped config GET epoch={mutationEpochAtRequest}.");
			}
			else if (!snapshot.PlayerStateIncluded)
			{
				// v1.0.8 predates PlayerStateIncluded. Its server only populated
				// the nullable stash balance after resolving the authenticated
				// profile, preserving legacy authoritative-empty ledgers.
				TscDiagnostics.LogPayment(
					"TSC inferred authenticated player state from a legacy snapshot stash balance.");
			}

		}
		else
		{
			FireSupportPlugin.LogSource.LogWarning(
				"TSC config snapshot did not include authenticated player state; preserving the last known stash, persistence, and authorization state.");
		}

		if (ShouldApplyLocalGlobalSettings())
		{
			ApplyGlobalSettings(snapshot, revision);
		}

		FireSupportPayment.NotifySettingsChanged(snapshot);
		TscDiagnostics.LogPayment(
			$"TSC server snapshot loaded revision={revision} playerStateIncluded={playerStateIncluded} playerStateApplied={playerStateApplied} authorizations={(playerStateIncluded ? snapshot.Authorizations?.Count ?? 0 : -1)} stashBalance={(playerStateIncluded && snapshot.StashRoubleBalance.HasValue ? snapshot.StashRoubleBalance.Value.ToString() : "unknown")} globalsApplied={ShouldApplyLocalGlobalSettings()}");
	}

	private static bool TryApplyPlayerState(
		RaidOpsFireSupportServerConfig snapshot,
		int revision,
		long mutationEpochAtRequest)
	{
		lock (s_profileMutationGate)
		{
			if (s_profileMutationEpoch != mutationEpochAtRequest ||
			    s_profileMutationsInFlight != 0)
			{
				return false;
			}

			// Keep application atomic with mutation start/end so a POST cannot
			// begin after the freshness check but before the ledger is installed.
			ApplyPlayerState(snapshot, revision);
			return true;
		}
	}

	private static void ApplyPlayerState(RaidOpsFireSupportServerConfig snapshot, int revision)
	{
		FireSupportPayment.SetServerProfileState(
			revision,
			snapshot.StashRoubleBalance,
			snapshot.PurchasePersistence?.Enabled == true,
			snapshot.PurchasePersistence?.RefundFailedDispatch != false,
			snapshot.PurchasePersistence?.SpendCreditsBeforeCash != false,
			snapshot.PurchasePersistence?.AllowAutoPurchaseOnUse == true);
		if (snapshot.Authorizations != null)
		{
			// A present empty ledger is authoritative and must clear stale credits.
			FireSupportAuthorizations.SetFromServer(snapshot.Authorizations);
		}
	}

	private static void ApplyGlobalSettings(RaidOpsFireSupportServerConfig snapshot, int revision)
	{
		FireSupportPayment.SetServerConfigGlobals(
			GetPrice(snapshot, "A10", ESupportType.Strafe),
			GetPrice(snapshot, "DoublePass", ESupportType.DoubleStrafe),
			GetPrice(snapshot, "Extraction", ESupportType.Extract),
			GetPrice(snapshot, "PriorityExfil", ESupportType.PriorityExfil),
			GetPrice(snapshot, "Uav", ESupportType.Uav),
			GetPrice(snapshot, "FocusedSweep", ESupportType.FocusedSweep),
			ParseEnum(snapshot.PaymentMode, FireSupportPayment.GetConfiguredPaymentMode()),
			ParseEnum(snapshot.PaymentSource, FireSupportPayment.GetConfiguredPaymentSource()));
		FireSupportServiceAvailability.SetServerConfigAvailability(
			GetEnabled(snapshot, "PriorityExfil", FireSupportServiceAvailability.GetConfiguredPriorityExfilEnabled()),
			GetEnabled(snapshot, "DoublePass", FireSupportServiceAvailability.GetConfiguredDoublePassEnabled()),
			GetEnabled(snapshot, "FocusedSweep", FireSupportServiceAvailability.GetConfiguredFocusedSweepEnabled()),
			revision);
		FireSupportTuningSettings.SetServerConfigTuning(
			(snapshot.DoublePass ?? new RaidOpsFireSupportServerConfig.A10Settings()).SecondPassDelaySeconds,
			(snapshot.Extraction ?? new RaidOpsFireSupportServerConfig.ExtractionSettings()).WaitTimeSeconds,
			(snapshot.PriorityExfil ?? new RaidOpsFireSupportServerConfig.ExtractionSettings()).WaitTimeSeconds,
			(snapshot.PriorityExfil ?? new RaidOpsFireSupportServerConfig.ExtractionSettings()).DispatchDelaySeconds,
			(snapshot.Extraction ?? new RaidOpsFireSupportServerConfig.ExtractionSettings()).ExtractTimeSeconds,
			(snapshot.Extraction ?? new RaidOpsFireSupportServerConfig.ExtractionSettings()).SpeedMultiplier,
			(snapshot.PriorityExfil ?? new RaidOpsFireSupportServerConfig.ExtractionSettings()).SpeedMultiplier,
			snapshot.RequestCooldownSeconds,
			revision);
		FireSupportServerConfigClient.ApplyUavSettings(snapshot, revision);
	}

	private static void ApplyUavSettings(RaidOpsFireSupportServerConfig snapshot, int revision)
	{
		RaidOpsFireSupportServerConfig.UavSettings uav = snapshot.Uav ?? new RaidOpsFireSupportServerConfig.UavSettings();
		RaidOpsFireSupportServerConfig.UavSettings focusedSweep = snapshot.FocusedSweep ?? new RaidOpsFireSupportServerConfig.UavSettings();
		UavReconSettings.SetServerConfigDuration(
			uav.DurationSeconds,
			uav.ScanIntervalSeconds,
			uav.RangeMeters,
			revision);
		UavReconSettings.SetServerConfigFocusedSweep(
			focusedSweep.DurationSeconds,
			focusedSweep.ScanIntervalSeconds,
			focusedSweep.RangeMeters,
			revision);
	}
	private static void HandleConfigFailure(string reason)
	{
		FireSupportPlugin.LogSource.LogWarning($"TSC authenticated player-state refresh failed: {reason}");
		if (!ShouldApplyLocalGlobalSettings())
		{
			// Fika host globals are a separate authority domain. A player-state
			// failure must not clear or mark those synchronized settings invalid.
			FireSupportPayment.NotifySettingsChanged(reason);
			return;
		}

		FireSupportPayment.MarkServerConfigUnavailable(reason);
		if (!ShouldRequireServerConfig())
		{
			ClearServerGlobalOverrides(notify: true);
		}
		else
		{
			FireSupportPayment.NotifySettingsChanged(reason);
		}
	}

	private static void ClearServerGlobalOverrides(bool notify)
	{
		FireSupportPayment.ClearServerGlobalConfig();
		FireSupportServiceAvailability.ClearServerConfigAvailability();
		FireSupportTuningSettings.ClearServerConfigTuning();
		UavReconSettings.ClearServerConfigDuration();
		if (notify)
		{
			FireSupportPayment.NotifySettingsChanged("TSC local server global settings cleared");
		}
	}

	private static int GetPrice(
		RaidOpsFireSupportServerConfig snapshot,
		string key,
		ESupportType supportType)
	{
		if (snapshot.Prices != null &&
		    snapshot.Prices.TryGetValue(key, out int value))
		{
			return value;
		}

		return FireSupportPayment.GetConfiguredCost(supportType);
	}

	private static bool GetEnabled(
		RaidOpsFireSupportServerConfig snapshot,
		string key,
		bool fallback)
	{
		return snapshot.Enabled != null &&
		       snapshot.Enabled.TryGetValue(key, out bool value)
			? value
			: fallback;
	}

	private static TEnum ParseEnum<TEnum>(string value, TEnum fallback)
		where TEnum : struct
	{
		return Enum.TryParse(value, ignoreCase: true, out TEnum parsed)
			? parsed
			: fallback;
	}

	private static bool ShouldFetchPlayerState()
	{
		// The /tsc/config route uses the game's authenticated SPT backend
		// connection. Per-profile ledger, persistence, and stash state therefore
		// remain safe and necessary even when the legacy URL toggle is false or a
		// Fika host owns all raid-global settings.
		return s_raidActive;
	}

	private static bool ShouldApplyLocalGlobalSettings()
	{
		return PluginSettings.UseServerConfigUrl?.Value == true &&
		       !s_globalSettingsSuppressedByFikaClient;
	}

	private static bool ShouldRequireServerConfig()
	{
		return PluginSettings.UseServerConfigUrl?.Value == true &&
		       PluginSettings.RequireServerConfigInFika?.Value == true;
	}

	private static void BeginProfileMutation()
	{
		lock (s_profileMutationGate)
		{
			s_profileMutationEpoch++;
			s_profileMutationsInFlight++;
		}
	}

	private static void EndProfileMutation()
	{
		lock (s_profileMutationGate)
		{
			s_profileMutationsInFlight--;
			s_profileMutationEpoch++;
		}
	}

	private static long CaptureProfileMutationEpoch()
	{
		lock (s_profileMutationGate)
		{
			return s_profileMutationEpoch;
		}
	}

	// All in-game TSC server calls go through SPT's RequestHandler, which uses the
	// game's own backend connection. That connection already points at the correct
	// TSC server for the host and for every Fika client on any network (LAN, Radmin
	// VPN, direct), and it carries the caller's session so the server charges the
	// right player's stash automatically. The Server Config URL is not used for
	// this and can be left at its default; a wrong value no longer matters.
	private static async UniTask<string> SendServerRequestAsync(
		HttpMethod method,
		string route,
		string jsonBody,
		CancellationToken cancellationToken)
	{
		string path = "/tsc/" + route;
		cancellationToken.ThrowIfCancellationRequested();
		string response = method == HttpMethod.Post
			? await RequestHandler.PostJsonAsync(path, jsonBody ?? string.Empty).AsUniTask()
			: await RequestHandler.GetJsonAsync(path).AsUniTask();
		// RequestHandler does not accept a CancellationToken. Recheck after its
		// task completes so a stopped refresh cannot apply an old raid's profile.
		cancellationToken.ThrowIfCancellationRequested();
		return response;
	}

	private static string BuildConfigRoute()
	{
		string profileId = GetLocalProfileId();
		if (string.IsNullOrWhiteSpace(profileId))
		{
			return "config";
		}

		string encodedProfileId = Uri.EscapeDataString(profileId.Trim());
		return $"config?profileId={encodedProfileId}&sessionId={encodedProfileId}";
	}

	private static string GetLocalProfileId()
	{
		// In raid the main player carries the active profile. Outside raid fall
		// back to the backend session profile so config polls still identify the
		// player; without an id the server cannot include the stash balance or
		// ledger credits in its response, and the phone showed carried-only
		// balances until the first in-raid sync completed.
		string raidProfileId = Singleton<GameWorld>.Instance?.MainPlayer?.ProfileId;
		if (!string.IsNullOrWhiteSpace(raidProfileId))
		{
			return raidProfileId;
		}

		try
		{
			EFT.Profile sessionProfile = SPT.Reflection.Utils.PatchConstants.BackEndSession?.Profile;
			return sessionProfile != null ? sessionProfile.Id : string.Empty;
		}
		catch
		{
			return string.Empty;
		}
	}
}
