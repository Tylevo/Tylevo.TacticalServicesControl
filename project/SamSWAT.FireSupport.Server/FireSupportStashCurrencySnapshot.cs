using SamSWAT.FireSupport.ArysReloaded.Unity;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace SamSWAT.FireSupport.ArysReloaded;

/// <summary>Exports current stash cash without retaining live inventory references.</summary>
internal static class FireSupportStashCurrencySnapshot
{
	private const int MaxInventoryItems = 100_000;
	private const int MaxAncestorDepth = 256;
	private static readonly JsonSerializerOptions NativeJson = new()
	{
		// Native upd names include StackObjectsCount and SpawnedInSession.
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	public static FireSupportStashCurrencyState? Create(PmcData pmc)
	{
		BotBaseInventory? inventory = pmc.Inventory;
		string profileId = pmc.Id?.ToString() ?? string.Empty;
		string stashId = inventory?.Stash?.ToString() ?? string.Empty;
		if (!ValidId(profileId) || !ValidId(stashId) || inventory?.Items == null ||
		    inventory.Items.Count > MaxInventoryItems)
		{
			return null;
		}

		var byId = new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);
		foreach (Item item in inventory.Items.ToArray())
		{
			if (item == null || !ValidId(item.Id.ToString()) || !byId.TryAdd(item.Id.ToString(), item))
			{
				return null;
			}
		}

		var state = new FireSupportStashCurrencyState
		{
			ProfileId = profileId.ToLowerInvariant(),
			StashId = stashId.ToLowerInvariant()
		};
		int metadataLength = 0;
		foreach (Item item in byId.Values)
		{
			string templateId = item.Template.ToString().ToLowerInvariant();
			if (templateId != PaymentCurrencyInfo.RoubleTemplateId &&
			    templateId != PaymentCurrencyInfo.DollarTemplateId &&
			    templateId != PaymentCurrencyInfo.EuroTemplateId)
			{
				continue;
			}

			bool? inStash = IsInStash(item, stashId, inventory.Equipment?.ToString(), byId);
			if (!inStash.HasValue) return null;
			if (!inStash.Value) continue;
			double count = item.Upd?.StackObjectsCount ?? 1d;
			if (state.Items.Count >= FireSupportStashCurrencyState.MaxItems ||
			    !double.IsFinite(count) || count < 1 || count > int.MaxValue || Math.Floor(count) != count ||
			    string.IsNullOrWhiteSpace(item.SlotId) || item.SlotId.Length > 128)
			{
				return null;
			}
			string locationJson;
			string updJson;
			try
			{
				locationJson = JsonSerializer.Serialize(item.Location, NativeJson);
				JsonObject upd = JsonSerializer.SerializeToNode(item.Upd ?? new Upd(), NativeJson)!.AsObject();
				// Missing native counts mean one item. Emit the validated absolute
				// integer explicitly, including when an existing upd omitted it.
				upd["StackObjectsCount"] = (int)count;
				updJson = upd.ToJsonString(NativeJson);
			}
			catch (JsonException) { return null; }
			catch (NotSupportedException) { return null; }
			metadataLength += locationJson.Length + updJson.Length;
			if (locationJson.Length > FireSupportStashCurrencyState.MaxMetadataJsonLength ||
			    updJson.Length > FireSupportStashCurrencyState.MaxMetadataJsonLength ||
			    metadataLength > FireSupportStashCurrencyState.MaxTotalMetadataJsonLength)
			{
				return null;
			}

			state.Items.Add(new FireSupportStashCurrencyItem
			{
				Id = item.Id.ToString().ToLowerInvariant(), TemplateId = templateId,
				ParentId = item.ParentId!.ToLowerInvariant(), SlotId = item.SlotId,
				StackObjectsCount = (int)count, LocationJson = locationJson, UpdJson = updJson
			});
		}
		state.Items.Sort((left, right) => StringComparer.Ordinal.Compare(left.Id, right.Id));
		return state;
	}

	private static bool? IsInStash(Item item, string stashId, string? equipmentId, Dictionary<string, Item> byId)
	{
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { item.Id.ToString() };
		string? parentId = item.ParentId;
		for (int depth = 0; depth < MaxAncestorDepth; depth++)
		{
			if (!ValidId(parentId) || !seen.Add(parentId!)) return null;
			if (string.Equals(parentId, stashId, StringComparison.OrdinalIgnoreCase)) return true;
			if (string.Equals(parentId, equipmentId, StringComparison.OrdinalIgnoreCase)) return false;
			if (!byId.TryGetValue(parentId!, out Item? parent)) return null;
			// A different, known inventory root (for example quest inventory) is out of scope.
			if (string.IsNullOrEmpty(parent.ParentId)) return false;
			parentId = parent.ParentId;
		}
		return null;
	}

	private static bool ValidId(string? value)
	{
		return value?.Length == 24 && value.All(character =>
			character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');
	}
}
