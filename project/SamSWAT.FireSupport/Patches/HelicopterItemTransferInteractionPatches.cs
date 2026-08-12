using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;
using JetBrains.Annotations;
using SamSWAT.FireSupport.ArysReloaded.Unity;
using SPT.Reflection.Patching;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

namespace SamSWAT.FireSupport.ArysReloaded.Patches;

/// <summary>
/// Supplies a normal EFT interaction action for the requester-local helicopter
/// cargo zone. This respects the player's configured interact binding and
/// opens the transfer screen only after an explicit interaction.
/// </summary>
[UsedImplicitly]
internal sealed class HelicopterItemTransferActionsPatch : ModulePatch
{
	protected override MethodBase GetTargetMethod()
	{
		return AccessTools.Method(
			typeof(InteractionContextHelper),
			nameof(InteractionContextHelper.GetAvailableActions),
			new[]
			{
				typeof(GamePlayerOwner),
				typeof(IInteractive)
			});
	}

	[PatchPrefix]
	private static bool Prefix(
		object[] __args,
		ref AvailableInteractionState __result)
	{
		if (__args == null ||
		    __args.Length < 2 ||
		    __args[1] is not HeliCargoTransferPoint point)
		{
			return true;
		}

		if (__args[0] is not GamePlayerOwner owner ||
		    !FireSupportItemTransfer.IsInteractionAvailable(
			    point,
			    owner.Player))
		{
			__result = null;
			return false;
		}

		__result = new AvailableInteractionState
		{
			Actions = new List<InteractionAction>
			{
				new()
				{
					Name = "SEND ITEMS VIA UH-60",
					Action = () =>
						FireSupportItemTransfer.TryOpen(
							point,
							owner.Player)
				}
			}
		};
		return false;
	}
}

/// <summary>
/// Makes the active helicopter marker participate in EFT's ordinary
/// interaction selection without replacing doors, loot, transit, or other
/// higher-priority nearby interactions.
/// </summary>
[UsedImplicitly]
internal sealed class HelicopterItemTransferInteractionStatePatch : ModulePatch
{
	protected override MethodBase GetTargetMethod()
	{
		return AccessTools.Method(
			typeof(GamePlayerOwner),
			nameof(GamePlayerOwner.InteractionsChangedHandler));
	}

	[PatchPostfix]
	private static void Postfix(GamePlayerOwner __instance)
	{
		if (__instance?.AvailableInteractionState?.Value != null)
		{
			return;
		}

		Player player = __instance?.Player;
		HeliCargoTransferPoint point =
			FireSupportItemTransfer.GetActivePoint(player);
		if (player == null ||
		    point == null ||
		    !FireSupportItemTransfer.IsInteractionAvailable(point, player))
		{
			return;
		}

		AvailableInteractionState actions =
			InteractionContextHelper.GetAvailableActions(__instance, point);
		actions?.InitSelected();
		__instance.AvailableInteractionState.Value = actions;
	}
}

/// <summary>
/// Distinguishes a completed native trader-service purchase from a cancelled
/// transfer screen so cleanup never restores stale pre-purchase availability.
/// </summary>
[UsedImplicitly]
internal sealed class HelicopterItemTransferPurchaseObservedPatch : ModulePatch
{
	protected override MethodBase GetTargetMethod()
	{
		return AccessTools.Method(
			typeof(LocalPlayer),
			nameof(LocalPlayer.ProcessTraderServicePurchase));
	}

	[PatchPostfix]
	private static void Postfix(
		LocalPlayer __instance,
		ETraderServiceType serviceType)
	{
		FireSupportItemTransfer.NotifyServicePurchased(
			__instance,
			serviceType);
	}
}

/// <summary>
/// Substitutes the server-authoritative stash-fee transaction only for the
/// exact requester-local TSC cargo screen. Every other EFT trader service and
/// the default carried-RUB cargo mode execute the public native method
/// unchanged.
/// </summary>
[UsedImplicitly]
internal sealed class HelicopterItemTransferStashFeePurchasePatch : ModulePatch
{
	protected override MethodBase GetTargetMethod()
	{
		return AccessTools.Method(
			typeof(InventoryController),
			nameof(InventoryController.TryPurchaseTraderService),
			new[]
			{
				typeof(ETraderServiceType),
				typeof(EFT.Quests.QuestController),
				typeof(string)
			});
	}

	[PatchPrefix]
	private static bool Prefix(
		InventoryController __instance,
		ETraderServiceType serviceType,
		EFT.Quests.QuestController questController,
		string subServiceId,
		ref Task<bool> __result)
	{
		if (!FireSupportItemTransfer.TryInterceptTraderServicePurchase(
			    __instance,
			    serviceType,
			    questController,
			    subServiceId,
			    out Task<bool> stashPurchaseTask))
		{
			return true;
		}

		__result = stashPurchaseTask;
		return false;
	}
}

/// <summary>
/// EFT's transfer panel normally disables its apply button when carried RUB is
/// below the displayed fee. Stash mode keeps the same native fee display and
/// item validation, but delegates the balance decision to the authenticated
/// Prepare request.
/// </summary>
[UsedImplicitly]
internal sealed class HelicopterItemTransferStashFeeButtonPatch : ModulePatch
{
	protected override MethodBase GetTargetMethod()
	{
		return AccessTools.Method(
			typeof(TransferItemsPanel),
			nameof(TransferItemsPanel.UpdateCounters));
	}

	[PatchPostfix]
	private static void Postfix(
		InventoryController ____inventoryController,
		Stash ____item,
		TransferItemsController ____transferItemsController,
		DefaultUIButton ____transferButton)
	{
		FireSupportItemTransfer.ApplyStashFeeTransferButtonState(
			____inventoryController,
			____item,
			____transferItemsController,
			____transferButton);
	}
}
