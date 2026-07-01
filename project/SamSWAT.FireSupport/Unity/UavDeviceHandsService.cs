using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using System;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

internal static class UavDeviceHandsService
{
	public static void BeginEquip(
		Player player,
		Item uplinkItem,
		UavPhoneLaunchMode launchMode,
		Action<UavDeviceController> onSpawned,
		Action<Exception> onFailed)
	{
		bool manual = launchMode == UavPhoneLaunchMode.ManualAuthorization;

		try
		{
			if (manual)
			{
				TscDiagnostics.LogPhone("TSC Uplink: beginning explicit controller swap");
			}

			if (player == null)
			{
				throw new ArgumentNullException(nameof(player));
			}

			if (uplinkItem == null)
			{
				throw new ArgumentNullException(nameof(uplinkItem));
			}

			if (!UavDeviceConstants.IsUavDevice(uplinkItem))
			{
				throw new InvalidOperationException(
					$"Item is not a TerraGroup TSC Uplink. tpl={uplinkItem.StringTemplateId}, type={uplinkItem.GetType().FullName}");
			}

			try
			{
				player.StopBlindFire();
			}
			catch (Exception ex)
			{
				FireSupportPlugin.LogSource.LogWarning($"TSC Uplink hands service StopBlindFire failed. {ex}");
			}

			try
			{
				player.RemoveLeftHandItem();
			}
			catch (Exception ex)
			{
				FireSupportPlugin.LogSource.LogWarning($"TSC Uplink hands service RemoveLeftHandItem failed. {ex}");
			}

			try
			{
				player.TrySaveLastItemInHands();
			}
			catch (Exception ex)
			{
				FireSupportPlugin.LogSource.LogWarning($"TSC Uplink hands service TrySaveLastItemInHands failed. {ex}");
			}

			if (manual)
			{
				TscDiagnostics.LogPhone("TSC Uplink: DropCurrentController started");
			}
			else
			{
				TscDiagnostics.LogPhone("TSC activation device DropCurrentController started.");
			}

			player.DropCurrentController(
				() => PostDropCreateController(player, uplinkItem, launchMode, onSpawned, onFailed),
				fastDrop: false,
				nextControllerItem: uplinkItem);
		}
		catch (Exception ex)
		{
			onFailed?.Invoke(ex);
		}
	}

	private static void PostDropCreateController(
		Player player,
		Item uplinkItem,
		UavPhoneLaunchMode launchMode,
		Action<UavDeviceController> onSpawned,
		Action<Exception> onFailed)
	{
		bool manual = launchMode == UavPhoneLaunchMode.ManualAuthorization;

		try
		{
			if (manual)
			{
				TscDiagnostics.LogPhone("TSC Uplink: post-drop callback fired");
			}
			else
			{
				TscDiagnostics.LogPhone("TSC activation device post-drop callback fired.");
			}

			if (player == null)
			{
				throw new ArgumentNullException(nameof(player));
			}

			if (uplinkItem == null)
			{
				throw new ArgumentNullException(nameof(uplinkItem));
			}

			if (player.HandsController != null)
			{
				if (manual)
				{
					TscDiagnostics.LogPhone(
						$"TSC Uplink: destroying previous controller: {player.HandsController.GetType().FullName}");
				}
				else
				{
					TscDiagnostics.LogPhone(
						$"TSC activation device destroying previous controller: {player.HandsController.GetType().FullName}");
				}

				player.DestroyController();
			}

			PoolManagerClass poolManager = Singleton<PoolManagerClass>.Instance;
			if (poolManager == null)
			{
				throw new InvalidOperationException("PoolManagerClass singleton was null.");
			}

			if (manual)
			{
				TscDiagnostics.LogPhone("TSC Uplink: creating UavDeviceController");
			}
			else
			{
				TscDiagnostics.LogPhone("TSC activation device creating UavDeviceController.");
			}

			var controller = Player.ItemHandsController.smethod_1<UavDeviceController>(
				player,
				uplinkItem,
				new Player.ItemHandsController.Delegate8(
					poolManager.CreateItemUsablePrefab));

			if (manual)
			{
				TscDiagnostics.LogPhone(
					$"TSC Uplink: controller factory returned {controller?.GetType().FullName ?? "null"}");
			}
			else
			{
				TscDiagnostics.LogPhone(
					$"TSC activation device controller factory returned {controller?.GetType().FullName ?? "null"}.");
			}

			if (controller == null)
			{
				throw new InvalidOperationException("UavDeviceController factory returned null.");
			}

			controller.LaunchMode = launchMode;
			if (manual)
			{
				TscDiagnostics.LogPhone("TSC Uplink: launch mode = ManualAuthorization");
				TscDiagnostics.LogPhone("TSC Uplink: initializing controller");
			}
			else
			{
				TscDiagnostics.LogPhone($"TSC activation device launch mode = {launchMode}.");
				TscDiagnostics.LogPhone("TSC activation device initializing controller.");
			}

			Player.UsableItemController.smethod_8<UavDeviceController>(controller, player);

			if (manual)
			{
				TscDiagnostics.LogPhone("TSC Uplink: spawning controller");
			}
			else
			{
				TscDiagnostics.LogPhone("TSC activation device spawning controller.");
			}

			player.SpawnController(controller, () =>
			{
				if (manual)
				{
					TscDiagnostics.LogPhone("TSC Uplink: SpawnController callback fired");
					TscDiagnostics.LogPhone(
						$"TSC Uplink: current HandsController = {player.HandsController?.GetType().FullName ?? "<null>"}");
				}
				else
				{
					TscDiagnostics.LogPhone("TSC activation device SpawnController callback fired.");
				}

				onSpawned?.Invoke(controller);
			});
		}
		catch (Exception ex)
		{
			onFailed?.Invoke(ex);
		}
	}
}
