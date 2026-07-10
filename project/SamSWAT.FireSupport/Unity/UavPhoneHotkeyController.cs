using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.InventoryLogic;
using System;
using System.Collections;
using UnityEngine;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public sealed class UavPhoneHotkeyController : UpdatableComponentBase
{
	private bool _equipInProgress;
	private Player _manualPlayer;
	private Item _previousHandsItem;
	private UavDeviceController _currentController;
	private Coroutine _restoreCoroutine;

	protected override void OnStart()
	{
		HasFinishedInitialization = true;
	}

	public override void ManualUpdate()
	{
		if (!PluginSettings.Enabled.Value)
		{
			return;
		}

		if (PluginSettings.OpenUplinkKey != null && PluginSettings.OpenUplinkKey.Value.IsDown())
		{
			TscDiagnostics.LogPhone("TSC Uplink key pressed.");
			TryOpenUplink(UavPhoneLaunchMode.ManualAuthorization);
			return;
		}

		if (PluginSettings.OpenDeployKey != null && PluginSettings.OpenDeployKey.Value.IsDown())
		{
			TscDiagnostics.LogPhone("TSC deploy key pressed.");
			TryOpenUplink(UavPhoneLaunchMode.DeployMenu);
		}
	}

	private void TryOpenUplink(UavPhoneLaunchMode launchMode)
	{
		GameWorld gameWorld = Singleton<GameWorld>.Instance;
		if (gameWorld == null)
		{
			TscDiagnostics.LogPhone("TSC Uplink ignored: GameWorld was null.");
			return;
		}

		Player player = gameWorld.MainPlayer;
		if (player == null)
		{
			TscDiagnostics.LogPhone("TSC Uplink ignored: MainPlayer was null.");
			return;
		}

		bool isAlive = player.ActiveHealthController?.IsAlive == true;
		TscDiagnostics.LogPhone(
			$"TSC Uplink player state: isYourPlayer={player.IsYourPlayer}, alive={isAlive}, equipInProgress={_equipInProgress}, hands={player.HandsController?.GetType().FullName ?? "<null>"}.");

		if (!player.IsYourPlayer || !isAlive)
		{
			return;
		}

		if (player.IsInventoryOpened)
		{
			TscDiagnostics.LogPhone("TSC Uplink ignored: inventory screen is open.");
			return;
		}

		UavDeviceController activeController = _currentController ?? player.HandsController as UavDeviceController;
		if (activeController != null)
		{
			// Sessions launched through EFT's quick-use flow (special slot key)
			// are restored by EFT itself once the session finishes. Attaching our
			// manual restore on top ran DestroyController mid hand-swap and left
			// the interaction state machine wedged, freezing movement and look on
			// the next pickup.
			if (activeController.IsQuickUseSession)
			{
				TscDiagnostics.LogPhone("TSC Uplink key pressed while quick-use phone is active; cancelling session, EFT restores hands.");
				activeController.CancelAuthorizationSession();
				return;
			}

			TscDiagnostics.LogPhone("TSC Uplink key pressed while phone is active; cancelling session.");
			_currentController = activeController;
			_manualPlayer = player;
			activeController.AuthorizationSessionFinished -= OnManualAuthorizationFinished;
			activeController.AuthorizationSessionFinished += OnManualAuthorizationFinished;
			activeController.CancelAuthorizationSession();
			return;
		}

		if (_equipInProgress)
		{
			TscDiagnostics.LogPhone("TSC Uplink ignored: manual equip already in progress.");
			return;
		}

		if (UavDeviceActivationController.IsActive)
		{
			TscDiagnostics.LogPhone("TSC Uplink ignored: internal UAV activation animation is active.");
			return;
		}

		PaymentMode paymentMode = FireSupportPayment.GetActivePaymentMode();
		TscDiagnostics.LogPhone($"TSC Uplink active payment mode: {paymentMode}.");
		if (paymentMode == PaymentMode.DirectRadial)
		{
			NotificationManagerClass.DisplayWarningNotification(
				"Set payment mode to PhoneAuthorizations or Hybrid.",
				ENotificationDurationType.Long);
			return;
		}

		UavDeviceItem uplinkItem = UavDeviceInventory.FindCarriedUplink(player);
		if (uplinkItem == null)
		{
			TscDiagnostics.LogPhone("TSC Uplink ignored: no carried TerraGroup TSC Uplink item was found.");
			NotificationManagerClass.DisplayWarningNotification(
				"TerraGroup TSC Uplink not found in carried inventory.",
				ENotificationDurationType.Long);
			return;
		}

		TscDiagnostics.LogPhone(
			$"TSC Uplink found carried item. item={uplinkItem.Id}, tpl={uplinkItem.StringTemplateId}, runtimeItemType={uplinkItem.GetType().FullName}, location={UavDeviceInventory.DescribeLocation(uplinkItem)}.");

		try
		{
			_equipInProgress = true;
			_manualPlayer = player;
			_previousHandsItem = player.HandsController?.Item;
			_currentController = null;

			UavDeviceHandsService.BeginEquip(
				player,
				uplinkItem,
				launchMode,
				controller => OnManualPhoneSpawned(player, controller),
				ex => CleanupFailedManualEquip(player, ex));
		}
		catch (Exception ex)
		{
			CleanupFailedManualEquip(player, ex);
		}
	}

	private void OnManualPhoneSpawned(Player player, UavDeviceController controller)
	{
		_equipInProgress = false;
		_manualPlayer = player;
		_currentController = controller;

		if (controller == null)
		{
			CleanupFailedManualEquip(player, new InvalidOperationException("SpawnController callback supplied a null UavDeviceController."));
			return;
		}

		FireSupportPlugin.LogSource.LogInfo($"TSC Uplink phone spawned; finish handler subscribed (mode={controller.LaunchMode}).");
		controller.AuthorizationSessionFinished -= OnManualAuthorizationFinished;
		controller.AuthorizationSessionFinished += OnManualAuthorizationFinished;
	}

	private void OnManualAuthorizationFinished(UavDeviceController controller, bool success)
	{
		FireSupportPlugin.LogSource.LogInfo(
			$"TSC Uplink finish received. success={success}, quickUse={controller?.IsQuickUseSession ?? false}, restoreRunning={_restoreCoroutine != null}.");
		if (controller == null)
		{
			CleanupFailedManualEquip(
				_manualPlayer ?? Singleton<GameWorld>.Instance?.MainPlayer,
				new InvalidOperationException("Manual phone finish callback supplied a null controller."));
			return;
		}

		if (_restoreCoroutine != null)
		{
			FireSupportPlugin.LogSource.LogInfo("TSC Uplink finish ignored: a restore coroutine is already running.");
			return;
		}

		_currentController = controller;
		Player player = _manualPlayer ?? Singleton<GameWorld>.Instance?.MainPlayer;
		_restoreCoroutine = StartCoroutine(RestoreManualPhoneAfterOutro(player, controller));
	}

	private IEnumerator RestoreManualPhoneAfterOutro(Player player, UavDeviceController controller)
	{
		if (controller != null)
		{
			yield return controller.WaitForAuthorizationOutro(1.7f);
		}
		else
		{
			yield return new WaitForSecondsRealtime(0.1f);
		}

		TscDiagnostics.LogPhone("TSC Uplink: outro complete");
		player ??= Singleton<GameWorld>.Instance?.MainPlayer;
		ESupportType pendingDeployment = controller != null
			? controller.PendingDeployment
			: ESupportType.None;

		try
		{
			if (controller != null && controller.IsQuickUseSession)
			{
				// EFT's quick-use flow restores the previous item itself; a second
				// restore here races EFT's hand swap and wedges the interaction
				// state machine.
				TscDiagnostics.LogPhone("TSC Uplink: skipping manual restore; EFT quick-use owns the hand swap.");
				yield break;
			}

			controller?.ShutdownPhoneScreenForExternalRestore();

			if (player?.HandsController is UavDeviceController)
			{
				player.DestroyController();
			}

			TscDiagnostics.LogPhone("TSC Uplink: DestroyController + TrySetLastEquippedWeapon");
			player?.TrySetLastEquippedWeapon(true, null);
			TscDiagnostics.LogPhone($"TSC Uplink: restored HandsController = {player?.HandsController?.GetType().FullName ?? "<null>"}");
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource.LogWarning($"TSC Uplink restore failed. {ex}");
		}
		finally
		{
			if (controller != null)
			{
				controller.AuthorizationSessionFinished -= OnManualAuthorizationFinished;
			}

			_currentController = null;
			_previousHandsItem = null;
			_manualPlayer = null;
			_equipInProgress = false;
			_restoreCoroutine = null;
		}

		FireSupportPlugin.LogSource.LogInfo($"TSC Uplink restore finished. pendingDeployment={pendingDeployment}.");
	}

	private void CleanupFailedManualEquip(Player player, Exception exception)
	{
		FireSupportPlugin.LogSource.LogWarning($"TSC Uplink explicit controller swap failed. {exception}");

		try
		{
			UavDeviceController controller = _currentController ?? player?.HandsController as UavDeviceController;
			controller?.ShutdownPhoneScreenForExternalRestore();
			if (controller != null)
			{
				controller.AuthorizationSessionFinished -= OnManualAuthorizationFinished;
			}

			if (player?.HandsController is UavDeviceController)
			{
				player.DestroyController();
			}

			TscDiagnostics.LogPhone("TSC Uplink: DestroyController + TrySetLastEquippedWeapon");
			player?.TrySetLastEquippedWeapon(true, null);
			TscDiagnostics.LogPhone($"TSC Uplink: restored HandsController = {player?.HandsController?.GetType().FullName ?? "<null>"}");
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource.LogWarning($"TSC Uplink failure cleanup failed. {ex}");
		}
		finally
		{
			_currentController = null;
			_previousHandsItem = null;
			_manualPlayer = null;
			_equipInProgress = false;
			_restoreCoroutine = null;
		}
	}

	private void OnDestroy()
	{
		if (_currentController != null)
		{
			_currentController.AuthorizationSessionFinished -= OnManualAuthorizationFinished;
		}
	}
}
