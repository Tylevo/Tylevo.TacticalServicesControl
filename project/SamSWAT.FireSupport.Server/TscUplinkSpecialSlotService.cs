using SPTarkov.DI.Annotations;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Servers;

namespace SamSWAT.FireSupport.ArysReloaded;

/// <summary>
/// Owns the data-driven fourth special slot used by the TSC Uplink and migrates
/// legacy profiles that had the Uplink equipped in one of EFT's stock slots.
/// </summary>
[Injectable(InjectionType.Singleton)]
public sealed class TscUplinkSpecialSlotService(
	ISptLogger<TscUplinkSpecialSlotService> logger,
	TemplateTable templateTable,
	SaveServer saveServer)
{
	public const string UplinkTemplateId = "66f51f3a0000000000000a01";
	public const string StandardPocketsTemplateId = "627a4e6b255f7527fb05a0f6";
	public const string UnheardPocketsTemplateId = "65e080be269cbd5c5005e529";
	public const string DedicatedSlotName = "SpecialSlot4";

	private const string SlotPrototypeId = "55d721144bdc2d89028b456f";
	private const string StandardSlotId = "66f51f3a0000000000000a04";
	private const string UnheardSlotId = "66f51f3a0000000000000a05";
	private const string PocketsEquipmentSlot = "Pockets";

	private static readonly HashSet<string> LegacySlotNames = new(
		["SpecialSlot1", "SpecialSlot2", "SpecialSlot3"],
		StringComparer.OrdinalIgnoreCase);

	private static readonly HashSet<string> SupportedPocketsTemplateIds = new(
		[StandardPocketsTemplateId, UnheardPocketsTemplateId],
		StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// Adds the dedicated slot and narrows Uplink eligibility idempotently. This
	/// runs after WTT registers the Uplink, because WTT initially adds custom
	/// special-slot items to all three stock slot filters.
	/// </summary>
	public void ConfigurePocketTemplates()
	{
		ConfigurePocketTemplate(StandardPocketsTemplateId, StandardSlotId);
		ConfigurePocketTemplate(UnheardPocketsTemplateId, UnheardSlotId);
	}

	/// <summary>
	/// Moves only Uplinks directly equipped in stock special slots on a supported
	/// pockets item. Conflicts are left untouched, and saves are rolled back in
	/// memory if persistence fails.
	/// </summary>
	public async Task MigrateLoadedProfilesAsync(CancellationToken cancellationToken)
	{
		foreach ((MongoId sessionId, var profile) in saveServer.GetProfiles())
		{
			cancellationToken.ThrowIfCancellationRequested();

			PmcData? pmcData = profile.CharacterData?.PmcData;
			List<Item>? items = pmcData?.Inventory?.Items;
			MongoId? equipmentId = pmcData?.Inventory?.Equipment;
			if (items == null || equipmentId == null || equipmentId.Value.IsEmpty)
			{
				continue;
			}

			List<(Item Item, string OriginalSlot)> pending = FindSafeMigrations(
				sessionId,
				items,
				equipmentId.Value.ToString());
			if (pending.Count == 0)
			{
				continue;
			}

			foreach ((Item item, _) in pending)
			{
				item.SlotId = DedicatedSlotName;
			}

			try
			{
				await saveServer.SaveProfileAsync(sessionId, cancellationToken);
				logger.Success(
					$"TSC moved {pending.Count} legacy Uplink item(s) into {DedicatedSlotName} for profile {sessionId}.");
			}
			catch (OperationCanceledException)
			{
				Rollback(pending);
				throw;
			}
			catch (Exception exception)
			{
				Rollback(pending);
				logger.Error(
					$"TSC could not persist the {DedicatedSlotName} migration for profile {sessionId}; the original slots were restored.",
					exception);
			}
		}
	}

	private void ConfigurePocketTemplate(string pocketsTemplateId, string dedicatedSlotId)
	{
		MongoId pocketsId = new(pocketsTemplateId);
		if (!templateTable.Items.TryGetValue(pocketsId, out TemplateItem? pockets) ||
		    pockets.Properties == null)
		{
			logger.Warning(
				$"TSC could not configure {DedicatedSlotName}: pockets template {pocketsTemplateId} is unavailable.");
			return;
		}

		List<Slot> slots = pockets.Properties.Slots?.ToList() ?? [];
		List<Slot> matchingDedicatedSlots = slots
			.Where(slot =>
				string.Equals(
					slot.Name,
					DedicatedSlotName,
					StringComparison.OrdinalIgnoreCase) ||
				string.Equals(
					slot.Id?.ToString(),
					dedicatedSlotId,
					StringComparison.OrdinalIgnoreCase))
			.ToList();
		if (matchingDedicatedSlots.Count > 1)
		{
			logger.Warning(
				$"TSC found conflicting {DedicatedSlotName} definitions on pockets template {pocketsTemplateId}; no additional slot was added.");
			pockets.Properties.Slots = slots;
			return;
		}

		if (matchingDedicatedSlots.Count == 1)
		{
			Slot existing = matchingDedicatedSlots[0];
			bool hasOwnedId = string.Equals(
				existing.Id?.ToString(),
				dedicatedSlotId,
				StringComparison.OrdinalIgnoreCase);
			bool hasOwnedName = string.Equals(
				existing.Name,
				DedicatedSlotName,
				StringComparison.OrdinalIgnoreCase);
			if (hasOwnedId && hasOwnedName)
			{
				ApplyDedicatedSlotContract(existing, pocketsId, dedicatedSlotId);
				RemoveLegacyEligibility(slots);
			}
			else
			{
				logger.Warning(
					$"TSC found a foreign {DedicatedSlotName} definition on pockets template {pocketsTemplateId}; it was left unchanged.");
			}

			pockets.Properties.Slots = slots;
			return;
		}

		var dedicatedSlot = new Slot();
		ApplyDedicatedSlotContract(dedicatedSlot, pocketsId, dedicatedSlotId);
		slots.Add(dedicatedSlot);
		RemoveLegacyEligibility(slots);
		pockets.Properties.Slots = slots;
	}

	private List<(Item Item, string OriginalSlot)> FindSafeMigrations(
		MongoId sessionId,
		List<Item> items,
		string equipmentId)
	{
		var pending = new List<(Item Item, string OriginalSlot)>();
		foreach (Item pockets in items.Where(item =>
		         string.Equals(item.ParentId, equipmentId, StringComparison.OrdinalIgnoreCase) &&
		         string.Equals(item.SlotId, PocketsEquipmentSlot, StringComparison.OrdinalIgnoreCase) &&
		         SupportedPocketsTemplateIds.Contains(item.Template.ToString()) &&
		         HasOwnedDedicatedSlot(item.Template.ToString())))
		{
			string pocketsId = pockets.Id.ToString();
			List<Item> legacyUplinks = items.Where(item =>
				string.Equals(item.ParentId, pocketsId, StringComparison.OrdinalIgnoreCase) &&
				string.Equals(item.Template.ToString(), UplinkTemplateId, StringComparison.OrdinalIgnoreCase) &&
				item.SlotId != null &&
				LegacySlotNames.Contains(item.SlotId)).ToList();

			if (legacyUplinks.Count == 0)
			{
				continue;
			}

			List<Item> dedicatedSlotOccupants = items.Where(item =>
				string.Equals(item.ParentId, pocketsId, StringComparison.OrdinalIgnoreCase) &&
				string.Equals(item.SlotId, DedicatedSlotName, StringComparison.OrdinalIgnoreCase)).ToList();
			if (dedicatedSlotOccupants.Count > 0)
			{
				logger.Warning(
					$"TSC left {legacyUplinks.Count} legacy Uplink item(s) unchanged for profile {sessionId}: {DedicatedSlotName} is already occupied.");
				continue;
			}

			if (legacyUplinks.Count != 1)
			{
				logger.Warning(
					$"TSC left {legacyUplinks.Count} legacy Uplink items unchanged for profile {sessionId}: a single-slot migration would be ambiguous.");
				continue;
			}

			Item uplink = legacyUplinks[0];
			pending.Add((uplink, uplink.SlotId!));
		}

		return pending;
	}

	private bool HasOwnedDedicatedSlot(string pocketsTemplateId)
	{
		string expectedSlotId = string.Equals(
			pocketsTemplateId,
			StandardPocketsTemplateId,
			StringComparison.OrdinalIgnoreCase)
			? StandardSlotId
			: UnheardSlotId;
		if (!templateTable.Items.TryGetValue(
			    new MongoId(pocketsTemplateId),
			    out TemplateItem? pockets))
		{
			return false;
		}

		List<Slot> ownedSlots = pockets.Properties?.Slots?.Where(slot =>
			string.Equals(
				slot.Id?.ToString(),
				expectedSlotId,
				StringComparison.OrdinalIgnoreCase) &&
			string.Equals(
				slot.Name,
				DedicatedSlotName,
				StringComparison.OrdinalIgnoreCase)).ToList() ?? [];
		if (ownedSlots.Count != 1)
		{
			return false;
		}

		List<SlotFilter> filters = ownedSlots[0].Properties?.Filters?.ToList() ?? [];
		return filters.Count == 1 &&
		       filters[0].Filter is { Count: 1 } filter &&
		       filter.Contains(new MongoId(UplinkTemplateId));
	}

	private static void RemoveLegacyEligibility(IEnumerable<Slot> slots)
	{
		foreach (Slot slot in slots.Where(IsLegacySlot))
		{
			foreach (SlotFilter filter in slot.Properties?.Filters ?? [])
			{
				filter.Filter?.Remove(new MongoId(UplinkTemplateId));
			}
		}
	}

	private static bool IsLegacySlot(Slot slot)
	{
		return slot.Name != null && LegacySlotNames.Contains(slot.Name);
	}

	private static void ApplyDedicatedSlotContract(
		Slot slot,
		MongoId pocketsId,
		string dedicatedSlotId)
	{
		slot.Id = new MongoId(dedicatedSlotId);
		slot.Name = DedicatedSlotName;
		slot.Parent = pocketsId;
		slot.Prototype = SlotPrototypeId;
		slot.Required = false;
		slot.MergeSlotWithChildren = false;
		slot.Properties = new SlotProperties
		{
			Filters =
			[
				new SlotFilter
				{
					Filter = [new MongoId(UplinkTemplateId)],
					Locked = false
				}
			]
		};
	}

	private static void Rollback(IEnumerable<(Item Item, string OriginalSlot)> pending)
	{
		foreach ((Item item, string originalSlot) in pending)
		{
			item.SlotId = originalSlot;
		}
	}
}
