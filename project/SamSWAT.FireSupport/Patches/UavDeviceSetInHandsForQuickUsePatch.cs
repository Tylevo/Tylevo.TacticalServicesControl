using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using SamSWAT.FireSupport.ArysReloaded.Unity;
using SPT.Reflection.Patching;
using System;
using System.Linq;
using System.Reflection;

namespace SamSWAT.FireSupport.ArysReloaded.Patches;

internal sealed class UavDeviceSetInHandsForQuickUsePatch : ModulePatch
{
	protected override MethodBase GetTargetMethod()
	{
		return typeof(Player)
			.GetMethods(BindingFlags.Public | BindingFlags.Instance)
			.First(method =>
				method.Name == nameof(Player.SetInHandsForQuickUse) &&
				method.GetParameters().Length == 2 &&
				method.GetParameters()[0].ParameterType == typeof(Item));
	}

	[PatchPrefix]
	private static bool Prefix(Player __instance, Item __0, Callback<IOnHandsUseCallback> __1)
	{
		Item item = __0;
		Callback<IOnHandsUseCallback> callback = __1;

		if (UavDeviceController.ShouldSuppressQuickUse(__instance) &&
		    !UavDeviceConstants.IsUavDeviceTemplate(item))
		{
			TscDiagnostics.LogPhone(
				$"TSC Uplink suppressed quick-slot hand swap while active. item={item?.Id ?? "<null>"}, tpl={item?.StringTemplateId ?? "<null>"}.");
			return false;
		}

		if (item is not UavDeviceItem)
		{
			if (UavDeviceConstants.IsUavDeviceTemplate(item))
			{
				FireSupportPlugin.LogSource.LogWarning(
					$"TerraGroup TSC Uplink quick-use not routed: runtime item type is {item.GetType().FullName}, expected {typeof(UavDeviceItem).FullName}.");
			}

			return true;
		}

		TscDiagnostics.LogPhone(
			$"TerraGroup TSC Uplink quick-use forwarding to SetInHandsUsableItem. item={item.Id}, tpl={item.TemplateId}, type={item.GetType().FullName}.");

		Callback<GInterface202> wrappedCallback = result =>
		{
			if (callback == null)
			{
				return;
			}

			if (result.Failed)
			{
				callback.Invoke(new Result<IOnHandsUseCallback>(null, result.Error, result.ErrorCode));
				return;
			}

			if (result.Value is IOnHandsUseCallback quickUseController)
			{
				callback.Invoke(new Result<IOnHandsUseCallback>(quickUseController));
				return;
			}

			callback.Invoke(new Result<IOnHandsUseCallback>(
				null,
				"TerraGroup TSC Uplink controller did not implement IOnHandsUseCallback.",
				0));
		};

		__instance.SetInHandsUsableItem(item, wrappedCallback);
		return false;
	}
}
