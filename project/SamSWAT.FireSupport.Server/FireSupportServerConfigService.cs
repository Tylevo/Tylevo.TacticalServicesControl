using SamSWAT.FireSupport.ArysReloaded.Unity;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils.Cloners;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using IOPath = System.IO.Path;

namespace SamSWAT.FireSupport.ArysReloaded;

[Injectable]
public sealed class FireSupportServerConfigService(
	ISptLogger<FireSupportServerConfigService> logger,
	ProfileHelper profileHelper,
	SaveServer saveServer,
	FireSupportAuthorizationLedger authorizationLedger,
	ICloner cloner)
{
	private const string ConfigFileName = "tsc-config.json";
	private const string LegacyConfigFileName = "raidops-firesupport.json";
	private const string AdminTokenFileName = "tsc-admin-token.txt";
	private const string LegacyAdminTokenFileName = "raidops-firesupport-admin-token.txt";
	private const string AdminTokenEnvironmentVariable = "TSC_ADMIN_TOKEN";
	private const string LegacyAdminTokenEnvironmentVariable = "RAIDOPS_FIRESUPPORT_ADMIN_TOKEN";
	private const string RoubleTemplateId = "5449016a4bdc2d6f028b456f";
	private static readonly TimeSpan s_purchaseRateLimitWindow = TimeSpan.FromSeconds(2);

	private static readonly JsonSerializerOptions s_jsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true
	};

	private readonly object _gate = new();
	private readonly SemaphoreSlim _purchaseGate = new(1, 1);
	private RaidOpsFireSupportServerConfig _config = CreateDefaultConfig();
	private readonly Dictionary<string, DateTimeOffset> _purchaseRateLimits = new(StringComparer.OrdinalIgnoreCase);
	private string _configPath = string.Empty;
	private string _adminTokenPath = string.Empty;
	private string _webRootPath = string.Empty;
	private string _storagePath = string.Empty;
	private DateTimeOffset _lastLoadedUtc;
	private DateTimeOffset _lastSavedUtc;

	public void Initialize(string pathToMod)
	{
		string configDirectory = IOPath.Combine(pathToMod, "config");
		Directory.CreateDirectory(configDirectory);
		_configPath = IOPath.Combine(configDirectory, ConfigFileName);
		MigrateLegacyConfigPath(configDirectory);
		_adminTokenPath = IOPath.Combine(configDirectory, AdminTokenFileName);
		MigrateLegacyAdminTokenPath(configDirectory);
		_webRootPath = IOPath.Combine(pathToMod, "web");
		_storagePath = IOPath.Combine(pathToMod, "storage");
		authorizationLedger.Initialize(_storagePath);
		EnsureAdminToken();

		lock (_gate)
		{
			_config = LoadConfig();
			NormalizeConfig(_config);
			if (_config.Revision <= 0)
			{
				_config.Revision = 1;
			}

			SaveConfig(_config);
		}

		logger.Success("TSC server config ready.");
		logger.Success("TSC Dashboard ready: /tsc/admin");
	}

	public string WebRootPath => _webRootPath;

	private void MigrateLegacyConfigPath(string configDirectory)
	{
		string legacyPath = IOPath.Combine(configDirectory, LegacyConfigFileName);
		if (File.Exists(_configPath) || !File.Exists(legacyPath))
		{
			return;
		}

		try
		{
			File.Copy(legacyPath, _configPath, overwrite: false);
			logger.Warning("TSC config migrated from legacy RaidOps config filename.");
		}
		catch (Exception ex)
		{
			logger.Warning($"TSC config migration skipped. {ex.Message}");
		}
	}

	private void MigrateLegacyAdminTokenPath(string configDirectory)
	{
		string legacyPath = IOPath.Combine(configDirectory, LegacyAdminTokenFileName);
		if (File.Exists(_adminTokenPath) || !File.Exists(legacyPath))
		{
			return;
		}

		try
		{
			File.Copy(legacyPath, _adminTokenPath, overwrite: false);
			logger.Warning("TSC admin token migrated from legacy filename.");
		}
		catch (Exception ex)
		{
			logger.Warning($"TSC admin token migration skipped. {ex.Message}");
		}
	}

	public object GetSnapshot(MongoId sessionId, FireSupportPurchaseRequest? request = null)
	{
		RaidOpsFireSupportServerConfig snapshot;
		lock (_gate)
		{
			snapshot = CloneConfig(_config);
		}

		snapshot.PlayerStateIncluded = false;
		snapshot.StashRoubleBalance = null;
		snapshot.Authorizations = new Dictionary<string, int>();
		snapshot.PreparedPurchases = null;
		Dictionary<string, string>? preparedPurchases = null;
		if (TryResolveAuthenticatedProfile(
			    sessionId,
			    request,
			    out PmcData? pmc,
			    out MongoId saveSessionId,
			    out _))
		{
			snapshot.PlayerStateIncluded = true;
			snapshot.StashRoubleBalance = CountStashRoubles(pmc);
			string profileLedgerId = GetCanonicalProfileLedgerId(pmc, saveSessionId);
			preparedPurchases =
				authorizationLedger.GetPreparedPersistentPurchaseRequestIds(profileLedgerId);
			if (snapshot.PurchasePersistence?.Enabled == true)
			{
				snapshot.Authorizations = authorizationLedger.GetCredits(
					profileLedgerId,
					snapshot.PurchasePersistence.PendingUseTimeoutSeconds);
			}
		}

		return preparedPurchases == null
			? snapshot
			: CreateAuthenticatedSnapshotPayload(snapshot, preparedPurchases);
	}

	public async Task<FireSupportPurchaseResponse> TryPurchaseAsync(
		MongoId sessionId,
		FireSupportPurchaseRequest request)
	{
		await _purchaseGate.WaitAsync();
		try
		{
			return await TryPurchaseSerializedAsync(sessionId, request);
		}
		finally
		{
			_purchaseGate.Release();
		}
	}

	private async Task<FireSupportPurchaseResponse> TryPurchaseSerializedAsync(
		MongoId sessionId,
		FireSupportPurchaseRequest request)
	{
		const string persistentPurchaseAction = "BuyPersistentAuthorization";
		bool requiresPersistentPurchase = string.Equals(
			request.Action,
			persistentPurchaseAction,
			StringComparison.OrdinalIgnoreCase);
		string purchaseRequestId = requiresPersistentPurchase
			? request.RequestId?.Trim() ?? string.Empty
			: string.Empty;
		RaidOpsFireSupportServerConfig config;
		lock (_gate)
		{
			config = CloneConfig(_config);
		}

		var response = new FireSupportPurchaseResponse
		{
			Ok = false,
			Reason = string.Empty,
			SupportType = request.SupportType,
			ServerRevision = config.Revision,
			PaymentSource = config.PaymentSource,
			RequestId = purchaseRequestId.Length <= FireSupportAuthorizationLedger.MaxPersistentPurchaseRequestIdLength
				? purchaseRequestId
				: string.Empty
		};
		PmcData? pmc;
		MongoId saveSessionId;
		string profileDenialReason;
		lock (_gate)
		{
			if (!TryResolveProfileForPurchase(
				    sessionId,
				    request,
				    out pmc,
				    out saveSessionId,
				    out profileDenialReason))
			{
				response.Reason = profileDenialReason;
				return response;
			}
		}

		if (requiresPersistentPurchase &&
		    (string.IsNullOrWhiteSpace(purchaseRequestId) ||
		     purchaseRequestId.Length > FireSupportAuthorizationLedger.MaxPersistentPurchaseRequestIdLength))
		{
			response.Reason = "InvalidRequestId";
			return response;
		}

		response.RequestId = purchaseRequestId;

		if (!TryResolveSupportType(request.SupportType, out ESupportType supportType))
		{
			response.Reason = "InvalidSupportType";
			return response;
		}

		response.SupportType = supportType.ToString();
		response.Cost = GetPrice(config, supportType);
		int requestedQuantity = request.Quantity <= 0 ? 1 : request.Quantity;
		if (requestedQuantity != 1)
		{
			response.Reason = "InvalidQuantity";
			return response;
		}

		PaymentSource paymentSource = ParseEnum(config.PaymentSource, PaymentSource.CarriedRoubles);
		response.PaymentSource = paymentSource.ToString();

		int newBalance;
		int chargedFromStash = 0;
		string profileLedgerId = string.Empty;
		List<Item>? inventorySnapshot = null;
		DateTimeOffset purchaseTime = DateTimeOffset.UtcNow;
		bool preparedRecovery = false;
		string expectedPostDebitFingerprint = string.Empty;
		lock (_gate)
		{
			profileLedgerId = GetCanonicalProfileLedgerId(pmc, saveSessionId);
			if (requiresPersistentPurchase)
			{
				PersistentPurchaseReplayStatus replayStatus =
					authorizationLedger.GetPersistentPurchaseReplay(
						profileLedgerId,
						supportType,
						requestedQuantity,
						purchaseRequestId,
						out Dictionary<string, int> replayAuthorizations,
						out FireSupportPersistentPurchaseRecord? journalEntry);
				if (replayStatus == PersistentPurchaseReplayStatus.Accepted)
				{
					response.NewBalance = CountStashRoubles(pmc);
					response.AuthorizationsIncluded = true;
					response.Authorizations = replayAuthorizations;
					response.Ok = true;
					response.Reason = "AlreadyAccepted";
					response.Cost = journalEntry?.Price ?? response.Cost;
					response.AuthorizationGranted = true;
					response.ChargedFromStash = 0;
					return response;
				}

				if (replayStatus == PersistentPurchaseReplayStatus.Conflict)
				{
					response.NewBalance = CountStashRoubles(pmc);
					response.AuthorizationsIncluded = true;
					response.Authorizations = replayAuthorizations;
					response.Reason = "PurchaseRequestConflict";
					return response;
				}

				if (replayStatus == PersistentPurchaseReplayStatus.Prepared)
				{
					if (journalEntry == null)
					{
						response.Reason = "PersistentPurchasePending";
						response.NewBalance = CountStashRoubles(pmc);
						return response;
					}

					response.Cost = journalEntry.Price;
					int recoveryBalance = CountStashRoubles(pmc);
					string recoveryFingerprint = ComputeRoubleInventoryFingerprint(pmc);
					bool matchesPreDebitFingerprint = string.Equals(
						recoveryFingerprint,
						journalEntry.PreDebitFingerprint,
						StringComparison.OrdinalIgnoreCase);
					bool matchesExpectedPostDebitFingerprint = string.Equals(
						recoveryFingerprint,
						journalEntry.ExpectedPostDebitFingerprint,
						StringComparison.OrdinalIgnoreCase);
					bool finalizeWithoutDebit =
						journalEntry.Price == 0 && matchesExpectedPostDebitFingerprint;
					if (journalEntry.Price >= 0 &&
					    !finalizeWithoutDebit &&
					    matchesPreDebitFingerprint)
					{
						preparedRecovery = true;
						expectedPostDebitFingerprint = journalEntry.ExpectedPostDebitFingerprint;
						logger.Warning(
							$"TSC persistent purchase recovery resuming original-price debit sessionId={FormatLogId(saveSessionId)} supportType={supportType} requestId={FormatRequestId(purchaseRequestId)} balance={recoveryBalance}");
					}
					else if (finalizeWithoutDebit || matchesExpectedPostDebitFingerprint)
					{
						logger.Warning(
							$"TSC persistent purchase recovery detected the expected post-debit rouble inventory; finalizing without another charge sessionId={FormatLogId(saveSessionId)} supportType={supportType} requestId={FormatRequestId(purchaseRequestId)} balance={recoveryBalance}");

						bool recovered = authorizationLedger.TryFinalizePersistentPurchase(
							profileLedgerId,
							supportType,
							requestedQuantity,
							purchaseRequestId,
							out Dictionary<string, int> recoveredAuthorizations,
							out FireSupportPersistentPurchaseRecord? recoveredPurchase,
							out string recoveryReason);
						response.NewBalance = recoveryBalance;
						response.AuthorizationsIncluded = true;
						response.Authorizations = recoveredAuthorizations;
						response.Cost = recoveredPurchase?.Price ?? journalEntry.Price;
						if (!recovered)
						{
							logger.Error(
								$"TSC persistent purchase recovery finalize failed reason={recoveryReason} sessionId={FormatLogId(saveSessionId)} supportType={supportType} requestId={FormatRequestId(purchaseRequestId)}");
							response.Reason = "PersistentPurchasePending";
							return response;
						}

						MarkPurchaseAttempt(saveSessionId, supportType, DateTimeOffset.UtcNow);
						response.Ok = true;
						response.Reason = "AlreadyAccepted";
						response.AuthorizationGranted = true;
						response.ChargedFromStash = 0;
						return response;
					}
					else
					{
						logger.Warning(
							$"TSC persistent purchase recovery fingerprint mismatch; leaving purchase prepared sessionId={FormatLogId(saveSessionId)} supportType={supportType} requestId={FormatRequestId(purchaseRequestId)} preBalance={journalEntry.PreDebitBalance} expectedPostBalance={journalEntry.ExpectedPostDebitBalance} currentBalance={recoveryBalance}");
						response.Reason = "PersistentPurchasePending";
						response.NewBalance = recoveryBalance;
						response.AuthorizationsIncluded = true;
						response.Authorizations = replayAuthorizations;
						return response;
					}
				}
				else if (config.PurchasePersistence?.Enabled != true)
				{
					response.Reason = "PurchasePersistenceDisabled";
					response.NewBalance = CountStashRoubles(pmc);
					return response;
				}
			}

			if (requiresPersistentPurchase &&
			    !preparedRecovery &&
			    request.ExpectedCost.HasValue &&
			    request.ExpectedCost.Value != response.Cost)
			{
				response.Reason = "PurchaseQuoteChanged";
				response.NewBalance = CountStashRoubles(pmc);
				logger.Warning(
					$"TSC persistent purchase quote changed sessionId={FormatLogId(saveSessionId)} supportType={supportType} requestId={FormatRequestId(purchaseRequestId)} expectedCost={request.ExpectedCost.Value} currentCost={response.Cost}");
				return response;
			}

			if (!preparedRecovery && !IsServiceEnabled(config, supportType))
			{
				response.Reason = "ServiceUnavailable";
				return response;
			}

			if (!preparedRecovery && !IsServerBackedPaymentSource(paymentSource))
			{
				response.Reason = "PaymentSourceNotServerBacked";
				return response;
			}

			if (!preparedRecovery && IsPurchaseRateLimited(saveSessionId, supportType, purchaseTime))
			{
				response.Reason = "RateLimited";
				response.NewBalance = CountStashRoubles(pmc);
				logger.Warning($"TSC purchase denied reason=RateLimited sessionId={FormatLogId(saveSessionId)} supportType={supportType}");
				return response;
			}

			if (!requiresPersistentPurchase && config.PurchasePersistence?.Enabled == true)
			{
				Dictionary<string, int> credits = authorizationLedger.GetCredits(
					profileLedgerId,
					config.PurchasePersistence.PendingUseTimeoutSeconds);
				string supportKey = GetSupportKey(supportType);
				int currentCredits = credits.TryGetValue(supportKey, out int count) ? Math.Max(0, count) : 0;
				if (currentCredits + requestedQuantity > config.PurchasePersistence.MaxStoredAuthorizationsPerService)
				{
					response.Reason = "AuthorizationLimitReached";
					response.NewBalance = CountStashRoubles(pmc);
					response.AuthorizationsIncluded = true;
					response.Authorizations = credits;
					return response;
				}
			}

			int stashBalance = CountStashRoubles(pmc);
			if (stashBalance < response.Cost)
			{
				response.Reason = "InsufficientRoubles";
				response.NewBalance = stashBalance;
				return response;
			}

			if (pmc.Inventory?.Items == null)
			{
				response.Reason = "ProfileInventoryUnavailable";
				return response;
			}

			inventorySnapshot = cloner.Clone(pmc.Inventory.Items);
			if (inventorySnapshot == null)
			{
				response.Reason = "PaymentSnapshotFailed";
				return response;
			}

			if (requiresPersistentPurchase && !preparedRecovery)
			{
				string preDebitFingerprint = ComputeRoubleInventoryFingerprint(pmc);
				if (!TryComputeExpectedPostDebitFingerprint(
					    pmc,
					    response.Cost,
					    out string projectedPostDebitFingerprint,
					    out int projectedCharge) ||
				    projectedCharge != response.Cost)
				{
					logger.Error(
						$"TSC persistent purchase could not project the exact stash mutation sessionId={FormatLogId(saveSessionId)} supportType={supportType} requestId={FormatRequestId(purchaseRequestId)} expectedCharge={response.Cost} projectedCharge={projectedCharge}");
					response.Reason = "PaymentMutationFailed";
					response.NewBalance = stashBalance;
					return response;
				}

				expectedPostDebitFingerprint = projectedPostDebitFingerprint;
				bool prepared = authorizationLedger.TryPreparePersistentPurchase(
					profileLedgerId,
					supportType,
					requestedQuantity,
					response.Cost,
					stashBalance,
					preDebitFingerprint,
					expectedPostDebitFingerprint,
					config.PurchasePersistence!.MaxStoredAuthorizationsPerService,
					purchaseRequestId,
					out Dictionary<string, int> preparedAuthorizations,
					out FireSupportPersistentPurchaseRecord? preparedPurchase,
					out string prepareReason);
				response.AuthorizationsIncluded = true;
				response.Authorizations = preparedAuthorizations;
				if (!prepared)
				{
					logger.Warning(
						$"TSC persistent purchase prepare failed reason={prepareReason} sessionId={FormatLogId(saveSessionId)} supportType={supportType} requestId={FormatRequestId(purchaseRequestId)}");
					response.Reason = string.Equals(prepareReason, "AlreadyPrepared", StringComparison.OrdinalIgnoreCase)
						? "PersistentPurchasePending"
						: prepareReason;
					response.NewBalance = stashBalance;
					return response;
				}

				response.Cost = preparedPurchase?.Price ?? response.Cost;
				expectedPostDebitFingerprint =
					preparedPurchase?.ExpectedPostDebitFingerprint ?? expectedPostDebitFingerprint;
			}

			string actualPostDebitFingerprint = string.Empty;
			try
			{
				chargedFromStash = DebitStashRoubles(pmc, response.Cost);
				newBalance = CountStashRoubles(pmc);
				if (requiresPersistentPurchase)
				{
					actualPostDebitFingerprint = ComputeRoubleInventoryFingerprint(pmc);
				}
			}
			catch (Exception ex)
			{
				pmc.Inventory.Items = inventorySnapshot;
				logger.Error($"TSC stash payment mutation failed sessionId={FormatLogId(saveSessionId)}", ex);
				bool cancelled = !requiresPersistentPurchase ||
				                 TryCancelPreparedPersistentPurchase(
					                 profileLedgerId,
					                 supportType,
					                 requestedQuantity,
					                 purchaseRequestId,
					                 saveSessionId,
					                 "payment mutation failure");
				response.Reason = cancelled ? "PaymentMutationFailed" : "PersistentPurchasePending";
				response.NewBalance = CountStashRoubles(pmc);
				return response;
			}

			if (chargedFromStash != response.Cost)
			{
				pmc.Inventory.Items = inventorySnapshot;
				bool cancelled = !requiresPersistentPurchase ||
				                 TryCancelPreparedPersistentPurchase(
					                 profileLedgerId,
					                 supportType,
					                 requestedQuantity,
					                 purchaseRequestId,
					                 saveSessionId,
					                 "incomplete payment mutation");
				response.Reason = cancelled ? "PaymentMutationFailed" : "PersistentPurchasePending";
				response.NewBalance = CountStashRoubles(pmc);
				return response;
			}

			if (requiresPersistentPurchase &&
			    !string.Equals(
				    actualPostDebitFingerprint,
				    expectedPostDebitFingerprint,
				    StringComparison.OrdinalIgnoreCase))
			{
				pmc.Inventory.Items = inventorySnapshot;
				logger.Error(
					$"TSC persistent purchase post-debit rouble inventory fingerprint mismatch sessionId={FormatLogId(saveSessionId)} supportType={supportType} requestId={FormatRequestId(purchaseRequestId)}");
				bool cancelled = TryCancelPreparedPersistentPurchase(
					profileLedgerId,
					supportType,
					requestedQuantity,
					purchaseRequestId,
					saveSessionId,
					"post-debit inventory fingerprint mismatch");
				response.Reason = cancelled ? "PaymentMutationFailed" : "PersistentPurchasePending";
				response.NewBalance = CountStashRoubles(pmc);
				return response;
			}
		}

		try
		{
			await saveServer.SaveProfileAsync(saveSessionId);
		}
		catch (Exception ex)
		{
			logger.Error($"TSC stash payment save failed sessionId={FormatLogId(saveSessionId)}", ex);
			bool rolledBack = await TryRollbackStashPaymentAsync(pmc, saveSessionId, inventorySnapshot, "profile save failure");
			bool cancelled = rolledBack &&
			                 (!requiresPersistentPurchase ||
			                  TryCancelPreparedPersistentPurchase(
				                  profileLedgerId,
				                  supportType,
				                  requestedQuantity,
				                  purchaseRequestId,
				                  saveSessionId,
				                  "profile save rollback"));
			response.Reason = requiresPersistentPurchase && !cancelled
				? "PersistentPurchasePending"
				: rolledBack ? "ProfileSaveFailed" : "PaymentRollbackFailed";
			response.NewBalance = CountStashRoubles(pmc);
			response.ChargedFromStash = rolledBack ? 0 : chargedFromStash;
			return response;
		}

		if (requiresPersistentPurchase)
		{
			bool finalized = authorizationLedger.TryFinalizePersistentPurchase(
				profileLedgerId,
				supportType,
				requestedQuantity,
				purchaseRequestId,
				out Dictionary<string, int> authorizations,
				out FireSupportPersistentPurchaseRecord? finalizedPurchase,
				out string ledgerReason);
			response.AuthorizationsIncluded = true;
			response.Authorizations = authorizations;
			response.Cost = finalizedPurchase?.Price ?? response.Cost;
			if (!finalized)
			{
				logger.Warning(
					$"TSC persistent purchase finalize failed reason={ledgerReason} sessionId={FormatLogId(saveSessionId)} supportType={supportType} requestId={FormatRequestId(purchaseRequestId)}");
				bool rolledBack = await TryRollbackStashPaymentAsync(
					pmc,
					saveSessionId,
					inventorySnapshot,
					"persistent purchase finalize failure");
				bool cancelled = rolledBack &&
				                 TryCancelPreparedPersistentPurchase(
					                 profileLedgerId,
					                 supportType,
					                 requestedQuantity,
					                 purchaseRequestId,
					                 saveSessionId,
					                 "persistent purchase finalize rollback");
				response.Ok = false;
				response.Reason = rolledBack && cancelled ? ledgerReason : "PersistentPurchasePending";
				response.NewBalance = CountStashRoubles(pmc);
				response.ChargedFromStash = rolledBack ? 0 : chargedFromStash;
				response.AuthorizationGranted = false;
				return response;
			}
		}
		else if (config.PurchasePersistence?.Enabled == true)
		{
			bool granted;
			Dictionary<string, int> authorizations;
			string ledgerReason;
			bool authorizationsIncluded;
			try
			{
				granted = authorizationLedger.TryGrant(
					profileLedgerId,
					supportType,
					requestedQuantity,
					response.Cost,
					config.PurchasePersistence.MaxStoredAuthorizationsPerService,
					config.PurchasePersistence.PendingUseTimeoutSeconds,
					out authorizations,
					out ledgerReason);
				authorizationsIncluded = !string.Equals(
					ledgerReason,
					"InvalidAuthorizationRequest",
					StringComparison.OrdinalIgnoreCase);
			}
			catch (Exception ex)
			{
				logger.Error($"TSC authorization ledger grant threw sessionId={FormatLogId(saveSessionId)} supportType={supportType}", ex);
				granted = false;
				authorizations = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
				ledgerReason = "AuthorizationLedgerSaveFailed";
				authorizationsIncluded = false;
			}

			if (!granted)
			{
				logger.Warning(
					$"TSC authorization ledger grant failed reason={ledgerReason} sessionId={FormatLogId(saveSessionId)} supportType={supportType}");
				bool rolledBack = await TryRollbackStashPaymentAsync(pmc, saveSessionId, inventorySnapshot, "authorization grant failure");
				response.Ok = false;
				response.Reason = rolledBack ? ledgerReason : "PaymentRollbackFailed";
				response.NewBalance = CountStashRoubles(pmc);
				response.ChargedFromStash = rolledBack ? 0 : chargedFromStash;
				response.AuthorizationGranted = false;
				if (authorizationsIncluded)
				{
					response.AuthorizationsIncluded = true;
					response.Authorizations = authorizations;
				}
				return response;
			}

			response.AuthorizationsIncluded = true;
			response.Authorizations = authorizations;
		}

		lock (_gate)
		{
			MarkPurchaseAttempt(saveSessionId, supportType, DateTimeOffset.UtcNow);
		}

		response.Ok = true;
		response.Reason = "Accepted";
		response.NewBalance = newBalance;
		response.ChargedFromStash = chargedFromStash;
		response.AuthorizationGranted = true;

		logger.Success(
			$"TSC authorization purchased: {supportType}. sessionId={FormatLogId(saveSessionId)} cost={response.Cost} chargedFromStash={chargedFromStash} newBalance={newBalance} revision={config.Revision}");
		return response;
	}

	private async Task<bool> TryRollbackStashPaymentAsync(
		PmcData pmc,
		MongoId saveSessionId,
		List<Item>? inventorySnapshot,
		string reason)
	{
		if (inventorySnapshot == null || pmc.Inventory == null)
		{
			logger.Error($"TSC stash payment rollback unavailable sessionId={FormatLogId(saveSessionId)} reason={reason}");
			return false;
		}

		try
		{
			pmc.Inventory.Items = inventorySnapshot;
			await saveServer.SaveProfileAsync(saveSessionId);
			logger.Warning($"TSC stash payment rolled back sessionId={FormatLogId(saveSessionId)} reason={reason}");
			return true;
		}
		catch (Exception ex)
		{
			logger.Error($"TSC stash payment rollback save failed sessionId={FormatLogId(saveSessionId)} reason={reason}", ex);
			return false;
		}
	}

	private bool TryCancelPreparedPersistentPurchase(
		string profileLedgerId,
		ESupportType supportType,
		int quantity,
		string requestId,
		MongoId saveSessionId,
		string context)
	{
		try
		{
			bool cancelled = authorizationLedger.TryCancelPreparedPersistentPurchase(
				profileLedgerId,
				supportType,
				quantity,
				requestId,
				out string cancellationReason);
			if (!cancelled)
			{
				logger.Error(
					$"TSC persistent purchase cancellation failed reason={cancellationReason} context={context} sessionId={FormatLogId(saveSessionId)} supportType={supportType} requestId={FormatRequestId(requestId)}");
			}

			return cancelled;
		}
		catch (Exception ex)
		{
			logger.Error(
				$"TSC persistent purchase cancellation threw context={context} sessionId={FormatLogId(saveSessionId)} supportType={supportType} requestId={FormatRequestId(requestId)}",
				ex);
			return false;
		}
	}

	public FireSupportPurchaseResponse TryConsumeAuthorization(
		MongoId sessionId,
		FireSupportPurchaseRequest request)
	{
		return TryMutateAuthorization(sessionId, request, AuthorizationMutation.Consume);
	}

	public FireSupportPurchaseResponse TryRefundAuthorization(
		MongoId sessionId,
		FireSupportPurchaseRequest request)
	{
		return TryMutateAuthorization(sessionId, request, AuthorizationMutation.Refund);
	}

	public FireSupportPurchaseResponse TryCommitAuthorization(
		MongoId sessionId,
		FireSupportPurchaseRequest request)
	{
		return TryMutateAuthorization(sessionId, request, AuthorizationMutation.Commit);
	}

	private FireSupportPurchaseResponse TryMutateAuthorization(
		MongoId sessionId,
		FireSupportPurchaseRequest request,
		AuthorizationMutation mutation)
	{
		RaidOpsFireSupportServerConfig config;
		lock (_gate)
		{
			config = CloneConfig(_config);
		}

		var response = new FireSupportPurchaseResponse
		{
			Ok = false,
			Reason = string.Empty,
			SupportType = request.SupportType,
			ServerRevision = config.Revision,
			RequestId = request.RequestId
		};

		if (config.PurchasePersistence?.Enabled != true)
		{
			response.Reason = "PurchasePersistenceDisabled";
			return response;
		}

		if (!TryResolveSupportType(request.SupportType, out ESupportType supportType))
		{
			response.Reason = "InvalidSupportType";
			return response;
		}

		response.SupportType = supportType.ToString();
		if (!TryResolveProfileForPurchase(sessionId, request, out PmcData? pmc, out MongoId saveSessionId, out string profileDenialReason))
		{
			response.Reason = profileDenialReason;
			return response;
		}

		string profileLedgerId = GetCanonicalProfileLedgerId(pmc, saveSessionId);
		bool ok;
		Dictionary<string, int> authorizations;
		string reason;
		if (mutation == AuthorizationMutation.Consume)
		{
			ok = authorizationLedger.TryConsume(
				profileLedgerId,
				supportType,
				request.RequestId,
				config.PurchasePersistence.PendingUseTimeoutSeconds,
				out authorizations,
				out reason);
		}
		else if (mutation == AuthorizationMutation.Commit)
		{
			ok = authorizationLedger.TryCommit(
				profileLedgerId,
				supportType,
				request.RequestId,
				config.PurchasePersistence.PendingUseTimeoutSeconds,
				out authorizations,
				out reason);
		}
		else
		{
			ok = authorizationLedger.TryRefund(
				profileLedgerId,
				supportType,
				request.RequestId,
				config.PurchasePersistence.MaxStoredAuthorizationsPerService,
				config.PurchasePersistence.PendingUseTimeoutSeconds,
				string.IsNullOrWhiteSpace(request.Action) ? "DispatchFailed" : request.Action,
				out authorizations,
				out reason);
		}

		response.Ok = ok;
		response.Reason = ok ? mutation.ToString() : reason;
		response.AuthorizationConsumed = mutation != AuthorizationMutation.Refund && ok;
		response.AuthorizationGranted = mutation == AuthorizationMutation.Refund && ok;
		response.AuthorizationsIncluded = !string.Equals(
			reason,
			"InvalidAuthorizationRequest",
			StringComparison.OrdinalIgnoreCase);
		response.Authorizations = authorizations;
		logger.Success(
			$"TSC authorization {mutation.ToString().ToLowerInvariant()} result={response.Reason} sessionId={FormatLogId(saveSessionId)} supportType={supportType}");
		return response;
	}

	private enum AuthorizationMutation
	{
		Consume,
		Commit,
		Refund
	}

	public bool IsAdminRequestAuthorized(string? authorizationHeader, string? tokenHeader)
	{
		return IsAdminRequestAuthorized(authorizationHeader, tokenHeader, isLocalRequest: false);
	}

	public bool IsAdminRequestAuthorized(string? authorizationHeader, string? tokenHeader, bool isLocalRequest)
	{
		if (!IsAdminDashboardAccessible(isLocalRequest, out _))
		{
			return false;
		}

		if (isLocalRequest && !GetAdminDashboardSettings().RequireTokenForLocalhost)
		{
			return true;
		}

		string expectedToken = GetAdminToken();
		if (string.IsNullOrWhiteSpace(expectedToken))
		{
			return false;
		}

		return IsTokenMatch(expectedToken, ExtractBearerToken(authorizationHeader)) ||
		       IsTokenMatch(expectedToken, tokenHeader);
	}

	public bool IsAdminDashboardAccessible(bool isLocalRequest, out string denialReason)
	{
		RaidOpsFireSupportServerConfig.AdminDashboardSettings settings = GetAdminDashboardSettings();
		if (!settings.Enabled)
		{
			denialReason = "TSC Dashboard is disabled.";
			return false;
		}

		if (!isLocalRequest && !settings.AllowRemoteAccess)
		{
			denialReason = "TSC Dashboard remote access is disabled.";
			return false;
		}

		denialReason = string.Empty;
		return true;
	}

	public object GetHealth(bool includeDiagnostics, bool isLocalRequest = false)
	{
		RaidOpsFireSupportServerConfig snapshot;
		lock (_gate)
		{
			snapshot = CloneConfig(_config);
		}

		object adminDashboard = GetAdminDashboardStatus(snapshot, isLocalRequest);

		if (!includeDiagnostics)
		{
			return new
			{
				ok = true,
				revision = snapshot.Revision,
				paymentMode = snapshot.PaymentMode,
				paymentSource = snapshot.PaymentSource,
				requestCooldownSeconds = snapshot.RequestCooldownSeconds,
				adminDashboard,
				adminTokenConfigured = !string.IsNullOrWhiteSpace(GetAdminToken()),
				lastLoadedUtc = _lastLoadedUtc,
				lastSavedUtc = _lastSavedUtc
			};
		}

		return new
		{
			ok = true,
			revision = snapshot.Revision,
			paymentMode = snapshot.PaymentMode,
			paymentSource = snapshot.PaymentSource,
			requestCooldownSeconds = snapshot.RequestCooldownSeconds,
			configFile = ConfigFileName,
			adminTokenFile = AdminTokenFileName,
			webRoot = "web",
			configPath = _configPath,
			webRootPath = _webRootPath,
			adminDashboard,
			adminTokenConfigured = !string.IsNullOrWhiteSpace(GetAdminToken()),
			lastLoadedUtc = _lastLoadedUtc,
			lastSavedUtc = _lastSavedUtc
		};
	}

	public object GetDashboardSchema()
	{
		return new
		{
			sections = new object[]
			{
				Section("main", "Main",
					Field("paymentMode", "Payment Mode", "select", options: new[] { "PhoneAuthorizations", "DirectRadial", "Hybrid" }),
					Field("requestCooldownSeconds", "Request Cooldown", "number", min: 0, max: 1800, step: 15),
					Field("revision", "Config Revision", "readonly")),
				Section("admin", "Admin Dashboard",
					Field("adminDashboard.enabled", "Dashboard Enabled", "toggle"),
					Field("adminDashboard.allowRemoteAccess", "Allow Remote Access", "toggle"),
					Field("adminDashboard.requireTokenForLocalhost", "Require Token Locally", "toggle")),
				Section("persistence", "Purchase Persistence",
					Field("purchasePersistence.enabled", "Persistent Authorizations", "toggle"),
					Field("purchasePersistence.maxStoredAuthorizationsPerService", "Max Stored Per Service", "number", min: 1, max: 25, step: 1),
					Field("purchasePersistence.pendingUseTimeoutSeconds", "Pending Use Timeout", "number", min: 10, max: 1800, step: 10),
					Field("purchasePersistence.spendCreditsBeforeCash", "Spend Credits First", "toggle"),
					Field("purchasePersistence.allowAutoPurchaseOnUse", "Allow Auto Purchase On Use", "toggle")),
				Section("payment", "Payment",
					Field("paymentSource", "Payment Source", "select", options: new[] { "CarriedRoubles", "StashRoubles", "PreferCarriedThenStash", "PreferStashThenCarried" })),
				Section("pricing", "Service Pricing",
					Field("prices.A10", "A-10 Price", "number", min: 0, max: 10000000, step: 5000, slider: true),
					Field("prices.DoublePass", "Double Pass Price", "number", min: 0, max: 10000000, step: 5000, slider: true),
					Field("prices.Uav", "UAV Price", "number", min: 0, max: 10000000, step: 5000, slider: true),
					Field("prices.FocusedSweep", "Focused Sweep Price", "number", min: 0, max: 10000000, step: 5000, slider: true),
					Field("prices.Extraction", "Extraction Price", "number", min: 0, max: 10000000, step: 5000, slider: true),
					Field("prices.PriorityExfil", "Priority Exfil Price", "number", min: 0, max: 10000000, step: 5000, slider: true)),
				Section("services", "Service Toggles",
					Field("enabled.A10", "A-10 Enabled", "toggle"),
					Field("enabled.DoublePass", "Double Pass Enabled", "toggle"),
					Field("enabled.Uav", "UAV Enabled", "toggle"),
					Field("enabled.FocusedSweep", "Focused Sweep Enabled", "toggle"),
					Field("enabled.Extraction", "Extraction Enabled", "toggle"),
					Field("enabled.PriorityExfil", "Priority Exfil Enabled", "toggle")),
				Section("recon", "Recon Services",
					Field("uav.durationSeconds", "UAV Duration", "number", min: 5, max: 1800, step: 5, slider: true),
					Field("uav.rangeMeters", "UAV Range", "number", min: 25, max: 1000, step: 25, slider: true),
					Field("uav.scanIntervalSeconds", "UAV Scan Interval", "number", min: 0.1, max: 10, step: 0.1),
					Field("focusedSweep.durationSeconds", "Focused Sweep Duration", "number", min: 5, max: 1800, step: 5, slider: true),
					Field("focusedSweep.rangeMeters", "Focused Sweep Range", "number", min: 25, max: 1000, step: 25, slider: true),
					Field("focusedSweep.scanIntervalSeconds", "Focused Sweep Scan Interval", "number", min: 0.1, max: 10, step: 0.05)),
				Section("extraction", "Extraction Services",
					Field("extraction.dispatchDelaySeconds", "Extraction Dispatch Delay", "number", min: 0, max: 120, step: 1),
					Field("extraction.waitTimeSeconds", "Extraction Wait Time", "number", min: 5, max: 300, step: 5, slider: true),
					Field("extraction.extractTimeSeconds", "Extraction Time", "number", min: 1, max: 60, step: 1),
					Field("extraction.speedMultiplier", "Extraction Speed", "number", min: 0.5, max: 3, step: 0.05, slider: true),
					Field("priorityExfil.dispatchDelaySeconds", "Priority Dispatch Delay", "number", min: 0, max: 120, step: 1),
					Field("priorityExfil.waitTimeSeconds", "Priority Wait Time", "number", min: 5, max: 300, step: 5, slider: true),
					Field("priorityExfil.extractTimeSeconds", "Priority Extraction Time", "number", min: 1, max: 60, step: 1),
					Field("priorityExfil.speedMultiplier", "Priority Speed", "number", min: 0.5, max: 3, step: 0.05, slider: true)),
				Section("fire", "Fire Support",
					Field("a10.secondPassDelaySeconds", "A-10 Second Pass Delay", "number", min: 0, max: 60, step: 1),
					Field("doublePass.secondPassDelaySeconds", "Double Pass Delay", "number", min: 0, max: 60, step: 1))
			}
		};
	}

	public bool TryUpdateConfig(RaidOpsFireSupportServerConfig incoming, out string error)
	{
		error = string.Empty;
		try
		{
			NormalizeConfig(incoming);
			lock (_gate)
			{
				incoming.Revision = Math.Max(incoming.Revision, _config.Revision + 1);
				_config = CloneConfig(incoming);
				SaveConfig(_config);
			}

			logger.Success($"TSC config updated revision={incoming.Revision}");
			return true;
		}
		catch (Exception ex)
		{
			logger.Error("TSC config update failed.", ex);
			error = ex.Message;
			return false;
		}
	}

	public bool TryReloadConfig(out RaidOpsFireSupportServerConfig snapshot, out string error)
	{
		error = string.Empty;
		snapshot = CreateDefaultConfig();
		try
		{
			lock (_gate)
			{
				_config = LoadConfig();
				NormalizeConfig(_config);
				if (_config.Revision <= 0)
				{
					_config.Revision = 1;
				}

				SaveConfig(_config);
				snapshot = CloneConfig(_config);
			}

			logger.Success($"TSC config reloaded revision={snapshot.Revision}");
			return true;
		}
		catch (Exception ex)
		{
			logger.Error("TSC config reload failed.", ex);
			error = ex.Message;
			return false;
		}
	}

	public bool TryResetConfig(out RaidOpsFireSupportServerConfig snapshot, out string error)
	{
		error = string.Empty;
		snapshot = CreateDefaultConfig();
		try
		{
			lock (_gate)
			{
				int nextRevision = Math.Max(1, _config.Revision + 1);
				_config = CreateDefaultConfig();
				_config.Revision = nextRevision;
				SaveConfig(_config);
				snapshot = CloneConfig(_config);
			}

			logger.Success($"TSC config reset revision={snapshot.Revision}");
			return true;
		}
		catch (Exception ex)
		{
			logger.Error("TSC config reset failed.", ex);
			error = ex.Message;
			return false;
		}
	}

	private RaidOpsFireSupportServerConfig LoadConfig()
	{
		if (!File.Exists(_configPath))
		{
			return CreateDefaultConfig();
		}

		try
		{
			string json = File.ReadAllText(_configPath);
			_lastLoadedUtc = DateTimeOffset.UtcNow;
			return JsonSerializer.Deserialize<RaidOpsFireSupportServerConfig>(json, s_jsonOptions) ??
			       CreateDefaultConfig();
		}
		catch (Exception ex)
		{
			logger.Warning($"TSC config failed to load; using defaults. {ex.Message}");
			return CreateDefaultConfig();
		}
	}

	private void SaveConfig(RaidOpsFireSupportServerConfig config)
	{
		if (string.IsNullOrWhiteSpace(_configPath))
		{
			return;
		}

		File.WriteAllText(_configPath, JsonSerializer.Serialize(config, s_jsonOptions));
		_lastSavedUtc = DateTimeOffset.UtcNow;
	}

	private void EnsureAdminToken()
	{
		if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(AdminTokenEnvironmentVariable)) ||
		    !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(LegacyAdminTokenEnvironmentVariable)) ||
		    File.Exists(_adminTokenPath))
		{
			return;
		}

		byte[] bytes = RandomNumberGenerator.GetBytes(32);
		string token = Convert.ToHexString(bytes);
		File.WriteAllText(_adminTokenPath, token);
		logger.Warning("TSC admin token created in the mod config directory.");
	}

	private string GetAdminToken()
	{
		string? token = Environment.GetEnvironmentVariable(AdminTokenEnvironmentVariable);
		if (!string.IsNullOrWhiteSpace(token))
		{
			return token.Trim();
		}

		token = Environment.GetEnvironmentVariable(LegacyAdminTokenEnvironmentVariable);
		if (!string.IsNullOrWhiteSpace(token))
		{
			return token.Trim();
		}

		if (File.Exists(_adminTokenPath))
		{
			return File.ReadAllText(_adminTokenPath).Trim();
		}

		return string.Empty;
	}

	private static object Section(string id, string label, params object[] fields)
	{
		return new { id, label, fields };
	}

	private static object Field(
		string path,
		string label,
		string type,
		double? min = null,
		double? max = null,
		double? step = null,
		bool slider = false,
		string[]? options = null)
	{
		return new
		{
			path,
			label,
			type,
			min,
			max,
			step,
			slider,
			options = options ?? Array.Empty<string>()
		};
	}

	private bool TryResolveAuthenticatedProfile(
		MongoId httpSessionId,
		FireSupportPurchaseRequest? request,
		[NotNullWhen(true)] out PmcData? pmc,
		out MongoId saveSessionId,
		out bool identityMismatch)
	{
		pmc = null;
		saveSessionId = default;
		identityMismatch = false;

		if (!IsUsableMongoId(httpSessionId) ||
		    !TryGetPmcProfileBySession(httpSessionId, out PmcData? sessionProfile))
		{
			return false;
		}

		saveSessionId = ResolveSaveSessionId(sessionProfile, httpSessionId);
		if (!IsUsableMongoId(saveSessionId))
		{
			return false;
		}

		if (request != null && !RequestHintsMatchProfile(sessionProfile, request, saveSessionId))
		{
			identityMismatch = true;
			return false;
		}

		pmc = sessionProfile;
		return true;
	}

	private bool TryResolveProfileForPurchase(
		MongoId httpSessionId,
		FireSupportPurchaseRequest request,
		[NotNullWhen(true)] out PmcData? pmc,
		out MongoId saveSessionId,
		out string denialReason)
	{
		pmc = null;
		saveSessionId = default;
		denialReason = "ProfileNotFound";

		if (!TryResolveAuthenticatedProfile(
			    httpSessionId,
			    request,
			    out PmcData? sessionProfile,
			    out MongoId resolvedSaveSessionId,
			    out bool identityMismatch))
		{
			if (identityMismatch)
			{
				denialReason = "ProfileSessionMismatch";
				logger.Warning($"TSC purchase denied reason=ProfileSessionMismatch sessionId={FormatLogId(httpSessionId)}");
			}

			return false;
		}

		pmc = sessionProfile;
		saveSessionId = resolvedSaveSessionId;
		return true;
	}

	private bool RequestHintsMatchProfile(
		PmcData resolvedProfile,
		FireSupportPurchaseRequest request,
		MongoId resolvedSessionId)
	{
		if (!string.IsNullOrWhiteSpace(request.ProfileId))
		{
			if (!TryCreateMongoId(request.ProfileId, out MongoId profileId) ||
			    !TryGetPmcProfileByProfileId(profileId, out PmcData? profileHint) ||
			    !IsSameProfile(resolvedProfile, profileHint))
			{
				return false;
			}
		}

		if (!string.IsNullOrWhiteSpace(request.SessionId))
		{
			if (!TryCreateMongoId(request.SessionId, out MongoId sessionId))
			{
				return false;
			}

			if (!AreSameMongoId(sessionId, resolvedSessionId))
			{
				bool matchesSessionProfile =
					TryGetPmcProfileBySession(sessionId, out PmcData? sessionHint) &&
					IsSameProfile(resolvedProfile, sessionHint);
				bool matchesPmcProfile =
					TryGetPmcProfileByProfileId(sessionId, out PmcData? profileHint) &&
					IsSameProfile(resolvedProfile, profileHint);
				if (!matchesSessionProfile && !matchesPmcProfile)
				{
					return false;
				}
			}
		}

		return true;
	}

	private bool TryGetPmcProfileBySession(
		MongoId sessionId,
		[NotNullWhen(true)] out PmcData? pmc)
	{
		pmc = null;
		if (!IsUsableMongoId(sessionId))
		{
			return false;
		}

		try
		{
			pmc = profileHelper.GetPmcProfile(sessionId);
			return pmc != null;
		}
		catch
		{
			return false;
		}
	}

	private bool TryGetPmcProfileByProfileId(
		MongoId profileId,
		[NotNullWhen(true)] out PmcData? pmc)
	{
		pmc = null;
		if (!IsUsableMongoId(profileId))
		{
			return false;
		}

		try
		{
			pmc = profileHelper.GetProfileByPmcId(profileId);
			return pmc != null;
		}
		catch
		{
			return false;
		}
	}

	private bool IsPurchaseRateLimited(MongoId saveSessionId, ESupportType supportType, DateTimeOffset now)
	{
		PrunePurchaseRateLimits(now);
		return _purchaseRateLimits.TryGetValue(GetPurchaseRateLimitKey(saveSessionId, supportType), out DateTimeOffset lastAttempt) &&
		       now - lastAttempt < s_purchaseRateLimitWindow;
	}

	private void MarkPurchaseAttempt(MongoId saveSessionId, ESupportType supportType, DateTimeOffset now)
	{
		_purchaseRateLimits[GetPurchaseRateLimitKey(saveSessionId, supportType)] = now;
	}

	private void PrunePurchaseRateLimits(DateTimeOffset now)
	{
		foreach (string key in _purchaseRateLimits
			         .Where(pair => now - pair.Value > TimeSpan.FromMinutes(5))
			         .Select(pair => pair.Key)
			         .ToList())
		{
			_purchaseRateLimits.Remove(key);
		}
	}

	private static string GetPurchaseRateLimitKey(MongoId saveSessionId, ESupportType supportType)
	{
		return $"{saveSessionId}:{supportType}";
	}

	private static MongoId ResolveSaveSessionId(PmcData pmc, MongoId fallback)
	{
		if (pmc.SessionId.HasValue && IsUsableMongoId(pmc.SessionId.Value))
		{
			return pmc.SessionId.Value;
		}

		return fallback;
	}

	private static bool TryCreateMongoId(string? value, out MongoId mongoId)
	{
		mongoId = default;
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}

		try
		{
			mongoId = new MongoId(value.Trim());
			return IsUsableMongoId(mongoId);
		}
		catch
		{
			return false;
		}
	}

	private static bool IsUsableMongoId(MongoId mongoId)
	{
		return !mongoId.IsEmpty && !string.IsNullOrWhiteSpace(mongoId.ToString());
	}

	private static bool IsSameProfile(PmcData left, PmcData right)
	{
		if (ReferenceEquals(left, right))
		{
			return true;
		}

		if (left.Id.HasValue &&
		    right.Id.HasValue &&
		    AreSameMongoId(left.Id.Value, right.Id.Value))
		{
			return true;
		}

		return left.SessionId.HasValue &&
		       right.SessionId.HasValue &&
		       AreSameMongoId(left.SessionId.Value, right.SessionId.Value);
	}

	private static bool AreSameMongoId(MongoId left, MongoId right)
	{
		return IsUsableMongoId(left) &&
		       IsUsableMongoId(right) &&
		       string.Equals(left.ToString(), right.ToString(), StringComparison.OrdinalIgnoreCase);
	}

	private static int CountStashRoubles(PmcData pmc)
	{
		return GetStashRoubleStacks(pmc).Sum(GetStackCount);
	}

	private static string ComputeRoubleInventoryFingerprint(PmcData pmc)
	{
		return ComputeRoubleInventoryFingerprint(
			GetStashRoubleStacks(pmc)
				.Select(stack =>
					new KeyValuePair<string, int>(
						stack.Id.ToString().ToLowerInvariant(),
						GetStackCount(stack))));
	}

	private static bool TryComputeExpectedPostDebitFingerprint(
		PmcData pmc,
		int amount,
		out string fingerprint,
		out int projectedCharge)
	{
		fingerprint = string.Empty;
		projectedCharge = 0;
		if (amount < 0)
		{
			return false;
		}

		List<Item> stacks = GetStashRoubleStacks(pmc).ToList();
		var projectedCounts = stacks.ToDictionary(
			stack => stack.Id.ToString(),
			GetStackCount,
			StringComparer.OrdinalIgnoreCase);
		var removedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		List<Item> inventoryItems = pmc.Inventory?.Items ?? new List<Item>();
		int remaining = amount;

		foreach (Item stack in stacks)
		{
			if (remaining <= 0)
			{
				break;
			}

			string stackId = stack.Id.ToString();
			int stackCount = projectedCounts[stackId];
			int take = Math.Min(stackCount, remaining);
			remaining -= take;

			if (take >= stackCount)
			{
				CollectDescendantIds(stackId, inventoryItems, removedIds);
				continue;
			}

			projectedCounts[stackId] = stackCount - take;
		}

		projectedCharge = amount - remaining;
		fingerprint = ComputeRoubleInventoryFingerprint(
			stacks
				.Where(stack => !removedIds.Contains(stack.Id.ToString()))
				.Select(stack =>
					new KeyValuePair<string, int>(
						stack.Id.ToString().ToLowerInvariant(),
						projectedCounts[stack.Id.ToString()])));
		return remaining == 0;
	}

	private static string ComputeRoubleInventoryFingerprint(
		IEnumerable<KeyValuePair<string, int>> stacks)
	{
		var fingerprintInput = new StringBuilder();
		foreach (KeyValuePair<string, int> stack in stacks
			         .OrderBy(entry => entry.Key, StringComparer.Ordinal))
		{
			fingerprintInput
				.Append(stack.Key)
				.Append(':')
				.Append(stack.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))
				.Append('\n');
		}

		byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintInput.ToString()));
		return Convert.ToHexString(hash);
	}

	private static int DebitStashRoubles(PmcData pmc, int amount)
	{
		int remaining = amount;
		foreach (Item stack in GetStashRoubleStacks(pmc).ToList())
		{
			if (remaining <= 0)
			{
				break;
			}

			int stackCount = GetStackCount(stack);
			int take = Math.Min(stackCount, remaining);
			remaining -= take;

			if (take >= stackCount)
			{
				RemoveItemAndChildren(pmc, stack);
				continue;
			}

			stack.Upd ??= new Upd();
			stack.Upd.StackObjectsCount = stackCount - take;
		}

		return amount - remaining;
	}

	private static IEnumerable<Item> GetStashRoubleStacks(PmcData pmc)
	{
		BotBaseInventory? inventory = pmc.Inventory;
		List<Item>? items = inventory?.Items;
		if (items == null || inventory == null || !inventory.Stash.HasValue)
		{
			yield break;
		}

		var itemsById = items
			.Where(item => item != null)
			.ToDictionary(item => item.Id.ToString(), item => item);
		string stashId = inventory.Stash.Value.ToString();

		foreach (Item item in items)
		{
			if (item == null ||
			    !string.Equals(item.Template.ToString(), RoubleTemplateId, StringComparison.OrdinalIgnoreCase) ||
			    !IsDescendantOfStash(item, stashId, itemsById))
			{
				continue;
			}

			yield return item;
		}
	}

	private static bool IsDescendantOfStash(Item item, string stashId, Dictionary<string, Item> itemsById)
	{
		string? parentId = item.ParentId;
		while (!string.IsNullOrWhiteSpace(parentId))
		{
			if (string.Equals(parentId, stashId, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}

			if (!itemsById.TryGetValue(parentId, out Item? parent))
			{
				return false;
			}

			parentId = parent.ParentId;
		}

		return false;
	}

	private static int GetStackCount(Item item)
	{
		double count = item.Upd?.StackObjectsCount ?? 1d;
		return Math.Max(0, (int)Math.Floor(count));
	}

	private static void RemoveItemAndChildren(PmcData pmc, Item item)
	{
		List<Item>? items = pmc.Inventory?.Items;
		if (items == null)
		{
			return;
		}

		var idsToRemove = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		CollectDescendantIds(item.Id.ToString(), items, idsToRemove);
		items.RemoveAll(candidate => candidate != null && idsToRemove.Contains(candidate.Id.ToString()));
	}

	private static void CollectDescendantIds(string itemId, List<Item> items, HashSet<string> idsToRemove)
	{
		if (!idsToRemove.Add(itemId))
		{
			return;
		}

		foreach (Item child in items.Where(candidate =>
			         candidate != null &&
			         string.Equals(candidate.ParentId, itemId, StringComparison.OrdinalIgnoreCase)))
		{
			CollectDescendantIds(child.Id.ToString(), items, idsToRemove);
		}
	}

	private static bool TryResolveSupportType(string value, out ESupportType supportType)
	{
		if (Enum.TryParse(value, ignoreCase: true, out supportType) && supportType != ESupportType.None)
		{
			return true;
		}

		supportType = value?.Trim().ToLowerInvariant() switch
		{
			"a10" => ESupportType.Strafe,
			"strafe" => ESupportType.Strafe,
			"doublepass" => ESupportType.DoubleStrafe,
			"doublestrafe" => ESupportType.DoubleStrafe,
			"extraction" => ESupportType.Extract,
			"extract" => ESupportType.Extract,
			"priorityexfil" => ESupportType.PriorityExfil,
			"uav" => ESupportType.Uav,
			"focusedsweep" => ESupportType.FocusedSweep,
			_ => ESupportType.None
		};

		return supportType != ESupportType.None;
	}

	private static int GetPrice(RaidOpsFireSupportServerConfig config, ESupportType supportType)
	{
		string key = GetConfigKey(supportType);
		return config.Prices.TryGetValue(key, out int price)
			? Math.Max(0, price)
			: 0;
	}

	private static bool IsServiceEnabled(RaidOpsFireSupportServerConfig config, ESupportType supportType)
	{
		string key = GetConfigKey(supportType);
		return !config.Enabled.TryGetValue(key, out bool enabled) || enabled;
	}

	private static string GetConfigKey(ESupportType supportType)
	{
		return supportType switch
		{
			ESupportType.Strafe => "A10",
			ESupportType.DoubleStrafe => "DoublePass",
			ESupportType.Extract => "Extraction",
			ESupportType.PriorityExfil => "PriorityExfil",
			ESupportType.Uav => "Uav",
			ESupportType.FocusedSweep => "FocusedSweep",
			_ => supportType.ToString()
		};
	}

	private static bool IsServerBackedPaymentSource(PaymentSource paymentSource)
	{
		return paymentSource == PaymentSource.StashRoubles ||
		       paymentSource == PaymentSource.PreferCarriedThenStash ||
		       paymentSource == PaymentSource.PreferStashThenCarried;
	}

	private static void NormalizeConfig(RaidOpsFireSupportServerConfig config)
	{
		RaidOpsFireSupportServerConfig defaults = CreateDefaultConfig();
		config.PaymentMode = Enum.TryParse(config.PaymentMode, ignoreCase: true, out PaymentMode paymentMode)
			? paymentMode.ToString()
			: defaults.PaymentMode;
		config.PaymentSource = Enum.TryParse(config.PaymentSource, ignoreCase: true, out PaymentSource paymentSource)
			? paymentSource.ToString()
			: defaults.PaymentSource;
		config.RequestCooldownSeconds = config.RequestCooldownSeconds < 0
			? defaults.RequestCooldownSeconds
			: config.RequestCooldownSeconds;
		config.Prices = MergeDictionary(config.Prices, defaults.Prices);
		config.Enabled = MergeDictionary(config.Enabled, defaults.Enabled);
		config.AdminDashboard = NormalizeAdminDashboardSettings(config.AdminDashboard, defaults.AdminDashboard);
		config.PurchasePersistence = NormalizePurchasePersistenceSettings(config.PurchasePersistence, defaults.PurchasePersistence);
		config.Uav = NormalizeUavSettings(config.Uav, defaults.Uav);
		config.FocusedSweep = NormalizeUavSettings(config.FocusedSweep, defaults.FocusedSweep);
		config.Extraction = NormalizeExtractionSettings(config.Extraction, defaults.Extraction);
		config.PriorityExfil = NormalizeExtractionSettings(config.PriorityExfil, defaults.PriorityExfil);
		config.A10 = config.A10 ?? defaults.A10;
		config.DoublePass = NormalizeA10Settings(config.DoublePass, defaults.DoublePass);
		// These fields are populated only on authenticated response snapshots.
		// Never accept or persist them as shared administrator configuration.
		config.PlayerStateIncluded = false;
		config.StashRoubleBalance = null;
		config.Authorizations = new Dictionary<string, int>();
		config.PreparedPurchases = null;
	}

	private static Dictionary<TKey, TValue> MergeDictionary<TKey, TValue>(
		Dictionary<TKey, TValue>? values,
		Dictionary<TKey, TValue> defaults)
		where TKey : notnull
	{
		var merged = new Dictionary<TKey, TValue>(defaults);
		if (values == null)
		{
			return merged;
		}

		foreach ((TKey key, TValue value) in values)
		{
			merged[key] = value;
		}

		return merged;
	}

	private static RaidOpsFireSupportServerConfig.UavSettings NormalizeUavSettings(
		RaidOpsFireSupportServerConfig.UavSettings? settings,
		RaidOpsFireSupportServerConfig.UavSettings defaults)
	{
		settings ??= new RaidOpsFireSupportServerConfig.UavSettings();
		settings.DurationSeconds = settings.DurationSeconds <= 0 ? defaults.DurationSeconds : settings.DurationSeconds;
		settings.RangeMeters = settings.RangeMeters <= 0f ? defaults.RangeMeters : settings.RangeMeters;
		settings.ScanIntervalSeconds = settings.ScanIntervalSeconds <= 0f
			? defaults.ScanIntervalSeconds
			: settings.ScanIntervalSeconds;
		return settings;
	}

	private static RaidOpsFireSupportServerConfig.AdminDashboardSettings NormalizeAdminDashboardSettings(
		RaidOpsFireSupportServerConfig.AdminDashboardSettings? settings,
		RaidOpsFireSupportServerConfig.AdminDashboardSettings defaults)
	{
		return settings ?? defaults;
	}

	private static RaidOpsFireSupportServerConfig.PurchasePersistenceSettings NormalizePurchasePersistenceSettings(
		RaidOpsFireSupportServerConfig.PurchasePersistenceSettings? settings,
		RaidOpsFireSupportServerConfig.PurchasePersistenceSettings defaults)
	{
		settings ??= defaults;
		settings.Mode = string.Equals(settings.Mode, "PersistentAuthorizations", StringComparison.OrdinalIgnoreCase)
			? "PersistentAuthorizations"
			: defaults.Mode;
		settings.ConsumeOn = string.Equals(settings.ConsumeOn, "AuthorizationAccepted", StringComparison.OrdinalIgnoreCase)
			? "AuthorizationAccepted"
			: defaults.ConsumeOn;
		settings.MaxStoredAuthorizationsPerService = settings.MaxStoredAuthorizationsPerService <= 0
			? defaults.MaxStoredAuthorizationsPerService
			: settings.MaxStoredAuthorizationsPerService;
		settings.PendingUseTimeoutSeconds = settings.PendingUseTimeoutSeconds <= 0
			? defaults.PendingUseTimeoutSeconds
			: settings.PendingUseTimeoutSeconds;
		return settings;
	}

	private static RaidOpsFireSupportServerConfig.ExtractionSettings NormalizeExtractionSettings(
		RaidOpsFireSupportServerConfig.ExtractionSettings? settings,
		RaidOpsFireSupportServerConfig.ExtractionSettings defaults)
	{
		settings ??= new RaidOpsFireSupportServerConfig.ExtractionSettings();
		settings.WaitTimeSeconds = settings.WaitTimeSeconds <= 0 ? defaults.WaitTimeSeconds : settings.WaitTimeSeconds;
		settings.ExtractTimeSeconds = settings.ExtractTimeSeconds <= 0f
			? defaults.ExtractTimeSeconds
			: settings.ExtractTimeSeconds;
		settings.SpeedMultiplier = settings.SpeedMultiplier <= 0f ? defaults.SpeedMultiplier : settings.SpeedMultiplier;
		return settings;
	}

	private static RaidOpsFireSupportServerConfig.A10Settings NormalizeA10Settings(
		RaidOpsFireSupportServerConfig.A10Settings? settings,
		RaidOpsFireSupportServerConfig.A10Settings defaults)
	{
		settings ??= new RaidOpsFireSupportServerConfig.A10Settings();
		settings.SecondPassDelaySeconds = settings.SecondPassDelaySeconds <= 0f
			? defaults.SecondPassDelaySeconds
			: settings.SecondPassDelaySeconds;
		return settings;
	}
	private static RaidOpsFireSupportServerConfig CloneConfig(RaidOpsFireSupportServerConfig config)
	{
		return JsonSerializer.Deserialize<RaidOpsFireSupportServerConfig>(
			       JsonSerializer.Serialize(config, s_jsonOptions),
			       s_jsonOptions) ??
		       CreateDefaultConfig();
	}

	private static Dictionary<string, JsonElement> CreateAuthenticatedSnapshotPayload(
		RaidOpsFireSupportServerConfig snapshot,
		Dictionary<string, string> preparedPurchases)
	{
		Dictionary<string, JsonElement> payload =
			JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
				JsonSerializer.Serialize(snapshot, s_jsonOptions),
				s_jsonOptions) ??
			new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
		payload["preparedPurchases"] =
			JsonSerializer.SerializeToElement(preparedPurchases, s_jsonOptions);
		return payload;
	}

	private static TEnum ParseEnum<TEnum>(string value, TEnum fallback)
		where TEnum : struct
	{
		return Enum.TryParse(value, ignoreCase: true, out TEnum parsed)
			? parsed
			: fallback;
	}

	private static string? ExtractBearerToken(string? authorizationHeader)
	{
		if (string.IsNullOrWhiteSpace(authorizationHeader))
		{
			return null;
		}

		const string bearerPrefix = "Bearer ";
		return authorizationHeader.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase)
			? authorizationHeader[bearerPrefix.Length..].Trim()
			: authorizationHeader.Trim();
	}

	private static bool IsTokenMatch(string expectedToken, string? providedToken)
	{
		if (string.IsNullOrWhiteSpace(providedToken))
		{
			return false;
		}

		byte[] expectedBytes = Encoding.UTF8.GetBytes(expectedToken);
		byte[] providedBytes = Encoding.UTF8.GetBytes(providedToken.Trim());
		return expectedBytes.Length == providedBytes.Length &&
		       CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
	}

	private static string FormatLogId(MongoId mongoId)
	{
		if (!IsUsableMongoId(mongoId))
		{
			return "<empty>";
		}

		string value = mongoId.ToString();
		int keep = Math.Min(6, value.Length);
		return $"...{value[^keep..]}";
	}

	private static string FormatRequestId(string? requestId)
	{
		if (string.IsNullOrWhiteSpace(requestId))
		{
			return "<empty>";
		}

		string value = requestId.Trim();
		int keep = Math.Min(8, value.Length);
		return $"...{value[^keep..]}";
	}

	private static string GetCanonicalProfileLedgerId(PmcData pmc, MongoId saveSessionId)
	{
		// v1.0.8 clients keyed the ledger with the PMC profile ID. Resolving that
		// ID from the authenticated profile preserves those credits without
		// trusting a request hint. The authenticated save session is only a
		// fallback for malformed legacy profiles that have no usable PMC ID.
		if (pmc.Id.HasValue && IsUsableMongoId(pmc.Id.Value))
		{
			return pmc.Id.Value.ToString();
		}

		return saveSessionId.ToString();
	}

	private RaidOpsFireSupportServerConfig.AdminDashboardSettings GetAdminDashboardSettings()
	{
		lock (_gate)
		{
			return CloneConfig(_config).AdminDashboard ?? CreateDefaultConfig().AdminDashboard;
		}
	}

	private object GetAdminDashboardStatus(RaidOpsFireSupportServerConfig snapshot, bool isLocalRequest)
	{
		RaidOpsFireSupportServerConfig.AdminDashboardSettings settings =
			snapshot.AdminDashboard ?? CreateDefaultConfig().AdminDashboard;
		return new
		{
			settings.Enabled,
			settings.AllowRemoteAccess,
			settings.RequireTokenForLocalhost,
			isLocalRequest,
			tokenRequired = !isLocalRequest || settings.RequireTokenForLocalhost,
			accessible = settings.Enabled && (isLocalRequest || settings.AllowRemoteAccess)
		};
	}

	private static string GetSupportKey(ESupportType supportType)
	{
		return supportType switch
		{
			ESupportType.Strafe => "A10",
			ESupportType.DoubleStrafe => "DoublePass",
			ESupportType.Extract => "Extraction",
			ESupportType.PriorityExfil => "PriorityExfil",
			ESupportType.Uav => "Uav",
			ESupportType.FocusedSweep => "FocusedSweep",
			_ => supportType.ToString()
		};
	}

	private static RaidOpsFireSupportServerConfig CreateDefaultConfig()
	{
		return new RaidOpsFireSupportServerConfig
		{
			Revision = 1,
			PaymentMode = nameof(PaymentMode.PhoneAuthorizations),
			PaymentSource = nameof(PaymentSource.CarriedRoubles),
			RequestCooldownSeconds = 300,
			Prices = new Dictionary<string, int>
			{
				["A10"] = 250000,
				["DoublePass"] = 450000,
				["Extraction"] = 300000,
				["PriorityExfil"] = 450000,
				["Uav"] = 125000,
				["FocusedSweep"] = 90000
			},
			Enabled = new Dictionary<string, bool>
			{
				["A10"] = true,
				["DoublePass"] = true,
				["Extraction"] = true,
				["PriorityExfil"] = true,
				["Uav"] = true,
				["FocusedSweep"] = true
			},
			AdminDashboard = new RaidOpsFireSupportServerConfig.AdminDashboardSettings
			{
				Enabled = true,
				AllowRemoteAccess = false,
				RequireTokenForLocalhost = false
			},
			PurchasePersistence = new RaidOpsFireSupportServerConfig.PurchasePersistenceSettings
			{
				Enabled = true,
				Mode = "PersistentAuthorizations",
				ConsumeOn = "AuthorizationAccepted",
				RefundFailedDispatch = true,
				MaxStoredAuthorizationsPerService = 2,
				PendingUseTimeoutSeconds = 180,
				SpendCreditsBeforeCash = true,
				AllowAutoPurchaseOnUse = true
			},
			Uav = new RaidOpsFireSupportServerConfig.UavSettings
			{
				DurationSeconds = 480,
				RangeMeters = 200f,
				ScanIntervalSeconds = 5f
			},
			FocusedSweep = new RaidOpsFireSupportServerConfig.UavSettings
			{
				DurationSeconds = 90,
				RangeMeters = 100f,
				ScanIntervalSeconds = 0.75f
			},
			Extraction = new RaidOpsFireSupportServerConfig.ExtractionSettings
			{
				DispatchDelaySeconds = 0f,
				WaitTimeSeconds = 30,
				ExtractTimeSeconds = 10f,
				SpeedMultiplier = 1f
			},
			PriorityExfil = new RaidOpsFireSupportServerConfig.ExtractionSettings
			{
				DispatchDelaySeconds = 3f,
				WaitTimeSeconds = 20,
				ExtractTimeSeconds = 10f,
				SpeedMultiplier = 1.35f
			},
			A10 = new RaidOpsFireSupportServerConfig.A10Settings(),
			DoublePass = new RaidOpsFireSupportServerConfig.A10Settings
			{
				SecondPassDelaySeconds = 14f
			},
		};
	}
}
