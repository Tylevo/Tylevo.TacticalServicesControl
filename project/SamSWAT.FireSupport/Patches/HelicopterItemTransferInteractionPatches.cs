using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;
using JetBrains.Annotations;
using SamSWAT.FireSupport.ArysReloaded.Unity;
using SPT.Reflection.Patching;
using System.Collections.Generic;
using System.Reflection;

namespace SamSWAT.FireSupport.ArysReloaded.Patches;

/// <summary>
/// Sizes EFT's native temporary delivery grid only for the exact active UH-60
/// cargo session. The underlying Transit/BTR controller and delivery remain
/// native, including its occupied-cell protection when a grid is resized.
/// </summary>
[UsedImplicitly]
internal sealed class HelicopterItemTransferGridSizePatch : ModulePatch
{
	protected override MethodBase GetTargetMethod()
	{
		return AccessTools.Method(
			typeof(TransferItemsController),
			nameof(TransferItemsController.GetGridSizeForProfileId),
			new[] { typeof(string) });
	}

	[PatchPostfix]
	private static void Postfix(
		TransferItemsController __instance,
		string __0,
		ref IntVec2 __result)
	{
		FireSupportItemTransfer.OverrideCargoGridSize(__instance, __0, ref __result);
	}
}

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
/// Quotes no additional handling charge only for the active UH-60 temporary
/// cargo grid. EFT keeps its own display, affordability and empty-grid checks.
/// </summary>
[UsedImplicitly]
internal sealed class HelicopterItemTransferIncludedFeeQuotePatch : ModulePatch
{
	protected override MethodBase GetTargetMethod()
	{
		return AccessTools.Method(
			typeof(TransferItemsController),
			nameof(TransferItemsController.GetGridItemsPrice),
			new[] { typeof(Stash), typeof(ETraderServiceType), typeof(float), typeof(float) });
	}

	[PatchPrefix]
	private static bool Prefix(
		TransferItemsController __instance,
		Stash __0,
		ETraderServiceType __1,
		ref int __result)
	{
		if (!FireSupportItemTransfer.TryOverrideCargoTransferPrice(__instance, __0, __1, out int price))
		{
			return true;
		}

		__result = price;
		return false;
	}
}

/// <summary>
/// Gives native simulation and execution a private service-data copy with no
/// payment requirements for this exact UH-60 purchase. Native delivery and
/// every other trader-service requirement remain unchanged.
/// </summary>
[UsedImplicitly]
internal sealed class HelicopterItemTransferIncludedFeePurchasePatch : ModulePatch
{
	protected override MethodBase GetTargetMethod()
	{
		return AccessTools.Method(
			typeof(ItemManipulator),
			nameof(ItemManipulator.PurchaseTraderService),
			new[]
			{
				typeof(GlobalConfiguration.ServiceData),
				typeof(string),
				typeof(EFT.Quests.QuestController),
				typeof(InventoryController),
				typeof(bool)
			});
	}

	[PatchPrefix]
	private static void Prefix(
		ref GlobalConfiguration.ServiceData __0,
		string __1,
		EFT.Quests.QuestController __2,
		InventoryController __3)
	{
		FireSupportItemTransfer.IncludeCargoHandlingInServicePurchase(__3, __2, __1, ref __0);
	}
}
