using Comfort.Common;
using Cysharp.Threading.Tasks;
using EFT;
using EFT.Communications;
using SamSWAT.FireSupport.ArysReloaded.Utils;
using System;
using System.Diagnostics;
using System.Threading;
using UnityEngine;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public class FireSupportSpotter : ScriptableObject
{
	[SerializeField] private GameObject[] spotterParticles;

	private GameObject _inputManager;
	private Player _player;
	private LayerMask _layerMask;

	private ColliderReporter _colliderCheckerObj;
	private GameObject _spotterPositionObj;
	private GameObject _spotterRotationObj;
	private GameObject _spotterConfirmationObj;
	private Transform _spotterDirectionStartTransform;
	private Transform _spotterDirectionEndTransform;

	public static async UniTask<FireSupportSpotter> Load(
		CancellationToken cancellationToken = default)
	{
		var instance =
			await AssetLoader.LoadAssetAsync<FireSupportSpotter>("assets/content/ui/firesupport_spotter.bundle");
		try
		{
			cancellationToken.ThrowIfCancellationRequested();
			var inputManagerWait = Stopwatch.StartNew();
			while (InputManagerUtil.GetInputManager() == null)
			{
				if (inputManagerWait.Elapsed >= TimeSpan.FromSeconds(5))
				{
					throw new InvalidOperationException(
						"TSC input manager was not captured within five seconds; targeting cannot initialize.");
				}

				await UniTask.Yield();
				cancellationToken.ThrowIfCancellationRequested();
			}

			instance.Initialize();
			return instance;
		}
		catch
		{
			if (instance != null)
			{
				DestroyImmediate(instance);
			}
			throw;
		}
	}

	private void Initialize()
	{
		_inputManager = InputManagerUtil.GetInputManager().gameObject;
		_player = Singleton<GameWorld>.Instance.MainPlayer;
		_layerMask = LayersMaskController.TerrainLowPoly;

		_spotterPositionObj = Instantiate(spotterParticles[0]);
		_colliderCheckerObj = _spotterPositionObj.GetComponentInChildren<ColliderReporter>();
		_spotterPositionObj.SetActive(false);

		_spotterRotationObj = Instantiate(spotterParticles[1]);
		_spotterDirectionStartTransform = _spotterRotationObj.transform.Find("Spotter Arrow Core (6)");
		_spotterDirectionEndTransform = _spotterRotationObj.transform.Find("Spotter Arrow Core (1)");
		_spotterRotationObj.SetActive(false);

		_spotterConfirmationObj = Instantiate(spotterParticles[2]);
		_spotterConfirmationObj.SetActive(false);
	}

	public async UniTask<SetLocationResult> SetLocation(bool checkSpace, CancellationToken cancellationToken)
	{
		await UniTask.WaitForSeconds(0.1f, cancellationToken: cancellationToken);
		NotificationManager.DisplayMessageNotification(
			"TSC TARGETING: Confirm with Middle Mouse or Enter. Cancel with Backspace.",
			ENotificationDurationType.Long,
			ENotificationIconType.Default,
			null);

		// Each targeting session must acquire its own hit before confirmation.
		// In particular, a confirm on the first frame cannot reuse the previous
		// session's marker transform.
		_spotterPositionObj.SetActive(false);
		bool hasSampledLocation = false;
		Vector3 sampledLocation = default;
		try
		{
			while (true)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (IsRequestCancelled())
				{
					return SetLocationResult.InvalidLocation;
				}

				bool confirmPressed = IsConfirmPressed();
				if (checkSpace && confirmPressed)
				{
					// Helicopter clearance belongs to the displayed collider's
					// existing position. Do not move it to a fresh ray hit and
					// approve that new position using the prior physics result.
					if (!hasSampledLocation || _colliderCheckerObj.HasCollision)
					{
						FireSupportAudio.Instance.PlayVoiceover(EVoiceoverType.StationDoesNotHear);
						return SetLocationResult.InvalidLocation;
					}
					return new SetLocationResult(sampledLocation, success: true);
				}

				Transform cameraT = _player.CameraPosition;
				bool hasHit = Physics.Raycast(
					origin: cameraT.position + cameraT.forward,
					direction: cameraT.forward,
					out RaycastHit hitInfo,
					maxDistance: 500,
					_layerMask,
					QueryTriggerInteraction.Ignore);

				FireSupportUI.Instance.SpotterNotice.SetActive(!hasHit);
				hasSampledLocation = hasHit;
				sampledLocation = hitInfo.point;
				_spotterPositionObj.SetActive(hasHit);
				if (hasHit)
				{
					_spotterPositionObj.transform.position = hitInfo.point;
				}

				bool hasBlockedSpace = checkSpace && hasHit && _colliderCheckerObj.HasCollision;
				FireSupportUI.Instance.SpotterHeliNotice.SetActive(hasBlockedSpace);
				if (hasBlockedSpace)
				{
					_colliderCheckerObj.Rotate(5f);
				}

				if (confirmPressed)
				{
					if (!hasHit || hasBlockedSpace)
					{
						FireSupportAudio.Instance.PlayVoiceover(EVoiceoverType.StationDoesNotHear);
						return SetLocationResult.InvalidLocation;
					}

					return new SetLocationResult(hitInfo.point, success: true);
				}

				await UniTask.NextFrame(PlayerLoopTiming.Update, cancellationToken);
			}
		}
		finally
		{
			if (_spotterPositionObj != null)
			{
				_spotterPositionObj.SetActive(false);
			}
			if (FireSupportUI.Instance != null)
			{
				FireSupportUI.Instance.SpotterNotice.SetActive(false);
				FireSupportUI.Instance.SpotterHeliNotice.SetActive(false);
			}
		}
	}

	public async UniTask<SetDirectionResult> SetSupportDirection(
		Vector3 targetLocation,
		CancellationToken cancellationToken)
	{
		await UniTask.WaitForSeconds(0.1f, cancellationToken: cancellationToken);
		NotificationManager.DisplayMessageNotification(
			"TSC DIRECTION: Move the mouse, then confirm with Middle Mouse or Enter.",
			ENotificationDurationType.Long,
			ENotificationIconType.Default,
			null);

		_spotterRotationObj.transform.SetPositionAndRotation(targetLocation, Quaternion.identity);
		_spotterRotationObj.SetActive(true);
		_inputManager.SetActive(false);

		try
		{
			while (true)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (IsRequestCancelled())
				{
					return SetDirectionResult.InvalidDirection;
				}
				if (IsConfirmPressed())
				{
					return new SetDirectionResult(
						_spotterDirectionStartTransform.position,
						_spotterDirectionEndTransform.position,
						_spotterRotationObj.transform.rotation,
						success: true);
				}

				float xAxisRotation = Input.GetAxis("Mouse X") * 5;
				_spotterRotationObj.transform.Rotate(Vector3.down, xAxisRotation);

				await UniTask.NextFrame(cancellationToken);
			}
		}
		finally
		{
			if (_inputManager != null)
			{
				_inputManager.SetActive(true);
			}
			if (_spotterRotationObj != null)
			{
				_spotterRotationObj.SetActive(false);
			}
		}
	}

	public UniTask ConfirmLocation(CancellationToken cancellationToken)
	{
		return ConfirmLocation(_spotterPositionObj.transform.position, cancellationToken);
	}

	public async UniTask ConfirmLocation(Vector3 targetLocation, CancellationToken cancellationToken)
	{
		_spotterConfirmationObj.transform.SetPositionAndRotation(
			targetLocation + Vector3.up,
			Quaternion.identity);
		
		_spotterConfirmationObj.SetActive(true);
		
		try
		{
			await UniTask.WaitForSeconds(0.8f, cancellationToken: cancellationToken);
		}
		finally
		{
			if (_spotterConfirmationObj != null)
			{
				_spotterConfirmationObj.SetActive(false);
			}
		}
	}

	// Targeting raycasts from the player camera, so no designator item is
	// required in hands. With a weapon out, LMB would fire it, so bare LMB
	// only confirms while the rangefinder is held; otherwise use the
	// configured spotter confirm key (default middle mouse) or Enter.
	private bool IsConfirmPressed()
	{
		if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
		{
			return true;
		}

		if (PluginSettings.SpotterConfirmKey != null && PluginSettings.SpotterConfirmKey.Value.IsDown())
		{
			return true;
		}

		return Input.GetMouseButtonDown(0) && HasRangefinderInHands();
	}

	private bool HasRangefinderInHands()
	{
		return _player != null &&
		       _player.HandsController?.Item?.TemplateId == ItemConstants.RANGEFINDER_TPL;
	}

	private bool IsRequestCancelled()
	{
		return (Input.GetMouseButtonDown(1) && Input.GetKey(KeyCode.LeftAlt)) ||
		       Input.GetKeyDown(KeyCode.Backspace);
	}
}
