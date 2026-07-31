using System.Text.Json;

namespace SamSWAT.FireSupport.ArysReloaded;

/// <summary>
/// Durable, bounded routing markers for items staged through the TSC UH-60
/// transfer service. This sidecar never owns the items themselves; losing or
/// rejecting its state deliberately leaves SPT's stock BTR delivery path in
/// control.
/// </summary>
public sealed class FireSupportUh60TransferMarkerStore
{
	public const int MaxItemIdsPerProfile = 4096;
	public const int MaxProfiles = 512;
	public const int MaxDeliveryReceiptsPerProfile = 128;
	public static readonly TimeSpan MarkerLifetime = TimeSpan.FromDays(30);

	private const int CurrentSchemaVersion = 2;
	private const string StateFileName = "tsc-uh60-transfer-markers.json";
	private const string PreparedState = "Prepared";
	private const string MailObservedState = "MailObserved";
	private const string CompletedState = "Completed";

	private static readonly JsonSerializerOptions s_jsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true
	};

	private readonly object _gate = new();
	private string _statePath = string.Empty;
	private FireSupportUh60TransferMarkerState _state = new();

	public string LastLoadWarning { get; private set; } = string.Empty;

	public void Initialize(string storageDirectory)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(storageDirectory);
		Directory.CreateDirectory(storageDirectory);

		lock (_gate)
		{
			_statePath = Path.Combine(storageDirectory, StateFileName);
			LastLoadWarning = string.Empty;
			_state = LoadLocked();
			NormalizeStateLocked();
			PruneExpiredLocked(DateTimeOffset.UtcNow);

			try
			{
				SaveLocked();
			}
			catch (Exception exception)
			{
				LastLoadWarning =
					$"UH-60 marker state loaded in memory but could not be normalized on disk: {exception.Message}";
			}
		}
	}

	public bool TryMark(
		string sessionId,
		string profileId,
		IEnumerable<string> itemIds,
		out int acceptedItemCount,
		out string reason)
	{
		acceptedItemCount = 0;
		reason = string.Empty;

		string normalizedSessionId = NormalizeMongoId(sessionId);
		string normalizedProfileId = NormalizeMongoId(profileId);
		if (string.IsNullOrEmpty(normalizedSessionId) ||
		    string.IsNullOrEmpty(normalizedProfileId))
		{
			reason = "InvalidIdentity";
			return false;
		}

		if (itemIds == null)
		{
			reason = "NoItems";
			return false;
		}

		var normalizedItemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string itemId in itemIds)
		{
			string normalizedItemId = NormalizeMongoId(itemId);
			if (string.IsNullOrEmpty(normalizedItemId))
			{
				reason = "InvalidItemId";
				return false;
			}

			normalizedItemIds.Add(normalizedItemId);
			if (normalizedItemIds.Count > MaxItemIdsPerProfile)
			{
				reason = "TooManyItems";
				return false;
			}
		}

		if (normalizedItemIds.Count == 0)
		{
			reason = "NoItems";
			return false;
		}

		lock (_gate)
		{
			if (string.IsNullOrWhiteSpace(_statePath))
			{
				reason = "MarkerStoreNotInitialized";
				return false;
			}

			DateTimeOffset now = DateTimeOffset.UtcNow;
			PruneExpiredLocked(now);
			FireSupportUh60TransferMarkerState snapshot = CloneState(_state);

			if (!_state.Profiles.TryGetValue(
				    normalizedSessionId,
				    out FireSupportUh60TransferMarkerProfile? profile) ||
			    !string.Equals(
				    profile.ProfileId,
				    normalizedProfileId,
				    StringComparison.OrdinalIgnoreCase))
			{
				if (!_state.Profiles.ContainsKey(normalizedSessionId) &&
				    _state.Profiles.Count >= MaxProfiles)
				{
					reason = "MarkerStoreFull";
					return false;
				}

				profile = new FireSupportUh60TransferMarkerProfile
				{
					ProfileId = normalizedProfileId,
					UpdatedUtc = now,
					ItemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
				};
				_state.Profiles[normalizedSessionId] = profile;
			}

			if (profile.ItemIds.Count + normalizedItemIds.Count(itemId => !profile.ItemIds.Contains(itemId)) >
			    MaxItemIdsPerProfile)
			{
				reason = "MarkerStoreFull";
				return false;
			}

			profile.ItemIds.UnionWith(normalizedItemIds);
			profile.UpdatedUtc = now;

			if (!TrySaveMutationLocked(snapshot, out reason))
			{
				return false;
			}

			acceptedItemCount = normalizedItemIds.Count;
			return true;
		}
	}

	public HashSet<string> GetMarkedItemIds(string sessionId, string profileId)
	{
		string normalizedSessionId = NormalizeMongoId(sessionId);
		string normalizedProfileId = NormalizeMongoId(profileId);
		if (string.IsNullOrEmpty(normalizedSessionId) ||
		    string.IsNullOrEmpty(normalizedProfileId))
		{
			return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		}

		lock (_gate)
		{
			if (!_state.Profiles.TryGetValue(
				    normalizedSessionId,
				    out FireSupportUh60TransferMarkerProfile? profile) ||
			    !string.Equals(
				    profile.ProfileId,
				    normalizedProfileId,
				    StringComparison.OrdinalIgnoreCase))
			{
				return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			}

			if (profile.UpdatedUtc < DateTimeOffset.UtcNow - MarkerLifetime)
			{
				FireSupportUh60TransferMarkerState snapshot = CloneState(_state);
				_state.Profiles.Remove(normalizedSessionId);
				TrySaveMutationLocked(snapshot, out _);
				return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			}

			return new HashSet<string>(profile.ItemIds, StringComparer.OrdinalIgnoreCase);
		}
	}

	public bool TryAcknowledge(
		string sessionId,
		string profileId,
		IEnumerable<string> deliveredItemIds,
		out string reason)
	{
		reason = string.Empty;
		string normalizedSessionId = NormalizeMongoId(sessionId);
		string normalizedProfileId = NormalizeMongoId(profileId);
		if (string.IsNullOrEmpty(normalizedSessionId) ||
		    string.IsNullOrEmpty(normalizedProfileId))
		{
			reason = "InvalidIdentity";
			return false;
		}

		var normalizedDeliveredItemIds = new HashSet<string>(
			(deliveredItemIds ?? [])
			.Select(NormalizeMongoId)
			.Where(itemId => !string.IsNullOrEmpty(itemId)),
			StringComparer.OrdinalIgnoreCase);
		if (normalizedDeliveredItemIds.Count == 0)
		{
			return true;
		}

		lock (_gate)
		{
			if (!_state.Profiles.TryGetValue(
				    normalizedSessionId,
				    out FireSupportUh60TransferMarkerProfile? profile) ||
			    !string.Equals(
				    profile.ProfileId,
				    normalizedProfileId,
				    StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}

			FireSupportUh60TransferMarkerState snapshot = CloneState(_state);
			profile.ItemIds.ExceptWith(normalizedDeliveredItemIds);
			if (profile.ItemIds.Count == 0 &&
			    (profile.DeliveryReceipts == null ||
			     profile.DeliveryReceipts.Count == 0))
			{
				_state.Profiles.Remove(normalizedSessionId);
			}
			else
			{
				profile.UpdatedUtc = DateTimeOffset.UtcNow;
			}

			return TrySaveMutationLocked(snapshot, out reason);
		}
	}

	public bool TryPrepareDelivery(
		string sessionId,
		string profileId,
		string packageId,
		IEnumerable<string> deliveryItemIds,
		out FireSupportUh60DeliveryReceipt receipt,
		out string reason)
	{
		receipt = new FireSupportUh60DeliveryReceipt();
		reason = string.Empty;

		string normalizedSessionId = NormalizeMongoId(sessionId);
		string normalizedProfileId = NormalizeMongoId(profileId);
		string normalizedPackageId = NormalizeMongoId(packageId);
		var normalizedDeliveryIds = NormalizeItemIds(deliveryItemIds);
		if (string.IsNullOrEmpty(normalizedSessionId) ||
		    string.IsNullOrEmpty(normalizedProfileId) ||
		    string.IsNullOrEmpty(normalizedPackageId))
		{
			reason = "InvalidDeliveryIdentity";
			return false;
		}

		if (normalizedDeliveryIds.Count == 0 ||
		    normalizedDeliveryIds.Count > MaxItemIdsPerProfile)
		{
			reason = "InvalidDeliveryItems";
			return false;
		}

		lock (_gate)
		{
			if (string.IsNullOrWhiteSpace(_statePath))
			{
				reason = "MarkerStoreNotInitialized";
				return false;
			}

			if (!_state.Profiles.TryGetValue(
				    normalizedSessionId,
				    out FireSupportUh60TransferMarkerProfile? profile) ||
			    !string.Equals(
				    profile.ProfileId,
				    normalizedProfileId,
				    StringComparison.OrdinalIgnoreCase))
			{
				reason = "MarkerNotFound";
				return false;
			}

			profile.DeliveryReceipts ??=
				new Dictionary<string, FireSupportUh60DeliveryReceipt>(
					StringComparer.OrdinalIgnoreCase);
			if (profile.DeliveryReceipts.TryGetValue(
				    normalizedPackageId,
				    out FireSupportUh60DeliveryReceipt? existing))
			{
				if (!existing.ItemIds.SetEquals(normalizedDeliveryIds))
				{
					reason = "DeliveryReceiptMismatch";
					return false;
				}

				receipt = CloneReceipt(existing);
				return true;
			}

			if (!normalizedDeliveryIds.Any(profile.ItemIds.Contains))
			{
				reason = "DeliveryItemsNotMarked";
				return false;
			}

			if (profile.DeliveryReceipts.Count >=
			    MaxDeliveryReceiptsPerProfile)
			{
				reason = "DeliveryReceiptStoreFull";
				return false;
			}

			DateTimeOffset now = DateTimeOffset.UtcNow;
			FireSupportUh60TransferMarkerState snapshot = CloneState(_state);
			var prepared = new FireSupportUh60DeliveryReceipt
			{
				PackageId = normalizedPackageId,
				ReceiptToken = CreateReceiptToken(profile),
				State = PreparedState,
				UpdatedUtc = now,
				ItemIds = normalizedDeliveryIds
			};
			profile.DeliveryReceipts[normalizedPackageId] = prepared;
			profile.UpdatedUtc = now;
			if (!TrySaveMutationLocked(snapshot, out reason))
			{
				return false;
			}

			receipt = CloneReceipt(prepared);
			return true;
		}
	}

	public bool TryRecordMailObserved(
		string sessionId,
		string profileId,
		string packageId,
		out string reason)
	{
		return TryTransitionDelivery(
			sessionId,
			profileId,
			packageId,
			MailObservedState,
			deliveredItemIds: null,
			out reason);
	}

	public bool TryCompleteDelivery(
		string sessionId,
		string profileId,
		string packageId,
		IEnumerable<string> deliveredItemIds,
		out string reason)
	{
		return TryTransitionDelivery(
			sessionId,
			profileId,
			packageId,
			CompletedState,
			deliveredItemIds,
			out reason);
	}

	private bool TryTransitionDelivery(
		string sessionId,
		string profileId,
		string packageId,
		string state,
		IEnumerable<string>? deliveredItemIds,
		out string reason)
	{
		reason = string.Empty;
		string normalizedSessionId = NormalizeMongoId(sessionId);
		string normalizedProfileId = NormalizeMongoId(profileId);
		string normalizedPackageId = NormalizeMongoId(packageId);
		if (string.IsNullOrEmpty(normalizedSessionId) ||
		    string.IsNullOrEmpty(normalizedProfileId) ||
		    string.IsNullOrEmpty(normalizedPackageId))
		{
			reason = "InvalidDeliveryIdentity";
			return false;
		}

		lock (_gate)
		{
			if (!_state.Profiles.TryGetValue(
				    normalizedSessionId,
				    out FireSupportUh60TransferMarkerProfile? profile) ||
			    !string.Equals(
				    profile.ProfileId,
				    normalizedProfileId,
				    StringComparison.OrdinalIgnoreCase) ||
			    profile.DeliveryReceipts == null ||
			    !profile.DeliveryReceipts.TryGetValue(
				    normalizedPackageId,
				    out FireSupportUh60DeliveryReceipt? receipt))
			{
				reason = "DeliveryReceiptNotFound";
				return false;
			}

			FireSupportUh60TransferMarkerState snapshot = CloneState(_state);
			receipt.State = state;
			receipt.UpdatedUtc = DateTimeOffset.UtcNow;
			profile.UpdatedUtc = receipt.UpdatedUtc;
			if (string.Equals(
				    state,
				    CompletedState,
				    StringComparison.OrdinalIgnoreCase))
			{
				HashSet<string> normalizedDeliveredIds =
					NormalizeItemIds(deliveredItemIds ?? []);
				profile.ItemIds.ExceptWith(normalizedDeliveredIds);
			}

			return TrySaveMutationLocked(snapshot, out reason);
		}
	}

	private FireSupportUh60TransferMarkerState LoadLocked()
	{
		if (!File.Exists(_statePath))
		{
			return TryLoadBackupLocked() ?? new FireSupportUh60TransferMarkerState();
		}

		try
		{
			string json = File.ReadAllText(_statePath);
			return JsonSerializer.Deserialize<FireSupportUh60TransferMarkerState>(
				       json,
				       s_jsonOptions) ??
			       new FireSupportUh60TransferMarkerState();
		}
		catch (Exception exception)
		{
			LastLoadWarning =
				$"UH-60 marker state was unreadable and was ignored: {exception.Message}";
			PreserveCorruptStateLocked();
			FireSupportUh60TransferMarkerState? backup = TryLoadBackupLocked();
			if (backup != null)
			{
				LastLoadWarning += " A readable backup was recovered.";
				return backup;
			}

			return new FireSupportUh60TransferMarkerState();
		}
	}

	private FireSupportUh60TransferMarkerState? TryLoadBackupLocked()
	{
		string backupPath = _statePath + ".bak";
		if (!File.Exists(backupPath))
		{
			return null;
		}

		try
		{
			string json = File.ReadAllText(backupPath);
			return JsonSerializer.Deserialize<FireSupportUh60TransferMarkerState>(
				json,
				s_jsonOptions);
		}
		catch
		{
			return null;
		}
	}

	private void PreserveCorruptStateLocked()
	{
		try
		{
			string timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
			File.Move(
				_statePath,
				_statePath + $".corrupt-{timestamp}",
				overwrite: true);
		}
		catch
		{
			// A preservation failure must not prevent stock BTR fallback.
		}
	}

	private bool TrySaveMutationLocked(
		FireSupportUh60TransferMarkerState snapshot,
		out string reason)
	{
		try
		{
			SaveLocked();
			reason = string.Empty;
			return true;
		}
		catch
		{
			_state = snapshot;
			NormalizeStateLocked();
			reason = "MarkerStoreSaveFailed";
			return false;
		}
	}

	private void SaveLocked()
	{
		string tempPath = _statePath + ".tmp";
		string backupPath = _statePath + ".bak";
		try
		{
			File.WriteAllText(
				tempPath,
				JsonSerializer.Serialize(_state, s_jsonOptions));
			if (File.Exists(_statePath))
			{
				File.Replace(
					tempPath,
					_statePath,
					backupPath,
					ignoreMetadataErrors: true);
			}
			else
			{
				File.Move(tempPath, _statePath);
			}
		}
		finally
		{
			if (File.Exists(tempPath))
			{
				File.Delete(tempPath);
			}
		}
	}

	private void NormalizeStateLocked()
	{
		if (_state.SchemaVersion is not (1 or CurrentSchemaVersion))
		{
			LastLoadWarning =
				$"UH-60 marker schema {_state.SchemaVersion} is unsupported; stock BTR fallback will be used.";
			_state = new FireSupportUh60TransferMarkerState();
			return;
		}

		var normalizedProfiles =
			new Dictionary<string, FireSupportUh60TransferMarkerProfile>(
				StringComparer.OrdinalIgnoreCase);
		foreach (KeyValuePair<string, FireSupportUh60TransferMarkerProfile> pair in
		         (_state.Profiles ??
		          new Dictionary<string, FireSupportUh60TransferMarkerProfile>())
		         .OrderByDescending(pair => pair.Value?.UpdatedUtc)
		         .Take(MaxProfiles))
		{
			string sessionId = NormalizeMongoId(pair.Key);
			string profileId = NormalizeMongoId(pair.Value?.ProfileId);
			if (string.IsNullOrEmpty(sessionId) ||
			    string.IsNullOrEmpty(profileId) ||
			    pair.Value == null)
			{
				continue;
			}

			var normalizedIds = new HashSet<string>(
				(pair.Value.ItemIds ?? [])
				.Select(NormalizeMongoId)
				.Where(itemId => !string.IsNullOrEmpty(itemId))
				.Take(MaxItemIdsPerProfile),
				StringComparer.OrdinalIgnoreCase);

			var normalizedReceipts =
				new Dictionary<string, FireSupportUh60DeliveryReceipt>(
					StringComparer.OrdinalIgnoreCase);
			foreach (KeyValuePair<string, FireSupportUh60DeliveryReceipt> receiptPair in
			         (pair.Value.DeliveryReceipts ??
			          new Dictionary<string, FireSupportUh60DeliveryReceipt>())
			         .Where(receiptPair => receiptPair.Value != null)
			         .OrderByDescending(receiptPair => receiptPair.Value.UpdatedUtc)
			         .Take(MaxDeliveryReceiptsPerProfile))
			{
				string packageId = NormalizeMongoId(
					string.IsNullOrWhiteSpace(receiptPair.Value.PackageId)
						? receiptPair.Key
						: receiptPair.Value.PackageId);
				string receiptToken = NormalizeReceiptToken(
					receiptPair.Value.ReceiptToken);
				HashSet<string> receiptItemIds =
					NormalizeItemIds(receiptPair.Value.ItemIds ?? []);
				if (string.IsNullOrEmpty(packageId) ||
				    string.IsNullOrEmpty(receiptToken) ||
				    receiptItemIds.Count == 0)
				{
					continue;
				}

				normalizedReceipts[packageId] =
					new FireSupportUh60DeliveryReceipt
					{
						PackageId = packageId,
						ReceiptToken = receiptToken,
						State = NormalizeDeliveryState(
							receiptPair.Value.State),
						UpdatedUtc = receiptPair.Value.UpdatedUtc,
						ItemIds = receiptItemIds
					};
			}

			if (normalizedIds.Count == 0 &&
			    normalizedReceipts.Count == 0)
			{
				continue;
			}

			normalizedProfiles[sessionId] =
				new FireSupportUh60TransferMarkerProfile
				{
					ProfileId = profileId,
					UpdatedUtc = pair.Value.UpdatedUtc,
					ItemIds = normalizedIds,
					DeliveryReceipts = normalizedReceipts
				};
		}

		_state = new FireSupportUh60TransferMarkerState
		{
			SchemaVersion = CurrentSchemaVersion,
			Profiles = normalizedProfiles
		};
	}

	private void PruneExpiredLocked(DateTimeOffset now)
	{
		DateTimeOffset cutoff = now - MarkerLifetime;
		foreach (FireSupportUh60TransferMarkerProfile profile in
		         _state.Profiles.Values.Where(profile => profile != null))
		{
			profile.DeliveryReceipts ??=
				new Dictionary<string, FireSupportUh60DeliveryReceipt>(
					StringComparer.OrdinalIgnoreCase);
			foreach (string packageId in profile.DeliveryReceipts
				         .Where(pair =>
					         pair.Value == null ||
					         pair.Value.UpdatedUtc < cutoff)
				         .Select(pair => pair.Key)
				         .ToList())
			{
				profile.DeliveryReceipts.Remove(packageId);
			}
		}

		foreach (string sessionId in _state.Profiles
			         .Where(pair =>
				         pair.Value == null ||
				         (pair.Value.UpdatedUtc < cutoff &&
				          (pair.Value.DeliveryReceipts == null ||
				           pair.Value.DeliveryReceipts.Count == 0)) ||
				         ((pair.Value.ItemIds == null ||
				           pair.Value.ItemIds.Count == 0) &&
				          (pair.Value.DeliveryReceipts == null ||
				           pair.Value.DeliveryReceipts.Count == 0)))
			         .Select(pair => pair.Key)
			         .ToList())
		{
			_state.Profiles.Remove(sessionId);
		}
	}

	private static FireSupportUh60TransferMarkerState CloneState(
		FireSupportUh60TransferMarkerState state)
	{
		string json = JsonSerializer.Serialize(state, s_jsonOptions);
		return JsonSerializer.Deserialize<FireSupportUh60TransferMarkerState>(
			       json,
			       s_jsonOptions) ??
		       new FireSupportUh60TransferMarkerState();
	}

	private static FireSupportUh60DeliveryReceipt CloneReceipt(
		FireSupportUh60DeliveryReceipt receipt)
	{
		return new FireSupportUh60DeliveryReceipt
		{
			PackageId = receipt.PackageId,
			ReceiptToken = receipt.ReceiptToken,
			State = receipt.State,
			UpdatedUtc = receipt.UpdatedUtc,
			ItemIds = new HashSet<string>(
				receipt.ItemIds,
				StringComparer.OrdinalIgnoreCase)
		};
	}

	private static HashSet<string> NormalizeItemIds(
		IEnumerable<string> itemIds)
	{
		return new HashSet<string>(
			(itemIds ?? [])
			.Select(NormalizeMongoId)
			.Where(itemId => !string.IsNullOrEmpty(itemId))
			.Take(MaxItemIdsPerProfile + 1),
			StringComparer.OrdinalIgnoreCase);
	}

	private static string CreateReceiptToken(
		FireSupportUh60TransferMarkerProfile profile)
	{
		HashSet<string> existingTokens = new(
			(profile.DeliveryReceipts ??
			 new Dictionary<string, FireSupportUh60DeliveryReceipt>())
			.Values
			.Where(receipt => receipt != null)
			.Select(receipt => receipt.ReceiptToken),
			StringComparer.OrdinalIgnoreCase);
		for (int attempt = 0; attempt < 16; attempt++)
		{
			string token = Guid.NewGuid()
				.ToString("N")[..12]
				.ToUpperInvariant();
			if (!existingTokens.Contains(token))
			{
				return token;
			}
		}

		throw new InvalidOperationException(
			"Could not allocate a unique UH-60 delivery receipt.");
	}

	private static string NormalizeReceiptToken(string? value)
	{
		string normalized = value?.Trim().ToUpperInvariant() ?? string.Empty;
		return normalized.Length == 12 &&
		       normalized.All(Uri.IsHexDigit)
			? normalized
			: string.Empty;
	}

	private static string NormalizeDeliveryState(string? value)
	{
		if (string.Equals(
			    value,
			    MailObservedState,
			    StringComparison.OrdinalIgnoreCase))
		{
			return MailObservedState;
		}

		return string.Equals(
			value,
			CompletedState,
			StringComparison.OrdinalIgnoreCase)
			? CompletedState
			: PreparedState;
	}

	private static string NormalizeMongoId(string? value)
	{
		string normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
		return normalized.Length == 24 && normalized.All(Uri.IsHexDigit)
			? normalized
			: string.Empty;
	}
}

public sealed class FireSupportUh60TransferMarkerState
{
	public int SchemaVersion { get; set; } = 2;
	public Dictionary<string, FireSupportUh60TransferMarkerProfile> Profiles { get; set; } =
		new(StringComparer.OrdinalIgnoreCase);
}

public sealed class FireSupportUh60TransferMarkerProfile
{
	public string ProfileId { get; set; } = string.Empty;
	public DateTimeOffset UpdatedUtc { get; set; }
	public HashSet<string> ItemIds { get; set; } =
		new(StringComparer.OrdinalIgnoreCase);
	public Dictionary<string, FireSupportUh60DeliveryReceipt> DeliveryReceipts { get; set; } =
		new(StringComparer.OrdinalIgnoreCase);
}

public sealed class FireSupportUh60DeliveryReceipt
{
	public string PackageId { get; set; } = string.Empty;
	public string ReceiptToken { get; set; } = string.Empty;
	public string State { get; set; } = string.Empty;
	public DateTimeOffset UpdatedUtc { get; set; }
	public HashSet<string> ItemIds { get; set; } =
		new(StringComparer.OrdinalIgnoreCase);
}
