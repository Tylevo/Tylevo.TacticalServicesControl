using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace SamSWAT.FireSupport.ArysReloaded;

/// <summary>
/// Allows the Uplink in pocket special slots without replacing another mod's
/// layout or moving items in player profiles.
/// </summary>
[Injectable(InjectionType.Singleton)]
public sealed class TscUplinkSpecialSlotService(TemplateTable templateTable)
{
	public const string UplinkTemplateId = "66f51f3a0000000000000a01";
	public const string StandardPocketsTemplateId = "627a4e6b255f7527fb05a0f6";
	public const string UnheardPocketsTemplateId = "65e080be269cbd5c5005e529";
	public const string PocketsParentId = "557596e64bdc2dc2118b4571";
	public const string DedicatedSlotName = "SpecialSlot4";

	private const string SlotPrototypeId = "55d721144bdc2d89028b456f";
	private const string StandardSlotId = "66f51f3a0000000000000a04";
	private const string UnheardSlotId = "66f51f3a0000000000000a05";

	/// <summary>
	/// Runs after item registration and again after other mods have configured
	/// pockets. Only the Uplink is added to existing special-slot allowlists.
	/// </summary>
	public void ConfigurePocketTemplates()
	{
		foreach ((MongoId templateId, TemplateItem pockets) in templateTable.Items)
		{
			if (pockets.Properties?.Slots == null || !IsPocketsTemplate(templateId, pockets))
			{
				continue;
			}

			List<Slot> slots = pockets.Properties.Slots.ToList();
			PreserveVanillaFourthSlot(templateId, slots);
			foreach (Slot slot in slots.Where(IsSpecialSlot))
			{
				AddUplinkEligibility(slot);
			}
			pockets.Properties.Slots = slots;
		}
	}

	private bool IsPocketsTemplate(MongoId templateId, TemplateItem template)
	{
		if (templateId.Equals(new MongoId(StandardPocketsTemplateId)) ||
		    templateId.Equals(new MongoId(UnheardPocketsTemplateId)))
		{
			return true;
		}

		var visited = new HashSet<MongoId> { templateId };
		MongoId parentId = template.Parent;
		while (!parentId.IsEmpty && visited.Add(parentId))
		{
			if (parentId.Equals(new MongoId(PocketsParentId))) return true;
			if (!templateTable.Items.TryGetValue(parentId, out TemplateItem? parent)) return false;
			parentId = parent.Parent;
		}
		return false;
	}

	private static bool IsSpecialSlot(Slot slot)
	{
		// Match EFT's Slot.IsSpecial, including names supplied by custom layouts.
		return slot.Name?.Contains("SpecialSlot", StringComparison.Ordinal) == true;
	}

	private static void AddUplinkEligibility(Slot slot)
	{
		// No filters means unrestricted in EFT; do not narrow an unrestricted slot.
		foreach (SlotFilter filter in slot.Properties?.Filters ?? [])
		{
			filter.Filter ??= [];
			filter.Filter.Add(new MongoId(UplinkTemplateId));
		}
	}

	private static void PreserveVanillaFourthSlot(MongoId pocketsId, List<Slot> slots)
	{
		string? dedicatedSlotId = pocketsId.Equals(new MongoId(StandardPocketsTemplateId))
			? StandardSlotId
			: pocketsId.Equals(new MongoId(UnheardPocketsTemplateId)) ? UnheardSlotId : null;
		if (dedicatedSlotId == null || slots.Any(slot =>
			    string.Equals(slot.Name, DedicatedSlotName, StringComparison.OrdinalIgnoreCase) ||
			    slot.Id?.Equals(new MongoId(dedicatedSlotId)) == true))
		{
			return;
		}

		// Keep existing saves that use TSC's fourth slot valid on stock pockets.
		// Expanded/custom layouts already provide their own capacity and ordering.
		List<Slot> specialSlots = slots.Where(IsSpecialSlot).ToList();
		if (specialSlots.Count != 3 || Enumerable.Range(1, 3).Any(index =>
			    !specialSlots.Any(slot => string.Equals(slot.Name, $"SpecialSlot{index}",
				    StringComparison.OrdinalIgnoreCase))))
		{
			return;
		}

		slots.Add(new Slot
		{
			Id = new MongoId(dedicatedSlotId),
			Name = DedicatedSlotName,
			Parent = pocketsId,
			Prototype = SlotPrototypeId,
			Required = false,
			MergeSlotWithChildren = false,
			Properties = new SlotProperties
			{
				Filters = [new SlotFilter { Filter = [new MongoId(UplinkTemplateId)], Locked = false }]
			}
		});
	}
}
