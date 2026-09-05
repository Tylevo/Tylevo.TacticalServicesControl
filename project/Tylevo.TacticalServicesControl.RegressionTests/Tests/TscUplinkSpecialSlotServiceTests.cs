using SamSWAT.FireSupport.ArysReloaded;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Servers;

internal static class TscUplinkSpecialSlotServiceTests
{
	private const string OtherAllowedItem = "5f4fbaaca5573a5ac31db429";

	[RegressionTest]
	private static void MigrationHookRunsAfterSptSaveCallbacksAndNormalModRegistration()
	{
		var attribute = (SPTarkov.DI.Annotations.InjectableAttribute?)Attribute.GetCustomAttribute(
			typeof(TscUplinkProfileMigrationOnLoad),
			typeof(SPTarkov.DI.Annotations.InjectableAttribute));

		attribute = AssertEx.NotNull(attribute);
		AssertEx.Equal(
			SPTarkov.Server.Core.DI.OnLoadOrder.PostLoad + 1,
			attribute.TypePriority);
		AssertEx.True(
			attribute.TypePriority > SPTarkov.Server.Core.DI.OnLoadOrder.SaveCallbacks);
	}

	[RegressionTest]
	private static void ConfiguresBothPocketTemplatesWithExclusiveFourthSlot()
	{
		Rig rig = CreateRig();

		rig.Service.ConfigurePocketTemplates();

		AssertPocketContract(rig.Templates, TscUplinkSpecialSlotService.StandardPocketsTemplateId);
		AssertPocketContract(rig.Templates, TscUplinkSpecialSlotService.UnheardPocketsTemplateId);
	}

	[RegressionTest]
	private static async Task PostLoadHookReassertsExclusiveFilterAfterLaterWttRegistration()
	{
		Rig rig = CreateRig();
		rig.Service.ConfigurePocketTemplates();
		TemplateItem pockets = rig.Templates.Items[
			new MongoId(TscUplinkSpecialSlotService.StandardPocketsTemplateId)];
		Slot dedicated = pockets.Properties!.Slots!.Single(slot =>
			string.Equals(
				slot.Name,
				TscUplinkSpecialSlotService.DedicatedSlotName,
				StringComparison.OrdinalIgnoreCase));
		dedicated.Properties!.Filters!.Single().Filter!.Add(new MongoId(OtherAllowedItem));

		var hook = new TscUplinkProfileMigrationOnLoad(rig.Service);
		await hook.OnLoadAsync(CancellationToken.None);

		IEnumerable<SlotFilter> filters = AssertEx.NotNull(dedicated.Properties.Filters);
		SlotFilter filter = filters.Single();
		AssertEx.Equal(1, filter.Filter!.Count);
		AssertEx.True(filter.Filter.Contains(
			new MongoId(TscUplinkSpecialSlotService.UplinkTemplateId)));
	}

	[RegressionTest]
	private static void PocketConfigurationIsIdempotent()
	{
		Rig rig = CreateRig();

		rig.Service.ConfigurePocketTemplates();
		rig.Service.ConfigurePocketTemplates();

		foreach (TemplateItem pockets in rig.Templates.Items.Values)
		{
			AssertEx.Equal(
				1,
				pockets.Properties!.Slots!.Count(slot =>
					string.Equals(
						slot.Name,
						TscUplinkSpecialSlotService.DedicatedSlotName,
						StringComparison.OrdinalIgnoreCase)));
		}
	}

	[RegressionTest]
	private static void ForeignFourthSlotConflictPreservesExistingSlotEligibility()
	{
		Rig rig = CreateRig();
		TemplateItem pockets = rig.Templates.Items[
			new MongoId(TscUplinkSpecialSlotService.StandardPocketsTemplateId)];
		List<Slot> slots = pockets.Properties!.Slots!.ToList();
		slots.Add(new Slot
		{
			Id = new MongoId("aaaaaaaaaaaaaaaaaaaaaaaa"),
			Name = TscUplinkSpecialSlotService.DedicatedSlotName,
			Properties = new SlotProperties
			{
				Filters =
				[
					new SlotFilter { Filter = [new MongoId(OtherAllowedItem)] }
				]
			}
		});
		pockets.Properties.Slots = slots;

		rig.Service.ConfigurePocketTemplates();

		AssertEx.Equal(
			1,
			pockets.Properties.Slots.Count(slot =>
				string.Equals(
					slot.Name,
					TscUplinkSpecialSlotService.DedicatedSlotName,
					StringComparison.OrdinalIgnoreCase)));
		foreach (Slot legacy in pockets.Properties.Slots.Where(slot => slot.Name is
		         "SpecialSlot1" or "SpecialSlot2" or "SpecialSlot3"))
		{
			AssertEx.True(legacy.Properties!.Filters!.Single().Filter!.Contains(
				new MongoId(TscUplinkSpecialSlotService.UplinkTemplateId)));
		}
		AssertEx.True(rig.Logger.Warnings.Any(message => message.Contains("foreign")));
	}

	[RegressionTest]
	private static async Task MigratesOnlyDirectlyEquippedLegacyUplink()
	{
		Rig rig = CreateRig();
		ProfileFixture fixture = AddProfile(rig, "session-a");
		Item equipped = AddItem(
			fixture.Items,
			"equipped-uplink",
			TscUplinkSpecialSlotService.UplinkTemplateId,
			fixture.Pockets.Id,
			"SpecialSlot2");
		Item stash = AddItem(
			fixture.Items,
			"stash-uplink",
			TscUplinkSpecialSlotService.UplinkTemplateId,
			"stash-root",
			"hideout");
		Item backpack = AddItem(
			fixture.Items,
			"backpack-uplink",
			TscUplinkSpecialSlotService.UplinkTemplateId,
			"backpack",
			"main");

		await rig.Service.MigrateLoadedProfilesAsync(CancellationToken.None);
		await rig.Service.MigrateLoadedProfilesAsync(CancellationToken.None);

		AssertEx.Equal(TscUplinkSpecialSlotService.DedicatedSlotName, equipped.SlotId);
		AssertEx.Equal("hideout", stash.SlotId);
		AssertEx.Equal("main", backpack.SlotId);
		AssertEx.Equal(1, rig.SaveCount, "An already-migrated profile must not be saved twice.");
	}

	[RegressionTest]
	private static async Task IgnoresUplinkOnUnsupportedPocketsTemplate()
	{
		Rig rig = CreateRig();
		ProfileFixture fixture = AddProfile(
			rig,
			"session-unsupported",
			"aaaaaaaaaaaaaaaaaaaaaaaa");
		Item uplink = AddItem(
			fixture.Items,
			"unsupported-uplink",
			TscUplinkSpecialSlotService.UplinkTemplateId,
			fixture.Pockets.Id,
			"SpecialSlot1");

		await rig.Service.MigrateLoadedProfilesAsync(CancellationToken.None);

		AssertEx.Equal("SpecialSlot1", uplink.SlotId);
		AssertEx.Equal(0, rig.SaveCount);
	}

	[RegressionTest]
	private static async Task OccupiedFourthSlotFailsClosedWithoutItemLoss()
	{
		Rig rig = CreateRig();
		ProfileFixture fixture = AddProfile(rig, "session-conflict");
		Item uplink = AddItem(
			fixture.Items,
			"legacy-uplink",
			TscUplinkSpecialSlotService.UplinkTemplateId,
			fixture.Pockets.Id,
			"SpecialSlot1");
		Item occupant = AddItem(
			fixture.Items,
			"foreign-occupant",
			OtherAllowedItem,
			fixture.Pockets.Id,
			TscUplinkSpecialSlotService.DedicatedSlotName);

		await rig.Service.MigrateLoadedProfilesAsync(CancellationToken.None);

		AssertEx.Equal("SpecialSlot1", uplink.SlotId);
		AssertEx.Equal(TscUplinkSpecialSlotService.DedicatedSlotName, occupant.SlotId);
		AssertEx.Equal(0, rig.SaveCount);
		AssertEx.True(rig.Logger.Warnings.Any(message => message.Contains("already occupied")));
	}

	[RegressionTest]
	private static async Task MultipleLegacyUplinksFailClosedWithoutChoosingOne()
	{
		Rig rig = CreateRig();
		ProfileFixture fixture = AddProfile(rig, "session-ambiguous");
		Item first = AddItem(
			fixture.Items,
			"legacy-one",
			TscUplinkSpecialSlotService.UplinkTemplateId,
			fixture.Pockets.Id,
			"SpecialSlot1");
		Item second = AddItem(
			fixture.Items,
			"legacy-two",
			TscUplinkSpecialSlotService.UplinkTemplateId,
			fixture.Pockets.Id,
			"SpecialSlot3");

		await rig.Service.MigrateLoadedProfilesAsync(CancellationToken.None);

		AssertEx.Equal("SpecialSlot1", first.SlotId);
		AssertEx.Equal("SpecialSlot3", second.SlotId);
		AssertEx.Equal(0, rig.SaveCount);
		AssertEx.True(rig.Logger.Warnings.Any(message => message.Contains("ambiguous")));
	}

	[RegressionTest]
	private static async Task SaveFailureRollsBackInMemoryMigration()
	{
		Rig rig = CreateRig();
		ProfileFixture fixture = AddProfile(rig, "session-save-failure");
		Item uplink = AddItem(
			fixture.Items,
			"legacy-uplink",
			TscUplinkSpecialSlotService.UplinkTemplateId,
			fixture.Pockets.Id,
			"SpecialSlot3");
		rig.SaveServer.SaveProfile = _ =>
		{
			rig.SaveCount++;
			return Task.FromException(new IOException("synthetic save failure"));
		};

		await rig.Service.MigrateLoadedProfilesAsync(CancellationToken.None);

		AssertEx.Equal("SpecialSlot3", uplink.SlotId);
		AssertEx.Equal(1, rig.SaveCount);
		AssertEx.Equal(1, rig.Logger.Errors.Count);
	}

	private static void AssertPocketContract(TemplateTable templates, string templateId)
	{
		TemplateItem pockets = templates.Items[new MongoId(templateId)];
		List<Slot> slots = pockets.Properties!.Slots!.ToList();
		Slot dedicated = AssertEx.NotNull(slots.SingleOrDefault(slot =>
			string.Equals(
				slot.Name,
				TscUplinkSpecialSlotService.DedicatedSlotName,
				StringComparison.OrdinalIgnoreCase)));
		SlotFilter filter = AssertEx.NotNull(dedicated.Properties?.Filters?.SingleOrDefault());

		AssertEx.False(dedicated.Required ?? true);
		AssertEx.False(dedicated.MergeSlotWithChildren ?? true);
		AssertEx.False(filter.Locked ?? true);
		AssertEx.Equal(1, filter.Filter!.Count);
		AssertEx.True(filter.Filter.Contains(
			new MongoId(TscUplinkSpecialSlotService.UplinkTemplateId)));

		foreach (Slot legacy in slots.Where(slot => slot.Name is
		         "SpecialSlot1" or "SpecialSlot2" or "SpecialSlot3"))
		{
			SlotFilter legacyFilter = legacy.Properties!.Filters!.Single();
			AssertEx.False(legacyFilter.Filter!.Contains(
				new MongoId(TscUplinkSpecialSlotService.UplinkTemplateId)));
			AssertEx.True(legacyFilter.Filter.Contains(new MongoId(OtherAllowedItem)));
		}
	}

	private static Rig CreateRig()
	{
		var templates = new TemplateTable();
		AddPocketTemplate(templates, TscUplinkSpecialSlotService.StandardPocketsTemplateId);
		AddPocketTemplate(templates, TscUplinkSpecialSlotService.UnheardPocketsTemplateId);

		var logger = new RecordingLogger();
		var saveServer = new SaveServer();
		var rig = new Rig(
			templates,
			logger,
			saveServer,
			new TscUplinkSpecialSlotService(logger, templates, saveServer));
		saveServer.SaveProfile = _ =>
		{
			rig.SaveCount++;
			return Task.CompletedTask;
		};
		return rig;
	}

	private static void AddPocketTemplate(TemplateTable templates, string templateId)
	{
		var slots = new List<Slot>();
		for (int index = 1; index <= 3; index++)
		{
			slots.Add(new Slot
			{
				Id = new MongoId($"00000000000000000000000{index}"),
				Name = $"SpecialSlot{index}",
				Properties = new SlotProperties
				{
					Filters =
					[
						new SlotFilter
						{
							Filter =
							[
								new MongoId(OtherAllowedItem),
								new MongoId(TscUplinkSpecialSlotService.UplinkTemplateId)
							]
						}
					]
				}
			});
		}

		templates.Items[new MongoId(templateId)] = new TemplateItem
		{
			Id = new MongoId(templateId),
			Properties = new TemplateItemProperties { Slots = slots }
		};
	}

	private static ProfileFixture AddProfile(
		Rig rig,
		string sessionId,
		string pocketsTemplateId = TscUplinkSpecialSlotService.StandardPocketsTemplateId)
	{
		rig.Service.ConfigurePocketTemplates();
		var items = new List<Item>();
		Item equipment = AddItem(items, "equipment", "equipment-template", null, null);
		Item pockets = AddItem(items, "pockets", pocketsTemplateId, equipment.Id, "Pockets");
		AddItem(items, "stash-root", "stash-template", null, null);
		AddItem(items, "backpack", "backpack-template", equipment.Id, "Backpack");

		rig.SaveServer.Profiles[new MongoId(sessionId)] = new SptProfile
		{
			CharacterData = new Characters
			{
				PmcData = new PmcData
				{
					Inventory = new BotBaseInventory
					{
						Equipment = new MongoId(equipment.Id),
						Stash = new MongoId("stash-root"),
						Items = items
					}
				}
			}
		};

		return new ProfileFixture(items, pockets);
	}

	private static Item AddItem(
		List<Item> items,
		string id,
		string template,
		string? parentId,
		string? slotId)
	{
		var item = new Item
		{
			Id = id,
			Template = template,
			ParentId = parentId,
			SlotId = slotId
		};
		items.Add(item);
		return item;
	}

	private sealed record ProfileFixture(List<Item> Items, Item Pockets);

	private sealed record Rig(
		TemplateTable Templates,
		RecordingLogger Logger,
		SaveServer SaveServer,
		TscUplinkSpecialSlotService Service)
	{
		public int SaveCount { get; set; }
	}

	private sealed class RecordingLogger : ISptLogger<TscUplinkSpecialSlotService>
	{
		public List<string> Warnings { get; } = [];
		public List<string> Errors { get; } = [];

		public void Success(string message)
		{
		}

		public void Warning(string message)
		{
			Warnings.Add(message);
		}

		public void Error(string message)
		{
			Errors.Add(message);
		}

		public void Error(string message, Exception exception)
		{
			Errors.Add(message);
		}
	}
}
