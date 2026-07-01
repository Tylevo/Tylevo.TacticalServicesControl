using Comfort.Common;
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
	private Action _onActivated;
	private CancellationToken _cancellationToken;
	private UavPhoneScreenRenderer _phoneScreen;
	private bool _activated;
	private bool _restored;

	public static bool TryPlay(Action onActivated, CancellationToken cancellationToken)
	{
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
			runner.Initialize(player, deviceItem, onActivated, cancellationToken);
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
		Action onActivated,
		CancellationToken cancellationToken)
	{
		_player = player;
		_deviceItem = deviceItem;
		_onActivated = onActivated;
		_cancellationToken = cancellationToken;
		s_active = this;

		TscDiagnostics.LogPhone(
			$"TSC Uplink activation animation started with item {deviceItem.Id} ({deviceItem.GetType().FullName}).");
		StartCoroutine(RunActivation());
	}

	private IEnumerator RunActivation()
	{
		if (!EquipDevice())
		{
			ActivateRadarOnce();
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
			RestoreHands();
			Destroy(gameObject);
			yield break;
		}

		GetController()?.PlayTap(0.1f);
		yield return new WaitForSecondsRealtime(TapImpactSeconds);
		_phoneScreen?.ShowAuthorizing();
		ActivateRadarOnce();
		TscDiagnostics.LogPhone("TSC Uplink activation tap completed; radar starting.");

		yield return new WaitForSecondsRealtime(TapHoldSeconds);
		UavDeviceController controller = GetController();
		_phoneScreen?.ShowAuthorized();
		controller?.PlayOutroSuccess();

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
			_previousHandsItem = _player.HandsController?.Item;
			if (!UavDeviceConstants.IsUavDevice(_deviceItem))
			{
				FireSupportPlugin.LogSource.LogWarning("TSC Uplink activation equip failed: selected item was not a TerraGroup TSC Uplink.");
				return false;
			}

			UavDeviceHandsService.BeginEquip(
				_player,
				_deviceItem,
				UavPhoneLaunchMode.InternalUavActivation,
				controller =>
				{
					TscDiagnostics.LogPhone(
						$"TSC Uplink activation controller spawned. controller={controller?.GetType().FullName ?? "<null>"}.");
				},
				ex =>
				{
					FireSupportPlugin.LogSource.LogWarning($"UAV activation device controller spawn failed. {ex}");
					ActivateRadarOnce();
					RestoreHands();
				});
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

			screenRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			screenRenderer.receiveShadows = false;
			screenRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
			screenRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

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
			    (info.IsName("Outro Success") && info.normalizedTime >= 0.95f))
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
			return _player?.HandsController as UavDeviceController;
		}
		catch
		{
			return null;
		}
	}

	private void ActivateRadarOnce()
	{
		if (_activated)
		{
			return;
		}

		_activated = true;
		try
		{
			_onActivated?.Invoke();
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource.LogWarning($"UAV activation callback failed. {ex}");
		}
	}

	private void RestoreHands()
	{
		if (_restored || _player == null)
		{
			return;
		}

		_restored = true;

		try
		{
			ShutdownPhoneScreen();

			if (_player.HandsController is UavDeviceController)
			{
				_player.DestroyController();
			}

			if (_previousHandsItem != null)
			{
				_player.TrySetLastEquippedWeapon(true, null);
			}
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource.LogWarning($"UAV activation device restore failed. {ex}");
		}
	}

	private void OnDestroy()
	{
		RestoreHands();
		if (s_active == this)
		{
			s_active = null;
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
