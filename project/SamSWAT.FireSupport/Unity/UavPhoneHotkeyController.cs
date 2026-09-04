using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.InventoryLogic;
using SamSWAT.FireSupport.ArysReloaded.Integration;
using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public sealed class UavPhoneHotkeyController : UpdatableComponentBase
{
	private const float DangerCloseRingDurationSeconds = 15f;
	private const float DangerCloseAnswerEquipTimeoutSeconds = 8f;
	private const float LastEquippedWeaponRestoreTimeoutSeconds = 6f;
	private const string DangerCloseRingtoneRelativePath =
		"assets/content/ui/phone/audio/danger-close-ringtone.mp3";

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
	private readonly DangerCloseIncomingCallState _dangerCloseCall = new();
	private AudioSource _dangerCloseRingtoneSource;
	private AudioClip _dangerCloseRingtoneClip;
	private int _dangerCloseAudioGeneration;

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

	internal static bool TryPresentDangerCloseAdvance(
		DangerCloseWarningPublication publication)
	{
		return s_instance?.BeginDangerCloseIncomingCall(publication) == true;
	}

	internal static void ApplyDangerCloseTerminal(
		DangerCloseWarningPublication publication)
	{
		s_instance?.TerminateDangerCloseIncomingCall(publication);
	}

	internal static void ResetDangerClosePresentation(string reason)
	{
		s_instance?.ResetDangerCloseIncomingCall(reason);
	}

	internal static bool IsDangerCloseAnswerActive =>
		s_instance?._dangerCloseCall.IsAnswerActive == true;

	internal static bool NotifyDangerClosePhonePresented()
	{
		return s_instance?._dangerCloseCall.TryMarkAnswerPresented(
			Time.unscaledTime) == true;
	}

	internal static int DangerCloseSecondsRemaining =>
		s_instance?._dangerCloseCall.GetSecondsRemaining(Time.unscaledTime) ?? 0;

	public static bool IsAnyPhonePresentationActive
	{
		get
		{
			if (s_instance?._equipInProgress == true ||
			    s_instance?._restoreCoroutine != null ||
			    s_instance?._currentController != null)
			{
				return true;
			}

			Player player =
				s_instance?._manualPlayer ??
				Singleton<GameWorld>.Instance?.MainPlayer;
			return player?.HandsController is UavDeviceController;
		}
	}

	public override void ManualUpdate()
	{
		bool pluginEnabled = PluginSettings.Enabled.Value;
		AdvanceDangerCloseIncomingCall();
		if (!pluginEnabled && _dangerCloseCall.IsActive)
		{
			ResetDangerCloseIncomingCall("plugin disabled");
		}

		if (HandleDangerCloseAnswer(pluginEnabled))
		{
			return;
		}

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

	private bool BeginDangerCloseIncomingCall(
		DangerCloseWarningPublication publication)
	{
		if (publication.Kind != DangerCloseWarningKind.Advance ||
		    !PluginSettings.Enabled.Value ||
		    !IsDangerCloseAnswerShortcutBound() ||
		    !IsLocalPlayerAlive() ||
		    IsAnyPhonePresentationActive ||
		    UavDeviceActivationController.IsActive)
		{
			return false;
		}

		string ringtonePath = GetDangerCloseRingtonePath();
		if (!File.Exists(ringtonePath))
		{
			FireSupportPlugin.LogSource?.LogWarning(
				$"TSC Danger Close ringtone was not found at '{ringtonePath}'.");
			return false;
		}

		if (!_dangerCloseCall.TryBeginAdvance(
			    publication.OpportunityId,
			    publication.SecondsRemaining,
			    Time.unscaledTime,
			    DangerCloseRingDurationSeconds))
		{
			return false;
		}

		StartDangerCloseRingtone();
		return true;
	}

	private void TerminateDangerCloseIncomingCall(
		DangerCloseWarningPublication publication)
	{
		if (publication.Kind == DangerCloseWarningKind.Cancel)
		{
			if (!_dangerCloseCall.TryCancel(publication.OpportunityId))
			{
				return;
			}
		}
		else if (publication.Kind == DangerCloseWarningKind.Inbound)
		{
			if (!_dangerCloseCall.TryMarkInbound(publication.OpportunityId))
			{
				return;
			}
		}
		else
		{
			return;
		}

		StopDangerCloseRingtone();
		RequestDangerClosePhoneRestore("warning lifecycle reached a terminal event");
	}

	private void ResetDangerCloseIncomingCall(string reason)
	{
		StopDangerCloseRingtone();
		_dangerCloseCall.Reset();
		RequestDangerClosePhoneRestore(reason ?? "warning lifecycle reset");
	}

	private void AdvanceDangerCloseIncomingCall()
	{
		if (_dangerCloseCall.IsActive && !IsLocalPlayerAlive())
		{
			ResetDangerCloseIncomingCall("local player died or left the raid");
			return;
		}

		DangerCloseIncomingCallTickResult result =
			_dangerCloseCall.Tick(Time.unscaledTime);
		if (result == DangerCloseIncomingCallTickResult.RingTimedOut)
		{
			StopDangerCloseRingtone();
			return;
		}

		if (result == DangerCloseIncomingCallTickResult.AdvanceExpired)
		{
			StopDangerCloseRingtone();
			RequestDangerClosePhoneRestore("advance forecast elapsed");
		}
		else if (result == DangerCloseIncomingCallTickResult.AnswerEquipTimedOut)
		{
			RequestDangerClosePhoneRestore("answer phone equip timed out");
		}
		else if (result == DangerCloseIncomingCallTickResult.ReopenEquipTimedOut)
		{
			RequestDangerClosePhoneRestore("reopened phone equip timed out");
		}
	}

	private void RequestDangerClosePhoneRestore(string reason)
	{
		if (_equipInProgress &&
		    _equipLaunchMode == UavPhoneLaunchMode.DangerCloseIncomingCall)
		{
			// The spawn callback will see that the answered phase ended and close
			// after EFT finishes its hand transaction.
			return;
		}

		Player player = _manualPlayer ?? Singleton<GameWorld>.Instance?.MainPlayer;
		UavDeviceController controller =
			_currentController ?? player?.HandsController as UavDeviceController;
		if (controller?.LaunchMode != UavPhoneLaunchMode.DangerCloseIncomingCall)
		{
			return;
		}

		_currentController = controller;
		_manualPlayer = player;
		controller.AuthorizationSessionFinished -= OnManualAuthorizationFinished;
		controller.AuthorizationSessionFinished += OnManualAuthorizationFinished;
		TscDiagnostics.LogPhone($"TSC Danger Close phone closing: {reason}.");
		controller.CancelAuthorizationSession();
	}

	private void StartDangerCloseRingtone()
	{
		StopDangerCloseRingtone();
		int audioGeneration = _dangerCloseAudioGeneration;
		FireSupportPlugin.LogSource?.LogInfo(
			$"TSC Danger Close ringtone load started generation={audioGeneration}.");
		StartCoroutine(LoadDangerCloseRingtone(audioGeneration));
	}

	private IEnumerator LoadDangerCloseRingtone(int audioGeneration)
	{
		string ringtoneUri = new Uri(GetDangerCloseRingtonePath()).AbsoluteUri;
		using (UnityWebRequest request =
		       UnityWebRequestMultimedia.GetAudioClip(ringtoneUri, AudioType.MPEG))
		{
			yield return request.SendWebRequest();
			if (audioGeneration != _dangerCloseAudioGeneration ||
			    !_dangerCloseCall.IsRinging)
			{
				FireSupportPlugin.LogSource?.LogInfo(
					$"TSC Danger Close ringtone load discarded generation={audioGeneration} " +
					$"currentGeneration={_dangerCloseAudioGeneration} ringing={_dangerCloseCall.IsRinging}.");
				yield break;
			}

			if (request.result != UnityWebRequest.Result.Success)
			{
				FireSupportPlugin.LogSource?.LogWarning(
					$"TSC Danger Close ringtone failed to load. {request.error}");
				yield break;
			}

			AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
			if (clip == null)
			{
				FireSupportPlugin.LogSource?.LogWarning(
					"TSC Danger Close ringtone decoded to a null AudioClip.");
				yield break;
			}

			clip.name = "TSC Danger Close Ringtone";
			_dangerCloseRingtoneClip = clip;
			PlayDangerCloseRingtone(clip, audioGeneration);
		}
	}

	private void PlayDangerCloseRingtone(AudioClip clip, int audioGeneration)
	{
		if (clip == null ||
		    audioGeneration != _dangerCloseAudioGeneration ||
		    !_dangerCloseCall.IsRinging)
		{
			return;
		}

		if (_dangerCloseRingtoneSource == null)
		{
			_dangerCloseRingtoneSource = gameObject.AddComponent<AudioSource>();
			_dangerCloseRingtoneSource.playOnAwake = false;
			_dangerCloseRingtoneSource.spatialBlend = 0f;
		}

		_dangerCloseRingtoneSource.Stop();
		_dangerCloseRingtoneSource.clip = clip;
		_dangerCloseRingtoneSource.loop = true;
		_dangerCloseRingtoneSource.volume = Mathf.Clamp01(
			PluginSettings.VoiceoverVolume.Value / 100f);
		_dangerCloseRingtoneSource.Play();
		FireSupportPlugin.LogSource?.LogInfo(
			$"TSC Danger Close ringtone started generation={audioGeneration} " +
			$"loadState={clip.loadState} length={clip.length:0.000}s " +
			$"sourceEnabled={_dangerCloseRingtoneSource.enabled} " +
			$"active={_dangerCloseRingtoneSource.gameObject.activeInHierarchy} " +
			$"volume={_dangerCloseRingtoneSource.volume:0.00} " +
			$"isPlaying={_dangerCloseRingtoneSource.isPlaying}.");
	}

	private void StopDangerCloseRingtone()
	{
		// Invalidate an in-flight local-file request but let its coroutine reach
		// the using scope's disposer. A stale generation can never start audio.
		_dangerCloseAudioGeneration++;
		if (_dangerCloseRingtoneSource != null)
		{
			_dangerCloseRingtoneSource.Stop();
			_dangerCloseRingtoneSource.loop = false;
			_dangerCloseRingtoneSource.clip = null;
		}

		AudioClip ringtoneClip = _dangerCloseRingtoneClip;
		_dangerCloseRingtoneClip = null;
		if (ringtoneClip != null)
		{
			Destroy(ringtoneClip);
		}
	}

	private static string GetDangerCloseRingtonePath()
	{
		string pluginDirectory =
			Path.GetDirectoryName(typeof(UavPhoneHotkeyController).Assembly.Location) ??
			string.Empty;
		return Path.Combine(
			pluginDirectory,
			DangerCloseRingtoneRelativePath.Replace('/', Path.DirectorySeparatorChar));
	}

	private static bool IsDangerCloseAnswerShortcutBound()
	{
		return PluginSettings.OpenUavRadarKey != null &&
		       PluginSettings.OpenUavRadarKey.Value.MainKey != KeyCode.None;
	}

	private static bool IsLocalPlayerAlive()
	{
		Player player = Singleton<GameWorld>.Instance?.MainPlayer;
		return player?.IsYourPlayer == true &&
		       player.ActiveHealthController?.IsAlive == true;
	}

	private bool HandleDangerCloseAnswer(bool allowOpen)
	{
		if (!_dangerCloseCall.IsActive)
		{
			return false;
		}

		bool shortcutPressed = IsRadarShortcutPressed();
		if (_radarHoldWasPressed)
		{
			if (!shortcutPressed)
			{
				_radarHoldWasPressed = false;
			}

			// Consume the full press/release edge so the shared UAV-radar hold
			// handler cannot treat an answered-call toggle as a radar action.
			return true;
		}

		if (_dangerCloseCall.IsAnswering || _dangerCloseCall.IsReopening)
		{
			// Never cancel or start a second hands transaction while EFT is
			// still raising the phone.
			return true;
		}

		if (!IsRadarShortcutDown())
		{
			// While the Danger Close phone owns the hands, keep U/K and normal
			// radar handling from racing it. Once fully stowed, only a fresh
			// answer-key press is reserved for this warning.
			return IsDangerClosePhoneTransitionActive();
		}

		_radarHoldWasPressed = shortcutPressed;

		if (!allowOpen)
		{
			return true;
		}

		if (_dangerCloseCall.IsAnswered)
		{
			_dangerCloseCall.MarkAnswerStowed();
			RequestDangerClosePhoneRestore("answer key toggled phone closed");
			return true;
		}

		if (_dangerCloseCall.IsAnsweredStowed)
		{
			TryReopenDangerClosePhone();
			return true;
		}

		if (!_dangerCloseCall.IsRinging)
		{
			return true;
		}

		if (!_dangerCloseCall.TryBeginAnswer(
			    Time.unscaledTime,
			    DangerCloseAnswerEquipTimeoutSeconds,
			    out int secondsRemaining))
		{
			return true;
		}

		if (!TryOpenUplink(UavPhoneLaunchMode.DangerCloseIncomingCall))
		{
			_dangerCloseCall.ResumeRingingAfterFailedAnswer(Time.unscaledTime);
			return true;
		}

		StopDangerCloseRingtone();
		NotificationManager.DisplayWarningNotification(
			$"DANGER CLOSE: A-10 STRAFE TASKED. ETA ~{secondsRemaining}s. SEEK COVER.",
			ENotificationDurationType.Long);
		return true;
	}

	private void TryReopenDangerClosePhone()
	{
		if (IsDangerClosePhoneTransitionActive() ||
		    !_dangerCloseCall.TryBeginReopen(
			    Time.unscaledTime,
			    DangerCloseAnswerEquipTimeoutSeconds,
			    out _))
		{
			return;
		}

		if (!TryOpenUplink(UavPhoneLaunchMode.DangerCloseIncomingCall))
		{
			_dangerCloseCall.ResumeStowedAfterFailedReopen(Time.unscaledTime);
		}
	}

	private bool IsDangerClosePhoneTransitionActive()
	{
		if (_equipInProgress &&
		    _equipLaunchMode == UavPhoneLaunchMode.DangerCloseIncomingCall)
		{
			return true;
		}

		if (_restoreCoroutine != null)
		{
			return true;
		}

		Player player = _manualPlayer ?? Singleton<GameWorld>.Instance?.MainPlayer;
		if ((_dangerCloseCall.IsRinging || _dangerCloseCall.IsAnsweredStowed) &&
		    player?.HandsController == null)
		{
			// EFT reports null hands while TrySetLastEquippedWeapon is still
			// completing. Reopening in that gap can overlap two hand swaps.
			return true;
		}

		UavDeviceController controller =
			_currentController ?? player?.HandsController as UavDeviceController;
		return controller?.LaunchMode == UavPhoneLaunchMode.DangerCloseIncomingCall;
	}

	private static bool IsRadarShortcutDown()
	{
		if (PluginSettings.OpenUavRadarKey == null)
		{
			return false;
		}

		BepInEx.Configuration.KeyboardShortcut shortcut = PluginSettings.OpenUavRadarKey.Value;
		if (shortcut.MainKey == KeyCode.None || !Input.GetKeyDown(shortcut.MainKey))
		{
			return false;
		}

		foreach (KeyCode modifier in shortcut.Modifiers)
		{
			if (!Input.GetKey(modifier))
			{
				return false;
			}
		}

		return true;
	}

	private bool HandleRadarHold(bool allowOpen)
	{
		// KeyboardShortcut.IsPressed evaluates the main key and every configured
		// modifier. Tracking the transition ourselves also treats releasing a
		// modifier as a release, which keeps custom bindings from leaving the
		// monitor stuck in the player's hands.
		bool phoneDisplayMode =
			PluginSettings.RadarDisplayMode?.Value !=
			UavRadarDisplayMode.HUD;
		bool isPressed =
			allowOpen &&
			phoneDisplayMode &&
			IsRadarShortcutPressed();
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
				NotificationManager.DisplayWarningNotification(
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

	private bool TryOpenUplink(UavPhoneLaunchMode launchMode)
	{
		GameWorld gameWorld = Singleton<GameWorld>.Instance;
		if (gameWorld == null)
		{
			TscDiagnostics.LogPhone("TSC Uplink ignored: GameWorld was null.");
			return false;
		}

		Player player = gameWorld.MainPlayer;
		if (player == null)
		{
			TscDiagnostics.LogPhone("TSC Uplink ignored: MainPlayer was null.");
			return false;
		}

		bool isAlive = player.ActiveHealthController?.IsAlive == true;
		TscDiagnostics.LogPhone(
			$"TSC Uplink player state: isYourPlayer={player.IsYourPlayer}, alive={isAlive}, equipInProgress={_equipInProgress}, hands={player.HandsController?.GetType().FullName ?? "<null>"}.");

		if (!player.IsYourPlayer || !isAlive)
		{
			return false;
		}

		if (player.IsInventoryOpened)
		{
			TscDiagnostics.LogPhone("TSC Uplink ignored: inventory screen is open.");
			return false;
		}

		// HandsController may already point at the phone before EFT completes the
		// SpawnController callback. Cancelling in that gap can strand the hand
		// swap, so every manual phone action waits for the transaction to finish.
		if (_equipInProgress)
		{
			TscDiagnostics.LogPhone("TSC Uplink ignored: manual equip already in progress.");
			return false;
		}

		if (_restoreCoroutine != null)
		{
			TscDiagnostics.LogPhone("TSC Uplink ignored: a prior phone restore is still in progress.");
			return false;
		}

		UavDeviceController handsController = player.HandsController as UavDeviceController;
		if (launchMode == UavPhoneLaunchMode.UavRadarMonitor)
		{
			if (!UavReconOverlay.IsReconActive)
			{
				TscDiagnostics.LogPhone("TSC UAV radar phone ignored: recon link ended before equip.");
				return false;
			}

			if (handsController != null)
			{
				string reason = handsController.LaunchMode == UavPhoneLaunchMode.UavRadarMonitor
					? "radar monitor is already in the player's hands"
					: $"another phone session is active ({handsController.LaunchMode})";
				TscDiagnostics.LogPhone($"TSC UAV radar phone ignored: {reason}.");
				return false;
			}

			if (_currentController != null)
			{
				TscDiagnostics.LogPhone("TSC UAV radar phone ignored: a prior phone restore is still in progress.");
				return false;
			}
		}

		UavDeviceController activeController = _currentController ?? handsController;
		if (activeController != null)
		{
			if (launchMode == UavPhoneLaunchMode.DangerCloseIncomingCall)
			{
				TscDiagnostics.LogPhone(
					$"TSC Danger Close answer ignored: another phone session is active ({activeController.LaunchMode}).");
				return false;
			}

			// Sessions launched through EFT's quick-use flow (special slot key)
			// are restored by EFT itself once the session finishes. Attaching our
			// manual restore on top ran DestroyController mid hand-swap and left
			// the interaction state machine wedged, freezing movement and look on
			// the next pickup.
			if (activeController.IsQuickUseSession)
			{
				TscDiagnostics.LogPhone("TSC Uplink key pressed while quick-use phone is active; cancelling session, EFT restores hands.");
				activeController.CancelAuthorizationSession();
				return false;
			}

			TscDiagnostics.LogPhone("TSC Uplink key pressed while phone is active; cancelling session.");
			_currentController = activeController;
			_manualPlayer = player;
			activeController.AuthorizationSessionFinished -= OnManualAuthorizationFinished;
			activeController.AuthorizationSessionFinished += OnManualAuthorizationFinished;
			activeController.CancelAuthorizationSession();
			return false;
		}

		if (UavDeviceActivationController.IsActive)
		{
			TscDiagnostics.LogPhone("TSC Uplink ignored: internal UAV activation animation is active.");
			return false;
		}

		PaymentMode paymentMode = FireSupportPayment.GetActivePaymentMode();
		TscDiagnostics.LogPhone($"TSC Uplink active payment mode: {paymentMode}.");
		if (launchMode != UavPhoneLaunchMode.UavRadarMonitor &&
		    launchMode != UavPhoneLaunchMode.DangerCloseIncomingCall &&
		    paymentMode == PaymentMode.DirectRadial)
		{
			NotificationManager.DisplayWarningNotification(
				"Set payment mode to PhoneAuthorizations or Hybrid.",
				ENotificationDurationType.Long);
			return false;
		}

		UavDeviceItem uplinkItem =
			launchMode == UavPhoneLaunchMode.DangerCloseIncomingCall
				? UavDeviceInventory.FindUplinkInDedicatedWarningSlot(player)
				: UavDeviceInventory.FindCarriedUplink(player);
		if (uplinkItem == null)
		{
			string locationRequirement =
				launchMode == UavPhoneLaunchMode.DangerCloseIncomingCall
					? " in SpecialSlot4"
					: " in carried inventory";
			TscDiagnostics.LogPhone(
				$"TSC Uplink ignored: no TerraGroup TSC Uplink item was found{locationRequirement}.");
			NotificationManager.DisplayWarningNotification(
				$"TerraGroup TSC Uplink not found{locationRequirement}.",
				ENotificationDurationType.Long);
			return false;
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

			bool launchStarted =
				_equipInProgress ||
				_currentController?.LaunchMode == launchMode;
			return launchStarted &&
			       (launchMode != UavPhoneLaunchMode.DangerCloseIncomingCall ||
			        _dangerCloseCall.IsAnswerActive);
		}
		catch (Exception ex)
		{
			OnManualEquipFailed(
				player,
				ex,
				_equipOperation,
				_lifecycleGeneration);
			return false;
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
			bool dangerCloseEquip =
				_equipLaunchMode == UavPhoneLaunchMode.DangerCloseIncomingCall;
			CleanupFailedManualEquip(
				player,
				new InvalidOperationException("SpawnController callback supplied a null UavDeviceController."),
				operation);
			RecoverDangerCloseAfterPhoneFailure(dangerCloseEquip);
			return;
		}

		FireSupportPlugin.LogSource.LogInfo($"TSC Uplink phone spawned; finish handler subscribed (mode={controller.LaunchMode}).");
		controller.AuthorizationSessionFinished -= OnManualAuthorizationFinished;
		controller.AuthorizationSessionFinished += OnManualAuthorizationFinished;
		_equipLaunchMode = UavPhoneLaunchMode.ManualAuthorization;
		controller.NotifyExternalEquipCompleted();

		if (controller.LaunchMode == UavPhoneLaunchMode.DangerCloseIncomingCall &&
		    !_dangerCloseCall.IsAnswerActive)
		{
			TscDiagnostics.LogPhone(
				"TSC Danger Close phone spawned after its local answer display ended; closing immediately.");
			controller.CancelAuthorizationSession();
		}
		else if (controller.LaunchMode == UavPhoneLaunchMode.UavRadarMonitor &&
		    (_radarReleaseQueued ||
		     PluginSettings.RadarDisplayMode?.Value ==
			     UavRadarDisplayMode.HUD ||
		     !PluginSettings.Enabled.Value ||
		     !IsRadarShortcutPressed() ||
		     !UavReconOverlay.IsReconActive))
		{
			TscDiagnostics.LogPhone("TSC UAV radar phone spawned after monitor eligibility ended; closing immediately.");
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

		if (controller.LaunchMode == UavPhoneLaunchMode.DangerCloseIncomingCall)
		{
			if (_dangerCloseCall.IsAnswered)
			{
				_dangerCloseCall.MarkAnswerStowed();
			}
			else
			{
				RecoverDangerCloseAfterPhoneFailure(dangerCloseEquip: true);
			}
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

			if (ownsController &&
			    !TryRemoveOwnedPhoneController(player, controller, "phone outro"))
			{
				yield break;
			}

			TscDiagnostics.LogPhone("TSC Uplink: owned controller removed; restoring last equipped weapon");
			yield return RestoreLastEquippedWeaponAndWait(
				player,
				lifecycleGeneration,
				"phone outro");
			TscDiagnostics.LogPhone($"TSC Uplink: restored HandsController = {player?.HandsController?.GetType().FullName ?? "<null>"}");
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

	private static bool TryRemoveOwnedPhoneController(
		Player player,
		UavDeviceController controller,
		string context)
	{
		try
		{
			controller.ShutdownPhoneScreenForExternalRestore();
			player.DestroyController();
			return true;
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource.LogWarning(
				$"TSC Uplink {context} controller removal failed. {ex}");
			return false;
		}
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

		bool dangerCloseEquip =
			_equipLaunchMode == UavPhoneLaunchMode.DangerCloseIncomingCall;
		CleanupFailedManualEquip(player, exception, operation);
		RecoverDangerCloseAfterPhoneFailure(dangerCloseEquip);
	}

	private void RecoverDangerCloseAfterPhoneFailure(bool dangerCloseEquip)
	{
		if (!dangerCloseEquip)
		{
			return;
		}

		if (_dangerCloseCall.IsAnswering)
		{
			_dangerCloseCall.ResumeRingingAfterFailedAnswer(Time.unscaledTime);
		}
		else if (_dangerCloseCall.IsReopening)
		{
			_dangerCloseCall.ResumeStowedAfterFailedReopen(Time.unscaledTime);
		}

		if (_dangerCloseCall.IsRinging &&
		    _dangerCloseRingtoneSource?.isPlaying != true)
		{
			StartDangerCloseRingtone();
		}
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
		bool restoreOwnedHands = false;
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
				restoreOwnedHands = true;
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

		if (restoreOwnedHands && player != null)
		{
			_manualPlayer = player;
			_restoreCoroutine = StartCoroutine(
				RestoreLastEquippedWeaponAfterFailedEquip(
					player,
					_lifecycleGeneration));
		}
	}

	private IEnumerator RestoreLastEquippedWeaponAfterFailedEquip(
		Player player,
		int lifecycleGeneration)
	{
		try
		{
			yield return RestoreLastEquippedWeaponAndWait(
				player,
				lifecycleGeneration,
				"failed phone equip");
		}
		finally
		{
			if (lifecycleGeneration == _lifecycleGeneration &&
			    ReferenceEquals(_manualPlayer, player))
			{
				_manualPlayer = null;
				_restoreCoroutine = null;
			}
		}
	}

	private IEnumerator RestoreLastEquippedWeaponAndWait(
		Player player,
		int lifecycleGeneration,
		string context)
	{
		bool completed = false;
		bool failed = false;
		string error = string.Empty;
		try
		{
			Callback callback = result =>
			{
				failed = result?.Failed == true;
				error = result?.Error ?? string.Empty;
				completed = true;
			};
			player.TrySetLastEquippedWeapon(true, callback);
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource.LogWarning(
				$"TSC Uplink {context} restore request failed. {ex}");
			yield break;
		}

		float deadline =
			Time.unscaledTime + LastEquippedWeaponRestoreTimeoutSeconds;
		while (!completed &&
		       lifecycleGeneration == _lifecycleGeneration &&
		       Time.unscaledTime < deadline)
		{
			yield return null;
		}

		if (lifecycleGeneration != _lifecycleGeneration)
		{
			yield break;
		}

		if (!completed)
		{
			FireSupportPlugin.LogSource.LogWarning(
				$"TSC Uplink {context} restore callback timed out; hands={player?.HandsController?.GetType().FullName ?? "<null>"}.");
		}
		else if (failed)
		{
			FireSupportPlugin.LogSource.LogWarning(
				$"TSC Uplink {context} restore failed: {error}.");
		}
	}

	private void ResetLifecycleState(string reason, bool destroyOwnedController)
	{
		_lifecycleGeneration++;
		_radarHoldWasPressed = false;
		_radarReleaseQueued = false;
		StopDangerCloseRingtone();
		_dangerCloseCall.Reset();

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
