using EFT;
using EFT.InventoryLogic;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

internal static class UavDeviceInventory
{
	private const string DedicatedWarningSlotName = "SpecialSlot4";

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

	public static bool HasUplinkInDedicatedWarningSlot(Player player)
	{
		return FindUplinkInDedicatedWarningSlot(player) != null;
	}

	public static UavDeviceItem FindUplinkInDedicatedWarningSlot(Player player)
	{
		if (player?.Profile?.Inventory?.AllRealPlayerItems == null)
		{
			return null;
		}

		foreach (Item item in player.Profile.Inventory.AllRealPlayerItems)
		{
			ItemAddress address = item?.CurrentAddress;
			if (IsValidUplink(item) &&
			    address?.IsSpecialSlotAddress() == true &&
			    string.Equals(
				    address.Container?.ID,
				    DedicatedWarningSlotName,
				    System.StringComparison.OrdinalIgnoreCase))
			{
				return (UavDeviceItem)item;
			}
		}

		return null;
	}

	private static bool IsValidUplink(Item item)
	{
		return item is UavDeviceItem && UavDeviceConstants.IsUavDeviceTemplate(item);
	}
}
