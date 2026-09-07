using System.Text.Json;

internal static class PilotQuestlineDataTests
{
	private const string ServerRoot = "project/SamSWAT.FireSupport.Server/";
	private const string BaseDataRoot = ServerRoot + "CopyToOutput/db/";
	private const string AddonRoot = "addons/pilot-questline/";
	private const string DataRoot = AddonRoot + "db/";
	private const string Mechanic = "5a7c2eca46aef81a7ca2145d";
	private const string Pilot = "66f51f3a0000000000000a60";
	private const string OpenChannel = "66f51f3a0000000000000b01";
	private const string Assembly = "66f51f3a0000000000000b02";
	private const string OnTheAir = "66f51f3a0000000000000b03";
	private const string Uplink = "66f51f3a0000000000000a01";
	private const string UplinkOffer = "66f51f3a0000000000000a02";
	private const string Repeater = "63a0b2eabea67a6d93009e52";
	private const string RepeaterOffer = "66f51f3a0000000000000a03";
	private const string Rouble = "5449016a4bdc2d6f028b456f";

	[RegressionTest]
	private static void OptionalQuestContentIsSeparateFromTheBaseInstallAndPinsItsCompatibility()
	{
		AssertEx.False(Directory.Exists(Resolve(BaseDataRoot + "CustomQuests")),
			"The main download must not import the optional introduction or its purchase gates.");
		using JsonDocument manifest = Load(AddonRoot + "addon.json");
		JsonElement root = manifest.RootElement;
		AssertEx.Equal(4, root.EnumerateObject().Count());
		AssertEx.Equal(1, root.GetProperty("schemaVersion").GetInt32());
		AssertEx.Equal("tsc-pilot-questline", root.GetProperty("id").GetString());
		AssertEx.Equal("1.3.11", root.GetProperty("version").GetString());
		AssertEx.Equal("4.1.5", root.GetProperty("targetSptVersion").GetString());
		AssertEx.Equal(5, Directory.GetFiles(Resolve(DataRoot + "CustomQuests"), "*.json", SearchOption.AllDirectories).Length);
		AssertEx.Equal(0, Directory.GetFiles(Resolve(AddonRoot), "*.dll", SearchOption.AllDirectories).Length,
			"The optional introduction must work with the shared main mod binaries.");
	}

	[RegressionTest]
	private static void IntroductionHasAnAttainableLinearSequenceAndOrdinaryHandovers()
	{
		Dictionary<string, JsonElement> quests = LoadQuests();
		AssertEx.Equal(3, quests.Count);
		AssertEx.Equal(Mechanic, quests[OpenChannel].GetProperty("traderId").GetString());
		AssertEx.Equal(Pilot, quests[Assembly].GetProperty("traderId").GetString());
		AssertEx.Equal(Pilot, quests[OnTheAir].GetProperty("traderId").GetString());
		JsonElement[] firstStart = Conditions(quests[OpenChannel], "AvailableForStart");
		AssertEx.Equal(1, firstStart.Length);
		AssertEx.Equal("Level", firstStart[0].GetProperty("conditionType").GetString());
		AssertEx.Equal(">=", firstStart[0].GetProperty("compareMethod").GetString());
		AssertEx.Equal(5, firstStart[0].GetProperty("value").GetInt32());
		AssertPrerequisite(quests[Assembly], OpenChannel);
		AssertPrerequisite(quests[OnTheAir], Assembly);
		AssertHandovers(quests[OpenChannel], new Dictionary<string, int>
		{
			["5c06779c86f77426e00dd782"] = 2,
			["5c06782b86f77426df5407d2"] = 2
		});
		AssertHandovers(quests[Assembly], new Dictionary<string, int>
		{
			["56742c324bdc2d150f8b456d"] = 1,
			["6389c70ca33d8c4cdf4932c6"] = 1,
			["590c2d8786f774245b1f03f3"] = 1
		});
		foreach (JsonElement quest in quests.Values)
		{
			AssertEx.False(quest.GetProperty("restartable").GetBoolean());
			AssertEx.False(quest.GetProperty("instantComplete").GetBoolean());
			AssertEx.Equal("Pmc", quest.GetProperty("side").GetString());
			AssertEx.Equal(0, Conditions(quest, "Fail").Length);
		}
	}

	[RegressionTest]
	private static void AntennaInstallationPersistsAndSurvivalCannotPrecedeIt()
	{
		JsonElement quest = LoadQuests()[OnTheAir];
		JsonElement[] finish = Conditions(quest, "AvailableForFinish");
		AssertEx.Equal(2, finish.Length);
		JsonElement placement = finish.Single(c => c.GetProperty("conditionType").GetString() == "PlaceBeacon");
		AssertEx.Equal("place_SIGNAL_03_1", placement.GetProperty("zoneId").GetString());
		AssertEx.Equal(40, placement.GetProperty("plantTime").GetInt32());
		AssertEx.Equal(1, placement.GetProperty("value").GetInt32());
		AssertEx.SequenceEqual(new[] { Repeater }, Strings(placement.GetProperty("target")));
		JsonElement survival = finish.Single(c => c.GetProperty("conditionType").GetString() == "CounterCreator");
		AssertEx.False(survival.GetProperty("oneSessionOnly").GetBoolean(),
			"A lost extraction may be retried after the completed repeater installation.");
		AssertEx.False(survival.GetProperty("isResetOnConditionFailed").GetBoolean());
		AssertEx.Equal(1, survival.GetProperty("value").GetInt32());
		JsonElement[] counter = survival.GetProperty("counter").GetProperty("conditions").EnumerateArray().ToArray();
		AssertEx.Equal(2, counter.Length);
		AssertEx.SequenceEqual(new[] { "Survived" }, Strings(counter.Single(c =>
			c.GetProperty("conditionType").GetString() == "ExitStatus").GetProperty("status")));
		AssertEx.SequenceEqual(new[] { "Shoreline" }, Strings(counter.Single(c =>
			c.GetProperty("conditionType").GetString() == "Location").GetProperty("target")));
		JsonElement visibility = survival.GetProperty("visibilityConditions");
		AssertEx.Equal(1, visibility.GetArrayLength());
		AssertEx.Equal("CompleteCondition", visibility[0].GetProperty("conditionType").GetString());
		AssertEx.Equal(placement.GetProperty("id").GetString(), visibility[0].GetProperty("target").GetString());
	}

	[RegressionTest]
	private static void RewardsUnlockPilotThenSupplyAndAwardTheDeviceAtTheCorrectStages()
	{
		Dictionary<string, JsonElement> quests = LoadQuests();
		AssertRewards(quests[OpenChannel], 2500, 20000, 0.01, Mechanic);
		AssertRewards(quests[Assembly], 3000, 25000, 0.03, Pilot);
		AssertRewards(quests[OnTheAir], 4000, 125000, 0.04, Pilot);
		JsonElement unlock = Rewards(quests[OpenChannel], "Success").Single(r => r.GetProperty("type").GetString() == "TraderUnlock");
		AssertEx.Equal(Pilot, unlock.GetProperty("target").GetString());
		AssertEx.Equal(0, Rewards(quests[OpenChannel], "Started").Length);
		AssertEx.Equal(0, Rewards(quests[Assembly], "Started").Length);
		AssertEx.Equal(1, RewardItemCount(quests[OnTheAir], "Started", Repeater));
		AssertEx.Equal(1, RewardItemCount(quests[OnTheAir], "Success", Uplink));
		AssertEx.Equal(0, RewardItemCount(quests[OnTheAir], "Started", Uplink));
		AssertEx.Equal(0, RewardItemCount(quests[Assembly], "Success", Uplink));
		AssertEx.Equal(0, RewardItemCount(quests[OpenChannel], "Success", Uplink));
		AssertEx.Equal(0, RewardItemCount(quests[OnTheAir], "Success", Repeater));
		AssertEx.Equal(4, Rewards(quests[OpenChannel], "Success").Length);
		AssertEx.Equal(3, Rewards(quests[Assembly], "Success").Length);
		AssertEx.Equal(2, Rewards(quests[OnTheAir], "Started").Length);
		AssertEx.Equal(5, Rewards(quests[OnTheAir], "Success").Length);
	}

	[RegressionTest]
	private static void ReplacementOffersUseNativeQuestGatesAndTheDeviceHasNoRandomLootBypass()
	{
		using JsonDocument gates = Load(DataRoot + "CustomQuests/" + Pilot + "/QuestAssort/pilot_introduction.json");
		AssertEx.Equal(OnTheAir, gates.RootElement.GetProperty("started").GetProperty(RepeaterOffer).GetString());
		AssertEx.Equal(OnTheAir, gates.RootElement.GetProperty("success").GetProperty(UplinkOffer).GetString());
		AssertEx.Equal(1, gates.RootElement.GetProperty("started").EnumerateObject().Count());
		AssertEx.Equal(1, gates.RootElement.GetProperty("success").EnumerateObject().Count());
		AssertEx.Equal(0, gates.RootElement.GetProperty("fail").EnumerateObject().Count());
		foreach ((string path, string offer, string tpl, int price) in new[]
		{
			(BaseDataRoot + "CustomAssortSchemes/jaeger_uav_uplink.json", UplinkOffer, Uplink, 50000),
			(DataRoot + "CustomAssortSchemes/pilot_repeater.json", RepeaterOffer, Repeater, 20000)
		})
		{
			using JsonDocument assort = Load(path);
			AssertEx.Equal(1, assort.RootElement.EnumerateObject().Count());
			JsonElement shop = assort.RootElement.GetProperty(Pilot);
			AssertEx.Equal(1, shop.GetProperty("items").GetArrayLength(),
				"The base phone and optional repeater offers must each exist in only their own package.");
			AssertEx.Equal(1, shop.GetProperty("barter_scheme").EnumerateObject().Count());
			AssertEx.Equal(1, shop.GetProperty("loyal_level_items").EnumerateObject().Count());
			JsonElement offerItem = shop.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("_id").GetString() == offer);
			AssertEx.Equal(tpl, offerItem.GetProperty("_tpl").GetString());
			AssertEx.True(offerItem.GetProperty("upd").GetProperty("UnlimitedCount").GetBoolean());
			JsonElement cost = shop.GetProperty("barter_scheme").GetProperty(offer);
			AssertEx.Equal(1, cost.GetArrayLength());
			AssertEx.Equal(1, cost[0].GetArrayLength());
			AssertEx.Equal(Rouble, cost[0][0].GetProperty("_tpl").GetString());
			AssertEx.Equal(price, cost[0][0].GetProperty("count").GetInt32());
			AssertEx.Equal(1, shop.GetProperty("loyal_level_items").GetProperty(offer).GetInt32());
		}
		using JsonDocument item = Load(BaseDataRoot + "CustomItems/RaidOpsUavDevice.json");
		JsonElement uplink = item.RootElement.GetProperty(Uplink);
		AssertEx.False(uplink.GetProperty("addtoStaticLootContainers").GetBoolean());
		AssertEx.Equal(0, uplink.GetProperty("staticLootContainers").GetArrayLength());
		AssertEx.False(uplink.GetProperty("addLooseLoot").GetBoolean());
		AssertEx.False(uplink.GetProperty("addtoBots").GetBoolean());
		AssertEx.False(uplink.GetProperty("overrideProperties").GetProperty("CanSellOnRagfair").GetBoolean());
	}

	[RegressionTest]
	private static void QuestReferencesHaveLocalesAndLoadAfterTheTraderAndAssortments()
	{
		Dictionary<string, JsonElement> quests = LoadQuests();
		var identifiers = new HashSet<string>(StringComparer.Ordinal);
		foreach ((string questId, JsonElement quest) in quests)
		{
			AssertEx.Equal(questId, quest.GetProperty("_id").GetString());
			AssertId(questId, identifiers);
			using JsonDocument locales = Load(DataRoot + "CustomQuests/" + quest.GetProperty("traderId").GetString() + "/Locales/en.json");
			foreach (string key in new[] { "name", "description", "startedMessageText", "successMessageText", "failMessageText",
				"acceptPlayerMessage", "declinePlayerMessage", "completePlayerMessage", "changeQuestMessageText", "note" })
				AssertLocale(locales.RootElement, quest.GetProperty(key).GetString()!);
			foreach (JsonElement condition in Conditions(quest, "AvailableForFinish"))
				AssertLocale(locales.RootElement, condition.GetProperty("id").GetString()!);
			foreach (JsonProperty stage in quest.GetProperty("conditions").EnumerateObject())
				AssertNestedIds(stage.Value, identifiers);
			foreach (JsonProperty stage in quest.GetProperty("rewards").EnumerateObject())
				foreach (JsonElement reward in stage.Value.EnumerateArray())
					AssertId(reward.GetProperty("id").GetString()!, identifiers);
		}
		string startup = File.ReadAllText(Resolve(ServerRoot + "ServerMod.cs"));
		int policy = startup.IndexOf("questlinePolicy.Initialize(pathToMod, ModMetadata.VERSION, ModMetadata.TARGET_SPT_VERSION)", StringComparison.Ordinal);
		int pilot = startup.IndexOf("uh60DeliveryService.Initialize(pathToMod)", StringComparison.Ordinal);
		int assort = startup.IndexOf("wttCommon.CustomAssortSchemeService.CreateCustomAssortSchemes(assembly)", StringComparison.Ordinal);
		int gate = startup.IndexOf("if (questlinePolicy.QuestlineRequired)", StringComparison.Ordinal);
		int repeater = startup.IndexOf("wttCommon.CustomAssortSchemeService.CreateCustomAssortSchemes(assembly, TscPilotQuestlinePolicy.AssortRelativePath)", StringComparison.Ordinal);
		int import = startup.IndexOf("wttCommon.CustomQuestService.CreateCustomQuests(assembly, TscPilotQuestlinePolicy.QuestRelativePath)", StringComparison.Ordinal);
		AssertEx.True(policy >= 0 && pilot > policy && assort > pilot && gate > assort && repeater > gate && import > repeater,
			"Resolve the add-on policy before registering Pilot, then conditionally import its repeater before native quests.");
		AssertEx.False(startup.Contains("CustomQuestService.CreateCustomQuests(assembly)", StringComparison.Ordinal),
			"The main mod must not import legacy bundled quest files from its default database path.");
	}

	private static void AssertPrerequisite(JsonElement quest, string previous)
	{
		JsonElement[] conditions = Conditions(quest, "AvailableForStart");
		AssertEx.Equal(1, conditions.Length);
		AssertEx.Equal("Quest", conditions[0].GetProperty("conditionType").GetString());
		AssertEx.Equal(previous, conditions[0].GetProperty("target").GetString());
		AssertEx.SequenceEqual(new[] { 4 }, conditions[0].GetProperty("status").EnumerateArray().Select(s => s.GetInt32()));
		AssertEx.Equal(0, conditions[0].GetProperty("availableAfter").GetInt32());
	}

	private static void AssertHandovers(JsonElement quest, Dictionary<string, int> expected)
	{
		JsonElement[] finish = Conditions(quest, "AvailableForFinish");
		AssertEx.Equal(expected.Count, finish.Length, "The supply quests finish with only their ordinary handovers.");
		foreach (JsonElement condition in finish)
		{
			AssertEx.Equal("HandoverItem", condition.GetProperty("conditionType").GetString());
			AssertEx.False(condition.GetProperty("onlyFoundInRaid").GetBoolean());
			AssertEx.Equal(1, condition.GetProperty("target").GetArrayLength());
			string tpl = condition.GetProperty("target")[0].GetString()!;
			AssertEx.True(expected.Remove(tpl, out int count), "Unexpected or duplicate handover template: " + tpl);
			AssertEx.Equal(count, condition.GetProperty("value").GetInt32());
		}
	}

	private static void AssertRewards(JsonElement quest, int experience, int money, double standing, string trader)
	{
		JsonElement[] rewards = Rewards(quest, "Success");
		AssertEx.Equal(experience, rewards.Single(r => r.GetProperty("type").GetString() == "Experience").GetProperty("value").GetInt32());
		AssertEx.Equal(money, RewardItemCount(quest, "Success", Rouble));
		JsonElement reputation = rewards.Single(r => r.GetProperty("type").GetString() == "TraderStanding");
		AssertEx.Equal(trader, reputation.GetProperty("target").GetString());
		AssertEx.Equal(standing, reputation.GetProperty("value").GetDouble());
	}

	private static int RewardItemCount(JsonElement quest, string stage, string tpl) => Rewards(quest, stage)
		.Where(r => r.GetProperty("type").GetString() == "Item")
		.SelectMany(r => r.GetProperty("items").EnumerateArray())
		.Where(i => i.GetProperty("_tpl").GetString() == tpl)
		.Sum(i => i.GetProperty("upd").GetProperty("StackObjectsCount").GetInt32());

	private static JsonElement[] Conditions(JsonElement quest, string stage) => quest.GetProperty("conditions").GetProperty(stage).EnumerateArray().ToArray();
	private static JsonElement[] Rewards(JsonElement quest, string stage) => quest.GetProperty("rewards").GetProperty(stage).EnumerateArray().ToArray();
	private static IEnumerable<string> Strings(JsonElement value) => value.EnumerateArray().Select(x => x.GetString()!);
	private static void AssertLocale(JsonElement locales, string key) => AssertEx.True(
		locales.TryGetProperty(key, out JsonElement value) && !string.IsNullOrWhiteSpace(value.GetString()), "Missing quest locale: " + key);

	private static void AssertNestedIds(JsonElement value, HashSet<string> ids)
	{
		if (value.ValueKind == JsonValueKind.Array)
			foreach (JsonElement child in value.EnumerateArray()) AssertNestedIds(child, ids);
		else if (value.ValueKind == JsonValueKind.Object)
			foreach (JsonProperty property in value.EnumerateObject())
			{
				if (property.Name == "id") AssertId(property.Value.GetString()!, ids);
				else AssertNestedIds(property.Value, ids);
			}
	}

	private static void AssertId(string id, HashSet<string> ids)
	{
		AssertEx.True(id.Length == 24 && id.All(Uri.IsHexDigit), "Quest identifiers must be valid Mongo IDs: " + id);
		AssertEx.True(ids.Add(id), "Duplicate quest, condition, counter, or reward ID: " + id);
	}

	private static Dictionary<string, JsonElement> LoadQuests()
	{
		var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
		foreach (string file in Directory.GetFiles(Resolve(DataRoot + "CustomQuests"), "*.json", SearchOption.AllDirectories)
			.Where(f => Path.GetFileName(Path.GetDirectoryName(f)) == "Quests"))
		{
			using JsonDocument document = JsonDocument.Parse(File.ReadAllText(file));
			foreach (JsonProperty quest in document.RootElement.EnumerateObject()) result.Add(quest.Name, quest.Value.Clone());
		}
		return result;
	}

	private static JsonDocument Load(string path) => JsonDocument.Parse(File.ReadAllText(Resolve(path)));
	private static string Resolve(string relativePath)
	{
		foreach (string seed in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
			for (DirectoryInfo? directory = new(seed); directory != null; directory = directory.Parent)
				if (File.Exists(Path.Combine(directory.FullName, ServerRoot, "ServerMod.cs")))
					return Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
		throw new RegressionAssertionException("Could not locate the TacticalServicesControl source root.");
	}
}
