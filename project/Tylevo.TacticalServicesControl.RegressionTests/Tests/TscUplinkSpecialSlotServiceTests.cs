using SamSWAT.FireSupport.ArysReloaded;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Servers;
using System.Text.Json;

internal static class TscUplinkSpecialSlotServiceTests
{
	private const string OtherAllowedItem = "5f4fbaaca5573a5ac31db429";
	private const string CustomPocketsId = "aaaaaaaaaaaaaaaaaaaaaaaa";
	private const string ForeignSlotId = "bbbbbbbbbbbbbbbbbbbbbbbb";
	private static readonly MongoId UplinkId = new(TscUplinkSpecialSlotService.UplinkTemplateId);

	[RegressionTest]
	private static void ReconciliationRunsAfterNormalModRegistration()
	{
		var attribute = AssertEx.NotNull((SPTarkov.DI.Annotations.InjectableAttribute?)
			Attribute.GetCustomAttribute(typeof(TscUplinkProfileMigrationOnLoad),
				typeof(SPTarkov.DI.Annotations.InjectableAttribute)));
		AssertEx.Equal(SPTarkov.Server.Core.DI.OnLoadOrder.PostLoad + 1, attribute.TypePriority);
	}

	[RegressionTest]
	private static void VanillaPocketsAcceptUplinkInAllSlotsAndRetainFourthSlotCompatibility()
	{
		TemplateTable templates = CreateTemplates();
		new TscUplinkSpecialSlotService(templates).ConfigurePocketTemplates();
		foreach (TemplateItem pockets in templates.Items.Values)
		{
			Slot[] slots = pockets.Properties!.Slots!.ToArray();
			AssertEx.Equal(4, slots.Length);
			foreach (Slot slot in slots) AssertAllowsUplink(slot);
			foreach (Slot original in slots.Take(3))
				AssertEx.True(original.Properties!.Filters!.Single().Filter!.Contains(new MongoId(OtherAllowedItem)));
			Slot fourth = slots[3];
			AssertEx.Equal("SpecialSlot4", fourth.Name);
			AssertEx.Equal(pockets.Id, fourth.Parent!.Value);
			AssertEx.False(fourth.Required ?? true);
			AssertEx.False(fourth.MergeSlotWithChildren ?? true);
			AssertEx.False(fourth.Properties!.Filters!.Single().Locked ?? true);
		}
	}

	[RegressionTest]
	private static void SixSlotLayoutsKeepTheirSlotsOrderAndFilters()
	{
		TemplateTable templates = CreateTemplates(6);
		var originals = templates.Items.ToDictionary(pair => pair.Key,
			pair => pair.Value.Properties!.Slots!.ToArray());
		var service = new TscUplinkSpecialSlotService(templates);
		service.ConfigurePocketTemplates();
		service.ConfigurePocketTemplates();
		foreach ((MongoId id, TemplateItem pockets) in templates.Items)
		{
			Slot[] slots = pockets.Properties!.Slots!.ToArray();
			AssertEx.Equal(6, slots.Length);
			for (int i = 0; i < slots.Length; i++)
			{
				AssertEx.True(ReferenceEquals(originals[id][i], slots[i]));
				AssertEx.Equal($"SpecialSlot{i + 1}", slots[i].Name);
				AssertAllowsUplink(slots[i]);
				AssertEx.Equal(2, slots[i].Properties!.Filters!.Single().Filter!.Count);
			}
		}
	}

	[RegressionTest]
	private static void ForeignFourthSlotIsExtendedWithoutReplacingItsContract()
	{
		TemplateTable templates = CreateTemplates();
		TemplateItem pockets = templates.Items[new MongoId(TscUplinkSpecialSlotService.StandardPocketsTemplateId)];
		Slot foreign = CreateSlot(4);
		foreign.Id = new MongoId(ForeignSlotId);
		foreign.Parent = new MongoId(CustomPocketsId);
		foreign.Prototype = "foreign-prototype";
		foreign.Required = true;
		foreign.MergeSlotWithChildren = true;
		SlotFilter first = foreign.Properties!.Filters!.Single();
		first.Locked = true;
		SlotFilter second = new() { Filter = [new MongoId(OtherAllowedItem)], Locked = false };
		foreign.Properties.Filters = new[] { first, second };
		pockets.Properties!.Slots = pockets.Properties.Slots!.Append(foreign).ToArray();

		new TscUplinkSpecialSlotService(templates).ConfigurePocketTemplates();

		AssertEx.Equal(4, pockets.Properties.Slots.Count());
		AssertEx.True(ReferenceEquals(foreign, pockets.Properties.Slots.Last()));
		AssertEx.Equal(new MongoId(ForeignSlotId), foreign.Id!.Value);
		AssertEx.Equal(new MongoId(CustomPocketsId), foreign.Parent!.Value);
		AssertEx.Equal("foreign-prototype", foreign.Prototype);
		AssertEx.True(foreign.Required!.Value);
		AssertEx.True(foreign.MergeSlotWithChildren!.Value);
		AssertEx.True(first.Locked!.Value);
		AssertEx.False(second.Locked!.Value);
		AssertEx.True(ReferenceEquals(first, foreign.Properties.Filters.First()));
		AssertEx.True(ReferenceEquals(second, foreign.Properties.Filters.Last()));
		AssertEx.True(first.Filter!.Contains(new MongoId(OtherAllowedItem)));
		AssertEx.True(second.Filter!.Contains(new MongoId(OtherAllowedItem)));
		foreach (Slot slot in pockets.Properties.Slots) AssertAllowsUplink(slot);
	}

	[RegressionTest]
	private static async Task LateRegistrationPreservesOtherModsAdditionsToOwnedFourthSlot()
	{
		TemplateTable templates = CreateTemplates();
		var service = new TscUplinkSpecialSlotService(templates);
		service.ConfigurePocketTemplates();
		Slot fourth = templates.Items[new MongoId(TscUplinkSpecialSlotService.StandardPocketsTemplateId)]
			.Properties!.Slots!.Single(slot => slot.Name == "SpecialSlot4");
		SlotFilter filter = fourth.Properties!.Filters!.Single();
		filter.Filter!.Add(new MongoId(OtherAllowedItem));
		filter.Locked = true;
		TemplateItem custom = AddPockets(templates, CustomPocketsId, 6);

		await new TscUplinkProfileMigrationOnLoad(service).OnLoadAsync(CancellationToken.None);

		AssertEx.True(ReferenceEquals(filter, fourth.Properties.Filters!.Single()));
		AssertEx.True(filter.Filter.Contains(new MongoId(OtherAllowedItem)));
		AssertEx.True(filter.Locked!.Value);
		foreach (Slot slot in custom.Properties!.Slots!) AssertAllowsUplink(slot);
		AssertEx.Equal(6, custom.Properties.Slots!.Count());
	}

	[RegressionTest]
	private static void CustomPocketAncestorsAndNamedSpecialSlotsAreSupportedWithoutWideningOtherSlots()
	{
		TemplateTable templates = CreateTemplates();
		AddPockets(templates, CustomPocketsId, 0);
		TemplateItem descendant = AddPockets(templates, "cccccccccccccccccccccccc", 0, CustomPocketsId);
		Slot special = CreateSlot(6);
		special.Name = "CustomSpecialSlotMedical";
		Slot ordinary = CreateSlot(1);
		ordinary.Name = "mod_armor_plate";
		descendant.Properties!.Slots = [special, ordinary];
		TemplateItem armor = AddPockets(templates, "dddddddddddddddddddddddd", 6, "armor-parent");
		AddPockets(templates, "eeeeeeeeeeeeeeeeeeeeeeee", 1, "ffffffffffffffffffffffff");
		AddPockets(templates, "ffffffffffffffffffffffff", 1, "eeeeeeeeeeeeeeeeeeeeeeee");

		new TscUplinkSpecialSlotService(templates).ConfigurePocketTemplates();

		AssertEx.Equal(2, descendant.Properties.Slots.Count());
		AssertAllowsUplink(special);
		AssertEx.False(ordinary.Properties!.Filters!.Single().Filter!.Contains(UplinkId));
		foreach (Slot slot in armor.Properties!.Slots!)
			AssertEx.False(slot.Properties!.Filters!.Single().Filter!.Contains(UplinkId));
		AssertEx.Equal(0, templates.Items[new MongoId(CustomPocketsId)].Properties!.Slots!.Count());
	}

	[RegressionTest]
	private static void UnrestrictedSpecialSlotsKeepTheirUnrestrictedFilters()
	{
		TemplateTable templates = CreateTemplates(6);
		Slot[] slots = templates.Items[new MongoId(TscUplinkSpecialSlotService.StandardPocketsTemplateId)]
			.Properties!.Slots!.ToArray();
		slots[0].Properties!.Filters = null;
		slots[1].Properties!.Filters = [];
		slots[2].Properties = null;
		slots[3].Properties!.Filters!.Single().Filter = [];

		new TscUplinkSpecialSlotService(templates).ConfigurePocketTemplates();

		AssertEx.True(slots[0].Properties!.Filters == null);
		AssertEx.Equal(0, slots[1].Properties!.Filters!.Count());
		AssertEx.True(slots[2].Properties == null);
		AssertAllowsUplink(slots[3]);
	}

	[RegressionTest]
	private static void PocketConfigurationIsIdempotentIncludingForeignSlots()
	{
		TemplateTable templates = CreateTemplates(6);
		AddPockets(templates, CustomPocketsId, 3);
		var service = new TscUplinkSpecialSlotService(templates);
		service.ConfigurePocketTemplates();
		string first = JsonSerializer.Serialize(templates.Items.Values);
		service.ConfigurePocketTemplates();
		AssertEx.Equal(first, JsonSerializer.Serialize(templates.Items.Values));
	}

	[RegressionTest]
	private static async Task ReconciliationNeverMovesOrSavesExistingProfileItems()
	{
		TemplateTable templates = CreateTemplates(6);
		var saveServer = new SaveServer();
		int saves = 0;
		saveServer.SaveProfile = _ => { saves++; return Task.FromException(new IOException("Profiles must not be saved")); };
		var items = new List<Item>();
		foreach (string slot in new[] { "SpecialSlot1", "SpecialSlot3", "SpecialSlot4", "SpecialSlot6", "hideout", "main" })
		{
			items.Add(new Item { Id = $"phone-{slot}", Template = TscUplinkSpecialSlotService.UplinkTemplateId, ParentId = "pockets", SlotId = slot });
		}
		items.Add(new Item { Id = "foreign-occupant", Template = OtherAllowedItem, ParentId = "pockets", SlotId = "SpecialSlot4" });
		saveServer.Profiles[new MongoId("session")] = new SptProfile
		{
			CharacterData = new Characters { PmcData = new PmcData { Inventory = new BotBaseInventory { Items = items } } }
		};
		string before = JsonSerializer.Serialize(saveServer.Profiles.Values);
		var hook = new TscUplinkProfileMigrationOnLoad(new TscUplinkSpecialSlotService(templates));

		await hook.OnLoadAsync(CancellationToken.None);
		await hook.OnLoadAsync(CancellationToken.None);

		AssertEx.Equal(before, JsonSerializer.Serialize(saveServer.Profiles.Values));
		AssertEx.Equal(0, saves);
	}

	[RegressionTest]
	private static async Task CancelledReconciliationDoesNotChangeTemplates()
	{
		TemplateTable templates = CreateTemplates();
		string before = JsonSerializer.Serialize(templates.Items.Values);
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		var hook = new TscUplinkProfileMigrationOnLoad(new TscUplinkSpecialSlotService(templates));
		await AssertEx.ThrowsAsync<OperationCanceledException>(() => hook.OnLoadAsync(cancellation.Token));
		AssertEx.Equal(before, JsonSerializer.Serialize(templates.Items.Values));
	}

	private static void AssertAllowsUplink(Slot slot)
	{
		foreach (SlotFilter filter in slot.Properties!.Filters!)
			AssertEx.True(filter.Filter!.Contains(UplinkId), $"{slot.Name} must accept the Uplink.");
	}

	private static TemplateTable CreateTemplates(int slotCount = 3)
	{
		var templates = new TemplateTable();
		AddPockets(templates, TscUplinkSpecialSlotService.StandardPocketsTemplateId, slotCount);
		AddPockets(templates, TscUplinkSpecialSlotService.UnheardPocketsTemplateId, slotCount);
		return templates;
	}

	private static TemplateItem AddPockets(TemplateTable templates, string templateId, int slotCount,
		string parentId = TscUplinkSpecialSlotService.PocketsParentId)
	{
		var pockets = new TemplateItem
		{
			Id = new MongoId(templateId),
			Parent = new MongoId(parentId),
			Properties = new TemplateItemProperties { Slots = Enumerable.Range(1, slotCount).Select(CreateSlot).ToArray() }
		};
		templates.Items[pockets.Id] = pockets;
		return pockets;
	}

	private static Slot CreateSlot(int index)
	{
		return new Slot
		{
			Id = new MongoId(index.ToString("x24")),
			Name = $"SpecialSlot{index}",
			Properties = new SlotProperties
			{
				Filters = [new SlotFilter { Filter = [new MongoId(OtherAllowedItem)], Locked = false }]
			}
		};
	}
}
