using Comfort.Common;
using Cysharp.Threading.Tasks;
using EFT;
using JetBrains.Annotations;
using SamSWAT.FireSupport.ArysReloaded.Utils;
using System;
using System.Threading;
using UnityEngine;
using UnityEngine.Audio;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public sealed class UH60Behaviour : FireSupportBehaviour
{
	private const float RemoteCargoDepartureFallbackSeconds = 600f;
	private const float CargoDeparturePublishRetrySeconds = 0.5f;
	private const int CargoDeparturePublishMaxAttempts = 5;
	private static readonly int s_flySpeedMultiplier = Animator.StringToHash("FlySpeedMultiplier");
	private static readonly int s_flyAway = Animator.StringToHash("FlyAway");

	[SerializeField] private Animator helicopterAnimator;
	[SerializeField] private AnimationCurve volumeCurve;
	public AudioSource engineCloseSource;
	public AudioSource engineDistantSource;
	public AudioSource rotorsCloseSource;
	public AudioSource rotorsDistantSource;

	private CancellationToken _cancellationToken;
	private CancellationTokenSource _requestCts;
	private GameWorld _gameWorld;
	private ESupportType _requestSupportType = ESupportType.Extract;
	private HelicopterTimingSnapshot _timingSnapshot;
	private bool _hasTimingSnapshot;
	private bool _allowLocalServicePoint = true;
	private GameObject _landingPoint;
	private bool _returningToPool;
	private bool _cargoDepartureRequested;
	private bool _cargoDeparturePublished;
	private bool _cargoTransferSucceeded;
	private int _cargoDeparturePublishAttempts;
	private float _nextCargoDeparturePublishTime;
	private string _pendingCargoDepartureRequesterProfileId =
		string.Empty;
	private bool _pendingCargoDepartureSuccessfulTransfer;
	private string _supportRequestId = string.Empty;
	private int _requestGeneration;

	// Both UH-60 products reuse the existing Extract prefab pool. Product
	// behavior is selected from _requestSupportType after the asset is leased.
	public override ESupportType SupportType => ESupportType.Extract;

	public override void ProcessRequest(
		Vector3 position,
		Vector3 direction,
		Vector3 rotation,
		CancellationToken cancellationToken,
		bool visualOnly = false,
		int visualSeed = 0,
		int passIndex = 0)
	{
		_requestGeneration++;
		CancelRequestLifetime();
		_requestCts =
			CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		_cancellationToken = _requestCts.Token;
		_returningToPool = false;
		if (!_hasTimingSnapshot)
		{
			SetRequestTiming(
				_requestSupportType,
				null,
				allowLocalServicePoint: true,
				supportRequestId: string.Empty);
		}

		Transform heliTransform = transform;
		heliTransform.position = position;
		heliTransform.eulerAngles = rotation;
		helicopterAnimator.SetFloat(s_flySpeedMultiplier, _timingSnapshot.SpeedMultiplier);
	}

	public void SetRequestTiming(
		ESupportType supportType,
		HelicopterTimingSnapshot? timingSnapshot,
		bool allowLocalServicePoint,
		string supportRequestId = "")
	{
		_requestSupportType = supportType == ESupportType.PriorityExfil
			? ESupportType.PriorityExfil
			: ESupportType.Extract;
		_timingSnapshot = timingSnapshot ??
			FireSupportTuningSettings.CaptureHelicopterTiming(_requestSupportType);
		_allowLocalServicePoint = allowLocalServicePoint;
		_supportRequestId = supportRequestId?.Trim() ?? string.Empty;
		_cargoDepartureRequested = false;
		_cargoDeparturePublished = false;
		_cargoTransferSucceeded = false;
		_cargoDeparturePublishAttempts = 0;
		_nextCargoDeparturePublishTime = 0f;
		_pendingCargoDepartureRequesterProfileId = string.Empty;
		_pendingCargoDepartureSuccessfulTransfer = false;
		if (_requestSupportType == ESupportType.PriorityExfil &&
		    Uh60CargoDepartureNetworking.TryGetRemoteDeparture(
			    _supportRequestId,
			    out bool successfulTransfer))
		{
			_cargoDepartureRequested = true;
			_cargoTransferSucceeded = successfulTransfer;
		}
		_hasTimingSnapshot = true;
	}

	public override void ManualUpdate()
	{
		if (_cancellationToken.IsCancellationRequested ||
		    !_gameWorld.IsMainPlayerAlive())
		{
			ReturnToPoolSafely();
			return;
		}

		RetryPendingCargoDeparturePublication();
		CrossFadeAudio();
	}

	protected override void OnAwake()
	{
		_gameWorld = Singleton<GameWorld>.Instance;

		AudioMixerGroup outputAudioMixerGroup = Singleton<BetterAudio>.Instance.EnvTechnicalSoundsGroup;
		engineCloseSource.outputAudioMixerGroup = outputAudioMixerGroup;
		engineDistantSource.outputAudioMixerGroup = outputAudioMixerGroup;
		rotorsCloseSource.outputAudioMixerGroup = outputAudioMixerGroup;
		rotorsDistantSource.outputAudioMixerGroup = outputAudioMixerGroup;
		Uh60CargoDepartureNetworking.RemoteDepartureReceived +=
			OnRemoteCargoDepartureReceived;

		HasFinishedInitialization = true;
	}

	private void CrossFadeAudio()
	{
		if (!_gameWorld.IsMainPlayerAlive())
		{
			return;
		}

		float distance = Vector3.Distance(_gameWorld.MainPlayer.CameraPosition.position,
			rotorsCloseSource.transform.position);
		float volume = volumeCurve.Evaluate(distance);

		rotorsCloseSource.volume = Mathf.Clamp01(volume);
		engineCloseSource.volume = Mathf.Clamp01(volume - 0.2f);
		rotorsDistantSource.volume = Mathf.Clamp01(1 - volume);
		engineDistantSource.volume = Mathf.Clamp01(1 - volume);
	}

	[UsedImplicitly]
	private async UniTaskVoid OnHelicopterArrive()
	{
		int requestGeneration = _requestGeneration;
		CancellationToken requestCancellationToken = _cancellationToken;
		HelicopterTimingSnapshot timingSnapshot = _timingSnapshot;
		ESupportType requestSupportType = _requestSupportType;
		bool allowLocalServicePoint = _allowLocalServicePoint;
		GameObject requestLandingPoint = null;
		HeliCargoTransferPoint requestCargoTransferPoint = null;
		try
		{
			FireSupportAudio.Instance.PlayVoiceover(EVoiceoverType.SupportHeliPickingUp);
			DestroyLandingPoint();
			if (allowLocalServicePoint)
			{
				requestLandingPoint = CreateLandingPoint(
					requestSupportType,
					timingSnapshot,
					requestCancellationToken);
				_landingPoint = requestLandingPoint;
				requestCargoTransferPoint =
					requestLandingPoint.GetComponent<HeliCargoTransferPoint>();
			}

			bool cargoTransferred;
			if (requestSupportType == ESupportType.PriorityExfil &&
			    !allowLocalServicePoint)
			{
				cargoTransferred =
					await WaitForAuthoritativeCargoDeparture(
						requestCancellationToken);
			}
			else
			{
				int configWaitTime = timingSnapshot.WaitTimeSeconds;
				float waitTime = configWaitTime * 0.75f;

				cargoTransferred =
					await WaitForAvailableHelicopterWindow(
						waitTime,
						requestCargoTransferPoint,
						requestCancellationToken);
				if (requestGeneration != _requestGeneration)
				{
					return;
				}

				if (!cargoTransferred)
				{
					FireSupportAudio.Instance.PlayVoiceover(
						EVoiceoverType.SupportHeliHurry);

					cargoTransferred =
						await WaitForAvailableHelicopterWindow(
							configWaitTime - waitTime,
							requestCargoTransferPoint,
							requestCancellationToken);
				}
			}

			if (requestGeneration != _requestGeneration)
			{
				return;
			}

			if (requestSupportType == ESupportType.PriorityExfil &&
			    allowLocalServicePoint)
			{
				PublishLocalCargoDeparture(
					requestCargoTransferPoint,
					cargoTransferred);
			}

			helicopterAnimator.SetTrigger(s_flyAway);
			FireSupportAudio.Instance.PlayVoiceover(
				cargoTransferred
					? EVoiceoverType.SupportHeliLeavingAfterPickup
					: EVoiceoverType.SupportHeliLeavingNoPickup);
		}
		catch (OperationCanceledException)
		{
			ReturnToPoolSafely(requestGeneration);
		}
		finally
		{
			DestroyLandingPoint(requestLandingPoint);
		}
	}

	private async UniTask<bool> WaitForAvailableHelicopterWindow(
		float durationSeconds,
		HeliCargoTransferPoint cargoTransferPoint,
		CancellationToken cancellationToken)
	{
		float elapsedSeconds = 0f;
		while (elapsedSeconds < durationSeconds)
		{
			await UniTask.Yield(cancellationToken: cancellationToken);
			if (TryBeginImmediateCargoDeparture(cargoTransferPoint))
			{
				return true;
			}

			if (cargoTransferPoint == null ||
			    (!cargoTransferPoint.IsItemTransferOpen &&
			     !cargoTransferPoint.IsSuccessfulTransferPending))
			{
				elapsedSeconds += Time.deltaTime;
			}
		}

		return TryBeginImmediateCargoDeparture(cargoTransferPoint);
	}

	private async UniTask<bool> WaitForAuthoritativeCargoDeparture(
		CancellationToken cancellationToken)
	{
		float elapsedSeconds = 0f;
		while (!_cargoDepartureRequested &&
		       elapsedSeconds < RemoteCargoDepartureFallbackSeconds)
		{
			await UniTask.Yield(cancellationToken: cancellationToken);
			elapsedSeconds += Time.deltaTime;
		}

		return _cargoDepartureRequested && _cargoTransferSucceeded;
	}

	private bool TryBeginImmediateCargoDeparture(
		HeliCargoTransferPoint cargoTransferPoint)
	{
		if (_requestSupportType != ESupportType.PriorityExfil)
		{
			return false;
		}

		if (!_cargoDepartureRequested &&
		    cargoTransferPoint?.HasCompletedTransfer == true)
		{
			_cargoDepartureRequested = true;
			_cargoTransferSucceeded = true;
			PublishLocalCargoDeparture(
				cargoTransferPoint,
				successfulTransfer: true);
		}

		return _cargoDepartureRequested;
	}

	private void PublishLocalCargoDeparture(
		HeliCargoTransferPoint cargoTransferPoint,
		bool successfulTransfer)
	{
		if (_cargoDeparturePublished)
		{
			return;
		}

		string requesterProfileId =
			cargoTransferPoint?.CompletedRequesterProfileId;
		if (string.IsNullOrWhiteSpace(requesterProfileId))
		{
			requesterProfileId =
				_gameWorld?.MainPlayer?.ProfileId?.Trim() ??
				string.Empty;
		}

		if (string.IsNullOrWhiteSpace(_supportRequestId) ||
		    string.IsNullOrWhiteSpace(requesterProfileId))
		{
			return;
		}

		if (string.IsNullOrWhiteSpace(
			    _pendingCargoDepartureRequesterProfileId))
		{
			_pendingCargoDepartureRequesterProfileId =
				requesterProfileId;
			_pendingCargoDepartureSuccessfulTransfer =
				successfulTransfer;
		}

		if (_cargoDeparturePublishAttempts == 0 ||
		    Time.unscaledTime >= _nextCargoDeparturePublishTime)
		{
			TryPublishPendingCargoDeparture();
		}
	}

	private void RetryPendingCargoDeparturePublication()
	{
		if (_cargoDeparturePublished ||
		    _cargoDeparturePublishAttempts <= 0 ||
		    _cargoDeparturePublishAttempts >=
		    CargoDeparturePublishMaxAttempts ||
		    string.IsNullOrWhiteSpace(
			    _pendingCargoDepartureRequesterProfileId) ||
		    Time.unscaledTime < _nextCargoDeparturePublishTime)
		{
			return;
		}

		TryPublishPendingCargoDeparture();
	}

	private void TryPublishPendingCargoDeparture()
	{
		if (_cargoDeparturePublished ||
		    _cargoDeparturePublishAttempts >=
		    CargoDeparturePublishMaxAttempts)
		{
			return;
		}

		_cargoDeparturePublishAttempts++;
		_cargoDeparturePublished =
			Uh60CargoDepartureNetworking.TryPublishDeparture(
				_supportRequestId,
				_pendingCargoDepartureRequesterProfileId,
				_pendingCargoDepartureSuccessfulTransfer);
		if (_cargoDeparturePublished)
		{
			return;
		}

		_nextCargoDeparturePublishTime =
			Time.unscaledTime +
			CargoDeparturePublishRetrySeconds;
		if (_cargoDeparturePublishAttempts >=
		    CargoDeparturePublishMaxAttempts)
		{
			FireSupportPlugin.LogSource?.LogWarning(
				$"TSC UH-60 cargo departure broadcast was not queued after {CargoDeparturePublishMaxAttempts} attempts; remote visuals will use their safety fallback.");
		}
	}

	private void OnRemoteCargoDepartureReceived(
		string supportRequestId,
		bool successfulTransfer)
	{
		if (_requestSupportType != ESupportType.PriorityExfil ||
		    string.IsNullOrWhiteSpace(_supportRequestId) ||
		    !string.Equals(
			    _supportRequestId,
			    supportRequestId,
			    StringComparison.Ordinal))
		{
			return;
		}

		_cargoDepartureRequested = true;
		_cargoTransferSucceeded = successfulTransfer;
	}

	[UsedImplicitly]
	private void OnHelicopterLeft()
	{
		ReturnToPoolSafely();
	}

	protected override void OnDisable()
	{
		CancelRequestLifetime();
		DestroyLandingPoint();
		base.OnDisable();
	}

	private void OnDestroy()
	{
		Uh60CargoDepartureNetworking.RemoteDepartureReceived -=
			OnRemoteCargoDepartureReceived;
		CancelRequestLifetime();
		DestroyLandingPoint();
	}

	private GameObject CreateLandingPoint(
		ESupportType requestSupportType,
		HelicopterTimingSnapshot timingSnapshot,
		CancellationToken cancellationToken)
	{
		if (requestSupportType == ESupportType.PriorityExfil)
		{
			return CreateCargoTransferPoint(cancellationToken);
		}

		return CreateExtractionPoint(
			timingSnapshot,
			cancellationToken);
	}

	private GameObject CreateCargoTransferPoint(
		CancellationToken cancellationToken)
	{
		GameObject cargoPoint =
			CreateLandingPointObject("HeliCargoTransferPoint");
		HeliCargoTransferPoint transferPoint =
			cargoPoint.AddComponent<HeliCargoTransferPoint>();
		transferPoint.Initialize(cancellationToken);
		return cargoPoint;
	}

	private GameObject CreateExtractionPoint(
		HelicopterTimingSnapshot timingSnapshot,
		CancellationToken cancellationToken)
	{
		GameObject extractionPoint =
			CreateLandingPointObject("HeliExfilPoint");
		HeliExfiltrationPoint exfiltrationPoint =
			extractionPoint.AddComponent<HeliExfiltrationPoint>();
		exfiltrationPoint.Initialize(
			timingSnapshot.ExtractTimeSeconds,
			cancellationToken);
		return extractionPoint;
	}

	private GameObject CreateLandingPointObject(string pointName)
	{
		var landingPoint = new GameObject
		{
			name = pointName,
			layer = 13,
			transform =
			{
				position = transform.position,
				eulerAngles = new Vector3(-90, 0, 0),
			}
		};
		var landingCollider = landingPoint.AddComponent<BoxCollider>();
		landingCollider.size = new Vector3(16.5f, 20f, 15);
		landingCollider.isTrigger = true;
		return landingPoint;
	}

	private void ReturnToPoolSafely(int expectedRequestGeneration = -1)
	{
		if (expectedRequestGeneration >= 0 &&
		    expectedRequestGeneration != _requestGeneration)
		{
			return;
		}

		if (_returningToPool)
		{
			return;
		}

		_returningToPool = true;
		_requestGeneration++;
		CancelRequestLifetime();
		DestroyLandingPoint();
		_cancellationToken = CancellationToken.None;
		_requestSupportType = ESupportType.Extract;
		_timingSnapshot = default;
		_hasTimingSnapshot = false;
		_allowLocalServicePoint = true;
		_supportRequestId = string.Empty;
		_cargoDepartureRequested = false;
		_cargoDeparturePublished = false;
		_cargoTransferSucceeded = false;
		_cargoDeparturePublishAttempts = 0;
		_nextCargoDeparturePublishTime = 0f;
		_pendingCargoDepartureRequesterProfileId = string.Empty;
		_pendingCargoDepartureSuccessfulTransfer = false;
		ReturnToPool();
	}

	private void DestroyLandingPoint()
	{
		DestroyLandingPoint(_landingPoint);
	}

	private void DestroyLandingPoint(GameObject landingPoint)
	{
		if (landingPoint == null)
		{
			return;
		}

		Destroy(landingPoint);
		if (_landingPoint == landingPoint)
		{
			_landingPoint = null;
		}
	}

	private void CancelRequestLifetime()
	{
		_requestCts?.Cancel();
		_requestCts?.Dispose();
		_requestCts = null;
	}
}
