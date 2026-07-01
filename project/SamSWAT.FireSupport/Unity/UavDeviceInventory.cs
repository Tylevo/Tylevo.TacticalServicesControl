using EFT;
using EFT.InventoryLogic;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

internal static class UavDeviceInventory
{
	public static UavDeviceItem FindCarriedUplink(Player player)
	{
		if (player?.InventoryController != null)
		{
			foreach (Item item in player.InventoryController.GetReachableItemsOfType<Item>(IsValidUplink))
			{
				return (UavDeviceItem)item;
			}
		}

		if (player?.Profile?.Inventory?.AllRealPlayerItems == null)
		{
			return null;
		}

		foreach (Item item in player.Profile.Inventory.AllRealPlayerItems)
		{
			if (IsValidUplink(item))
			{
				return (UavDeviceItem)item;
			}
		}

		return null;
	}

	public static string DescribeLocation(Item item)
	{
		if (item == null)
		{
			return "<null>";
		}

		ItemAddress address = item.CurrentAddress ?? item.Parent;
		return address == null ? "<no address>" : $"{address.GetType().FullName}:{address}";
	}

	private static bool IsValidUplink(Item item)
	{
		return item is UavDeviceItem && UavDeviceConstants.IsUavDeviceTemplate(item);
	}
}
