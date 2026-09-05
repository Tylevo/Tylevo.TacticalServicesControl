using Comfort.Common;
using EFT;
using EFT.Airdrop;
using EFT.InputSystem;
using EFT.InventoryLogic;
using EFT.UI.Gestures;
using HarmonyLib;
using JetBrains.Annotations;
using SamSWAT.FireSupport.ArysReloaded.Unity;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using ZLinq;

namespace SamSWAT.FireSupport.ArysReloaded.Patches;

[UsedImplicitly]
public class GesturesMenuPatch : ModulePatch
{
	protected override MethodBase GetTargetMethod()
	{
		return AccessTools.Method(typeof(EftGamePlayerOwner), nameof(EftGamePlayerOwner.InitBattleUIScreen));
	}

	[PatchPostfix]
	private static async void PatchPostfix(IBattleUIScreenController ___BattleUIScreenController)
	{
		try
		{
			FireSupportController.DestroyCurrent("battle UI reinitialized");
			
			if (!IsFireSupportAvailable())
			{
				return;
			}

			GameWorld gameWorld = Singleton<GameWorld>.Instance;
			Player player = gameWorld?.MainPlayer;
			var owner = player?.GetComponent<GamePlayerOwner>();
			if (player == null || owner == null)
			{
				return;
			}
			
			GesturesMenu gesturesMenu = ___BattleUIScreenController.GesturesQuickPanel.GesturesMenu;
			
			var fireSupportController = await FireSupportController.Create(gesturesMenu);
			if (fireSupportController == null ||
			    !ReferenceEquals(FireSupportController.Instance, fireSupportController) ||
			    Singleton<GameWorld>.Instance != gameWorld ||
			    gameWorld.MainPlayer != player ||
			    owner == null)
			{
				if (fireSupportController != null)
				{
					UnityEngine.Object.DestroyImmediate(fireSupportController);
				}

				return;
			}
			
			List<InputNode> children = Traverse.Create(owner)
				.Field<List<InputNode>>("_children")
				.Value;
			if (children != null && !children.Contains(fireSupportController))
			{
				children.Add(fireSupportController);
			}

			if (FireSupportUI.IsRadialWorkflowEnabled())
			{
				var gesturesBindPanel = (GesturesBindPanel)AccessTools.Field(typeof(GesturesMenu),
						"_gesturesBindPanel")
					.GetValue(gesturesMenu);

				gesturesBindPanel.transform.localPosition = new Vector3(0, -530, 0);
			}
		}
		catch (OperationCanceledException) {}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource.LogError(ex);
		}
	}

	private static bool IsFireSupportAvailable()
	{
		if (!PluginSettings.Enabled.Value)
		{
			return false;
		}

		GameWorld gameWorld = Singleton<GameWorld>.Instance;
		if (gameWorld == null)
		{
			return false;
		}

		Player player = gameWorld.MainPlayer;
		if (player == null)
		{
			return false;
		}

		bool locationIsSuitable = player.Location.ToLower() == "sandbox" || LocationScene.GetAll<AirdropPoint>().AsValueEnumerable().Any();
		if (!locationIsSuitable)
		{
			return false;
		}

		bool hasFireSupportDevice = player.Profile.Inventory.AllRealPlayerItems.AsValueEnumerable().Any(IsFireSupportDevice);
		return hasFireSupportDevice;
	}

	private static bool IsFireSupportDevice(Item item)
	{
		return item.TemplateId == ItemConstants.RANGEFINDER_TPL ||
		       item.TemplateId == UavDeviceConstants.UavDeviceTpl;
	}
}
