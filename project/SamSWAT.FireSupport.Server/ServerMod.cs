using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Spt.Mod;
using System.Reflection;
using Path = System.IO.Path;
using Range = SemanticVersioning.Range;
using Version = SemanticVersioning.Version;

namespace SamSWAT.FireSupport.ArysReloaded;

public record ServerModMetadata : IModMetadata
{
	public string ModGuid { get; init; } = "com.tylevo.tacticalservicescontrol";
	public string Name { get; init; } = "TylevoTacticalServicesControl";
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
}

[Injectable(TypePriority = OnLoadOrder.GameCallbacks + 1)]
public class ServerMod(
	CustomItemServiceExtended customItemService,
	TscUplinkSpecialSlotService uplinkSpecialSlotService,
	FireSupportServerConfigService fireSupportServerConfigService,
	FireSupportUh60DeliveryService uh60DeliveryService,
	FireSupportUh60TransferFeeService uh60TransferFeeService,
	ModHelper modHelper,
	WTTServerCommonLib.WTTServerCommonLib wttCommon) : IOnLoad
{
	public async Task OnLoadAsync(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		Assembly assembly = Assembly.GetExecutingAssembly();
		string pathToMod = modHelper.GetAbsolutePathToModFolder(assembly);

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
		}
		cancellationToken.ThrowIfCancellationRequested();

		fireSupportServerConfigService.Initialize(pathToMod);
		uh60TransferFeeService.Initialize(pathToMod);
		AddCustomItems();
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
