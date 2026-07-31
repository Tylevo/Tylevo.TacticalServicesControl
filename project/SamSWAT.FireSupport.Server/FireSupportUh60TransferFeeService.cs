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
using IOPath = System.IO.Path;

namespace SamSWAT.FireSupport.ArysReloaded;

/// <summary>
/// Server-authoritative stash payment for the native UH-60 cargo-transfer fee.
/// The client supplies the exact RUB quote shown by EFT, then prepares the
/// debit before allowing the stock transfer purchase to proceed.
/// </summary>
[Injectable(InjectionType.Singleton)]
public sealed class FireSupportUh60TransferFeeService(
	ISptLogger<FireSupportUh60TransferFeeService> logger,
	ProfileHelper profileHelper,
	SaveServer saveServer,
	ICloner cloner,
	FireSupportProfileMutationGate profileMutationGate,
	FireSupportUh60TransferFeeJournal journal)
{
	public const string Route = "/tsc/uh60-transfer/fee";

	public void Initialize(string pathToMod)
	{
		journal.Initialize(IOPath.Combine(pathToMod, "storage"));
		logger.Success("TSC UH-60 transfer-fee journal ready.");
	}

	public Task<FireSupportUh60TransferFeeResponse> TryHandleAsync(
		MongoId sessionId,
		FireSupportUh60TransferFeeRequest request)
	{
		return profileMutationGate.RunAsync(
			() => TryHandleSerializedAsync(sessionId, request));
	}

	private async Task<FireSupportUh60TransferFeeResponse>
		TryHandleSerializedAsync(
			MongoId sessionId,
			FireSupportUh60TransferFeeRequest? request)
	{
		if (request == null)
		{
			return Rejected("InvalidRequest");
		}

		string transactionId = request.TransactionId?.Trim() ?? string.Empty;
		if (!IsValidTransactionId(transactionId))
		{
			return Rejected(
				"InvalidTransactionId",
				transactionId,
				request.AmountRoubles);
		}

		if (request.AmountRoubles is <= 0 or >
		    FireSupportUh60TransferFeeJournal.MaxAmountRoubles)
		{
			return Rejected(
				"InvalidFeeAmount",
				transactionId,
				request.AmountRoubles);
		}

		if (!TryResolveAuthenticatedProfile(
			    sessionId,
			    request.ProfileId,
			    out PmcData? pmc,
			    out MongoId saveSessionId,
			    out string profileId,
			    out string profileReason))
		{
			return Rejected(
				profileReason,
				transactionId,
				request.AmountRoubles);
		}

		string action = request.Action?.Trim() ?? string.Empty;
		bool exists = journal.TryGet(
			transactionId,
			out FireSupportUh60TransferFeeRecord? record);
		if (exists &&
		    (!string.Equals(
			     record!.ProfileId,
			     profileId,
			     StringComparison.OrdinalIgnoreCase) ||
		     record.AmountRoubles != request.AmountRoubles))
		{
			return CreateResponse(
				pmc,
				false,
				"FeeTransactionConflict",
				record.State,
				transactionId,
				record.AmountRoubles);
		}

		if (string.Equals(action, "Status", StringComparison.OrdinalIgnoreCase))
		{
			if (!exists)
			{
				return CreateResponse(
					pmc,
					false,
					"FeeTransactionNotFound",
					string.Empty,
					transactionId,
					request.AmountRoubles);
			}

			record = TryRecoverTerminalJournalState(pmc, record!);
			return CreateResponse(
				pmc,
				true,
				"Status",
				record.State,
				transactionId,
				record.AmountRoubles);
		}

		if (string.Equals(action, "Prepare", StringComparison.OrdinalIgnoreCase))
		{
			return await PrepareAsync(
				pmc,
				saveSessionId,
				profileId,
				transactionId,
				request.AmountRoubles,
				record);
		}

		if (!exists)
		{
			return CreateResponse(
				pmc,
				false,
				"FeeTransactionNotFound",
				string.Empty,
				transactionId,
				request.AmountRoubles);
		}

		if (string.Equals(action, "Commit", StringComparison.OrdinalIgnoreCase))
		{
			return await CommitAsync(
				pmc,
				record!);
		}

		if (string.Equals(action, "Refund", StringComparison.OrdinalIgnoreCase))
		{
			return await RefundAsync(
				pmc,
				saveSessionId,
				record!);
		}

		return CreateResponse(
			pmc,
			false,
			"InvalidAction",
			record!.State,
			transactionId,
			record.AmountRoubles);
	}

	private async Task<FireSupportUh60TransferFeeResponse> PrepareAsync(
		PmcData pmc,
		MongoId saveSessionId,
		string profileId,
		string transactionId,
		int amountRoubles,
		FireSupportUh60TransferFeeRecord? record)
	{
		if (record != null)
		{
			if (string.Equals(
				    record.State,
				    FireSupportUh60TransferFeeJournal.PreparedState,
				    StringComparison.Ordinal))
			{
				return CreateResponse(
					pmc,
					true,
					"AlreadyPrepared",
					record.State,
					transactionId,
					record.AmountRoubles);
			}

			if (string.Equals(
				    record.State,
				    FireSupportUh60TransferFeeJournal.CommittedState,
				    StringComparison.Ordinal))
			{
				return CreateResponse(
					pmc,
					true,
					"AlreadyCommitted",
					record.State,
					transactionId,
					record.AmountRoubles);
			}

			if (string.Equals(
				    record.State,
				    FireSupportUh60TransferFeeJournal.RefundedState,
				    StringComparison.Ordinal) ||
			    string.Equals(
				    record.State,
				    FireSupportUh60TransferFeeJournal.RefundPendingState,
				    StringComparison.Ordinal))
			{
				record = TryRecoverTerminalJournalState(pmc, record);
				return CreateResponse(
					pmc,
					false,
					"FeeTransactionRefunded",
					record.State,
					transactionId,
					record.AmountRoubles);
			}

			return await ResumeDebitAsync(pmc, saveSessionId, record);
		}

		if (pmc.Inventory?.Items == null)
		{
			return CreateResponse(
				pmc,
				false,
				"ProfileInventoryUnavailable",
				string.Empty,
				transactionId,
				amountRoubles);
		}

		int stashBalance = CountStashRoubles(pmc);
		if (stashBalance < amountRoubles)
		{
			return CreateResponse(
				pmc,
				false,
				"InsufficientRoubles",
				string.Empty,
				transactionId,
				amountRoubles);
		}

		if (!TryBuildDebitPlan(
			    pmc,
			    amountRoubles,
			    out List<FireSupportUh60TransferFeeDebit> debits,
			    out string expectedPostDebitFingerprint))
		{
			return CreateResponse(
				pmc,
				false,
				"PaymentMutationFailed",
				string.Empty,
				transactionId,
				amountRoubles);
		}

		DateTimeOffset now = DateTimeOffset.UtcNow;
		var pendingRecord = new FireSupportUh60TransferFeeRecord
		{
			TransactionId = transactionId,
			ProfileId = profileId,
			AmountRoubles = amountRoubles,
			State = FireSupportUh60TransferFeeJournal.DebitPendingState,
			CreatedUtc = now,
			UpdatedUtc = now,
			PreDebitFingerprint = ComputeRoubleFingerprint(pmc),
			ExpectedPostDebitFingerprint =
				expectedPostDebitFingerprint,
			Debits = debits
		};
		if (!journal.TryCreate(
			    pendingRecord,
			    out FireSupportUh60TransferFeeRecord? current,
			    out string createReason))
		{
			if (current != null &&
			    string.Equals(
				    current.ProfileId,
				    profileId,
				    StringComparison.OrdinalIgnoreCase) &&
			    current.AmountRoubles == amountRoubles)
			{
				return await PrepareAsync(
					pmc,
					saveSessionId,
					profileId,
					transactionId,
					amountRoubles,
					current);
			}

			return CreateResponse(
				pmc,
				false,
				createReason,
				string.Empty,
				transactionId,
				amountRoubles);
		}

		return await ResumeDebitAsync(
			pmc,
			saveSessionId,
			current!);
	}

	private async Task<FireSupportUh60TransferFeeResponse> ResumeDebitAsync(
		PmcData pmc,
		MongoId saveSessionId,
		FireSupportUh60TransferFeeRecord record)
	{
		string currentFingerprint = ComputeRoubleFingerprint(pmc);
		if (string.Equals(
			    currentFingerprint,
			    record.ExpectedPostDebitFingerprint,
			    StringComparison.OrdinalIgnoreCase))
		{
			return FinalizePrepared(pmc, record, "RecoveredPrepared");
		}

		if (!string.Equals(
			    currentFingerprint,
			    record.PreDebitFingerprint,
			    StringComparison.OrdinalIgnoreCase))
		{
			return CreateResponse(
				pmc,
				false,
				"FeePaymentStateAmbiguous",
				record.State,
				record.TransactionId,
				record.AmountRoubles);
		}

		if (pmc.Inventory?.Items == null)
		{
			return CreateResponse(
				pmc,
				false,
				"ProfileInventoryUnavailable",
				record.State,
				record.TransactionId,
				record.AmountRoubles);
		}

		List<Item>? inventorySnapshot = cloner.Clone(pmc.Inventory.Items);
		if (inventorySnapshot == null)
		{
			return CreateResponse(
				pmc,
				false,
				"PaymentSnapshotFailed",
				record.State,
				record.TransactionId,
				record.AmountRoubles);
		}

		try
		{
			int charged = ApplyDebitPlan(pmc, record.Debits);
			if (charged != record.AmountRoubles ||
			    !string.Equals(
				    ComputeRoubleFingerprint(pmc),
				    record.ExpectedPostDebitFingerprint,
				    StringComparison.OrdinalIgnoreCase))
			{
				pmc.Inventory.Items = inventorySnapshot;
				return CreateResponse(
					pmc,
					false,
					"PaymentMutationFailed",
					record.State,
					record.TransactionId,
					record.AmountRoubles);
			}
		}
		catch (Exception exception)
		{
			pmc.Inventory.Items = inventorySnapshot;
			logger.Error(
				$"TSC UH-60 stash fee debit failed transactionId={FormatId(record.TransactionId)} profileId={FormatId(record.ProfileId)}",
				exception);
			return CreateResponse(
				pmc,
				false,
				"PaymentMutationFailed",
				record.State,
				record.TransactionId,
				record.AmountRoubles);
		}

		try
		{
			await saveServer.SaveProfileAsync(saveSessionId);
		}
		catch (Exception exception)
		{
			logger.Error(
				$"TSC UH-60 stash fee save failed transactionId={FormatId(record.TransactionId)} profileId={FormatId(record.ProfileId)}",
				exception);
			bool rolledBack = await TryRollbackDebitAsync(
				pmc,
				saveSessionId,
				inventorySnapshot,
				record.TransactionId);
			return CreateResponse(
				pmc,
				false,
				rolledBack
					? "ProfileSaveFailed"
					: "PaymentRollbackFailed",
				record.State,
				record.TransactionId,
				record.AmountRoubles);
		}

		return FinalizePrepared(pmc, record, "Prepared");
	}

	private FireSupportUh60TransferFeeResponse FinalizePrepared(
		PmcData pmc,
		FireSupportUh60TransferFeeRecord record,
		string successReason)
	{
		record.State = FireSupportUh60TransferFeeJournal.PreparedState;
		record.UpdatedUtc = DateTimeOffset.UtcNow;
		if (!journal.TrySave(record, out string journalReason))
		{
			return CreateResponse(
				pmc,
				false,
				journalReason,
				FireSupportUh60TransferFeeJournal.DebitPendingState,
				record.TransactionId,
				record.AmountRoubles);
		}

		logger.Success(
			$"TSC UH-60 transfer fee prepared transactionId={FormatId(record.TransactionId)} profileId={FormatId(record.ProfileId)} amountRoubles={record.AmountRoubles}");
		return CreateResponse(
			pmc,
			true,
			successReason,
			record.State,
			record.TransactionId,
			record.AmountRoubles);
	}

	private Task<FireSupportUh60TransferFeeResponse> CommitAsync(
		PmcData pmc,
		FireSupportUh60TransferFeeRecord record)
	{
		record = TryRecoverTerminalJournalState(pmc, record);
		if (string.Equals(
			    record.State,
			    FireSupportUh60TransferFeeJournal.CommittedState,
			    StringComparison.Ordinal))
		{
			return Task.FromResult(
				CreateResponse(
					pmc,
					true,
					"AlreadyCommitted",
					record.State,
					record.TransactionId,
					record.AmountRoubles));
		}

		if (!string.Equals(
			    record.State,
			    FireSupportUh60TransferFeeJournal.PreparedState,
			    StringComparison.Ordinal))
		{
			string reason = string.Equals(
				record.State,
				FireSupportUh60TransferFeeJournal.RefundedState,
				StringComparison.Ordinal)
				? "FeeTransactionRefunded"
				: "FeeTransactionNotPrepared";
			return Task.FromResult(
				CreateResponse(
					pmc,
					false,
					reason,
					record.State,
					record.TransactionId,
					record.AmountRoubles));
		}

		record.State = FireSupportUh60TransferFeeJournal.CommittedState;
		record.UpdatedUtc = DateTimeOffset.UtcNow;
		if (!journal.TrySave(record, out string journalReason))
		{
			return Task.FromResult(
				CreateResponse(
					pmc,
					false,
					journalReason,
					FireSupportUh60TransferFeeJournal.PreparedState,
					record.TransactionId,
					record.AmountRoubles));
		}

		logger.Success(
			$"TSC UH-60 transfer fee committed transactionId={FormatId(record.TransactionId)} profileId={FormatId(record.ProfileId)} amountRoubles={record.AmountRoubles}");
		return Task.FromResult(
			CreateResponse(
				pmc,
				true,
				"Committed",
				record.State,
				record.TransactionId,
				record.AmountRoubles));
	}

	private async Task<FireSupportUh60TransferFeeResponse> RefundAsync(
		PmcData pmc,
		MongoId saveSessionId,
		FireSupportUh60TransferFeeRecord record)
	{
		record = TryRecoverTerminalJournalState(pmc, record);
		if (string.Equals(
			    record.State,
			    FireSupportUh60TransferFeeJournal.RefundedState,
			    StringComparison.Ordinal))
		{
			return CreateResponse(
				pmc,
				true,
				"AlreadyRefunded",
				record.State,
				record.TransactionId,
				record.AmountRoubles);
		}

		if (string.Equals(
			    record.State,
			    FireSupportUh60TransferFeeJournal.CommittedState,
			    StringComparison.Ordinal))
		{
			return CreateResponse(
				pmc,
				false,
				"FeeTransactionCommitted",
				record.State,
				record.TransactionId,
				record.AmountRoubles);
		}

		if (string.Equals(
			    record.State,
			    FireSupportUh60TransferFeeJournal.DebitPendingState,
			    StringComparison.Ordinal))
		{
			string currentFingerprint = ComputeRoubleFingerprint(pmc);
			if (string.Equals(
				    currentFingerprint,
				    record.PreDebitFingerprint,
				    StringComparison.OrdinalIgnoreCase))
			{
				// The write-ahead entry exists but no debit reached the
				// profile. Mark it refunded without minting any RUB.
				record.State =
					FireSupportUh60TransferFeeJournal.RefundedState;
				record.UpdatedUtc = DateTimeOffset.UtcNow;
				if (!journal.TrySave(record, out string noDebitReason))
				{
					return CreateResponse(
						pmc,
						false,
						noDebitReason,
						FireSupportUh60TransferFeeJournal.DebitPendingState,
						record.TransactionId,
						record.AmountRoubles);
				}

				return CreateResponse(
					pmc,
					true,
					"RefundedBeforeDebit",
					record.State,
					record.TransactionId,
					record.AmountRoubles);
			}

			if (!string.Equals(
				    currentFingerprint,
				    record.ExpectedPostDebitFingerprint,
				    StringComparison.OrdinalIgnoreCase))
			{
				return CreateResponse(
					pmc,
					false,
					"FeePaymentStateAmbiguous",
					record.State,
					record.TransactionId,
					record.AmountRoubles);
			}

			record.State =
				FireSupportUh60TransferFeeJournal.PreparedState;
			record.UpdatedUtc = DateTimeOffset.UtcNow;
			if (!journal.TrySave(record, out string recoverReason))
			{
				return CreateResponse(
					pmc,
					false,
					recoverReason,
					FireSupportUh60TransferFeeJournal.DebitPendingState,
					record.TransactionId,
					record.AmountRoubles);
			}
		}

		if (string.Equals(
			    record.State,
			    FireSupportUh60TransferFeeJournal.RefundPendingState,
			    StringComparison.Ordinal))
		{
			return await ResumeRefundAsync(pmc, saveSessionId, record);
		}

		if (!string.Equals(
			    record.State,
			    FireSupportUh60TransferFeeJournal.PreparedState,
			    StringComparison.Ordinal))
		{
			return CreateResponse(
				pmc,
				false,
				"FeeTransactionNotPrepared",
				record.State,
				record.TransactionId,
				record.AmountRoubles);
		}

		if (!TryBuildRefundPlan(
			    pmc,
			    record,
			    out List<FireSupportUh60TransferFeeRefundCredit> refundCredits,
			    out string expectedPostRefundFingerprint,
			    out string refundPlanReason))
		{
			return CreateResponse(
				pmc,
				false,
				refundPlanReason,
				record.State,
				record.TransactionId,
				record.AmountRoubles);
		}

		record.PreRefundFingerprint = ComputeRoubleFingerprint(pmc);
		record.ExpectedPostRefundFingerprint =
			expectedPostRefundFingerprint;
		record.RefundCredits = refundCredits;
		record.State =
			FireSupportUh60TransferFeeJournal.RefundPendingState;
		record.UpdatedUtc = DateTimeOffset.UtcNow;
		if (!journal.TrySave(record, out string journalReason))
		{
			return CreateResponse(
				pmc,
				false,
				journalReason,
				FireSupportUh60TransferFeeJournal.PreparedState,
				record.TransactionId,
				record.AmountRoubles);
		}

		return await ResumeRefundAsync(pmc, saveSessionId, record);
	}

	private async Task<FireSupportUh60TransferFeeResponse> ResumeRefundAsync(
		PmcData pmc,
		MongoId saveSessionId,
		FireSupportUh60TransferFeeRecord record)
	{
		string currentFingerprint = ComputeRoubleFingerprint(pmc);
		if (string.Equals(
			    currentFingerprint,
			    record.ExpectedPostRefundFingerprint,
			    StringComparison.OrdinalIgnoreCase))
		{
			return FinalizeRefunded(pmc, record, "RecoveredRefund");
		}

		if (!string.Equals(
			    currentFingerprint,
			    record.PreRefundFingerprint,
			    StringComparison.OrdinalIgnoreCase))
		{
			return CreateResponse(
				pmc,
				false,
				"FeeRefundStateAmbiguous",
				record.State,
				record.TransactionId,
				record.AmountRoubles);
		}

		if (!TryApplyRefundPlan(
			    pmc,
			    record.RefundCredits,
			    out List<FireSupportUh60AppliedRefund> appliedRefunds,
			    out string mutationReason))
		{
			return CreateResponse(
				pmc,
				false,
				mutationReason,
				record.State,
				record.TransactionId,
				record.AmountRoubles);
		}

		if (!string.Equals(
			    ComputeRoubleFingerprint(pmc),
			    record.ExpectedPostRefundFingerprint,
			    StringComparison.OrdinalIgnoreCase))
		{
			UndoAppliedRefunds(pmc, appliedRefunds);
			return CreateResponse(
				pmc,
				false,
				"RefundMutationFailed",
				record.State,
				record.TransactionId,
				record.AmountRoubles);
		}

		try
		{
			await saveServer.SaveProfileAsync(saveSessionId);
		}
		catch (Exception exception)
		{
			logger.Error(
				$"TSC UH-60 stash fee refund save failed transactionId={FormatId(record.TransactionId)} profileId={FormatId(record.ProfileId)}",
				exception);
			bool rolledBack = await TryRollbackRefundAsync(
				pmc,
				saveSessionId,
				appliedRefunds,
				record.TransactionId);
			return CreateResponse(
				pmc,
				false,
				rolledBack
					? "ProfileSaveFailed"
					: "RefundRollbackFailed",
				record.State,
				record.TransactionId,
				record.AmountRoubles);
		}

		return FinalizeRefunded(pmc, record, "Refunded");
	}

	private FireSupportUh60TransferFeeResponse FinalizeRefunded(
		PmcData pmc,
		FireSupportUh60TransferFeeRecord record,
		string successReason)
	{
		record.State = FireSupportUh60TransferFeeJournal.RefundedState;
		record.UpdatedUtc = DateTimeOffset.UtcNow;
		if (!journal.TrySave(record, out string journalReason))
		{
			return CreateResponse(
				pmc,
				false,
				journalReason,
				FireSupportUh60TransferFeeJournal.RefundPendingState,
				record.TransactionId,
				record.AmountRoubles);
		}

		logger.Warning(
			$"TSC UH-60 transfer fee refunded transactionId={FormatId(record.TransactionId)} profileId={FormatId(record.ProfileId)} amountRoubles={record.AmountRoubles}");
		return CreateResponse(
			pmc,
			true,
			successReason,
			record.State,
			record.TransactionId,
			record.AmountRoubles);
	}

	private FireSupportUh60TransferFeeRecord TryRecoverTerminalJournalState(
		PmcData pmc,
		FireSupportUh60TransferFeeRecord record)
	{
		string fingerprint = ComputeRoubleFingerprint(pmc);
		string? recoveredState = null;
		if (string.Equals(
			    record.State,
			    FireSupportUh60TransferFeeJournal.DebitPendingState,
			    StringComparison.Ordinal) &&
		    string.Equals(
			    fingerprint,
			    record.ExpectedPostDebitFingerprint,
			    StringComparison.OrdinalIgnoreCase))
		{
			recoveredState =
				FireSupportUh60TransferFeeJournal.PreparedState;
		}
		else if (string.Equals(
			         record.State,
			         FireSupportUh60TransferFeeJournal.RefundPendingState,
			         StringComparison.Ordinal) &&
		         string.Equals(
			         fingerprint,
			         record.ExpectedPostRefundFingerprint,
			         StringComparison.OrdinalIgnoreCase))
		{
			recoveredState =
				FireSupportUh60TransferFeeJournal.RefundedState;
		}

		if (recoveredState == null)
		{
			return record;
		}

		string priorState = record.State;
		record.State = recoveredState;
		record.UpdatedUtc = DateTimeOffset.UtcNow;
		if (!journal.TrySave(record, out _))
		{
			record.State = priorState;
		}

		return record;
	}

	private bool TryBuildDebitPlan(
		PmcData pmc,
		int amountRoubles,
		out List<FireSupportUh60TransferFeeDebit> debits,
		out string expectedPostDebitFingerprint)
	{
		debits = new List<FireSupportUh60TransferFeeDebit>();
		expectedPostDebitFingerprint = string.Empty;
		List<Item> stacks = GetStashRoubleStacks(pmc).ToList();
		var projectedCounts = stacks.ToDictionary(
			stack => stack.Id.ToString(),
			GetStackCount,
			StringComparer.OrdinalIgnoreCase);
		int remaining = amountRoubles;
		foreach (Item stack in stacks)
		{
			if (remaining <= 0)
			{
				break;
			}

			int debitAmount = Math.Min(
				projectedCounts[stack.Id.ToString()],
				remaining);
			if (debitAmount <= 0)
			{
				continue;
			}

			Item? originalItem = cloner.Clone(stack);
			if (originalItem == null)
			{
				return false;
			}

			debits.Add(new FireSupportUh60TransferFeeDebit
			{
				OriginalItem = originalItem,
				AmountRoubles = debitAmount
			});
			projectedCounts[stack.Id.ToString()] -= debitAmount;
			remaining -= debitAmount;
		}

		if (remaining != 0)
		{
			return false;
		}

		expectedPostDebitFingerprint = ComputeRoubleFingerprint(
			projectedCounts
				.Where(pair => pair.Value > 0)
				.Select(pair =>
					new KeyValuePair<string, int>(
						pair.Key.ToLowerInvariant(),
						pair.Value)));
		return true;
	}

	private static int ApplyDebitPlan(
		PmcData pmc,
		IEnumerable<FireSupportUh60TransferFeeDebit> debits)
	{
		int charged = 0;
		foreach (FireSupportUh60TransferFeeDebit debit in debits)
		{
			Item? original = debit.OriginalItem;
			Item? current = FindInventoryItem(
				pmc,
				original?.Id.ToString());
			if (original == null ||
			    current == null ||
			    !IsStashRouble(pmc, current))
			{
				throw new InvalidOperationException(
					"UH-60 fee debit target is no longer a stash RUB stack.");
			}

			int stackCount = GetStackCount(current);
			if (debit.AmountRoubles <= 0 ||
			    stackCount < debit.AmountRoubles)
			{
				throw new InvalidOperationException(
					"UH-60 fee debit target no longer contains the quoted RUB amount.");
			}

			if (stackCount == debit.AmountRoubles)
			{
				RemoveItemAndChildren(pmc, current);
			}
			else
			{
				current.Upd ??= new Upd();
				current.Upd.StackObjectsCount =
					stackCount - debit.AmountRoubles;
			}

			charged = checked(charged + debit.AmountRoubles);
		}

		return charged;
	}

	private bool TryBuildRefundPlan(
		PmcData pmc,
		FireSupportUh60TransferFeeRecord record,
		out List<FireSupportUh60TransferFeeRefundCredit> credits,
		out string expectedPostRefundFingerprint,
		out string reason)
	{
		credits = new List<FireSupportUh60TransferFeeRefundCredit>();
		expectedPostRefundFingerprint = string.Empty;
		reason = string.Empty;
		var projectedCounts = GetStashRoubleStacks(pmc).ToDictionary(
			stack => stack.Id.ToString(),
			GetStackCount,
			StringComparer.OrdinalIgnoreCase);
		int plannedAmount = 0;

		foreach (FireSupportUh60TransferFeeDebit debit in record.Debits)
		{
			Item? original = debit.OriginalItem;
			if (original == null || debit.AmountRoubles <= 0)
			{
				reason = "RefundPlanUnavailable";
				return false;
			}

			string originalId = original.Id.ToString();
			Item? current = FindInventoryItem(pmc, originalId);
			if (current != null && IsStashRouble(pmc, current))
			{
				int beforeCount = GetStackCount(current);
				int originalCount = GetStackCount(original);
				if ((long)beforeCount + debit.AmountRoubles >
				    originalCount)
				{
					// Never overstack a currency item. The original snapshot
					// was already a valid EFT stack, so its count is a safe
					// upper bound even if another serialized TSC purchase
					// consumed more of this stack after fee preparation.
					reason = "RefundStackChanged";
					return false;
				}

				credits.Add(new FireSupportUh60TransferFeeRefundCredit
				{
					TargetItemId = originalId,
					AmountRoubles = debit.AmountRoubles,
					BeforeCount = beforeCount
				});
				projectedCounts[originalId] =
					beforeCount + debit.AmountRoubles;
			}
			else if (current == null &&
			         IsValidRestoredItemParent(pmc, original))
			{
				Item? restoredItem = cloner.Clone(original);
				if (restoredItem == null)
				{
					reason = "RefundPlanUnavailable";
					return false;
				}

				// A later serialized purchase can remove the remainder of a
				// partially debited stack. Restore only this transaction's
				// credit, never the full pre-debit snapshot.
				restoredItem.Upd ??= new Upd();
				restoredItem.Upd.StackObjectsCount =
					debit.AmountRoubles;
				credits.Add(new FireSupportUh60TransferFeeRefundCredit
				{
					TargetItemId = originalId,
					AmountRoubles = debit.AmountRoubles,
					BeforeCount = 0,
					RestoredItem = restoredItem
				});
				projectedCounts[originalId] = debit.AmountRoubles;
			}
			else
			{
				// A conflicting item ID or missing parent means we cannot
				// place the captured RUB stack without risking inventory
				// corruption. Keep the transaction Prepared for a safe retry.
				reason = "RefundPlacementUnavailable";
				return false;
			}

			plannedAmount = checked(plannedAmount + debit.AmountRoubles);
		}

		if (plannedAmount != record.AmountRoubles)
		{
			reason = "RefundPlanMismatch";
			return false;
		}

		expectedPostRefundFingerprint = ComputeRoubleFingerprint(
			projectedCounts.Select(pair =>
				new KeyValuePair<string, int>(
					pair.Key.ToLowerInvariant(),
					pair.Value)));
		return true;
	}

	private static bool TryApplyRefundPlan(
		PmcData pmc,
		IEnumerable<FireSupportUh60TransferFeeRefundCredit> credits,
		out List<FireSupportUh60AppliedRefund> applied,
		out string reason)
	{
		applied = new List<FireSupportUh60AppliedRefund>();
		reason = string.Empty;
		if (pmc.Inventory?.Items == null)
		{
			reason = "ProfileInventoryUnavailable";
			return false;
		}

		try
		{
			foreach (FireSupportUh60TransferFeeRefundCredit credit in credits)
			{
				if (credit.AmountRoubles <= 0)
				{
					throw new InvalidOperationException(
						"Invalid UH-60 refund credit.");
				}

				if (credit.RestoredItem != null)
				{
					if (FindInventoryItem(pmc, credit.TargetItemId) != null ||
					    GetStackCount(credit.RestoredItem) !=
					    credit.AmountRoubles ||
					    !IsValidRestoredItemParent(
						    pmc,
						    credit.RestoredItem))
					{
						throw new InvalidOperationException(
							"UH-60 refund item can no longer be restored safely.");
					}

					pmc.Inventory.Items.Add(credit.RestoredItem);
					applied.Add(new FireSupportUh60AppliedRefund
					{
						ItemId = credit.TargetItemId,
						AddedItem = true
					});
					continue;
				}

				Item? target = FindInventoryItem(
					pmc,
					credit.TargetItemId);
				if (target == null ||
				    !IsStashRouble(pmc, target) ||
				    GetStackCount(target) != credit.BeforeCount)
				{
					throw new InvalidOperationException(
						"UH-60 refund target changed after the refund was journaled.");
				}

				int beforeCount = GetStackCount(target);
				target.Upd ??= new Upd();
				target.Upd.StackObjectsCount = checked(
					beforeCount + credit.AmountRoubles);
				applied.Add(new FireSupportUh60AppliedRefund
				{
					ItemId = credit.TargetItemId,
					BeforeCount = beforeCount
				});
			}

			return true;
		}
		catch
		{
			UndoAppliedRefunds(pmc, applied);
			reason = "RefundMutationFailed";
			return false;
		}
	}

	private static void UndoAppliedRefunds(
		PmcData pmc,
		IEnumerable<FireSupportUh60AppliedRefund> applied)
	{
		foreach (FireSupportUh60AppliedRefund mutation in
		         applied.Reverse())
		{
			Item? item = FindInventoryItem(pmc, mutation.ItemId);
			if (item == null)
			{
				continue;
			}

			if (mutation.AddedItem)
			{
				RemoveItemAndChildren(pmc, item);
				continue;
			}

			item.Upd ??= new Upd();
			item.Upd.StackObjectsCount = mutation.BeforeCount;
		}
	}

	private async Task<bool> TryRollbackDebitAsync(
		PmcData pmc,
		MongoId saveSessionId,
		List<Item> inventorySnapshot,
		string transactionId)
	{
		if (pmc.Inventory == null)
		{
			return false;
		}

		try
		{
			pmc.Inventory.Items = inventorySnapshot;
			await saveServer.SaveProfileAsync(saveSessionId);
			logger.Warning(
				$"TSC UH-60 transfer-fee debit rolled back transactionId={FormatId(transactionId)}");
			return true;
		}
		catch (Exception exception)
		{
			logger.Error(
				$"TSC UH-60 transfer-fee debit rollback save failed transactionId={FormatId(transactionId)}",
				exception);
			return false;
		}
	}

	private async Task<bool> TryRollbackRefundAsync(
		PmcData pmc,
		MongoId saveSessionId,
		List<FireSupportUh60AppliedRefund> applied,
		string transactionId)
	{
		try
		{
			// Refund rollback is deliberately additive/incremental: only the
			// stacks touched by this transaction are reversed.
			UndoAppliedRefunds(pmc, applied);
			await saveServer.SaveProfileAsync(saveSessionId);
			logger.Warning(
				$"TSC UH-60 transfer-fee refund rolled back transactionId={FormatId(transactionId)}");
			return true;
		}
		catch (Exception exception)
		{
			logger.Error(
				$"TSC UH-60 transfer-fee refund rollback save failed transactionId={FormatId(transactionId)}",
				exception);
			return false;
		}
	}

	private bool TryResolveAuthenticatedProfile(
		MongoId sessionId,
		string? requestedProfileId,
		[NotNullWhen(true)] out PmcData? pmc,
		out MongoId saveSessionId,
		out string profileId,
		out string reason)
	{
		pmc = null;
		saveSessionId = default;
		profileId = string.Empty;
		reason = "AuthenticatedSessionRequired";
		if (sessionId.IsEmpty ||
		    string.IsNullOrWhiteSpace(sessionId.ToString()))
		{
			return false;
		}

		try
		{
			pmc = profileHelper.GetPmcProfile(sessionId);
		}
		catch (Exception exception)
		{
			logger.Warning(
				$"TSC UH-60 transfer fee could not resolve authenticated session: {exception.Message}");
			reason = "ProfileNotFound";
			return false;
		}

		if (pmc?.Id == null ||
		    pmc.Id.Value.IsEmpty ||
		    string.IsNullOrWhiteSpace(pmc.Id.Value.ToString()))
		{
			pmc = null;
			reason = "ProfileNotFound";
			return false;
		}

		profileId = pmc.Id.Value.ToString();
		if (string.IsNullOrWhiteSpace(requestedProfileId) ||
		    !string.Equals(
			    profileId,
			    requestedProfileId.Trim(),
			    StringComparison.OrdinalIgnoreCase))
		{
			pmc = null;
			reason = "ProfileMismatch";
			return false;
		}

		saveSessionId =
			pmc.SessionId.HasValue &&
			!pmc.SessionId.Value.IsEmpty &&
			!string.IsNullOrWhiteSpace(pmc.SessionId.Value.ToString())
				? pmc.SessionId.Value
				: sessionId;
		return true;
	}

	private static int CountStashRoubles(PmcData pmc)
	{
		long total = GetStashRoubleStacks(pmc)
			.Sum(item => (long)GetStackCount(item));
		return total >= int.MaxValue ? int.MaxValue : (int)total;
	}

	private static string ComputeRoubleFingerprint(PmcData pmc)
	{
		return ComputeRoubleFingerprint(
			GetStashRoubleStacks(pmc).Select(stack =>
				new KeyValuePair<string, int>(
					stack.Id.ToString().ToLowerInvariant(),
					GetStackCount(stack))));
	}

	private static string ComputeRoubleFingerprint(
		IEnumerable<KeyValuePair<string, int>> stacks)
	{
		var input = new StringBuilder();
		foreach (KeyValuePair<string, int> stack in stacks
			         .Where(pair => pair.Value > 0)
			         .OrderBy(pair => pair.Key, StringComparer.Ordinal))
		{
			input.Append(stack.Key)
				.Append(':')
				.Append(
					stack.Value.ToString(
						System.Globalization.CultureInfo.InvariantCulture))
				.Append('\n');
		}

		return Convert.ToHexString(
			SHA256.HashData(Encoding.UTF8.GetBytes(input.ToString())));
	}

	private static IEnumerable<Item> GetStashRoubleStacks(PmcData pmc)
	{
		BotBaseInventory? inventory = pmc.Inventory;
		List<Item>? items = inventory?.Items;
		if (inventory == null ||
		    items == null ||
		    !inventory.Stash.HasValue)
		{
			yield break;
		}

		var itemsById = new Dictionary<string, Item>(
			StringComparer.OrdinalIgnoreCase);
		foreach (Item item in items.Where(item => item != null))
		{
			itemsById[item.Id.ToString()] = item;
		}

		string stashId = inventory.Stash.Value.ToString();
		foreach (Item item in items)
		{
			if (item != null &&
			    string.Equals(
				    item.Template.ToString(),
				    PaymentCurrencyInfo.RoubleTemplateId,
				    StringComparison.OrdinalIgnoreCase) &&
			    IsDescendantOfStash(item, stashId, itemsById))
			{
				yield return item;
			}
		}
	}

	private static bool IsStashRouble(PmcData pmc, Item item)
	{
		return GetStashRoubleStacks(pmc).Any(stack =>
			string.Equals(
				stack.Id.ToString(),
				item.Id.ToString(),
				StringComparison.OrdinalIgnoreCase));
	}

	private static bool IsValidRestoredItemParent(
		PmcData pmc,
		Item item)
	{
		BotBaseInventory? inventory = pmc.Inventory;
		List<Item>? items = inventory?.Items;
		if (inventory == null ||
		    items == null ||
		    !inventory.Stash.HasValue ||
		    !string.Equals(
			    item.Template.ToString(),
			    PaymentCurrencyInfo.RoubleTemplateId,
			    StringComparison.OrdinalIgnoreCase) ||
		    string.IsNullOrWhiteSpace(item.ParentId))
		{
			return false;
		}

		string stashId = inventory.Stash.Value.ToString();
		if (string.Equals(
			    item.ParentId,
			    stashId,
			    StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		Item? parent = FindInventoryItem(pmc, item.ParentId);
		if (parent == null)
		{
			return false;
		}

		var itemsById = new Dictionary<string, Item>(
			StringComparer.OrdinalIgnoreCase);
		foreach (Item candidate in items.Where(candidate => candidate != null))
		{
			itemsById[candidate.Id.ToString()] = candidate;
		}

		return IsDescendantOfStash(parent, stashId, itemsById);
	}

	private static bool IsDescendantOfStash(
		Item item,
		string stashId,
		IReadOnlyDictionary<string, Item> itemsById)
	{
		string? parentId = item.ParentId;
		while (!string.IsNullOrWhiteSpace(parentId))
		{
			if (string.Equals(
				    parentId,
				    stashId,
				    StringComparison.OrdinalIgnoreCase))
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

	private static Item? FindInventoryItem(
		PmcData pmc,
		string? itemId)
	{
		if (string.IsNullOrWhiteSpace(itemId))
		{
			return null;
		}

		return pmc.Inventory?.Items?.FirstOrDefault(item =>
			item != null &&
			string.Equals(
				item.Id.ToString(),
				itemId,
				StringComparison.OrdinalIgnoreCase));
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

		var idsToRemove = new HashSet<string>(
			StringComparer.OrdinalIgnoreCase);
		CollectDescendantIds(
			item.Id.ToString(),
			items,
			idsToRemove);
		items.RemoveAll(candidate =>
			candidate != null &&
			idsToRemove.Contains(candidate.Id.ToString()));
	}

	private static void CollectDescendantIds(
		string itemId,
		List<Item> items,
		ISet<string> ids)
	{
		if (!ids.Add(itemId))
		{
			return;
		}

		foreach (Item child in items.Where(candidate =>
			         candidate != null &&
			         string.Equals(
				         candidate.ParentId,
				         itemId,
				         StringComparison.OrdinalIgnoreCase)))
		{
			CollectDescendantIds(
				child.Id.ToString(),
				items,
				ids);
		}
	}

	private static bool IsValidTransactionId(string transactionId)
	{
		if (transactionId.Length is 0 or >
		    FireSupportUh60TransferFeeJournal.MaxTransactionIdLength)
		{
			return false;
		}

		return transactionId.All(character =>
			char.IsAsciiLetterOrDigit(character) ||
			character is '-' or '_' or '.' or ':');
	}

	private static FireSupportUh60TransferFeeResponse CreateResponse(
		PmcData pmc,
		bool ok,
		string reason,
		string state,
		string transactionId,
		int amountRoubles)
	{
		return new FireSupportUh60TransferFeeResponse
		{
			Ok = ok,
			Reason = reason,
			State = state,
			TransactionId = transactionId,
			AmountRoubles = amountRoubles,
			StashRoubleBalance = CountStashRoubles(pmc)
		};
	}

	private static FireSupportUh60TransferFeeResponse Rejected(
		string reason,
		string transactionId = "",
		int amountRoubles = 0)
	{
		return new FireSupportUh60TransferFeeResponse
		{
			Ok = false,
			Reason = reason,
			TransactionId = transactionId,
			AmountRoubles = amountRoubles,
			StashRoubleBalance = -1
		};
	}

	private static string FormatId(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return "<none>";
		}

		string trimmed = value.Trim();
		return trimmed.Length <= 8
			? trimmed
			: trimmed[..4] + "..." + trimmed[^4..];
	}
}

internal sealed class FireSupportUh60AppliedRefund
{
	public string ItemId { get; set; } = string.Empty;
	public bool AddedItem { get; set; }
	public int BeforeCount { get; set; }
}
