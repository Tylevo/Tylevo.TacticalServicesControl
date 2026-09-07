using BepInEx.Configuration;
using Comfort.Common;
using Cysharp.Threading.Tasks;
using EFT;
using Newtonsoft.Json;
using SPT.Common.Http;
using System;
using System.Collections.Generic;
using System.Linq;
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

	public static bool IsFikaClientHostAuthorityActive =>
		s_globalSettingsSuppressedByFikaClient;

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
		FireSupportProgression.Clear();
		s_raidActive = true;
		RestartRefresh("raid started");
	}

	public static void OnRaidEnded()
	{
		FireSupportProgression.Clear();
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
		FireSupportProgression.Clear();
		FireSupportProgression.SetHostSupportsProgression(false);
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

	/// <summary>
	/// Identifies the authenticated backend session and PMC profile currently
	/// owned by RequestHandler. Menu work captures this value so a response from
	/// a prior login can never be installed into the next profile.
	/// </summary>
	public static string GetAuthenticatedSessionKey()
	{
		string sessionId = RequestHandler.SessionId?.Trim() ?? string.Empty;
		string profileId = GetLocalProfileId()?.Trim() ?? string.Empty;
		return string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(profileId)
			? string.Empty
			: $"{sessionId}|{profileId}";
	}

	public static string GetAuthenticatedProfileId()
	{
		return GetLocalProfileId()?.Trim() ?? string.Empty;
	}

	public static bool IsAuthenticatedProfile(string profileId)
	{
		return !string.IsNullOrWhiteSpace(profileId) &&
		       string.Equals(
			       GetLocalProfileId()?.Trim(),
			       profileId.Trim(),
			       StringComparison.Ordinal);
	}

	/// <summary>
	/// Clears all cached server/profile state when the main menu changes backend
	/// session. This is deliberately separate from raid start/end so the physical
	/// phone lifecycle remains unchanged.
	/// </summary>
	public static void ClearPreRaidSessionState()
	{
		if (s_raidActive)
		{
			FireSupportPlugin.LogSource.LogWarning(
				"TSC ignored a pre-raid session reset while a raid was active.");
			return;
		}

		lock (s_profileMutationGate)
		{
			// Invalidate any menu GET that began under the previous session.
			s_profileMutationEpoch++;
		}

		FireSupportAuthorizations.Reset();
		FireSupportProgression.Clear();
		FireSupportPayment.ClearServerProfileState();
		FireSupportPayment.NotifySettingsChanged("TSC pre-raid backend session changed");
	}

	/// <summary>
	/// Performs one authenticated menu refresh without starting the raid polling
	/// loop. Only the profile-scoped fields are installed; the menu renders prices
	/// and availability directly from the returned server snapshot.
	/// </summary>
	public static async UniTask<RaidOpsFireSupportServerConfig> FetchPreRaidSnapshotOnceAsync(
		string expectedSessionKey,
		CancellationToken cancellationToken)
	{
		if (s_raidActive)
		{
			throw new InvalidOperationException("Pre-raid TSC synchronization is unavailable during a raid.");
		}

		if (string.IsNullOrWhiteSpace(expectedSessionKey) ||
		    !string.Equals(expectedSessionKey, GetAuthenticatedSessionKey(), StringComparison.Ordinal))
		{
			throw new InvalidOperationException("The authenticated backend profile is unavailable or changed.");
		}

		string profileId = GetLocalProfileId();
		if (string.IsNullOrWhiteSpace(profileId))
		{
			throw new InvalidOperationException("The authenticated PMC profile is unavailable.");
		}

		await Uh60TransferFeeRecoveryStore.RetryMatchingProfileAsync(
			profileId,
			"pre-raid menu synchronization");

		// Full currency stacks are needed only for the native menu inventory;
		// periodic in-raid configuration polling remains aggregate-only.
		string route = BuildConfigRoute(profileId) + "&includeStashCurrencyState=true&includePurchaseHistory=true";
		long mutationEpochAtRequest = CaptureProfileMutationEpoch();
		TscDiagnostics.LogPayment($"TSC pre-raid player-state snapshot requested: {route}");
		string body = await SendServerRequestAsync(HttpMethod.Get, route, null, cancellationToken);
		RaidOpsFireSupportServerConfig snapshot =
			JsonConvert.DeserializeObject<RaidOpsFireSupportServerConfig>(body);
		if (snapshot == null)
		{
			throw new InvalidOperationException("The TSC server returned an empty or invalid configuration snapshot.");
		}
		if (!HasValidSnapshotCurrency(snapshot))
		{
			FireSupportProgression.Clear();
			throw new InvalidOperationException(
				"The TSC server payment currency is invalid. Select RUB, USD, or EUR in the dashboard.");
		}

		cancellationToken.ThrowIfCancellationRequested();
		if (!string.Equals(expectedSessionKey, GetAuthenticatedSessionKey(), StringComparison.Ordinal))
		{
			throw new InvalidOperationException("The authenticated backend profile changed during synchronization.");
		}

		// Menu purchasing intentionally requires the explicit new-server
		// presence contract; legacy inferred state is not safe enough to charge a
		// profile before a raid.
		if (!snapshot.PlayerStateIncluded)
		{
			FireSupportProgression.Clear();
			throw new InvalidOperationException(
				"The TSC server did not include authoritative player state.");
		}
		if (snapshot.PurchaseHistory != null && !snapshot.PurchaseHistory.IsValidFor(profileId))
		{
			// An invalid optional history must never expose another profile's receipts.
			snapshot.PurchaseHistory = null;
			FireSupportPlugin.LogSource.LogWarning("TSC ignored an invalid purchase history snapshot.");
		}

		if (!TryApplyPlayerState(snapshot, Math.Max(0, snapshot.Revision), mutationEpochAtRequest))
		{
			throw new InvalidOperationException(
				"The TSC player ledger changed while the menu snapshot was loading. Refresh and try again.");
		}

		FireSupportPayment.NotifySettingsChanged(snapshot);
		PaymentCurrency snapshotCurrency = GetSnapshotCurrency(snapshot);
		int? snapshotBalance = GetSnapshotStashBalance(snapshot, snapshotCurrency);
		TscDiagnostics.LogPayment(
			$"TSC pre-raid snapshot loaded revision={Math.Max(0, snapshot.Revision)} authorizations={snapshot.Authorizations?.Count ?? 0} currency={snapshotCurrency} stashBalance={(snapshotBalance.HasValue ? snapshotBalance.Value.ToString() : "unknown")}.");
		return snapshot;
	}

	public static UniTask<FireSupportPurchaseResponse> PurchaseAuthorizationAsync(
		ESupportType supportType,
		PaymentCurrency expectedCurrency,
		int clientKnownRevision)
	{
		return SendPurchaseRequestAsync(
			"BuyAuthorization",
			supportType,
			requestId: string.Empty,
			expectedSessionKey: string.Empty,
			expectedProfileId: string.Empty,
			expectedCost: null,
			expectedCurrency: expectedCurrency,
			clientKnownRevision: clientKnownRevision);
	}

	public static UniTask<FireSupportPurchaseResponse> PurchasePersistentAuthorizationAsync(
		ESupportType supportType,
		string requestId,
		string expectedSessionKey,
		string expectedProfileId,
		int expectedCost,
		PaymentCurrency expectedCurrency,
		int clientKnownRevision)
	{
		return SendPurchaseRequestAsync(
			"BuyPersistentAuthorization",
			supportType,
			requestId,
			expectedSessionKey,
			expectedProfileId,
			expectedCost,
			expectedCurrency,
			clientKnownRevision);
	}

	/// <summary>
	/// Tags only cargo that EFT has already moved into its canonical persistent
	/// transfer grid. A failed tag never rolls back or blocks the native
	/// transfer: the server intentionally falls back to the stock BTR sender.
	/// </summary>
	public static async UniTask<bool> TryMarkUh60TransferAsync(
		string profileId,
		IReadOnlyCollection<string> itemIds)
	{
		string normalizedProfileId = profileId?.Trim() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(normalizedProfileId) ||
		    !IsAuthenticatedProfile(normalizedProfileId) ||
		    itemIds == null ||
		    itemIds.Count == 0)
		{
			return false;
		}

		string[] distinctItemIds =
			itemIds
				.Where(itemId => !string.IsNullOrWhiteSpace(itemId))
				.Select(itemId => itemId.Trim())
				.Distinct(StringComparer.Ordinal)
				.Take(4096)
				.ToArray();
		if (distinctItemIds.Length == 0)
		{
			return false;
		}

		try
		{
			string requestBody = JsonConvert.SerializeObject(
				new
				{
					profileId = normalizedProfileId,
					itemIds = distinctItemIds
				});
			string responseBody = await SendServerRequestAsync(
				HttpMethod.Post,
				"uh60-transfer/mark",
				requestBody,
				CancellationToken.None);
			Uh60TransferMarkResponse response =
				JsonConvert.DeserializeObject<Uh60TransferMarkResponse>(
					responseBody);
			if (response?.Ok == true &&
			    response.AcceptedItemCount > 0)
			{
				return true;
			}

			FireSupportPlugin.LogSource?.LogWarning(
				$"UH-60 Pilot marker was declined ({response?.Reason ?? "invalid response"}); native delivery remains active.");
			return false;
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource?.LogWarning(
				$"UH-60 Pilot marker request failed; native delivery remains active. {ex.Message}");
			return false;
		}
	}

	internal static UniTask<FireSupportUh60TransferFeeResponse> PrepareUh60TransferFeeAsync(
		string profileId,
		string transactionId,
		int amountRoubles)
	{
		return SendUh60TransferFeeActionAsync(
			"Prepare",
			profileId,
			transactionId,
			amountRoubles);
	}

	internal static UniTask<FireSupportUh60TransferFeeResponse> CommitUh60TransferFeeAsync(
		string profileId,
		string transactionId,
		int amountRoubles)
	{
		return SendUh60TransferFeeActionAsync(
			"Commit",
			profileId,
			transactionId,
			amountRoubles);
	}

	internal static UniTask<FireSupportUh60TransferFeeResponse> RefundUh60TransferFeeAsync(
		string profileId,
		string transactionId,
		int amountRoubles)
	{
		return SendUh60TransferFeeActionAsync(
			"Refund",
			profileId,
			transactionId,
			amountRoubles);
	}

	internal static UniTask<FireSupportUh60TransferFeeResponse> GetUh60TransferFeeStatusAsync(
		string profileId,
		string transactionId,
		int amountRoubles)
	{
		return SendUh60TransferFeeActionAsync(
			"Status",
			profileId,
			transactionId,
			amountRoubles);
	}

	private static async UniTask<FireSupportUh60TransferFeeResponse> SendUh60TransferFeeActionAsync(
		string action,
		string profileId,
		string transactionId,
		int amountRoubles)
	{
		string normalizedProfileId = profileId?.Trim() ?? string.Empty;
		string normalizedTransactionId = transactionId?.Trim() ?? string.Empty;
		var fallback = new FireSupportUh60TransferFeeResponse
		{
			Ok = false,
			Reason = "ServerConfigUnavailable",
			State = string.Empty,
			TransactionId = normalizedTransactionId,
			AmountRoubles = amountRoubles
		};

		if (string.IsNullOrWhiteSpace(normalizedProfileId) ||
		    !IsAuthenticatedProfile(normalizedProfileId))
		{
			fallback.Reason = "ProfileSessionChanged";
			return fallback;
		}

		if (string.IsNullOrWhiteSpace(normalizedTransactionId) ||
		    amountRoubles < 0)
		{
			fallback.Reason = "InvalidRequest";
			return fallback;
		}

		BeginProfileMutation();
		try
		{
			var request = new FireSupportUh60TransferFeeRequest
			{
				Action = action,
				ProfileId = normalizedProfileId,
				TransactionId = normalizedTransactionId,
				AmountRoubles = amountRoubles
			};
			string responseBody = await SendServerRequestAsync(
				HttpMethod.Post,
				"uh60-transfer/fee",
				JsonConvert.SerializeObject(request),
				CancellationToken.None);
			FireSupportUh60TransferFeeResponse response =
				JsonConvert.DeserializeObject<FireSupportUh60TransferFeeResponse>(
					responseBody);
			if (response == null ||
			    string.IsNullOrWhiteSpace(response.TransactionId) ||
			    !string.Equals(
				    response.TransactionId,
				    normalizedTransactionId,
				    StringComparison.Ordinal) ||
			    response.AmountRoubles != amountRoubles)
			{
				fallback.Reason = "InvalidServerResponse";
				return fallback;
			}

			FireSupportPayment.ApplyAuthenticatedStashBalance(
				PaymentCurrency.RUB,
				response.StashRoubleBalance,
				$"UH-60 cargo fee {action}");
			return response;
		}
		catch (Exception ex)
		{
			// A legacy TSC server has no /uh60-transfer/fee route. Its 404
			// reaches this path and deliberately fails Prepare before EFT's
			// native purchase can be invoked.
			FireSupportPlugin.LogSource?.LogWarning(
				$"UH-60 cargo transfer fee {action} request failed closed. {ex.Message}");
			fallback.Reason = "RequestFailed";
			return fallback;
		}
		finally
		{
			EndProfileMutation();
		}
	}

	private static async UniTask<FireSupportPurchaseResponse> SendPurchaseRequestAsync(
		string action,
		ESupportType supportType,
		string requestId,
		string expectedSessionKey,
		string expectedProfileId,
		int? expectedCost,
		PaymentCurrency expectedCurrency,
		int clientKnownRevision)
	{
		expectedCurrency = PaymentCurrencyInfo.Normalize(expectedCurrency);
		var fallback = new FireSupportPurchaseResponse
		{
			Ok = false,
			Reason = "ServerConfigUnavailable",
			SupportType = supportType.ToString(),
			Cost = FireSupportPayment.GetActiveCost(supportType),
			PaymentSource = nameof(PaymentSource.StashRoubles),
			Currency = PaymentCurrencyInfo.GetCode(expectedCurrency),
			NewBalance = FireSupportPayment.GetEffectiveBalance(),
			AuthorizationGranted = false,
			ServerRevision = Math.Max(clientKnownRevision, s_hostPurchaseRevision),
			RequestId = requestId ?? string.Empty
		};

		BeginProfileMutation();
		try
		{
			bool persistentPurchase =
				string.Equals(action, "BuyPersistentAuthorization", StringComparison.OrdinalIgnoreCase);
			if (persistentPurchase && string.IsNullOrWhiteSpace(requestId))
			{
				fallback.Reason = "InvalidRequestId";
				return fallback;
			}

			if (persistentPurchase &&
			    (string.IsNullOrWhiteSpace(expectedSessionKey) ||
			     string.IsNullOrWhiteSpace(expectedProfileId) ||
			     !string.Equals(
				     expectedSessionKey,
				     GetAuthenticatedSessionKey(),
				     StringComparison.Ordinal) ||
			     !IsAuthenticatedProfile(expectedProfileId)))
			{
				fallback.Reason = "ProfileSessionChanged";
				return fallback;
			}

			// Persistent menu requests must retain the profile captured when the
			// user clicked. If RequestHandler switches sessions after this check,
			// the old profile in the body will fail server-side auth/profile
			// validation instead of charging the newly selected profile.
			string profileId = persistentPurchase
				? expectedProfileId.Trim()
				: GetLocalProfileId();
			if (string.IsNullOrWhiteSpace(profileId))
			{
				fallback.Reason = "ProfileNotFound";
				return fallback;
			}

			var body = new FireSupportPurchaseRequest
			{
				Action = action,
				SessionId = profileId,
				ProfileId = profileId,
				SupportType = supportType.ToString(),
				RequestId = requestId ?? string.Empty,
				ClientKnownRevision = clientKnownRevision,
				ExpectedCost = expectedCost,
				ExpectedCurrency = PaymentCurrencyInfo.GetCode(expectedCurrency),
				Quantity = 1
			};
			if (persistentPurchase &&
			    (!string.Equals(
				     expectedSessionKey,
				     GetAuthenticatedSessionKey(),
				     StringComparison.Ordinal) ||
			     !IsAuthenticatedProfile(profileId)))
			{
				fallback.Reason = "ProfileSessionChanged";
				return fallback;
			}

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
			FireSupportPlugin.LogSource.LogWarning($"FireSupport purchase request {action} failed. {ex}");
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
		int clientKnownRevision,
		string expectedSessionKey,
		string expectedProfileId)
	{
		return SendAuthorizationActionAsync(
			"ConsumeAuthorization",
			supportType,
			requestId,
			clientKnownRevision,
			expectedSessionKey,
			expectedProfileId);
	}

	public static UniTask<FireSupportPurchaseResponse> RefundAuthorizationAsync(
		ESupportType supportType,
		string requestId,
		int clientKnownRevision,
		string expectedSessionKey,
		string expectedProfileId)
	{
		return SendAuthorizationActionAsync(
			"RefundAuthorization",
			supportType,
			requestId,
			clientKnownRevision,
			expectedSessionKey,
			expectedProfileId);
	}

	public static UniTask<FireSupportPurchaseResponse> CommitAuthorizationAsync(
		ESupportType supportType,
		string requestId,
		int clientKnownRevision,
		string expectedSessionKey,
		string expectedProfileId)
	{
		return SendAuthorizationActionAsync(
			"CommitAuthorization",
			supportType,
			requestId,
			clientKnownRevision,
			expectedSessionKey,
			expectedProfileId);
	}

	private static async UniTask<FireSupportPurchaseResponse> SendAuthorizationActionAsync(
		string action,
		ESupportType supportType,
		string requestId,
		int clientKnownRevision,
		string expectedSessionKey,
		string expectedProfileId)
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
			if (string.IsNullOrWhiteSpace(expectedSessionKey) ||
			    string.IsNullOrWhiteSpace(expectedProfileId) ||
			    !string.Equals(
				    expectedSessionKey,
				    GetAuthenticatedSessionKey(),
				    StringComparison.Ordinal) ||
			    !IsAuthenticatedProfile(expectedProfileId))
			{
				fallback.Reason = "ProfileSessionChanged";
				return fallback;
			}

			var body = new FireSupportPurchaseRequest
			{
				Action = action,
				SessionId = expectedProfileId,
				ProfileId = expectedProfileId,
				SupportType = supportType.ToString(),
				RequestId = requestId,
				ClientKnownRevision = clientKnownRevision,
				Quantity = 1
			};
			if (!string.Equals(
				    expectedSessionKey,
				    GetAuthenticatedSessionKey(),
				    StringComparison.Ordinal) ||
			    !IsAuthenticatedProfile(expectedProfileId))
			{
				fallback.Reason = "ProfileSessionChanged";
				return fallback;
			}

			string responseBody = await SendServerRequestAsync(
				HttpMethod.Post, "purchase", JsonConvert.SerializeObject(body), CancellationToken.None);
			FireSupportPurchaseResponse result = JsonConvert.DeserializeObject<FireSupportPurchaseResponse>(responseBody);
			if (result == null)
			{
				fallback.Reason = "InvalidServerResponse";
				return fallback;
			}

			if (!string.Equals(
				    expectedSessionKey,
				    GetAuthenticatedSessionKey(),
				    StringComparison.Ordinal) ||
			    !IsAuthenticatedProfile(expectedProfileId))
			{
				fallback.Reason = "ProfileSessionChanged";
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
			string expectedSessionKey = GetAuthenticatedSessionKey();
			string profileId = GetLocalProfileId();
			if (!string.IsNullOrWhiteSpace(profileId))
			{
				await Uh60TransferFeeRecoveryStore
					.RetryMatchingProfileAsync(
						profileId,
						"in-raid server synchronization");
			}

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
			if (string.IsNullOrWhiteSpace(expectedSessionKey) ||
			    !string.Equals(expectedSessionKey, GetAuthenticatedSessionKey(), StringComparison.Ordinal))
			{
				FireSupportProgression.Clear();
				return;
			}
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
		if (!snapshot.PlayerStateIncluded || snapshot.UplinkUnlocked != true ||
		    !FireSupportProgressionState.IsValidPermit(snapshot.ProgressionPermit))
		{
			FireSupportProgression.Clear();
		}
		if (!HasValidSnapshotCurrency(snapshot))
		{
			FireSupportProgression.Clear();
			if (ShouldApplyLocalGlobalSettings())
			{
				ClearServerGlobalOverrides(notify: false);
			}
			FireSupportPayment.MarkServerPaymentCurrencyInvalid(
				$"schema={snapshot.ConfigSchemaVersion}, value={snapshot.PaymentCurrency ?? "<missing>"}");
			FireSupportPayment.NotifySettingsChanged(snapshot);
			return;
		}

		int revision = Math.Max(0, snapshot.Revision);
		bool playerStateIncluded =
			snapshot.PlayerStateIncluded ||
			snapshot.StashCurrencyBalance.HasValue ||
			snapshot.StashRoubleBalance.HasValue;
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
			FireSupportProgression.Clear();
			FireSupportPlugin.LogSource.LogWarning(
				"TSC config snapshot did not include authenticated player state; preserving the last known stash, persistence, and authorization state.");
		}

		if (ShouldApplyLocalGlobalSettings())
		{
			ApplyGlobalSettings(snapshot, revision);
		}

		FireSupportPayment.NotifySettingsChanged(snapshot);
		PaymentCurrency snapshotCurrency = GetSnapshotCurrency(snapshot);
		int? snapshotBalance = GetSnapshotStashBalance(snapshot, snapshotCurrency);
		TscDiagnostics.LogPayment(
			$"TSC server snapshot loaded revision={revision} playerStateIncluded={playerStateIncluded} playerStateApplied={playerStateApplied} authorizations={(playerStateIncluded ? snapshot.Authorizations?.Count ?? 0 : -1)} currency={snapshotCurrency} stashBalance={(playerStateIncluded && snapshotBalance.HasValue ? snapshotBalance.Value.ToString() : "unknown")} globalsApplied={ShouldApplyLocalGlobalSettings()}");
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
		FireSupportProgression.ApplySnapshot(GetAuthenticatedSessionKey(), snapshot);
		PaymentCurrency currency = GetSnapshotCurrency(snapshot);
		FireSupportPayment.SetServerProfileState(
			revision,
			GetSnapshotStashBalance(snapshot, currency),
			currency,
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
			ParseEnum(snapshot.PaymentSource, FireSupportPayment.GetConfiguredPaymentSource()),
			GetSnapshotCurrency(snapshot));
		FireSupportServiceAvailability.SetServerConfigAvailability(
			GetEnabled(snapshot, "PriorityExfil", FireSupportServiceAvailability.GetConfiguredPriorityExfilEnabled()),
			GetEnabled(snapshot, "DoublePass", FireSupportServiceAvailability.GetConfiguredDoublePassEnabled()),
			GetEnabled(snapshot, "FocusedSweep", FireSupportServiceAvailability.GetConfiguredFocusedSweepEnabled()),
			revision);
		FireSupportTuningSettings.SetServerConfigTuning(
			(snapshot.DoublePass ?? new RaidOpsFireSupportServerConfig.A10Settings()).SecondPassDelaySeconds,
			(snapshot.Extraction ?? new RaidOpsFireSupportServerConfig.ExtractionSettings()).DispatchDelaySeconds,
			(snapshot.Extraction ?? new RaidOpsFireSupportServerConfig.ExtractionSettings()).WaitTimeSeconds,
			(snapshot.Extraction ?? new RaidOpsFireSupportServerConfig.ExtractionSettings()).ExtractTimeSeconds,
			(snapshot.Extraction ?? new RaidOpsFireSupportServerConfig.ExtractionSettings()).SpeedMultiplier,
			(snapshot.PriorityExfil ?? new RaidOpsFireSupportServerConfig.CargoSettings()).DispatchDelaySeconds,
			(snapshot.PriorityExfil ?? new RaidOpsFireSupportServerConfig.CargoSettings()).WaitTimeSeconds,
			(snapshot.PriorityExfil ?? new RaidOpsFireSupportServerConfig.CargoSettings()).SpeedMultiplier,
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
		FireSupportProgression.Clear();
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

	public static PaymentCurrency GetSnapshotCurrency(
		RaidOpsFireSupportServerConfig snapshot)
	{
		return PaymentCurrencyInfo.Parse(
			snapshot?.PaymentCurrency,
			PaymentCurrency.RUB);
	}

	private static bool HasValidSnapshotCurrency(
		RaidOpsFireSupportServerConfig snapshot)
	{
		if (snapshot == null)
		{
			return false;
		}

		if (PaymentCurrencyInfo.TryParse(snapshot.PaymentCurrency, out _))
		{
			return true;
		}

		// Servers predating config schema 3 had no currency field and were
		// unambiguously RUB-only. Current-schema omissions are invalid.
		return snapshot.ConfigSchemaVersion < 3 &&
		       string.IsNullOrWhiteSpace(snapshot.PaymentCurrency);
	}

	public static int? GetSnapshotStashBalance(
		RaidOpsFireSupportServerConfig snapshot,
		PaymentCurrency currency)
	{
		if (snapshot == null)
		{
			return null;
		}

		PaymentCurrency normalizedCurrency =
			PaymentCurrencyInfo.Normalize(currency);
		if (normalizedCurrency != GetSnapshotCurrency(snapshot))
		{
			// The generic balance is denominated in the snapshot's one selected
			// currency. Prepared retries can intentionally quote an older
			// currency, but the current snapshot cannot supply that old balance.
			return null;
		}

		if (snapshot.StashCurrencyBalance.HasValue)
		{
			return snapshot.StashCurrencyBalance;
		}

		// A legacy server's only balance field is explicitly RUB-denominated.
		// Never reinterpret it as dollars or euros.
		return normalizedCurrency == PaymentCurrency.RUB
			? snapshot.StashRoubleBalance
			: null;
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
	public static async UniTask<FireSupportProgressionVerifyResponse> VerifyProgressionPermitAsync(
		string permit,
		string requesterProfileId,
		CancellationToken cancellationToken)
	{
		var denied = new FireSupportProgressionVerifyResponse { Reason = "ProgressionPermitInvalid" };
		if (!FireSupportProgressionState.IsValidPermit(permit) || string.IsNullOrWhiteSpace(requesterProfileId)) return denied;
		try
		{
			string body = await SendServerRequestAsync(HttpMethod.Post, "progression/verify",
				JsonConvert.SerializeObject(new FireSupportProgressionVerifyRequest
				{
					Permit = permit,
					RequesterProfileId = requesterProfileId
				}), cancellationToken);
			return JsonConvert.DeserializeObject<FireSupportProgressionVerifyResponse>(body) ?? denied;
		}
		catch (OperationCanceledException) { throw; }
		catch (Exception)
		{
			// Never log a profile's capability token or authorize on backend failure.
			denied.Reason = "ProgressionVerificationUnavailable";
			return denied;
		}
	}

	/// <summary>Every manual payment verifies fresh server permission, including free/local modes.</summary>
	public static async UniTask<bool> EnsureLocalProgressionVerifiedAsync()
	{
		string sessionKey = GetAuthenticatedSessionKey();
		string profileId = GetAuthenticatedProfileId();
		if (string.IsNullOrWhiteSpace(sessionKey)) { FireSupportProgression.Clear(); return false; }
		for (int attempt = 0; attempt < 2; attempt++)
		{
			if (!FireSupportProgression.UplinkUnlocked)
			{
				await FetchConfigOnce(CancellationToken.None);
			}
			if (!string.Equals(sessionKey, GetAuthenticatedSessionKey(), StringComparison.Ordinal)) return false;
			if (!string.IsNullOrEmpty(FireSupportProgression.RestrictionReason)) return false;
			FireSupportProgressionVerifyResponse result = await VerifyProgressionPermitAsync(
				FireSupportProgression.Permit, profileId, CancellationToken.None);
			if (!string.Equals(sessionKey, GetAuthenticatedSessionKey(), StringComparison.Ordinal)) return false;
			if (result.Ok) return true;
			FireSupportProgression.Clear();
			if (result.Reason != "ProgressionPermitInvalid") break;
		}
		FireSupportPayment.NotifySettingsChanged("TSC progression verification required");
		return false;
	}

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
		return BuildConfigRoute(GetLocalProfileId());
	}

	private static string BuildConfigRoute(string profileId)
	{
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

	private sealed class Uh60TransferMarkResponse
	{
		public bool Ok { get; set; }
		public int AcceptedItemCount { get; set; }
		public string Reason { get; set; } = string.Empty;
	}

}
