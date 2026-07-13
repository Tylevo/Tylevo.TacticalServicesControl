using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Server;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Services.Mod;
using SPTarkov.Server.Core.Utils.Cloners;
using System.Reflection;
using Path = System.IO.Path;

namespace SamSWAT.FireSupport.ArysReloaded;

[Injectable]
public class CustomItemServiceExtended(
	ISptLogger<CustomItemService> logger,
	ConfigServer configServer,
	DatabaseService databaseService,
	ItemHelper itemHelper,
	ItemBaseClassService itemBaseClassService,
	ModItemCacheService modItemCacheService,
	ICloner cloner)
	: CustomItemService(logger, databaseService, itemHelper, itemBaseClassService, modItemCacheService, cloner)
{
	private const string UavDeviceTpl = "66f51f3a0000000000000a01";
	private const string HackerModLootBundlePath = "manimal/hacker_loot.bundle";
	private const string HackerModContainerBundlePath = "manimal/hacker_container.bundle";

	private readonly ItemConfig _itemConfig = configServer.GetConfig<ItemConfig>();
	private readonly TraderConfig _traderConfig = configServer.GetConfig<TraderConfig>();
	private readonly RagfairConfig _ragfairConfig = configServer.GetConfig<RagfairConfig>();
	private readonly ScavCaseConfig _scavCaseConfig = configServer.GetConfig<ScavCaseConfig>();
	
	private static readonly MethodInfo s_addModItem =
		typeof(ModItemCacheService).GetMethod("AddModItem", BindingFlags.NonPublic | BindingFlags.Instance)!;

	public void ApplyHackerModBundleCompatibility(string pathToMod)
	{
		try
		{
			ApplyHackerModBundleCompatibilityCore(pathToMod);
		}
		catch (Exception ex)
		{
			logger.Warning($"TSC HackerMod compatibility check failed; keeping TSC phone bundles. {ex.Message}");
		}
	}

	private void ApplyHackerModBundleCompatibilityCore(string pathToMod)
	{
		string normalizedPathToMod = Path.TrimEndingDirectorySeparator(Path.GetFullPath(pathToMod));
		DirectoryInfo? modsDirectory = Directory.GetParent(normalizedPathToMod);
		if (modsDirectory == null || !modsDirectory.Exists)
		{
			logger.Warning("TSC HackerMod compatibility check skipped because the server mods directory could not be resolved.");
			return;
		}

		bool foundPartialHackerBundleSet = false;
		foreach (DirectoryInfo modDirectory in modsDirectory.EnumerateDirectories())
		{
			if (string.Equals(modDirectory.FullName, normalizedPathToMod, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			string hackerLootBundle = Path.Combine(modDirectory.FullName, "bundles", "manimal", "hacker_loot.bundle");
			string hackerContainerBundle = Path.Combine(modDirectory.FullName, "bundles", "manimal", "hacker_container.bundle");
			bool hasLootBundle = File.Exists(hackerLootBundle);
			bool hasContainerBundle = File.Exists(hackerContainerBundle);

			if (!hasLootBundle && !hasContainerBundle)
			{
				continue;
			}

			if (!hasLootBundle || !hasContainerBundle)
			{
				foundPartialHackerBundleSet = true;
				continue;
			}

			DatabaseTables tables = databaseService.GetTables();
			if (!tables.Templates.Items.TryGetValue(UavDeviceTpl, out TemplateItem? uavDevice) ||
			    uavDevice.Properties?.Prefab == null ||
			    uavDevice.Properties.UsePrefab == null)
			{
				logger.Warning("TSC HackerMod compatibility detected HackerMod, but the TSC Uplink item template was unavailable.");
				return;
			}

			uavDevice.Properties.Prefab.Path = HackerModLootBundlePath;
			uavDevice.Properties.Prefab.Rcid = string.Empty;
			uavDevice.Properties.UsePrefab.Path = HackerModContainerBundlePath;
			uavDevice.Properties.UsePrefab.Rcid = string.Empty;

			logger.Success(
				$"TSC HackerMod compatibility enabled. Reusing HackerMod phone bundles from {modDirectory.Name} to prevent duplicate AssetBundle loads.");
			return;
		}

		if (foundPartialHackerBundleSet)
		{
			logger.Warning("TSC found an incomplete HackerMod phone bundle set. Keeping TSC phone bundles to avoid a broken partial redirect.");
		}
	}

	// Thanks to SPT Devs for the code, I've borrowed the CustomItemService logic and modified it for my own use.
	public void CreateItem(NewCustomItemDetails newItemDetails)
	{
		DatabaseTables tables = databaseService.GetTables();

		TemplateItem newItem = newItemDetails.NewItem!;

		// Fail if itemId already exists
		if (tables.Templates.Items.TryGetValue(newItem.Id, out TemplateItem? item))
		{
			logger.Warning($"ItemId already exists. {item.Name}");
			return;
		}

		AddToItemsDb(newItem.Id, newItem);

		AddToLocaleDbs(newItemDetails.Locales!, newItem.Id);
		
		AddToBlacklists(newItemDetails.BlacklistDetails, newItem.Id);

		itemBaseClassService.HydrateItemBaseClassCache();

		s_addModItem.Invoke(modItemCacheService, [Assembly.GetExecutingAssembly(), newItem.Id]);
	}

	private void AddToBlacklists(BlacklistDetails? blacklistDetails, MongoId newItemId)
	{
		if (blacklistDetails == null) return;
		
		if (blacklistDetails.BlacklistGlobally)
		{
			_itemConfig.Blacklist.Add(newItemId);
		}
		
		if (blacklistDetails.BlacklistFromFence)
		{
			_traderConfig.Fence.Blacklist.Add(newItemId);
		}

		if (blacklistDetails.BlacklistFromFlea)
		{
			_ragfairConfig.Dynamic.Blacklist.Custom.Add(newItemId);
		}

		if (blacklistDetails.BlacklistFromScavCase)
		{
			_scavCaseConfig.RewardItemParentBlacklist.Add(newItemId);
		}
	}
}
