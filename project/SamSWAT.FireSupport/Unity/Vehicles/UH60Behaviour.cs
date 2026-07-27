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
	private bool _allowLocalExtraction = true;
	private GameObject _extractionPoint;
	private bool _returningToPool;
	private int _requestGeneration;

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
				allowLocalExtraction: true);
		}

		Transform heliTransform = transform;
		heliTransform.position = position;
		heliTransform.eulerAngles = rotation;
		helicopterAnimator.SetFloat(s_flySpeedMultiplier, _timingSnapshot.SpeedMultiplier);
	}

	public void SetPriorityExfil(bool priorityExfil)
	{
		SetRequestTiming(
			priorityExfil ? ESupportType.PriorityExfil : ESupportType.Extract,
			null,
			allowLocalExtraction: true);
	}

	public void SetRequestTiming(
		ESupportType supportType,
		HelicopterTimingSnapshot? timingSnapshot,
		bool allowLocalExtraction)
	{
		_requestSupportType = supportType == ESupportType.PriorityExfil
			? ESupportType.PriorityExfil
			: ESupportType.Extract;
		_timingSnapshot = timingSnapshot ??
			FireSupportTuningSettings.CaptureHelicopterTiming(_requestSupportType);
		_allowLocalExtraction = allowLocalExtraction;
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
		bool allowLocalExtraction = _allowLocalExtraction;
		GameObject requestExtractionPoint = null;
		try
		{
			FireSupportAudio.Instance.PlayVoiceover(EVoiceoverType.SupportHeliPickingUp);
			DestroyExtractionPoint();
			if (allowLocalExtraction)
			{
				requestExtractionPoint = CreateExfilPoint(
					requestSupportType,
					timingSnapshot,
					requestCancellationToken);
				_extractionPoint = requestExtractionPoint;
			}

			int configWaitTime = timingSnapshot.WaitTimeSeconds;
			float waitTime = configWaitTime * 0.75f;

			await UniTask.WaitForSeconds(
				waitTime,
				cancellationToken: requestCancellationToken);
			if (requestGeneration != _requestGeneration)
			{
				return;
			}

			FireSupportAudio.Instance.PlayVoiceover(EVoiceoverType.SupportHeliHurry);

			await UniTask.WaitForSeconds(
				duration: configWaitTime - waitTime,
				cancellationToken: requestCancellationToken);
			if (requestGeneration != _requestGeneration)
			{
				return;
			}

			helicopterAnimator.SetTrigger(s_flyAway);
			FireSupportAudio.Instance.PlayVoiceover(EVoiceoverType.SupportHeliLeavingNoPickup);
		}
		catch (OperationCanceledException)
		{
			ReturnToPoolSafely(requestGeneration);
		}
		finally
		{
			DestroyExtractionPoint(requestExtractionPoint);
		}
	}

	[UsedImplicitly]
	private void OnHelicopterLeft()
	{
		ReturnToPoolSafely();
	}

	protected override void OnDisable()
	{
		CancelRequestLifetime();
		DestroyExtractionPoint();
		base.OnDisable();
	}

	private void OnDestroy()
	{
		CancelRequestLifetime();
		DestroyExtractionPoint();
	}

	private GameObject CreateExfilPoint(
		ESupportType requestSupportType,
		HelicopterTimingSnapshot timingSnapshot,
		CancellationToken cancellationToken)
	{
		var extractionPoint = new GameObject
		{
			name = "HeliExfilPoint",
			layer = 13,
			transform =
			{
				position = transform.position,
				eulerAngles = new Vector3(-90, 0, 0),
			}
		};
		var extractionCollider = extractionPoint.AddComponent<BoxCollider>();
		extractionCollider.size = new Vector3(16.5f, 20f, 15);
		extractionCollider.isTrigger = true;
		HeliExfiltrationPoint exfiltrationPoint =
			extractionPoint.AddComponent<HeliExfiltrationPoint>();
		exfiltrationPoint.Initialize(
			requestSupportType,
			timingSnapshot.ExtractTimeSeconds,
			cancellationToken);

		return extractionPoint;
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
		DestroyExtractionPoint();
		_cancellationToken = CancellationToken.None;
		_requestSupportType = ESupportType.Extract;
		_timingSnapshot = default;
		_hasTimingSnapshot = false;
		_allowLocalExtraction = true;
		ReturnToPool();
	}

	private void DestroyExtractionPoint()
	{
		DestroyExtractionPoint(_extractionPoint);
	}

	private void DestroyExtractionPoint(GameObject extractionPoint)
	{
		if (extractionPoint == null)
		{
			return;
		}

		Destroy(extractionPoint);
		if (_extractionPoint == extractionPoint)
		{
			_extractionPoint = null;
		}
	}

	private void CancelRequestLifetime()
	{
		_requestCts?.Cancel();
		_requestCts?.Dispose();
		_requestCts = null;
	}
}
