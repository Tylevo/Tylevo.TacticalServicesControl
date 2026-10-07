using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Web;
using System.Reflection;
using Path = System.IO.Path;
using Range = SemanticVersioning.Range;
using Version = SemanticVersioning.Version;

namespace SamSWAT.FireSupport.ArysReloaded;

public record ServerModMetadata : IModMetadata, IModBlazorMetadata
{
	public string ModGuid { get; init; } = "com.tylevo.tacticalservicescontrol";
	public string Name { get; init; } = "Tactical Services Control";
	public string Author { get; init; } = "Tylevo";
	public List<string>? Contributors { get; init; }
	public Version Version { get; init; } = new(ModMetadata.VERSION);
	public Range SptVersion { get; init; } = new($"~{ModMetadata.TARGET_SPT_VERSION}");
	public bool HasPrepatcher { get; init; }
	public List<string>? Incompatibilities { get; init; }
	public Dictionary<string, Range>? ModDependencies { get; init; } = new()
	{
		{ "com.wtt.commonlib", new Range("~3.0.0") }
	};
	public string? Url { get; init; }
	public string License { get; init; } = "Creative Commons BY-NC 4.0";
	public string? WWWRootUrl { get; init; }
	public string? HomePage { get; init; } = "/tsc/admin";
	public string? HomePageDescription { get; init; } =
		"Open the Tactical Services Control dashboard for service pricing, tuning, and server diagnostics.";
}

[Injectable(TypePriority = OnLoadOrder.GameCallbacks + 1)]
public class ServerMod(
	CustomItemServiceExtended customItemService,
	TscUplinkSpecialSlotService uplinkSpecialSlotService,
	FireSupportServerConfigService fireSupportServerConfigService,
	FireSupportUh60DeliveryService uh60DeliveryService,
	FireSupportUh60TransferFeeService uh60TransferFeeService,
	TscPilotQuestlinePolicy questlinePolicy,
	TemplateTable templateTable,
	TradersTable tradersTable,
	ModHelper modHelper,
	WTTServerCommonLib.WTTServerCommonLib wttCommon) : IOnLoad
{
	public async Task OnLoadAsync(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		Assembly assembly = Assembly.GetExecutingAssembly();
		string pathToMod = modHelper.GetAbsolutePathToModFolder(assembly);
		// Resolve the installed add-on before registering traders, loading profiles, or issuing permission snapshots.
		questlinePolicy.Initialize(pathToMod, ModMetadata.VERSION, ModMetadata.TARGET_SPT_VERSION);

		await wttCommon.CustomItemParentService.CreateCustomParents(assembly);
		cancellationToken.ThrowIfCancellationRequested();
		await wttCommon.CustomItemServiceExtended.CreateCustomItems(assembly);
		uplinkSpecialSlotService.ConfigurePocketTemplates();
		customItemService.ApplyHackerModBundleCompatibility(pathToMod);
		// Register the Pilot before WTT resolves the Uplink's trader ID.
		uh60DeliveryService.Initialize(pathToMod);
		if (uh60DeliveryService.IsPilotShopReady)
		{
			await wttCommon.CustomAssortSchemeService.CreateCustomAssortSchemes(assembly);
			cancellationToken.ThrowIfCancellationRequested();
			if (questlinePolicy.QuestlineRequired)
			{
				await wttCommon.CustomAssortSchemeService.CreateCustomAssortSchemes(assembly, TscPilotQuestlinePolicy.AssortRelativePath);
				cancellationToken.ThrowIfCancellationRequested();
				// Quest rewards and replacement offers reference the registered Pilot and Uplink.
				await wttCommon.CustomQuestService.CreateCustomQuests(assembly, TscPilotQuestlinePolicy.QuestRelativePath);
			}
		}
		cancellationToken.ThrowIfCancellationRequested();
		ValidatePilotContent();
		questlinePolicy.Activate();

		fireSupportServerConfigService.Initialize(pathToMod);
		uh60TransferFeeService.Initialize(pathToMod);
		AddCustomItems();
	}

	private void ValidatePilotContent()
	{
		// WTT can report a malformed quest file and continue. Never activate a partially loaded mode.
		if (!tradersTable.TryGetValue(TscPilotQuestlinePolicy.PilotId, out var pilot) ||
		    !uh60DeliveryService.IsPilotShopReady || pilot?.Assort == null ||
		    pilot.Assort.Items?.Any(item => item.Id == TscPilotQuestlinePolicy.PhoneOfferId &&
			item.Template == TscPilotQuestlinePolicy.PhoneTemplateId) != true ||
		    !pilot.Assort.BarterScheme.ContainsKey(TscPilotQuestlinePolicy.PhoneOfferId) ||
		    !pilot.Assort.LoyalLevelItems.ContainsKey(TscPilotQuestlinePolicy.PhoneOfferId))
			throw new InvalidOperationException("TSC Pilot shop failed to load; server permissions remain disabled.");
		if (!questlinePolicy.QuestlineRequired) return;

		foreach ((string questId, string traderId) in new[]
		{
			(TscPilotQuestlinePolicy.OpenChannelId, TscPilotQuestlinePolicy.MechanicId),
			(TscPilotQuestlinePolicy.AssemblyQuestId, TscPilotQuestlinePolicy.PilotId),
			(TscPilotProgressionService.FinalQuestId, TscPilotQuestlinePolicy.PilotId)
		})
			if (!templateTable.Quests.TryGetValue(questId, out var quest) ||
			    quest?.Id != questId || quest.TraderId != traderId ||
			    quest.Conditions?.AvailableForFinish?.Count is not > 0 ||
			    quest.Rewards?.GetValueOrDefault("Success")?.Count is not > 0)
				throw new InvalidOperationException($"TSC Pilot Questline quest {questId} failed to load; server permissions remain disabled.");

		if (pilot.Assort.Items?.Any(item => item.Id == TscPilotQuestlinePolicy.RepeaterOfferId &&
			item.Template == TscPilotQuestlinePolicy.RepeaterTemplateId) != true ||
		    !pilot.Assort.BarterScheme.ContainsKey(TscPilotQuestlinePolicy.RepeaterOfferId) ||
		    !pilot.Assort.LoyalLevelItems.ContainsKey(TscPilotQuestlinePolicy.RepeaterOfferId) ||
		    !pilot.QuestAssort.TryGetValue("started", out var started) ||
		    !started.TryGetValue(TscPilotQuestlinePolicy.RepeaterOfferId, out var repeaterQuest) ||
		    repeaterQuest != TscPilotProgressionService.FinalQuestId ||
		    !pilot.QuestAssort.TryGetValue("success", out var success) ||
		    !success.TryGetValue(TscPilotQuestlinePolicy.PhoneOfferId, out var phoneQuest) ||
		    phoneQuest != TscPilotProgressionService.FinalQuestId)
			throw new InvalidOperationException("TSC Pilot Questline purchase unlocks failed to load; server permissions remain disabled.");
	}

	private void AddCustomItems()
	{
		string pathToMod = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
		string pathToModDatabase = Path.Combine(pathToMod, "database");
		string[] databaseFiles = Directory.GetFiles(pathToModDatabase, "*.json");

		foreach (string databaseFile in databaseFiles)
		{
			var newItemDetails = modHelper.GetJsonDataFromFile<NewCustomItemDetails>(pathToModDatabase, databaseFile);
			customItemService.CreateItem(newItemDetails);
		}
	}
}
