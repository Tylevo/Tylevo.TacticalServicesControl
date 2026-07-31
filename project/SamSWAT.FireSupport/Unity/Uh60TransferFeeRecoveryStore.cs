using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

/// <summary>
/// Persists the terminal intent for a stash-funded native cargo purchase.
/// A lost Commit/Refund acknowledgement can therefore be retried after a
/// client restart without ever guessing whether EFT accepted the cargo.
/// </summary>
internal static class Uh60TransferFeeRecoveryStore
{
	private const string CommitAction = "Commit";
	private const string RefundAction = "Refund";
	private const int MaxAmountRoubles = 10_000_000;
	private const int MaxTransactionIdLength = 128;

	private static readonly object s_gate = new();
	private static readonly SemaphoreSlim s_retryGate = new(1, 1);
	private static List<RecoveryIntent> s_intents = new();
	private static bool s_loaded;
	private static bool s_storageHealthy = true;

	internal static bool PersistRefundIntent(
		string profileId,
		string transactionId,
		int amountRoubles,
		bool notFoundIsSafe,
		string trigger)
	{
		return PersistIntent(
			profileId,
			transactionId,
			amountRoubles,
			RefundAction,
			notFoundIsSafe,
			trigger);
	}

	internal static bool PersistCommitIntent(
		string profileId,
		string transactionId,
		int amountRoubles,
		string trigger)
	{
		return PersistIntent(
			profileId,
			transactionId,
			amountRoubles,
			CommitAction,
			notFoundIsSafe: false,
			trigger);
	}

	internal static bool CanStartNewTransaction(
		string profileId,
		out string reason)
	{
		reason = string.Empty;
		string normalizedProfileId = profileId?.Trim() ?? string.Empty;
		lock (s_gate)
		{
			if (!EnsureLoadedLocked())
			{
				reason =
					"The local UH-60 fee recovery journal is unavailable.";
				return false;
			}

			if (HasQuarantinedJournalLocked())
			{
				reason =
					"The local UH-60 fee recovery journal is quarantined. Check the TSC config and server log before another stash-funded transfer.";
				return false;
			}

			if (!s_storageHealthy)
			{
				reason =
					"The local UH-60 fee recovery journal could not be saved.";
				return false;
			}

			if (s_intents.Any(intent =>
				    string.Equals(
					    intent.ProfileId,
					    normalizedProfileId,
					    StringComparison.Ordinal)))
			{
				reason =
					"A prior UH-60 stash payment is still being reconciled. Check the TSC server log before retrying.";
				return false;
			}
		}

		return true;
	}

	internal static async UniTask<int> RetryMatchingProfileAsync(
		string profileId,
		string trigger)
	{
		string normalizedProfileId = profileId?.Trim() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(normalizedProfileId) ||
		    !FireSupportServerConfigClient.IsAuthenticatedProfile(
			    normalizedProfileId))
		{
			return 0;
		}

		await s_retryGate.WaitAsync();
		try
		{
			List<RecoveryIntent> snapshot;
			lock (s_gate)
			{
				if (!EnsureLoadedLocked())
				{
					return 0;
				}

				snapshot = s_intents
					.Where(intent =>
						string.Equals(
							intent.ProfileId,
							normalizedProfileId,
							StringComparison.Ordinal))
					.ToList();
			}

			foreach (RecoveryIntent intent in snapshot)
			{
				if (!FireSupportServerConfigClient.IsAuthenticatedProfile(
					    normalizedProfileId))
				{
					break;
				}

				await ResolveIntentAsync(intent, trigger);
			}

			lock (s_gate)
			{
				return s_intents.Count(intent =>
					string.Equals(
						intent.ProfileId,
						normalizedProfileId,
						StringComparison.Ordinal));
			}
		}
		finally
		{
			s_retryGate.Release();
		}
	}

	internal static async UniTask<bool> TryResolveIntentAsync(
		string profileId,
		string transactionId,
		string trigger)
	{
		string normalizedProfileId = profileId?.Trim() ?? string.Empty;
		string normalizedTransactionId =
			transactionId?.Trim() ?? string.Empty;
		await s_retryGate.WaitAsync();
		try
		{
			RecoveryIntent intent;
			lock (s_gate)
			{
				if (!EnsureLoadedLocked())
				{
					return false;
				}

				intent = s_intents.FirstOrDefault(candidate =>
					string.Equals(
						candidate.ProfileId,
						normalizedProfileId,
						StringComparison.Ordinal) &&
					string.Equals(
						candidate.TransactionId,
						normalizedTransactionId,
						StringComparison.Ordinal));
			}

			// Another retry may have received the terminal acknowledgement
			// while this caller waited for the gate.
			return intent == null ||
			       await ResolveIntentAsync(intent, trigger);
		}
		finally
		{
			s_retryGate.Release();
		}
	}

	private static bool PersistIntent(
		string profileId,
		string transactionId,
		int amountRoubles,
		string targetAction,
		bool notFoundIsSafe,
		string trigger)
	{
		string normalizedProfileId = profileId?.Trim() ?? string.Empty;
		string normalizedTransactionId =
			transactionId?.Trim() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(normalizedProfileId) ||
		    string.IsNullOrWhiteSpace(normalizedTransactionId) ||
		    normalizedTransactionId.Length > MaxTransactionIdLength ||
		    amountRoubles is <= 0 or > MaxAmountRoubles)
		{
			FireSupportPlugin.LogSource?.LogError(
				$"TSC refused to persist an invalid UH-60 fee recovery intent. transaction={normalizedTransactionId} trigger={trigger}");
			return false;
		}

		lock (s_gate)
		{
			if (!EnsureLoadedLocked() || HasQuarantinedJournalLocked())
			{
				return false;
			}

			var next = new List<RecoveryIntent>(s_intents);
			int existingIndex = next.FindIndex(intent =>
				string.Equals(
					intent.TransactionId,
					normalizedTransactionId,
					StringComparison.Ordinal));
			if (existingIndex >= 0)
			{
				RecoveryIntent existing = next[existingIndex];
				if (!string.Equals(
					    existing.ProfileId,
					    normalizedProfileId,
					    StringComparison.Ordinal) ||
				    existing.AmountRoubles != amountRoubles)
				{
					FireSupportPlugin.LogSource?.LogError(
						$"TSC detected a conflicting UH-60 fee recovery intent. transaction={normalizedTransactionId}");
					return false;
				}

				// A known native success is irreversible: Commit always wins
				// and can never be downgraded to Refund.
				string effectiveAction =
					string.Equals(
						existing.TargetAction,
						CommitAction,
						StringComparison.Ordinal) ||
					string.Equals(
						targetAction,
						CommitAction,
						StringComparison.Ordinal)
						? CommitAction
						: RefundAction;
				next[existingIndex] = new RecoveryIntent
				{
					ProfileId = existing.ProfileId,
					TransactionId = existing.TransactionId,
					AmountRoubles = existing.AmountRoubles,
					TargetAction = effectiveAction,
					NotFoundIsSafe =
						string.Equals(
							effectiveAction,
							RefundAction,
							StringComparison.Ordinal) &&
						existing.NotFoundIsSafe &&
						notFoundIsSafe,
					CreatedUtc = existing.CreatedUtc
				};
			}
			else
			{
				next.Add(new RecoveryIntent
				{
					ProfileId = normalizedProfileId,
					TransactionId = normalizedTransactionId,
					AmountRoubles = amountRoubles,
					TargetAction = targetAction,
					NotFoundIsSafe = notFoundIsSafe,
					CreatedUtc = DateTimeOffset.UtcNow
				});
			}

			bool saved = TryWriteJournalLocked(next, trigger);
			// Keep the intent in memory even if disk persistence failed so the
			// current process can still attempt the idempotent terminal action.
			s_intents = next;
			return saved;
		}
	}

	private static async UniTask<bool> ResolveIntentAsync(
		RecoveryIntent intent,
		string trigger)
	{
		FireSupportUh60TransferFeeResponse response =
			string.Equals(
				intent.TargetAction,
				CommitAction,
				StringComparison.Ordinal)
				? await FireSupportServerConfigClient
					.CommitUh60TransferFeeAsync(
						intent.ProfileId,
						intent.TransactionId,
						intent.AmountRoubles)
				: await FireSupportServerConfigClient
					.RefundUh60TransferFeeAsync(
						intent.ProfileId,
						intent.TransactionId,
						intent.AmountRoubles);

		bool terminalState =
			string.Equals(
				response?.State,
				"Committed",
				StringComparison.OrdinalIgnoreCase) ||
			string.Equals(
				response?.State,
				"Refunded",
				StringComparison.OrdinalIgnoreCase);
		bool safeNotFound =
			intent.NotFoundIsSafe &&
			string.Equals(
				response?.Reason,
				"FeeTransactionNotFound",
				StringComparison.OrdinalIgnoreCase);
		if (!terminalState && !safeNotFound)
		{
			FireSupportPlugin.LogSource?.LogWarning(
				$"UH-60 fee recovery remains pending. action={intent.TargetAction} transaction={intent.TransactionId} state={response?.State ?? "<none>"} reason={response?.Reason ?? "<none>"} trigger={trigger}");
			return false;
		}

		lock (s_gate)
		{
			if (!EnsureLoadedLocked())
			{
				return false;
			}

			var next = s_intents
				.Where(candidate => !Matches(candidate, intent))
				.ToList();
			if (next.Count == s_intents.Count)
			{
				return true;
			}

			if (!TryWriteJournalLocked(next, trigger))
			{
				return false;
			}

			s_intents = next;
		}

		FireSupportPlugin.LogSource?.LogInfo(
			$"UH-60 fee recovery completed. action={intent.TargetAction} transaction={intent.TransactionId} state={response?.State ?? "<not-found>"} trigger={trigger}");
		return true;
	}

	private static bool EnsureLoadedLocked()
	{
		if (s_loaded)
		{
			return true;
		}

		if (PluginSettings.Uh60TransferFeeRecoveryJournal == null)
		{
			s_storageHealthy = false;
			return false;
		}

		string raw =
			PluginSettings.Uh60TransferFeeRecoveryJournal.Value ?? "[]";
		try
		{
			List<RecoveryIntent> loaded =
				JsonConvert.DeserializeObject<List<RecoveryIntent>>(raw) ??
				new List<RecoveryIntent>();
			if (!IsValidJournal(loaded))
			{
				throw new JsonSerializationException(
					"The journal contained an invalid or duplicate recovery intent.");
			}

			s_intents = loaded;
			s_loaded = true;
			return true;
		}
		catch (Exception ex)
		{
			QuarantineCorruptJournalLocked(raw, ex);
			s_intents = new List<RecoveryIntent>();
			s_loaded = true;
			return true;
		}
	}

	private static bool IsValidJournal(List<RecoveryIntent> intents)
	{
		var transactionIds = new HashSet<string>(StringComparer.Ordinal);
		return intents.All(intent =>
			intent != null &&
			!string.IsNullOrWhiteSpace(intent.ProfileId) &&
			!string.IsNullOrWhiteSpace(intent.TransactionId) &&
			intent.TransactionId.Length <= MaxTransactionIdLength &&
			intent.AmountRoubles is > 0 and <= MaxAmountRoubles &&
			(string.Equals(
				 intent.TargetAction,
				 CommitAction,
				 StringComparison.Ordinal) ||
			 string.Equals(
				 intent.TargetAction,
				 RefundAction,
				 StringComparison.Ordinal)) &&
			(!intent.NotFoundIsSafe ||
			 string.Equals(
				 intent.TargetAction,
				 RefundAction,
				 StringComparison.Ordinal)) &&
			transactionIds.Add(intent.TransactionId));
	}

	private static void QuarantineCorruptJournalLocked(
		string raw,
		Exception exception)
	{
		try
		{
			var quarantine = new CorruptJournalQuarantine
			{
				CapturedUtc = DateTimeOffset.UtcNow,
				Payload = raw,
				Previous =
					PluginSettings.Uh60TransferFeeRecoveryQuarantine?.Value
			};
			PluginSettings.Uh60TransferFeeRecoveryQuarantine.Value =
				JsonConvert.SerializeObject(quarantine);
			PluginSettings.Uh60TransferFeeRecoveryQuarantine.ConfigFile
				.Save();
			PluginSettings.Uh60TransferFeeRecoveryJournal.Value = "[]";
			PluginSettings.Uh60TransferFeeRecoveryJournal.ConfigFile.Save();
		}
		catch (Exception saveException)
		{
			s_storageHealthy = false;
			FireSupportPlugin.LogSource?.LogError(
				$"TSC could not quarantine its corrupt UH-60 fee recovery journal. {saveException}");
		}

		FireSupportPlugin.LogSource?.LogError(
			$"TSC quarantined a corrupt UH-60 fee recovery journal and disabled new stash-funded cargo payments. {exception}");
	}

	private static bool HasQuarantinedJournalLocked()
	{
		return !string.IsNullOrWhiteSpace(
			PluginSettings.Uh60TransferFeeRecoveryQuarantine?.Value);
	}

	private static bool TryWriteJournalLocked(
		List<RecoveryIntent> intents,
		string trigger)
	{
		try
		{
			PluginSettings.Uh60TransferFeeRecoveryJournal.Value =
				JsonConvert.SerializeObject(intents);
			PluginSettings.Uh60TransferFeeRecoveryJournal.ConfigFile.Save();
			s_storageHealthy = true;
			return true;
		}
		catch (Exception ex)
		{
			s_storageHealthy = false;
			FireSupportPlugin.LogSource?.LogError(
				$"TSC could not persist its UH-60 fee recovery journal. trigger={trigger} {ex}");
			return false;
		}
	}

	private static bool Matches(
		RecoveryIntent candidate,
		RecoveryIntent expected)
	{
		return string.Equals(
			       candidate.ProfileId,
			       expected.ProfileId,
			       StringComparison.Ordinal) &&
		       string.Equals(
			       candidate.TransactionId,
			       expected.TransactionId,
			       StringComparison.Ordinal) &&
		       candidate.AmountRoubles == expected.AmountRoubles &&
		       string.Equals(
			       candidate.TargetAction,
			       expected.TargetAction,
			       StringComparison.Ordinal);
	}

	private sealed class RecoveryIntent
	{
		public string ProfileId { get; set; }
		public string TransactionId { get; set; }
		public int AmountRoubles { get; set; }
		public string TargetAction { get; set; }
		public bool NotFoundIsSafe { get; set; }
		public DateTimeOffset CreatedUtc { get; set; }
	}

	private sealed class CorruptJournalQuarantine
	{
		public DateTimeOffset CapturedUtc { get; set; }
		public string Payload { get; set; }
		public string Previous { get; set; }
	}
}
