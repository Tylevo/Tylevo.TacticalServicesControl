using Comfort.Common;
using Cysharp.Threading.Tasks;
using EFT;
using EFT.InventoryLogic;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

/// <summary>Applies an authenticated current stash snapshot without submitting another payment.</summary>
internal static class PilotServicesStashSynchronizer
{
	internal static SnapshotReadGuard BeginSnapshotRead(IEftSession session) => new(session);

	/// <summary>Rejects a fetched snapshot if native inventory traffic overlapped the read.</summary>
	internal sealed class SnapshotReadGuard : IDisposable
	{
		private readonly IEftSession _session;
		private Action _unsubscribe;
		private int _invalidated;
		private int _disposed;

		internal SnapshotReadGuard(IEftSession session)
		{
			_session = session;
			if (session == null || session.QueueStatusChanged == null)
			{
				_invalidated = 1;
				return;
			}
			_unsubscribe = session.QueueStatusChanged.Subscribe(() => Interlocked.Exchange(ref _invalidated, 1));
			if (!IsNativeQueueIdle(session))
				Interlocked.Exchange(ref _invalidated, 1);
		}

		internal bool IsUnchanged => Volatile.Read(ref _disposed) == 0 &&
			Volatile.Read(ref _invalidated) == 0 && IsNativeQueueIdle(_session);

		public void Dispose()
		{
			if (Interlocked.Exchange(ref _disposed, 1) == 0)
				Interlocked.Exchange(ref _unsubscribe, null)?.Invoke();
		}
	}

	internal static async UniTask<string> FlushPendingOperationsAsync(IEftSession session)
	{
		if (session == null || Singleton<GameWorld>.Instance != null)
			return "The menu inventory is no longer available.";
		try
		{
			IResult result = await session.FlushOperationQueue();
			if (result == null || result.Failed)
				return "Tarkov could not finish the pending inventory changes.";
			// Native RaiseBindEvents completes the flush task before its final
			// queue event. Let that callback finish before installing a read guard.
			await UniTask.Yield();
			return string.Empty;
		}
		catch (Exception exception)
		{
			FireSupportPlugin.LogSource?.LogWarning($"TSC native inventory flush failed: {exception}");
			return "Tarkov could not finish the pending inventory changes.";
		}
	}

	internal static bool TryApplyNative(IEftSession session, Profile profile,
		InventoryController inventoryController, FireSupportStashCurrencyState state, out string reason)
	{
		reason = string.Empty;
		try
		{
			if (Singleton<GameWorld>.Instance != null ||
			    session is not ClientBackendSession backend || profile?.Inventory?.Stash == null ||
			    !ReferenceEquals(session.Profile, profile) ||
			    !ReferenceEquals(inventoryController?.Inventory, profile.Inventory) ||
			    !IsNativeQueueIdle(session) ||
			    !backend._profileUpdaters.TryGetValue(profile.Id, out IProfileUpdatesHandler updater) ||
			    updater is not ProfileUpdatesHandler handler ||
			    !ReferenceEquals(handler._profile, profile) ||
			    !ReferenceEquals(handler._inventoryController, inventoryController))
				return Fail("The menu inventory changed while the stash was loading.", out reason);

			if (state == null || state.Items == null ||
			    !SameId(state.ProfileId, profile.Id) || !SameId(state.StashId, profile.Inventory.Stash.Id) ||
			    !IsId(state.ProfileId) || !IsId(state.StashId) ||
			    state.Items.Count > FireSupportStashCurrencyState.MaxItems)
				return Fail("The server did not return a valid stash currency snapshot.", out reason);

			Dictionary<string, Item> stashItems = profile.Inventory.Stash.GetAllItems()
				.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
			Dictionary<string, Item> localCash = stashItems.Values.Where(IsCurrency)
				.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
			Dictionary<string, Item> allItems = profile.Inventory.AllRealPlayerItems
				.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
			var records = new Dictionary<string, ParsedCurrency>(StringComparer.OrdinalIgnoreCase);
			long metadataLength = 0;
			foreach (FireSupportStashCurrencyItem item in state.Items)
			{
				if (item == null || !IsId(item.Id) || !IsId(item.TemplateId) || !IsId(item.ParentId) ||
				    !IsCurrencyTemplate(item.TemplateId) || item.StackObjectsCount <= 0 ||
				    string.IsNullOrWhiteSpace(item.SlotId) || item.SlotId.Length > 128 ||
				    string.IsNullOrWhiteSpace(item.UpdJson) || string.IsNullOrWhiteSpace(item.LocationJson) ||
				    item.UpdJson.Length > FireSupportStashCurrencyState.MaxMetadataJsonLength ||
				    item.LocationJson.Length > FireSupportStashCurrencyState.MaxMetadataJsonLength ||
				    records.ContainsKey(item.Id))
					return Fail("The server returned invalid currency item data.", out reason);
				metadataLength += item.UpdJson.Length + item.LocationJson.Length;
				if (metadataLength > FireSupportStashCurrencyState.MaxTotalMetadataJsonLength)
					return Fail("The stash currency snapshot exceeded its size limit.", out reason);
				JObject upd = JObject.Parse(item.UpdJson);
				JToken count = upd["StackObjectsCount"];
				if (count == null || (count.Type != JTokenType.Integer && count.Type != JTokenType.Float) ||
				    count.Value<decimal>() != item.StackObjectsCount)
					return Fail("The server returned inconsistent currency stack counts.", out reason);
				JObject locationJson = JObject.Parse(item.LocationJson);
				if (locationJson["x"]?.Type != JTokenType.Integer || locationJson["y"]?.Type != JTokenType.Integer ||
				    (locationJson["r"]?.Type != JTokenType.Integer && locationJson["r"]?.Type != JTokenType.String))
					return Fail("The server returned an incomplete currency item location.", out reason);
				LocationInGrid location = locationJson.ToObject<LocationInGrid>();
				if (location == null || location.x < 0 || location.y < 0 ||
				    location.x > 10000 || location.y > 10000 ||
				    (location.r != ItemRotation.Horizontal && location.r != ItemRotation.Vertical))
					return Fail("The server returned an invalid currency item location.", out reason);
				records.Add(item.Id, new ParsedCurrency(item, upd, location));
			}

			var deleted = new HashSet<string>(localCash.Keys.Where(id => !records.ContainsKey(id)),
				StringComparer.OrdinalIgnoreCase);
			var changes = new List<JsonType.FlatItem>();
			var additions = new List<CurrencyAddition>();
			var occupiedNewCells = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (ParsedCurrency parsed in records.Values)
			{
				FireSupportStashCurrencyItem item = parsed.Record;
				if (localCash.TryGetValue(item.Id, out Item local))
				{
					if (!SameId(local.StringTemplateId, item.TemplateId) ||
					    (local.CurrentAddress ?? local.Parent) is not GridItemAddress address ||
					    !SameId(address.Grid.ParentItem.Id, item.ParentId) ||
					    !string.Equals(address.Grid.ID, item.SlotId, StringComparison.Ordinal) ||
					    !SameLocation(address.LocationInGrid, parsed.Location))
						return Fail("A currency item moved while the stash was loading. Refresh and try again.", out reason);
					if (local.StackObjectsCount != item.StackObjectsCount)
						changes.Add(new JsonType.FlatItem
						{
							_id = new MongoID(item.Id), _tpl = new MongoID(item.TemplateId),
							// Count-only updates preserve all unrelated local item metadata.
							upd = new UnparsedData { JToken = new JObject { ["StackObjectsCount"] = item.StackObjectsCount } }
						});
					continue;
				}
				if (allItems.ContainsKey(item.Id) || stashItems.ContainsKey(item.Id) ||
				    !stashItems.TryGetValue(item.ParentId, out Item parent) ||
				    parent is not ContainerCollection container)
					return Fail("The local stash is missing a currency container. Reopen the trader and refresh.", out reason);
				Grid grid = container.Containers.OfType<Grid>()
					.SingleOrDefault(candidate => string.Equals(candidate.ID, item.SlotId, StringComparison.Ordinal));
				if (grid == null || Singleton<ItemFactory>.Instance == null)
					return Fail("The local stash cannot place a currency item.", out reason);
				Item created = Singleton<ItemFactory>.Instance.CreateItem(item.Id, item.TemplateId,
					new UnparsedData { JToken = parsed.Upd });
				if (created is not Money || created.StackObjectsCount != item.StackObjectsCount ||
				    !grid.CheckCompatibility(created))
					return Fail("The local stash rejected a currency item.", out reason);
				int width = parsed.Location.r == ItemRotation.Horizontal ? created.Width : created.Height;
				int height = parsed.Location.r == ItemRotation.Horizontal ? created.Height : created.Width;
				if (width <= 0 || height <= 0 || width > grid.GridWidth || height > grid.GridHeight ||
				    parsed.Location.x > grid.GridWidth - width || parsed.Location.y > grid.GridHeight - height)
					return Fail("A currency item no longer fits its stash location.", out reason);
				for (int y = parsed.Location.y; y < parsed.Location.y + height; y++)
				for (int x = parsed.Location.x; x < parsed.Location.x + width; x++)
				{
					var cell = new LocationInGrid { x = x, y = y, r = ItemRotation.Horizontal };
					Item occupant = grid.GetItemAt(cell);
					if ((occupant != null && !deleted.Contains(occupant.Id)) ||
					    (occupant == null && !grid.CheckLayout(new IntVec2(1, 1), cell)) ||
					    !occupiedNewCells.Add($"{item.ParentId}:{item.SlotId}:{x}:{y}"))
						return Fail("A currency item's stash location is occupied. Refresh and try again.", out reason);
				}
				additions.Add(new CurrencyAddition(created, grid.CreateItemAddress(parsed.Location)));
			}

			// Validate the complete snapshot before changing any inventory object.
			// Native absolute changes and removals raise the same events as normal trading.
			if (changes.Count > 0 || deleted.Count > 0)
				handler.ApplyStashChanges(new StashChangesResponse
				{
					@new = Array.Empty<JsonType.FlatItem>(), change = changes.ToArray(),
					del = deleted.Select(id => new JsonType.FlatItem { _id = new MongoID(id) }).ToArray()
				}, null);
			foreach (CurrencyAddition addition in additions)
			{
				// ManageNewItems only resolves top-level roots. This native add also
				// supports currency inside an existing money case or other stash container.
				var result = ItemManipulator.AddWithoutRestrictions(addition.Item, addition.Address, inventoryController);
				if (!result.Succeeded)
					return Fail("Tarkov could not finish synchronizing a currency item. Refresh and try again.", out reason);
				result.Value.RaiseEvents(inventoryController, CommandStatus.Begin);
				result.Value.RaiseEvents(inventoryController, CommandStatus.Succeed);
			}
			Dictionary<string, Item> synchronized = profile.Inventory.Stash.GetAllItems().Where(IsCurrency)
				.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
			if (synchronized.Count != records.Count || records.Any(pair =>
				!synchronized.TryGetValue(pair.Key, out Item item) ||
				!SameId(item.StringTemplateId, pair.Value.Record.TemplateId) ||
				item.StackObjectsCount != pair.Value.Record.StackObjectsCount))
				return Fail("Tarkov's stash has not finished synchronizing. Refresh and try again.", out reason);
			return true;
		}
		catch (Exception exception)
		{
			FireSupportPlugin.LogSource?.LogWarning($"TSC native stash synchronization failed: {exception}");
			return Fail("The stash could not be synchronized. Refresh and try again.", out reason);
		}
	}

	// Idle can still contain batched, unsent commands; require all native queues to be empty.
	private static bool IsNativeQueueIdle(IEftSession session) => session is ClientBackendSession backend &&
		backend.QueueStatus == EOperationQueueStatus.Idle && !backend.IsFlushing &&
		backend._unsentCommands != null && backend._unsentCommands.Count == 0 &&
		backend._incomingOperations != null && backend._incomingOperations.Count == 0 &&
		backend._waitingOperation == null;
	private static bool IsCurrency(Item item) => item is Money && IsCurrencyTemplate(item.StringTemplateId);
	private static bool IsCurrencyTemplate(string templateId) =>
		SameId(templateId, PaymentCurrencyInfo.RoubleTemplateId) ||
		SameId(templateId, PaymentCurrencyInfo.DollarTemplateId) ||
		SameId(templateId, PaymentCurrencyInfo.EuroTemplateId);
	private static bool SameId(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
	private static bool SameLocation(LocationInGrid left, LocationInGrid right) =>
		left != null && right != null && left.x == right.x && left.y == right.y && left.r == right.r;
	private static bool IsId(string value) => value?.Length == 24 && value.All(character =>
		character >= '0' && character <= '9' || character >= 'a' && character <= 'f' || character >= 'A' && character <= 'F');
	private static bool Fail(string message, out string reason) { reason = message; return false; }

	private sealed class ParsedCurrency(FireSupportStashCurrencyItem record, JObject upd, LocationInGrid location)
	{
		internal readonly FireSupportStashCurrencyItem Record = record;
		internal readonly JObject Upd = upd;
		internal readonly LocationInGrid Location = location;
	}

	private sealed class CurrencyAddition(Item item, GridItemAddress address)
	{
		internal readonly Item Item = item;
		internal readonly GridItemAddress Address = address;
	}
}
