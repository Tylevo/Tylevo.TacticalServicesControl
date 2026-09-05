using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Mod;
using System.Reflection;
using Path = System.IO.Path;

namespace SamSWAT.FireSupport.ArysReloaded;

[Injectable]
public class CustomItemServiceExtended(
	ISptLogger<CustomItemServiceExtended> logger,
	CustomItemService customItemService,
	TemplateTable templateTable,
	ItemConfig itemConfig,
	TraderConfig traderConfig,
	RagfairConfig ragfairConfig,
	ScavCaseConfig scavCaseConfig)
{
	private const string UavDeviceTpl = "66f51f3a0000000000000a01";
	private const string HackerModLootBundlePath = "manimal/hacker_loot.bundle";
	private const string HackerModContainerBundlePath = "manimal/hacker_container.bundle";

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

			if (!templateTable.Items.TryGetValue(UavDeviceTpl, out TemplateItem? uavDevice) ||
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

	public void CreateItem(NewCustomItemDetails newItemDetails)
	{
		// TSC's historical item JSON adds only the template/locales. Explicitly
		// disable the newer SPT helper's handbook, flea, and weapon-shelf defaults
		// before delegating the actual database/cache registration.
		newItemDetails.AddToHandbook = false;
		newItemDetails.AddToFleaPriceDb = false;
		newItemDetails.AddToWeaponShelf = false;
		CreateItemResult result = customItemService.CreateItem(
			newItemDetails,
			Assembly.GetExecutingAssembly());
		if (!result.Success)
		{
			logger.Warning(
				$"TSC item {result.ItemId} could not be registered: {string.Join("; ", result.Errors)}");
			return;
		}

		AddToBlacklists(newItemDetails.BlacklistDetails, result.ItemId);
	}

	private void AddToBlacklists(BlacklistDetails? blacklistDetails, MongoId newItemId)
	{
		if (blacklistDetails == null) return;
		
		if (blacklistDetails.BlacklistGlobally)
		{
			itemConfig.Blacklist.Add(newItemId);
		}
		
		if (blacklistDetails.BlacklistFromFence)
		{
			traderConfig.Fence.Blacklist.Add(newItemId);
		}

		if (blacklistDetails.BlacklistFromFlea)
		{
			ragfairConfig.Dynamic.Blacklist.Custom.Add(newItemId);
		}

		if (blacklistDetails.BlacklistFromScavCase)
		{
			scavCaseConfig.RewardItemParentBlacklist.Add(newItemId);
		}
	}
}
