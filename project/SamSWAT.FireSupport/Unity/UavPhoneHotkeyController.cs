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
	private static UavPhoneHotkeyController s_instance;

	private bool _equipInProgress;
	private Player _manualPlayer;
	private Item _previousHandsItem;
	private UavDeviceController _currentController;
	private UavDeviceHandsService.EquipOperation _equipOperation;
	private Coroutine _restoreCoroutine;
	private UavPhoneLaunchMode _equipLaunchMode = UavPhoneLaunchMode.ManualAuthorization;
	private bool _radarHoldWasPressed;
	private bool _radarReleaseQueued;
	private int _lifecycleGeneration;

	protected override void OnStart()
	{
		s_instance = this;
		HasFinishedInitialization = true;
	}

	public static void ResetForRaidBoundary(string reason)
	{
		if (s_instance != null)
		{
			s_instance.ResetLifecycleState(reason, destroyOwnedController: true);
		}
	}

	public override void ManualUpdate()
	{
		bool pluginEnabled = PluginSettings.Enabled.Value;
		if (HandleRadarHold(pluginEnabled))
		{
			return;
		}

		if (!pluginEnabled)
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

	private bool HandleRadarHold(bool allowOpen)
	{
		// KeyboardShortcut.IsPressed evaluates the main key and every configured
		// modifier. Tracking the transition ourselves also treats releasing a
		// modifier as a release, which keeps custom bindings from leaving the
		// monitor stuck in the player's hands.
		bool isPressed = allowOpen && IsRadarShortcutPressed();
		bool pressedThisFrame = isPressed && !_radarHoldWasPressed;
		bool releasedThisFrame = !isPressed && _radarHoldWasPressed;
		_radarHoldWasPressed = isPressed;

		if (releasedThisFrame)
		{
			TscDiagnostics.LogPhone("TSC UAV radar hold released.");
			RequestRadarRestore();
			return true;
		}

		if (_radarReleaseQueued)
		{
			TryFinishQueuedRadarMonitor();
		}

		if (pressedThisFrame)
		{
			TscDiagnostics.LogPhone("TSC UAV radar hold pressed.");
			if (!UavReconOverlay.IsReconActive)
			{
				NotificationManagerClass.DisplayWarningNotification(
					"No active UAV recon link.",
					ENotificationDurationType.Default);
				return true;
			}

			TryOpenUplink(UavPhoneLaunchMode.UavRadarMonitor);
			return true;
		}

		// Do not let the purchase/deploy hotkeys race any part of the radar hand
		// swap. U and K resume as soon as the prior weapon has been restored.
		return IsRadarPhoneTransitionActive();
	}

	private static bool IsRadarShortcutPressed()
	{
		if (PluginSettings.OpenUavRadarKey == null)
		{
			return false;
		}

		BepInEx.Configuration.KeyboardShortcut shortcut = PluginSettings.OpenUavRadarKey.Value;
		if (shortcut.MainKey == KeyCode.None || !Input.GetKey(shortcut.MainKey))
		{
			return false;
		}

		// KeyboardShortcut.IsPressed() rejects the chord when any unrelated key
		// is held. That makes W/Shift look like a J release. Check only the main
		// key and configured modifiers so movement is allowed while viewing radar.
		foreach (KeyCode modifier in shortcut.Modifiers)
		{
			if (!Input.GetKey(modifier))
			{
				return false;
			}
		}

		return true;
	}

	private bool IsRadarPhoneTransitionActive()
	{
		if (_equipInProgress && _equipLaunchMode == UavPhoneLaunchMode.UavRadarMonitor)
		{
			return true;
		}

		Player player = _manualPlayer ?? Singleton<GameWorld>.Instance?.MainPlayer;
		if (player?.HandsController is UavDeviceController handsController &&
		    handsController.LaunchMode == UavPhoneLaunchMode.UavRadarMonitor)
		{
			return true;
		}

		return _restoreCoroutine != null &&
		       _currentController?.LaunchMode == UavPhoneLaunchMode.UavRadarMonitor;
	}

	private void RequestRadarRestore()
	{
		_radarReleaseQueued = true;
		if (_equipInProgress && _equipLaunchMode == UavPhoneLaunchMode.UavRadarMonitor)
		{
			TscDiagnostics.LogPhone("TSC UAV radar release queued while phone equip is in progress.");
			return;
		}

		TryFinishQueuedRadarMonitor();
	}

	private void TryFinishQueuedRadarMonitor()
	{
		if (!_radarReleaseQueued)
		{
			return;
		}

		// HandsController can be assigned before EFT fires SpawnController's
		// completion callback. Starting the outro in that gap can prevent the
		// callback from ever arriving, so the release stays latched until
		// OnManualPhoneSpawned confirms that the equip transaction completed.
		if (_equipInProgress && _equipLaunchMode == UavPhoneLaunchMode.UavRadarMonitor)
		{
			return;
		}

		Player player = _manualPlayer ?? Singleton<GameWorld>.Instance?.MainPlayer;
		if (player?.HandsController is not UavDeviceController controller ||
		    controller.LaunchMode != UavPhoneLaunchMode.UavRadarMonitor)
		{
			// Keep the request queued only while a radar equip can still produce
			// the controller. A restore already in progress needs no second close.
			if (!_equipInProgress || _equipLaunchMode != UavPhoneLaunchMode.UavRadarMonitor)
			{
				_radarReleaseQueued = false;
			}

			return;
		}

		_radarReleaseQueued = false;
		_currentController = controller;
		_manualPlayer = player;
		controller.AuthorizationSessionFinished -= OnManualAuthorizationFinished;
		controller.AuthorizationSessionFinished += OnManualAuthorizationFinished;
		TscDiagnostics.LogPhone("TSC UAV radar monitor closing; current hands controller confirmed.");
		controller.CancelAuthorizationSession();
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

		// HandsController may already point at the phone before EFT completes the
		// SpawnController callback. Cancelling in that gap can strand the hand
		// swap, so every manual phone action waits for the transaction to finish.
		if (_equipInProgress)
		{
			TscDiagnostics.LogPhone("TSC Uplink ignored: manual equip already in progress.");
			return;
		}

		UavDeviceController handsController = player.HandsController as UavDeviceController;
		if (launchMode == UavPhoneLaunchMode.UavRadarMonitor)
		{
			if (!UavReconOverlay.IsReconActive)
			{
				TscDiagnostics.LogPhone("TSC UAV radar phone ignored: recon link ended before equip.");
				return;
			}

			if (handsController != null)
			{
				string reason = handsController.LaunchMode == UavPhoneLaunchMode.UavRadarMonitor
					? "radar monitor is already in the player's hands"
					: $"another phone session is active ({handsController.LaunchMode})";
				TscDiagnostics.LogPhone($"TSC UAV radar phone ignored: {reason}.");
				return;
			}

			if (_restoreCoroutine != null || _currentController != null)
			{
				TscDiagnostics.LogPhone("TSC UAV radar phone ignored: a prior phone restore is still in progress.");
				return;
			}
		}

		UavDeviceController activeController = _currentController ?? handsController;
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

		if (UavDeviceActivationController.IsActive)
		{
			TscDiagnostics.LogPhone("TSC Uplink ignored: internal UAV activation animation is active.");
			return;
		}

		PaymentMode paymentMode = FireSupportPayment.GetActivePaymentMode();
		TscDiagnostics.LogPhone($"TSC Uplink active payment mode: {paymentMode}.");
		if (launchMode != UavPhoneLaunchMode.UavRadarMonitor &&
		    paymentMode == PaymentMode.DirectRadial)
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
			int lifecycleGeneration = _lifecycleGeneration;
			_equipInProgress = true;
			_equipLaunchMode = launchMode;
			_manualPlayer = player;
			_previousHandsItem = player.HandsController?.Item;
			_currentController = null;
			if (launchMode == UavPhoneLaunchMode.UavRadarMonitor)
			{
				_radarReleaseQueued = false;
			}

			UavDeviceHandsService.EquipOperation operation = UavDeviceHandsService.BeginEquip(
				player,
				uplinkItem,
				launchMode,
				(callbackOperation, controller) => OnManualPhoneSpawned(
					player,
					controller,
					callbackOperation,
					lifecycleGeneration),
				(callbackOperation, ex) => OnManualEquipFailed(
					player,
					ex,
					callbackOperation,
					lifecycleGeneration));
			if (_equipInProgress && lifecycleGeneration == _lifecycleGeneration)
			{
				_equipOperation = operation;
			}
			else
			{
				operation.Cancel("manual equip completed synchronously");
			}
		}
		catch (Exception ex)
		{
			OnManualEquipFailed(
				player,
				ex,
				_equipOperation,
				_lifecycleGeneration);
		}
	}

	private void OnManualPhoneSpawned(
		Player player,
		UavDeviceController controller,
		UavDeviceHandsService.EquipOperation operation,
		int lifecycleGeneration)
	{
		if (lifecycleGeneration != _lifecycleGeneration)
		{
			TscDiagnostics.LogPhone("TSC Uplink ignored a phone spawn from an earlier raid lifecycle.");
			DestroyControllerIfOwned(player, controller, "stale manual phone spawn");
			return;
		}

		_equipInProgress = false;
		_equipOperation = null;
		_manualPlayer = player;
		_currentController = controller;

		if (controller == null)
		{
			CleanupFailedManualEquip(
				player,
				new InvalidOperationException("SpawnController callback supplied a null UavDeviceController."),
				operation);
			return;
		}

		FireSupportPlugin.LogSource.LogInfo($"TSC Uplink phone spawned; finish handler subscribed (mode={controller.LaunchMode}).");
		controller.AuthorizationSessionFinished -= OnManualAuthorizationFinished;
		controller.AuthorizationSessionFinished += OnManualAuthorizationFinished;
		_equipLaunchMode = UavPhoneLaunchMode.ManualAuthorization;
		controller.NotifyExternalEquipCompleted();

		if (controller.LaunchMode == UavPhoneLaunchMode.UavRadarMonitor &&
		    (_radarReleaseQueued ||
		     !PluginSettings.Enabled.Value ||
		     !IsRadarShortcutPressed() ||
		     !UavReconOverlay.IsReconActive))
		{
			TscDiagnostics.LogPhone("TSC UAV radar phone spawned after its hold ended; closing immediately.");
			_radarReleaseQueued = false;
			controller.CancelAuthorizationSession();
		}
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

		if (_currentController != null &&
		    !ReferenceEquals(_currentController, controller))
		{
			TscDiagnostics.LogPhone("TSC Uplink ignored a finish callback from a controller it no longer owns.");
			return;
		}

		if (_restoreCoroutine != null)
		{
			FireSupportPlugin.LogSource.LogInfo("TSC Uplink finish ignored: a restore coroutine is already running.");
			return;
		}

		_currentController = controller;
		Player player = _manualPlayer ?? Singleton<GameWorld>.Instance?.MainPlayer;
		_restoreCoroutine = StartCoroutine(
			RestoreManualPhoneAfterOutro(
				player,
				controller,
				_lifecycleGeneration));
	}

	private IEnumerator RestoreManualPhoneAfterOutro(
		Player player,
		UavDeviceController controller,
		int lifecycleGeneration)
	{
		if (controller != null)
		{
			yield return controller.WaitForAuthorizationOutro(1.7f);
		}
		else
		{
			yield return new WaitForSecondsRealtime(0.1f);
		}

		if (lifecycleGeneration != _lifecycleGeneration)
		{
			yield break;
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

			bool ownsController =
				player != null &&
				ReferenceEquals(player.HandsController, controller);
			bool ownsEmptyHands =
				player != null &&
				player.HandsController == null &&
				ReferenceEquals(_currentController, controller);
			if (!ownsController && !ownsEmptyHands)
			{
				TscDiagnostics.LogPhone(
					$"TSC Uplink restore skipped: hands ownership moved to {player?.HandsController?.GetType().FullName ?? "<null>"}.");
				yield break;
			}

			if (ownsController)
			{
				controller.ShutdownPhoneScreenForExternalRestore();
				player.DestroyController();
			}

			TscDiagnostics.LogPhone("TSC Uplink: owned controller removed; restoring last equipped weapon");
			player.TrySetLastEquippedWeapon(true, null);
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

			if (lifecycleGeneration == _lifecycleGeneration &&
			    ReferenceEquals(_currentController, controller))
			{
				_currentController = null;
				_previousHandsItem = null;
				_manualPlayer = null;
				_equipInProgress = false;
				_equipLaunchMode = UavPhoneLaunchMode.ManualAuthorization;
				_radarReleaseQueued = false;
				_restoreCoroutine = null;
			}
		}

		FireSupportPlugin.LogSource.LogInfo($"TSC Uplink restore finished. pendingDeployment={pendingDeployment}.");
	}

	private void OnManualEquipFailed(
		Player player,
		Exception exception,
		UavDeviceHandsService.EquipOperation operation,
		int lifecycleGeneration)
	{
		if (lifecycleGeneration != _lifecycleGeneration)
		{
			TscDiagnostics.LogPhone("TSC Uplink ignored an equip failure from an earlier raid lifecycle.");
			return;
		}

		CleanupFailedManualEquip(player, exception, operation);
	}

	private void CleanupFailedManualEquip(
		Player player,
		Exception exception,
		UavDeviceHandsService.EquipOperation operationOverride = null)
	{
		FireSupportPlugin.LogSource.LogWarning($"TSC Uplink explicit controller swap failed. {exception}");

		UavDeviceHandsService.EquipOperation operation =
			operationOverride ?? _equipOperation;
		operation?.Cancel("manual equip failed");
		try
		{
			UavDeviceController controller = _currentController ?? operation?.Controller;
			if (controller != null)
			{
				controller.AuthorizationSessionFinished -= OnManualAuthorizationFinished;
			}

			bool ownsController =
				player != null &&
				controller != null &&
				ReferenceEquals(player.HandsController, controller);
			bool ownsEmptyHands =
				player != null &&
				player.HandsController == null &&
				(operation?.MayOwnEmptyHands == true ||
				 ReferenceEquals(_currentController, controller));
			if (ownsController)
			{
				controller.ShutdownPhoneScreenForExternalRestore();
				player.DestroyController();
			}

			if (ownsController || ownsEmptyHands)
			{
				TscDiagnostics.LogPhone("TSC Uplink: failed owned equip removed; restoring last equipped weapon");
				player.TrySetLastEquippedWeapon(true, null);
				TscDiagnostics.LogPhone($"TSC Uplink: restored HandsController = {player.HandsController?.GetType().FullName ?? "<null>"}");
			}
			else
			{
				TscDiagnostics.LogPhone(
					$"TSC Uplink failure restore skipped: hands ownership moved to {player?.HandsController?.GetType().FullName ?? "<null>"}.");
			}
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource.LogWarning($"TSC Uplink failure cleanup failed. {ex}");
		}
		finally
		{
			_currentController = null;
			_equipOperation = null;
			_previousHandsItem = null;
			_manualPlayer = null;
			_equipInProgress = false;
			_equipLaunchMode = UavPhoneLaunchMode.ManualAuthorization;
			_radarReleaseQueued = false;
			_restoreCoroutine = null;
		}
	}

	private void ResetLifecycleState(string reason, bool destroyOwnedController)
	{
		_lifecycleGeneration++;
		_radarHoldWasPressed = false;
		_radarReleaseQueued = false;

		UavDeviceHandsService.EquipOperation operation = _equipOperation;
		operation?.Cancel(reason);

		if (_restoreCoroutine != null)
		{
			StopCoroutine(_restoreCoroutine);
			_restoreCoroutine = null;
		}

		Player player = _manualPlayer;
		UavDeviceController controller = _currentController ?? operation?.Controller;
		if (controller != null)
		{
			controller.AuthorizationSessionFinished -= OnManualAuthorizationFinished;
		}

		if (destroyOwnedController)
		{
			DestroyControllerIfOwned(player, controller, reason);
		}

		_currentController = null;
		_equipOperation = null;
		_previousHandsItem = null;
		_manualPlayer = null;
		_equipInProgress = false;
		_equipLaunchMode = UavPhoneLaunchMode.ManualAuthorization;
		TscDiagnostics.LogPhone($"TSC Uplink hotkey lifecycle reset: {reason}.");
	}

	private static void DestroyControllerIfOwned(
		Player player,
		UavDeviceController controller,
		string reason)
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
			TscDiagnostics.LogPhone($"TSC Uplink removed its owned hands controller: {reason}.");
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource.LogWarning(
				$"TSC Uplink owned controller cleanup failed ({reason}). {ex}");
		}
	}

	private void OnDestroy()
	{
		ResetLifecycleState("hotkey component destroyed", destroyOwnedController: true);
		if (ReferenceEquals(s_instance, this))
		{
			s_instance = null;
		}
	}
}
