using Cysharp.Text;
using Cysharp.Threading.Tasks;
using EFT.Communications;
using EFT.InputSystem;
using EFT.UI;
using EFT.UI.Gestures;
using SamSWAT.FireSupport.ArysReloaded.Utils;
using System;
using System.Runtime.CompilerServices;
using System.Threading;
using UnityEngine;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

/// <summary>
/// Entry point for Fire Support in-raid logic.
/// </summary>
public class FireSupportController : UIInputNode
{
	[NonSerialized] private FireSupportAudio _audio;
	[NonSerialized] private FireSupportUI _ui;
	[NonSerialized] private FireSupportSpotter _spotter;
	[NonSerialized] private GesturesMenu _gesturesMenu;

	[NonSerialized] private readonly FireSupportServiceMappings _services = new(new SupportTypeComparer());

	[NonSerialized] private bool _canCallSupport = true;
	[NonSerialized] private int _cooldownTimer;

	public static FireSupportController Instance { get; private set; }

	public int CooldownSecondsRemaining => _cooldownTimer;

	public static async UniTask<FireSupportController> Create(GesturesMenu gesturesMenu)
	{
		Instance = new GameObject("FireSupportController").AddComponent<FireSupportController>();
		await Instance.Initialize(gesturesMenu);
		return Instance;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void CanCallSupport(bool canCall)
	{
		_canCallSupport = canCall;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool IsSupportAvailable()
	{
		return _cooldownTimer == 0 && _canCallSupport;
	}

	private async UniTask Initialize(GesturesMenu gesturesMenu)
	{
		_gesturesMenu = gesturesMenu;
		await FireSupportRuntime.EnsureInitialized();
		_audio = FireSupportAudio.Instance;
		_spotter = await FireSupportSpotter.Load();

		var heliExfil = new HeliExfiltrationService(
			_spotter,
			PluginSettings.AmountOfExtractionRequests.Value,
			ESupportType.Extract);
		_services[heliExfil.SupportType] = heliExfil;
		var priorityExfil = new HeliExfiltrationService(
			_spotter,
			PluginSettings.AmountOfExtractionRequests.Value,
			ESupportType.PriorityExfil);
		_services[priorityExfil.SupportType] = priorityExfil;

		var jetStrafe = new JetStrafeService(
			_spotter,
			PluginSettings.AmountOfStrafeRequests.Value,
			ESupportType.Strafe);
		_services[jetStrafe.SupportType] = jetStrafe;
		var doubleStrafe = new JetStrafeService(
			_spotter,
			PluginSettings.AmountOfStrafeRequests.Value,
			ESupportType.DoubleStrafe);
		_services[doubleStrafe.SupportType] = doubleStrafe;

		var uavRecon = new UavReconService(
			PluginSettings.AmountOfUavRequests.Value,
			ESupportType.Uav);
		_services[uavRecon.SupportType] = uavRecon;
		var focusedSweep = new UavReconService(
			PluginSettings.AmountOfUavRequests.Value,
			ESupportType.FocusedSweep);
		_services[focusedSweep.SupportType] = focusedSweep;

		_ui = await FireSupportUI.Load(_services, gesturesMenu);
		_ui.SupportRequested += OnSupportRequested;

	}

	private void OnDestroy()
	{
		_ui.SupportRequested -= OnSupportRequested;
		AssetLoader.UnloadAllBundles();
		FireSupportPoolManager.Instance.Dispose();
		FireSupportAudio.Instance.Dispose();
		DestroyImmediate(_spotter);
		FireSupportUI.Instance.Dispose();
		Instance = null;
		DestroyImmediate(gameObject);
	}

	/// <summary>
	/// Schedules a deploy committed on the TSC Uplink phone. Runs on this
	/// controller (alive for the whole raid) instead of the phone's own
	/// lifecycle, and waits for the phone hand-swap to settle before starting
	/// the support flow, so it survives whichever restore path EFT takes.
	/// </summary>
	public void ScheduleDeployAfterHandsRestore(ESupportType supportType)
	{
		FireSupportPlugin.LogSource.LogInfo($"TSC deploy scheduled: {supportType}.");
		StartCoroutine(DeployAfterHandsRestore(supportType));
	}

	private System.Collections.IEnumerator DeployAfterHandsRestore(ESupportType supportType)
	{
		// The spotter and UAV flows are camera/UI driven and independent of
		// the hands controller, and uplink deploys suppress the activation
		// device re-equip, so there is no hand-swap race to wait out. Waiting
		// for the full stow + weapon re-equip added ~3-4s between the tap and
		// the designator appearing; a short beat keeps the inputs distinct.
		yield return new WaitForSecondsRealtime(0.35f);

		FireSupportPlugin.LogSource.LogInfo($"TSC deploy dispatch: {supportType}.");
		RequestSupport(supportType);
	}

	/// <summary>
	/// Deploys an exact support type from the TSC Uplink deploy selector.
	/// Unlike the radial path there is no base/variant resolution: the phone
	/// already committed a specific authorization.
	/// </summary>
	public void RequestSupport(ESupportType supportType)
	{
		try
		{
			if (!_services.TryGetValue(supportType, out IFireSupportService service))
			{
				FireSupportPlugin.LogSource.LogWarning($"TSC deploy request had no service registered for {supportType}.");
				return;
			}

			if (!IsSupportAvailable())
			{
				FireSupportPlugin.LogSource.LogInfo(
					$"TSC deploy request blocked: support unavailable (cooldown={_cooldownTimer}, canCall={_canCallSupport}).");
				NotificationManagerClass.DisplayWarningNotification(
					"Support station is busy. Wait for the current request or cooldown to finish.",
					ENotificationDurationType.Default);
				return;
			}

			if (!service.IsRequestAvailable())
			{
				FireSupportPlugin.LogSource.LogInfo($"TSC deploy request blocked: no request available for {supportType}.");
				NotificationManagerClass.DisplayWarningNotification(
					$"{FireSupportPayment.GetSupportName(supportType)} is not available right now.",
					ENotificationDurationType.Default);
				return;
			}

			if (!FireSupportPayment.CanDeployFromRadial(supportType, notify: true))
			{
				FireSupportPlugin.LogSource.LogInfo($"TSC deploy request blocked: payment gate for {supportType}.");
				return;
			}

			FireSupportPlugin.LogSource.LogInfo($"TSC deploy request planning: {supportType}.");
			// The deploy phone already played the authorization; don't pull it
			// back out for the UAV activation animation.
			UavDeviceActivationController.SuppressNextActivation();
			service.PlanRequest(destroyCancellationToken).Forget();
		}
		catch (OperationCanceledException) {}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource.LogError(ex);
		}
	}

	private void OnSupportRequested(ESupportType supportType)
	{
		try
		{
			ESupportType requestedSupportType = supportType;
			supportType = FireSupportDeploymentSelection.ResolveRadialRequest(
				supportType,
				_services,
				type => FireSupportPayment.CanDeployFromRadial(type, notify: false));
			if (requestedSupportType != supportType)
			{
				FireSupportPlugin.LogSource.LogInfo(
					$"TSC radial support resolved requested={requestedSupportType} deployed={supportType}.");
			}

			if (!_services.TryGetValue(supportType, out IFireSupportService service))
			{
				throw new ArgumentException($"No service registered for support type {supportType}");
			}

			if (!service.IsRequestAvailable())
			{
				FireSupportPlugin.LogSource.LogWarning("Should not be able to reach this line, bad logic somewhere...");
				return;
			}

			if (!FireSupportPayment.CanDeployFromRadial(supportType, notify: true))
			{
				return;
			}

			_gesturesMenu.Close();
			service.PlanRequest(destroyCancellationToken).Forget();
		}
		catch (OperationCanceledException) {}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource.LogError(ex);
		}
	}

	public async UniTaskVoid StartCooldown(int time, CancellationToken cancellationToken, Action callback = null)
	{
		try
		{
			_ui.timerText.enabled = true;
			_cooldownTimer = time;

			while (_cooldownTimer > 0)
			{
				_cooldownTimer--;

				float minutes = Mathf.FloorToInt(_cooldownTimer / 60f);
				float seconds = Mathf.FloorToInt(_cooldownTimer % 60);

				using (Utf16ValueStringBuilder sb = ZString.CreateStringBuilder())
				{
					sb.AppendFormat("{0:00}.{1:00}", minutes, seconds);
					_ui.timerText.text = sb.ToString();
				}

				await UniTask.WaitForSeconds(1, cancellationToken: cancellationToken);
			}

			_ui.timerText.enabled = false;

			if (_services.AnyAvailableRequests())
			{
				FireSupportAudio.Instance.PlayVoiceover(EVoiceoverType.StationAvailable);
			}

			CanCallSupport(true);
			callback?.Invoke();
		}
		catch (OperationCanceledException) {}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource.LogError(ex);
		}
	}

	public override ETranslateResult TranslateCommand(ECommand command)
	{
		return ETranslateResult.Ignore;
	}

	public override void TranslateAxes(ref float[] axes)
	{
	}

	public override ECursorResult ShouldLockCursor()
	{
		return ECursorResult.Ignore;
	}
}
