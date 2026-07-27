using Comfort.Common;
using Cysharp.Threading.Tasks;
using EFT;
using EFT.InventoryLogic;
using System;
using System.Collections;
using System.Threading;
using UnityEngine;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public sealed class UavDeviceActivationController : MonoBehaviour
{
	private const float EquipSeconds = 22f / 30f;
	private const float TapImpactSeconds = 8f / 30f;
	private const float TapHoldSeconds = 0.3f;
	private const float FallbackStartSeconds = 2.0f;
	private const int HandsLayer = 1;

	private static UavDeviceActivationController s_active;

	public static bool IsActive => s_active != null;

	private Player _player;
	private Item _deviceItem;
	private Item _previousHandsItem;
	private Func<CancellationToken, UniTask<bool>> _onActivated;
	private Action _onCancelled;
	private CancellationToken _cancellationToken;
	private CancellationTokenSource _activationCts;
	private UavPhoneScreenRenderer _phoneScreen;
	private UavDeviceHandsService.EquipOperation _equipOperation;
	private UavDeviceController _ownedController;
	private int _equipGeneration;
	private bool _activationStarted;
	private bool _activationCompleted;
	private bool _activationSucceeded;
	private bool _cancelCallbackInvoked;
	private bool _restored;
	private bool _destroyed;
	private bool _raidBoundaryReset;

	private static float s_suppressUntil;

	/// <summary>
	/// Skips the next activation-device equip. Used when the deploy was just
	/// committed on the Uplink deploy phone: pulling the phone back out a
	/// second time to "authorize" an already-authorized deployment reads
	/// wrong, so the wrist-visual fallback is used instead.
	/// </summary>
	public static void SuppressNextActivation(float windowSeconds = 20f)
	{
		s_suppressUntil = Time.unscaledTime + windowSeconds;
	}

	public static void ResetForRaidBoundary(string reason)
	{
		s_suppressUntil = 0f;
		UavDeviceActivationController active = s_active;
		if (active == null)
		{
			return;
		}

		active.ResetLifecycleState(reason);
	}

	public static bool TryPlay(Action onActivated, CancellationToken cancellationToken)
	{
		if (onActivated == null)
		{
			FireSupportPlugin.LogSource.LogWarning("UAV activation device animation skipped: activation callback was null.");
			return false;
		}

		return TryPlay(
			_ =>
			{
				onActivated();
				return UniTask.FromResult(true);
			},
			onCancelled: null,
			cancellationToken: cancellationToken);
	}

	public static bool TryPlay(
		Func<UniTask<bool>> onActivated,
		Action onCancelled,
		CancellationToken cancellationToken)
	{
		return TryPlay(
			onActivated == null ? null : _ => onActivated(),
			onCancelled,
			cancellationToken);
	}

	public static bool TryPlay(
		Func<CancellationToken, UniTask<bool>> onActivated,
		Action onCancelled,
		CancellationToken cancellationToken)
	{
		if (Time.unscaledTime < s_suppressUntil)
		{
			s_suppressUntil = 0f;
			TscDiagnostics.LogPhone("TSC Uplink activation animation skipped: deploy came from the Uplink phone.");
			return false;
		}

		if (!PluginSettings.UavActivationDeviceAnimation.Value)
		{
			TscDiagnostics.LogPhone("TSC Uplink activation animation skipped: config disabled.");
			return false;
		}

		if (onActivated == null)
		{
			FireSupportPlugin.LogSource.LogWarning("UAV activation device animation skipped: activation callback was null.");
			return false;
		}

		if (s_active != null)
		{
			TscDiagnostics.LogPhone("TSC Uplink activation animation skipped: another activation is already running.");
			return false;
		}

		try
		{
			Player player = Singleton<GameWorld>.Instance?.MainPlayer;

			if (player == null)
			{
				FireSupportPlugin.LogSource.LogWarning("UAV activation device animation skipped: main player was null.");
				return false;
			}

			Item deviceItem = UavDeviceInventory.FindCarriedUplink(player);
			if (deviceItem == null)
			{
				TscDiagnostics.LogPhone("TSC Uplink activation animation skipped: no carried TerraGroup TSC Uplink item was found.");
				return false;
			}

			var runnerObject = new GameObject("TSCUplinkActivation");
			var runner = runnerObject.AddComponent<UavDeviceActivationController>();
			runner.Initialize(player, deviceItem, onActivated, onCancelled, cancellationToken);
			return true;
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource.LogWarning($"UAV activation device animation unavailable. {ex}");
			return false;
		}
	}

	private void Initialize(
		Player player,
		Item deviceItem,
		Func<CancellationToken, UniTask<bool>> onActivated,
		Action onCancelled,
		CancellationToken cancellationToken)
	{
		_player = player;
		_deviceItem = deviceItem;
		_onActivated = onActivated;
		_onCancelled = onCancelled;
		_cancellationToken = cancellationToken;
		_activationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		s_active = this;

		TscDiagnostics.LogPhone(
			$"TSC Uplink activation animation started with item {deviceItem.Id} ({deviceItem.GetType().FullName}).");
		StartCoroutine(RunActivation());
	}

	private IEnumerator RunActivation()
	{
		if (!EquipDevice())
		{
			BeginActivationOnce();
			while (!_activationCompleted && !_cancellationToken.IsCancellationRequested)
			{
				yield return null;
			}

			if (!_activationSucceeded)
			{
				CancelActivationOnce();
			}
			Destroy(gameObject);
			yield break;
		}

		Animator animator = null;
		float waitStop = Time.unscaledTime + FallbackStartSeconds;
		while (!_cancellationToken.IsCancellationRequested && Time.unscaledTime < waitStop)
		{
			animator = GetController()?.PhoneAnimator;
			if (animator != null)
			{
				break;
			}

			yield return null;
		}

		if (_cancellationToken.IsCancellationRequested)
		{
			CancelActivationOnce();
			RestoreHands();
			Destroy(gameObject);
			yield break;
		}

		if (animator != null)
		{
			StartPhoneScreenUI(animator);

			float equipStop = Time.unscaledTime + EquipSeconds + 1f;
			while (!_cancellationToken.IsCancellationRequested && Time.unscaledTime < equipStop)
			{
				AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(HandsLayer);
				if (info.IsName("Idle_Loop") || (info.IsName("Equip") && info.normalizedTime >= 0.8f))
				{
					break;
				}

				yield return null;
			}
		}
		else
		{
			yield return new WaitForSecondsRealtime(EquipSeconds * 0.8f);
		}

		if (_cancellationToken.IsCancellationRequested)
		{
			CancelActivationOnce();
			RestoreHands();
			Destroy(gameObject);
			yield break;
		}

		GetController()?.PlayTap(0.1f);
		yield return new WaitForSecondsRealtime(TapImpactSeconds);
		_phoneScreen?.ShowAuthorizing();
		BeginActivationOnce();
		TscDiagnostics.LogPhone("TSC Uplink activation tap completed; waiting for support authority.");

		float holdStop = Time.unscaledTime + TapHoldSeconds;
		while (!_cancellationToken.IsCancellationRequested &&
		       (!_activationCompleted || Time.unscaledTime < holdStop))
		{
			yield return null;
		}

		if (_cancellationToken.IsCancellationRequested && !_activationSucceeded)
		{
			CancelActivationOnce();
			RestoreHands();
			Destroy(gameObject);
			yield break;
		}

		UavDeviceController controller = GetController();
		if (_activationSucceeded)
		{
			_phoneScreen?.ShowAuthorized();
			controller?.PlayOutroSuccess();
			TscDiagnostics.LogPhone("TSC Uplink support authority accepted; radar starting.");
		}
		else
		{
			_phoneScreen?.ShowDenied();
			controller?.PlayOutroFail();
			CancelActivationOnce();
			TscDiagnostics.LogPhone("TSC Uplink support authority rejected; radar activation cancelled.");
		}

		yield return WaitForOutro(controller?.PhoneAnimator);
		RestoreHands();
		Destroy(gameObject);
	}

	private bool EquipDevice()
	{
		if (_player == null || _deviceItem == null)
		{
			return false;
		}

		try
		{
			int equipGeneration = ++_equipGeneration;
			_previousHandsItem = _player.HandsController?.Item;
			if (!UavDeviceConstants.IsUavDevice(_deviceItem))
			{
				FireSupportPlugin.LogSource.LogWarning("TSC Uplink activation equip failed: selected item was not a TerraGroup TSC Uplink.");
				return false;
			}

			UavDeviceHandsService.EquipOperation operation = UavDeviceHandsService.BeginEquip(
				_player,
				_deviceItem,
				UavPhoneLaunchMode.InternalUavActivation,
				(callbackOperation, controller) =>
				{
					if (_destroyed || equipGeneration != _equipGeneration)
					{
						DestroyControllerIfOwned(controller, "stale activation phone spawn");
						return;
					}

					_ownedController = controller;
					TscDiagnostics.LogPhone(
						$"TSC Uplink activation controller spawned. controller={controller?.GetType().FullName ?? "<null>"}.");
				},
				(callbackOperation, ex) =>
				{
					if (_destroyed || equipGeneration != _equipGeneration)
					{
						return;
					}

					FireSupportPlugin.LogSource.LogWarning($"UAV activation device controller spawn failed. {ex}");
					BeginActivationOnce();
					RestoreHands(callbackOperation);
				});
			if (!_destroyed &&
			    !_restored &&
			    equipGeneration == _equipGeneration)
			{
				_equipOperation = operation;
			}
			else
			{
				operation.Cancel("activation equip completed synchronously");
			}
			return true;
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource.LogWarning($"UAV activation device equip failed. {ex}");
			return false;
		}
	}

	private void StartPhoneScreenUI(Animator animator)
	{
		if (animator == null)
		{
			return;
		}

		try
		{
			ShutdownPhoneScreen();

			Renderer screenRenderer = UavPhoneScreenRenderer.FindBestScreenRenderer(
				animator.transform.root,
				"InternalUavActivation",
				logCandidates: true);
			if (screenRenderer == null)
			{
				FireSupportPlugin.LogSource.LogWarning("TSC Uplink UI skipped: screen mesh had no renderer.");
				return;
			}

			var context = new UavPhoneScreenContext(
				FireSupportPayment.GetActiveCost(ESupportType.Uav),
				FireSupportPayment.GetCarriedRoubleBalance(),
				UavReconSettings.GetDurationSeconds());

			_phoneScreen = gameObject.AddComponent<UavPhoneScreenRenderer>();
			_phoneScreen.Initialize(
				screenRenderer,
				UavPhoneScreenRenderer.CaptureScreenUVRect(screenRenderer),
				canvasRotation: 90f,
				context,
				animator.transform.root);
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource.LogWarning($"UAV phone UI failed to start. {ex}");
		}
	}

	private void ShutdownPhoneScreen()
	{
		if (_phoneScreen == null)
		{
			return;
		}

		try
		{
			_phoneScreen.Shutdown();
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource.LogWarning($"UAV phone UI shutdown failed. {ex}");
		}

		Destroy(_phoneScreen);
		_phoneScreen = null;
	}

	private IEnumerator WaitForOutro(Animator phoneAnimator)
	{
		if (phoneAnimator == null)
		{
			yield return new WaitForSecondsRealtime(1.3f);
			yield break;
		}

		int layer = phoneAnimator.layerCount > HandsLayer ? HandsLayer : 0;
		float stop = Time.unscaledTime + 1.7f;
		while (Time.unscaledTime < stop)
		{
			AnimatorStateInfo info = phoneAnimator.GetCurrentAnimatorStateInfo(layer);
			if (info.IsName("Spawn") ||
			    ((info.IsName("Outro Success") || info.IsName("Outro Fail")) &&
			     info.normalizedTime >= 0.95f))
			{
				break;
			}

			yield return null;
		}
	}

	private UavDeviceController GetController()
	{
		try
		{
			UavDeviceController controller = _ownedController ?? _equipOperation?.Controller;
			return controller != null &&
			       _player != null &&
			       ReferenceEquals(_player.HandsController, controller)
				? controller
				: null;
		}
		catch
		{
			return null;
		}
	}

	private void BeginActivationOnce()
	{
		if (_activationStarted || _cancelCallbackInvoked || _destroyed)
		{
			return;
		}

		_activationStarted = true;
		InvokeActivationAsync().Forget();
	}

	private async UniTaskVoid InvokeActivationAsync()
	{
		try
		{
			CancellationToken activationToken =
				_activationCts?.Token ?? _cancellationToken;
			_activationSucceeded =
				_onActivated != null &&
				await _onActivated(activationToken);
		}
		catch (OperationCanceledException)
		{
			_activationSucceeded = false;
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource.LogWarning($"UAV activation callback failed. {ex}");
			_activationSucceeded = false;
		}
		finally
		{
			_activationCompleted = true;
			if (_destroyed)
			{
				DisposeActivationTokenSource();
			}
		}
	}

	private void CancelActivationOnce()
	{
		if (_activationSucceeded || _cancelCallbackInvoked)
		{
			return;
		}

		// Once activation begins, its awaited callback exclusively arbitrates
		// Accepted versus Cancelled. Teardown only cancels that callback's token;
		// it must not race it with a second refund mutation.
		if (_activationStarted)
		{
			CancelActivationDispatch();
			return;
		}

		CancelActivationDispatch();
		_cancelCallbackInvoked = true;
		try
		{
			_onCancelled?.Invoke();
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource.LogWarning($"UAV activation cancellation callback failed. {ex}");
		}
	}

	private void CancelActivationDispatch()
	{
		try
		{
			_activationCts?.Cancel();
		}
		catch (ObjectDisposedException)
		{
			// Completion can dispose the linked token while a Unity teardown
			// callback is still unwinding.
		}
	}

	private void DisposeActivationTokenSource()
	{
		CancellationTokenSource activationCts = _activationCts;
		_activationCts = null;
		activationCts?.Dispose();
	}

	private void RestoreHands(
		UavDeviceHandsService.EquipOperation operationOverride = null)
	{
		if (_restored)
		{
			return;
		}

		_restored = true;
		_equipGeneration++;
		UavDeviceHandsService.EquipOperation operation =
			operationOverride ?? _equipOperation;
		Player restorePlayer = _player;
		Item previousHandsItem = _previousHandsItem;
		Action restoreAfterCancelledDrop =
			!_raidBoundaryReset &&
			restorePlayer != null &&
			previousHandsItem != null &&
			operation != null
				? () => TryRestoreAfterCancelledDrop(
					restorePlayer,
					previousHandsItem,
					operation)
				: null;
		bool restoreDeferred = operation?.Cancel(
			"activation restore started",
			restoreAfterCancelledDrop) == true;
		if (_player == null)
		{
			_ownedController = null;
			_equipOperation = null;
			return;
		}

		try
		{
			ShutdownPhoneScreen();

			UavDeviceController controller = _ownedController ?? operation?.Controller;
			bool ownsController =
				controller != null &&
				ReferenceEquals(_player.HandsController, controller);
			bool ownsEmptyHands =
				_player.HandsController == null &&
				operation?.MayOwnEmptyHands == true;
			if (ownsController)
			{
				controller.ShutdownPhoneScreenForExternalRestore();
				_player.DestroyController();
			}

			if (!_raidBoundaryReset &&
			    _previousHandsItem != null &&
			    (ownsController || ownsEmptyHands) &&
			    !restoreDeferred &&
			    (operation == null || operation.TryClaimHandsRestore()))
			{
				_player.TrySetLastEquippedWeapon(true, null);
			}
			else if (!ownsController && !ownsEmptyHands && _player.HandsController != null)
			{
				TscDiagnostics.LogPhone(
					$"TSC activation restore skipped: hands ownership moved to {_player.HandsController.GetType().FullName}.");
			}
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource.LogWarning($"UAV activation device restore failed. {ex}");
		}
		finally
		{
			_ownedController = null;
			_equipOperation = null;
		}
	}

	private static void TryRestoreAfterCancelledDrop(
		Player player,
		Item previousHandsItem,
		UavDeviceHandsService.EquipOperation operation)
	{
		if (player == null ||
		    previousHandsItem == null ||
		    operation == null ||
		    !operation.IsBoundaryCurrent)
		{
			return;
		}

		GameWorld gameWorld = Singleton<GameWorld>.Instance;
		if (gameWorld == null ||
		    !ReferenceEquals(gameWorld.MainPlayer, player) ||
		    player.ActiveHealthController?.IsAlive != true ||
		    player.HandsController != null)
		{
			TscDiagnostics.LogPhone(
				$"TSC activation delayed restore skipped: raid/player ownership changed or hands are {player.HandsController?.GetType().FullName ?? "<null>"}.");
			return;
		}

		if (!operation.TryClaimHandsRestore())
		{
			return;
		}

		try
		{
			player.TrySetLastEquippedWeapon(true, null);
			TscDiagnostics.LogPhone(
				$"TSC activation restored prior hands after cancelled drop settled: {player.HandsController?.GetType().FullName ?? "<null>"}.");
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource.LogWarning(
				$"TSC activation delayed hands restore failed. {ex}");
		}
	}

	private void ResetLifecycleState(string reason)
	{
		if (_destroyed)
		{
			return;
		}

		_raidBoundaryReset = true;
		_equipGeneration++;
		_equipOperation?.Cancel(reason);
		CancelActivationOnce();
		RestoreHands();
		if (gameObject != null)
		{
			DestroyImmediate(gameObject);
		}
	}

	private void DestroyControllerIfOwned(
		UavDeviceController controller,
		string reason)
	{
		if (_player == null ||
		    controller == null ||
		    !ReferenceEquals(_player.HandsController, controller))
		{
			return;
		}

		try
		{
			controller.ShutdownPhoneScreenForExternalRestore();
			_player.DestroyController();
			TscDiagnostics.LogPhone($"TSC activation removed its owned hands controller: {reason}.");
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource.LogWarning(
				$"TSC activation owned controller cleanup failed ({reason}). {ex}");
		}
	}

	private void OnDestroy()
	{
		_destroyed = true;
		_equipGeneration++;
		if (!_activationSucceeded)
		{
			CancelActivationOnce();
		}

		RestoreHands();
		if (s_active == this)
		{
			s_active = null;
		}

		if (!_activationStarted || _activationCompleted)
		{
			DisposeActivationTokenSource();
		}

		TscDiagnostics.LogPhone("TSC Uplink activation animation destroyed.");
	}

	private static Transform FindChildByName(Transform root, string name)
	{
		if (root == null)
		{
			return null;
		}

		foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
		{
			if (child.name == name)
			{
				return child;
			}
		}

		return null;
	}
}
