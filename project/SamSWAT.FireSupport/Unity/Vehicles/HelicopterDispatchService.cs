using Cysharp.Threading.Tasks;
using System;
using System.Threading;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

/// <summary>
/// Shared request, payment, and dispatch lifecycle for UH-60 services.
/// Product-specific behavior belongs to the concrete extraction and cargo
/// services rather than being selected inside an extraction service.
/// </summary>
public abstract class HelicopterDispatchService : FireSupportService
{
	private readonly FireSupportSpotter _spotter;

	protected HelicopterDispatchService(
		FireSupportSpotter spotter,
		int maxRequests)
		: base(maxRequests)
	{
		_spotter = spotter;
	}

	public override async UniTaskVoid PlanRequest(
		CancellationToken cancellationToken)
	{
		SetLocationResult locationResult =
			await _spotter.SetLocation(checkSpace: true, cancellationToken);

		if (!locationResult.Success)
		{
			return;
		}

		await _spotter.ConfirmLocation(cancellationToken);
		FireSupportAuthorizationUse authorizationUse =
			await FireSupportPayment.TryPayForDeploymentAsync(SupportType);
		if (!authorizationUse.Ok)
		{
			return;
		}

		ConfirmRequest(
				locationResult.TargetLocation,
				authorizationUse,
				cancellationToken)
			.Forget();
	}

	protected virtual void PlayRequestAcknowledgement()
	{
	}

	private async UniTaskVoid ConfirmRequest(
		Vector3 position,
		FireSupportAuthorizationUse authorizationUse,
		CancellationToken cancellationToken)
	{
		requestAvailable = false;
		bool consumedBaseRequest = !authorizationUse.ConsumedAuthorization;
		ESupportType effectiveSupportType = authorizationUse.ConsumedAuthorization
			? authorizationUse.ConsumedAuthorizationType
			: SupportType;
		HelicopterTimingSnapshot timingSnapshot =
			FireSupportTuningSettings.CaptureHelicopterTiming(effectiveSupportType);
		if (consumedBaseRequest)
		{
			availableRequests--;
		}

		bool accepted = false;
		bool failureFinalized = false;
		try
		{
			cancellationToken.ThrowIfCancellationRequested();
			FireSupportController controller = FireSupportController.Instance;
			if (controller == null)
			{
				throw new InvalidOperationException(
					"Fire support controller was unavailable after payment.");
			}

			controller.CanCallSupport(false);
			PlayRequestAcknowledgement();

			var randomEulerAngles =
				new Vector3(0, Random.Range(0, 360), 0);
			string supportRequestId =
				BuildDispatchRequestId(authorizationUse);
			bool playLocalArrivalAudio = false;
			FireSupportNetworkRequestResult dispatchResult =
				await FireSupportNetworking.TryHandleSupportRequestAsync(
					effectiveSupportType,
					position,
					Vector3.zero,
					randomEulerAngles,
					cancellationToken,
					supportRequestId: supportRequestId,
					helicopterTimingSnapshot: timingSnapshot);
			if (!dispatchResult.Handled)
			{
				if (cancellationToken.IsCancellationRequested)
				{
					dispatchResult = FireSupportNetworkRequestResult.Cancel();
				}
				else
				{
					await UniTask.WaitForSeconds(
						timingSnapshot.DispatchDelaySeconds,
						cancellationToken: cancellationToken);
					bool localSuccess =
						await FireSupportRuntime.TryProcessRequest(
							effectiveSupportType,
							position,
							Vector3.zero,
							randomEulerAngles,
							visualOnly: false,
							visualSeed: 0,
							cancellationToken: cancellationToken,
							helicopterTimingSnapshot: timingSnapshot,
							supportRequestId: supportRequestId);
					dispatchResult = localSuccess
						? FireSupportNetworkRequestResult.Accept(
							"LocalRuntimeStarted")
						: FireSupportNetworkRequestResult.Reject(
							"LocalRuntimeStartFailed");
					playLocalArrivalAudio = localSuccess;
				}
			}

			if (!dispatchResult.Accepted)
			{
				failureFinalized = true;
				await RestoreFailedDispatch(
					authorizationUse,
					consumedBaseRequest,
					dispatchResult);
				return;
			}

			accepted = true;
			FireSupportPayment.CommitConsumedAuthorization(authorizationUse);
			if (playLocalArrivalAudio)
			{
				TryPlayHeliArrivalAudio();
			}

			await UniTask.WaitForSeconds(
				GetCompletionDelay(timingSnapshot),
				cancellationToken: cancellationToken);

			controller
				.StartCooldown(
					FireSupportTuningSettings.GetRequestCooldown(),
					cancellationToken,
					OnCooldownOver)
				.Forget();
		}
		catch (OperationCanceledException)
		{
			if (!accepted && !failureFinalized)
			{
				await RestoreFailedDispatch(
					authorizationUse,
					consumedBaseRequest,
					FireSupportNetworkRequestResult.Cancel());
			}
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource.LogError(ex);
			if (!accepted && !failureFinalized)
			{
				await RestoreFailedDispatch(
					authorizationUse,
					consumedBaseRequest,
					FireSupportNetworkRequestResult.Reject(
						"LocalDispatchException"));
			}
		}
	}

	private static void TryPlayHeliArrivalAudio()
	{
		try
		{
			FireSupportAudio.Instance?.PlayVoiceover(
				EVoiceoverType.SupportHeliArrivingToPickup);
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource?.LogWarning(
				$"UH-60 arrival voiceover failed after dispatch acceptance. {ex}");
		}
	}

	private async UniTask RestoreFailedDispatch(
		FireSupportAuthorizationUse authorizationUse,
		bool consumedBaseRequest,
		FireSupportNetworkRequestResult result)
	{
		await FireSupportPayment.RefundConsumedAuthorizationAsync(
			authorizationUse);
		if (consumedBaseRequest)
		{
			availableRequests++;
		}

		requestAvailable = true;
		FireSupportController.Instance?.CanCallSupport(true);
		FireSupportPlugin.LogSource.LogWarning(
			$"UH-60 dispatch did not start. state={result.State}, reason={result.Reason}, requestId={authorizationUse.RequestId}.");
	}

	private static string BuildDispatchRequestId(
		FireSupportAuthorizationUse authorizationUse)
	{
		string parentId =
			string.IsNullOrWhiteSpace(authorizationUse?.RequestId)
				? Guid.NewGuid().ToString("N")
				: authorizationUse.RequestId;
		return $"{parentId}:pass:0";
	}

	private void OnCooldownOver()
	{
		requestAvailable = true;
	}

	private static float GetCompletionDelay(
		HelicopterTimingSnapshot timingSnapshot)
	{
		const float baseArrivalAnimationSeconds = 35f;
		float speedMultiplier =
			Mathf.Max(0.01f, timingSnapshot.SpeedMultiplier);
		return baseArrivalAnimationSeconds / speedMultiplier +
		       timingSnapshot.WaitTimeSeconds;
	}
}
