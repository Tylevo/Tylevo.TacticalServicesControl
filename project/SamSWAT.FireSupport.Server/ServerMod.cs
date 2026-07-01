using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Spt.Mod;
using System.Reflection;
using Path = System.IO.Path;
using Range = SemanticVersioning.Range;
using Version = SemanticVersioning.Version;

namespace SamSWAT.FireSupport.ArysReloaded;

public record ServerModMetadata : AbstractModMetadata
{
	public override string ModGuid { get; init; } = "com.tylevo.tacticalservicescontrol";
	public override string Name { get; init; } = "TylevoTacticalServicesControl";
	public override string Author { get; init; } = "Tylevo";
	public override List<string>? Contributors { get; init; }
	public override Version Version { get; init; } = new(ModMetadata.VERSION);
	public override Range SptVersion { get; init; } = new($"~{ModMetadata.TARGET_SPT_VERSION}");
	public override List<string>? Incompatibilities { get; init; }
	public override Dictionary<string, Range>? ModDependencies { get; init; } = new()
	{
		{ "com.wtt.commonlib", new Range("~2.0.20") }
	};
	public override string? Url { get; init; }
	public override bool? IsBundleMod { get; init; } = true;
	public override string License { get; init; } = "Creative Commons BY-NC 3.0";
}

[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 2)]
public class ServerMod(
	CustomItemServiceExtended customItemService,
	FireSupportServerConfigService fireSupportServerConfigService,
	ModHelper modHelper,
	WTTServerCommonLib.WTTServerCommonLib wttCommon) : IOnLoad
{
	public async Task OnLoad()
	{
		Assembly assembly = Assembly.GetExecutingAssembly();
		string pathToMod = modHelper.GetAbsolutePathToModFolder(assembly);

		await wttCommon.CustomItemParentService.CreateCustomParents(assembly);
		await wttCommon.CustomItemServiceExtended.CreateCustomItems(assembly);
		await wttCommon.CustomAssortSchemeService.CreateCustomAssortSchemes(assembly);

		fireSupportServerConfigService.Initialize(pathToMod);
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
