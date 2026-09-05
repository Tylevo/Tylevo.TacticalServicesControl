using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using System;
using System.Threading;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

internal static class UavDeviceHandsService
{
	private static int s_boundaryGeneration;

	internal sealed class EquipOperation
	{
		private readonly object _stateGate = new();
		private readonly int _boundaryGeneration;
		private int _status;
		private int _dropStarted;
		private int _postDropEntered;
		private int _cancelledDropSettledInvoked;
		private int _handsRestoreClaimed;
		private UavDeviceController _controller;
		private Action _onCancelledDropSettled;

		internal EquipOperation(int boundaryGeneration)
		{
			_boundaryGeneration = boundaryGeneration;
		}

		public bool IsActive =>
			Volatile.Read(ref _status) == 0 &&
			_boundaryGeneration == Volatile.Read(ref s_boundaryGeneration);

		public bool IsBoundaryCurrent =>
			_boundaryGeneration == Volatile.Read(ref s_boundaryGeneration);

		public bool MayOwnEmptyHands => Volatile.Read(ref _dropStarted) != 0;
		public UavDeviceController Controller => _controller;

		internal void MarkDropStarted()
		{
			lock (_stateGate)
			{
				Volatile.Write(ref _dropStarted, 1);
			}
		}

		internal void MarkPostDropEntered()
		{
			lock (_stateGate)
			{
				Volatile.Write(ref _postDropEntered, 1);
			}
		}

		internal void TrackController(UavDeviceController controller)
		{
			_controller = controller;
		}

		internal bool TryComplete()
		{
			lock (_stateGate)
			{
				if (_status != 0)
				{
					return false;
				}

				Volatile.Write(ref _status, 1);
				return true;
			}
		}

		public bool TryClaimHandsRestore()
		{
			return Interlocked.Exchange(ref _handsRestoreClaimed, 1) == 0;
		}

		public bool Cancel(
			string reason = null,
			Action onCancelledDropSettled = null)
		{
			bool restoreDeferred = false;
			bool cancelled = false;
			lock (_stateGate)
			{
				if (onCancelledDropSettled != null &&
				    _dropStarted != 0 &&
				    _postDropEntered == 0 &&
				    _status == 0)
				{
					_onCancelledDropSettled ??= onCancelledDropSettled;
					restoreDeferred =
						ReferenceEquals(_onCancelledDropSettled, onCancelledDropSettled);
				}

				if (_status == 0)
				{
					Volatile.Write(ref _status, 2);
					cancelled = true;
				}
			}

			if (!cancelled)
			{
				return restoreDeferred;
			}

			if (!string.IsNullOrWhiteSpace(reason))
			{
				TscDiagnostics.LogPhone($"TSC Uplink hands equip cancelled: {reason}.");
			}

			return restoreDeferred;
		}

		internal void NotifyCancelledDropSettled()
		{
			Action callback;
			lock (_stateGate)
			{
				if (_onCancelledDropSettled == null ||
				    _cancelledDropSettledInvoked != 0)
				{
					return;
				}

				_cancelledDropSettledInvoked = 1;
				callback = _onCancelledDropSettled;
			}

			try
			{
				callback();
			}
			catch (Exception ex)
			{
				FireSupportPlugin.LogSource.LogWarning(
					$"TSC Uplink cancelled-drop restoration callback failed. {ex}");
			}
		}
	}

	public static void CancelAllPending(string reason)
	{
		Interlocked.Increment(ref s_boundaryGeneration);
		TscDiagnostics.LogPhone($"TSC Uplink invalidated pending hands equips: {reason}.");
	}

	public static EquipOperation BeginEquip(
		Player player,
		Item uplinkItem,
		UavPhoneLaunchMode launchMode,
		Action<EquipOperation, UavDeviceController> onSpawned,
		Action<EquipOperation, Exception> onFailed)
	{
		var operation = new EquipOperation(Volatile.Read(ref s_boundaryGeneration));
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

			object originalController = player.HandsController;
			operation.MarkDropStarted();
			player.DropCurrentController(
				() => PostDropCreateController(
					player,
					uplinkItem,
					launchMode,
					operation,
					originalController,
					onSpawned,
					onFailed),
				fastDrop: false,
				nextControllerItem: uplinkItem);
		}
		catch (Exception ex)
		{
			Fail(operation, onFailed, ex);
		}

		return operation;
	}

	private static void PostDropCreateController(
		Player player,
		Item uplinkItem,
		UavPhoneLaunchMode launchMode,
		EquipOperation operation,
		object originalController,
		Action<EquipOperation, UavDeviceController> onSpawned,
		Action<EquipOperation, Exception> onFailed)
	{
		bool manual = launchMode == UavPhoneLaunchMode.ManualAuthorization;

		try
		{
			operation.MarkPostDropEntered();
			if (!operation.IsActive)
			{
				TscDiagnostics.LogPhone("TSC Uplink ignored a stale post-drop hands callback.");
				operation.NotifyCancelledDropSettled();
				return;
			}

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
				if (!ReferenceEquals(player.HandsController, originalController))
				{
					throw new InvalidOperationException(
						$"TSC Uplink lost hands ownership during equip; current controller is {player.HandsController.GetType().FullName}.");
				}

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

			if (!operation.IsActive)
			{
				TscDiagnostics.LogPhone("TSC Uplink hands equip was cancelled before controller creation.");
				operation.NotifyCancelledDropSettled();
				return;
			}

			ObjectsFactory poolManager = Singleton<ObjectsFactory>.Instance;
			if (poolManager == null)
			{
				throw new InvalidOperationException("ObjectsFactory singleton was null.");
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

			operation.TrackController(controller);
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

			Player.UsableItemController.Setup<UavDeviceController>(controller, player);

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
				if (!operation.IsActive)
				{
					TscDiagnostics.LogPhone("TSC Uplink ignored a stale SpawnController callback.");
					DestroyControllerIfOwned(player, controller);
					operation.NotifyCancelledDropSettled();
					return;
				}

				if (!ReferenceEquals(player.HandsController, controller))
				{
					Fail(
						operation,
						onFailed,
						new InvalidOperationException(
							$"TSC Uplink lost hands ownership before spawn completion; current controller is {player.HandsController?.GetType().FullName ?? "<null>"}."));
					operation.NotifyCancelledDropSettled();
					return;
				}

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

				if (operation.TryComplete())
				{
					onSpawned?.Invoke(operation, controller);
				}
			});
		}
		catch (Exception ex)
		{
			Fail(operation, onFailed, ex);
			operation.NotifyCancelledDropSettled();
		}
	}

	private static void Fail(
		EquipOperation operation,
		Action<EquipOperation, Exception> onFailed,
		Exception exception)
	{
		if (operation != null && operation.TryComplete())
		{
			onFailed?.Invoke(operation, exception);
		}
	}

	private static void DestroyControllerIfOwned(Player player, UavDeviceController controller)
	{
		if (player == null ||
		    controller == null ||
		    !ReferenceEquals(player.HandsController, controller))
		{
			return;
		}

		try
		{
			controller.ShutdownPhoneScreenForExternalRestore();
			player.DestroyController();
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource.LogWarning(
				$"TSC Uplink stale hands controller cleanup failed. {ex}");
		}
	}
}
