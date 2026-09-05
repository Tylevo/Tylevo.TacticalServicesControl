using Comfort.Common;
using Cysharp.Threading.Tasks;
using EFT;
using EFT.Communications;
using System;
using System.Threading;
using UnityEngine;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public sealed class UavReconService(
	int maxRequests,
	ESupportType supportType = ESupportType.Uav) : FireSupportService(maxRequests)
{
	public override ESupportType SupportType => supportType;

	public override UniTaskVoid PlanRequest(CancellationToken cancellationToken)
	{
		ConfirmRequest(cancellationToken).Forget();
		return default;
	}

	private async UniTaskVoid ConfirmRequest(CancellationToken cancellationToken)
	{
		if (UavReconOverlay.TryGetSessionSnapshot(out UavReconOverlay.ReconSessionSnapshot activeRecon))
		{
			int secondsRemaining = Mathf.CeilToInt(activeRecon.RemainingSeconds);
			NotificationManager.DisplayWarningNotification(
				$"RECON LINK ACTIVE - {secondsRemaining / 60:00}:{secondsRemaining % 60:00} remaining.",
				ENotificationDurationType.Default);
			return;
		}

		requestAvailable = false;
		FireSupportController controller = FireSupportController.Instance;
		if (controller == null)
		{
			requestAvailable = true;
			FireSupportPlugin.LogSource.LogWarning(
				"UAV request skipped: fire support controller was unavailable.");
			return;
		}

		controller.CanCallSupport(false);

		FireSupportAuthorizationUse authorizationUse =
			await FireSupportPayment.TryPayForDeploymentAsync(SupportType);
		if (!authorizationUse.Ok)
		{
			controller.CanCallSupport(true);
			requestAvailable = true;
			return;
		}

		bool consumedBaseRequest = !authorizationUse.ConsumedAuthorization;
		if (consumedBaseRequest)
		{
			availableRequests--;
		}

		ESupportType effectiveSupportType = authorizationUse.ConsumedAuthorization
			? authorizationUse.ConsumedAuthorizationType
			: SupportType;
		int durationSeconds = UavReconSettings.GetDurationSeconds(effectiveSupportType);
		float scanInterval = UavReconSettings.GetScanInterval(effectiveSupportType);
		float rangeMeters = UavReconSettings.GetRangeMeters(effectiveSupportType);
		Vector3 uavCenter = GetUavCenter();
		bool publishActivationPhoneVisual = authorizationUse.ConsumedAuthorization;
		int terminalState = 0;

		if (cancellationToken.IsCancellationRequested)
		{
			await FinalizeFailedDispatch(
				authorizationUse,
				consumedBaseRequest,
				FireSupportNetworkRequestResult.Cancel());
			return;
		}

		if (UavDeviceActivationController.TryPlay(
			    activationToken => StartUavReconAsync(
				    playWristFallback: false,
				    activationToken),
			    () => CancelActivationAsync().Forget(),
			    cancellationToken))
		{
			return;
		}

		FireSupportPlugin.LogSource.LogInfo("UAV activation device animation did not start; using immediate radar fallback.");
		await StartUavReconAsync(
			playWristFallback: true,
			cancellationToken);
		return;

		async UniTask CancelActivationAsync()
		{
			await FinalizeFailedDispatch(
				authorizationUse,
				consumedBaseRequest,
				FireSupportNetworkRequestResult.Cancel());
		}

		async UniTask<bool> StartUavReconAsync(
			bool playWristFallback,
			CancellationToken dispatchCancellationToken)
		{
			if (Volatile.Read(ref terminalState) != 0)
			{
				return false;
			}

			try
			{
				dispatchCancellationToken.ThrowIfCancellationRequested();

				FireSupportNetworkRequestResult dispatchResult =
					await FireSupportNetworking.TryHandleSupportRequestAsync(
						effectiveSupportType,
						uavCenter,
						Vector3.zero,
						Vector3.zero,
						dispatchCancellationToken,
						durationSeconds,
						supportRequestId: BuildDispatchRequestId(authorizationUse));
				bool networkAuthorityHandled = dispatchResult.Handled;
				if (!dispatchResult.Handled)
				{
					if (dispatchCancellationToken.IsCancellationRequested)
					{
						dispatchResult = FireSupportNetworkRequestResult.Cancel();
					}
					else
					{
						UavReconOverlay.Activate(
							durationSeconds,
							cancellationToken,
							playActivationVisual: false,
							scanInterval,
							rangeMeters);
						dispatchResult = FireSupportNetworkRequestResult.Accept(
							"LocalRuntimeStarted",
							durationSeconds,
							scanInterval,
							rangeMeters);
					}
				}

				if (!dispatchResult.Accepted)
				{
					await FinalizeFailedDispatch(authorizationUse, consumedBaseRequest, dispatchResult);
					return false;
				}

				if (Interlocked.CompareExchange(ref terminalState, 1, 0) != 0)
				{
					return false;
				}

				FireSupportPayment.CommitConsumedAuthorization(authorizationUse);
				float acceptedDurationSeconds = dispatchResult.DurationSeconds > 0f
					? dispatchResult.DurationSeconds
					: durationSeconds;

				// Presentation and the cosmetic loiter are downstream of authority
				// acceptance. The physical phone remains on its authorizing screen
				// while the async authority request is pending.
				if (playWristFallback)
				{
					UavWristPhoneController.Play(cancellationToken);
				}

				if (publishActivationPhoneVisual)
				{
					UavPhoneVisualNetworkService.PublishLocal(
						effectiveSupportType,
						UavPhoneVisualPhase.StartActivationPhone,
						duration: playWristFallback ? 1.1f : 2.2f,
						cancellationToken: cancellationToken);
					UavPhoneVisualNetworkService.PublishLocal(
						effectiveSupportType,
						UavPhoneVisualPhase.Authorized,
						duration: 0.9f,
						success: true,
						cancellationToken: cancellationToken);
				}

				// A Fika authority publishes one request-bound loiter event from
				// its accepted outcome. Starting another one here let a client
				// originate an unauthenticated duplicate. Solo still owns its
				// local cosmetic presentation.
				if (!networkAuthorityHandled)
				{
					UavAircraftLoiterController.StartConfigured(
						uavCenter,
						acceptedDurationSeconds,
						cancellationToken);
				}

				NotificationManager.DisplayMessageNotification(
					$"{FireSupportPayment.GetSupportName(effectiveSupportType)} active for {acceptedDurationSeconds:0.#}s.",
					ENotificationDurationType.Default,
					ENotificationIconType.Default,
					null);

				controller
					.StartCooldown(FireSupportTuningSettings.GetRequestCooldown(), cancellationToken, OnCooldownOver)
					.Forget();
				return true;
			}
			catch (OperationCanceledException)
			{
				if (Volatile.Read(ref terminalState) == 1)
				{
					return true;
				}

				await FinalizeFailedDispatch(
					authorizationUse,
					consumedBaseRequest,
					FireSupportNetworkRequestResult.Cancel());
				return false;
			}
			catch (Exception ex)
			{
				FireSupportPlugin.LogSource.LogError(ex);
				if (Volatile.Read(ref terminalState) == 1)
				{
					// Authority already accepted. A downstream presentation
					// failure cannot turn that terminal commit into a refund.
					return true;
				}

				await FinalizeFailedDispatch(
					authorizationUse,
					consumedBaseRequest,
					FireSupportNetworkRequestResult.Reject("LocalDispatchException"));
				return false;
			}
		}

		async UniTask FinalizeFailedDispatch(
			FireSupportAuthorizationUse use,
			bool restoreBaseRequest,
			FireSupportNetworkRequestResult result)
		{
			if (Interlocked.CompareExchange(ref terminalState, 2, 0) != 0)
			{
				return;
			}

			await FireSupportPayment.RefundConsumedAuthorizationAsync(use);
			if (restoreBaseRequest)
			{
				availableRequests++;
			}

			requestAvailable = true;
			FireSupportController.Instance?.CanCallSupport(true);
			FireSupportPlugin.LogSource.LogWarning(
				$"UAV dispatch did not start. state={result.State}, reason={result.Reason}, requestId={use.RequestId}.");
		}
	}

	private static string BuildDispatchRequestId(FireSupportAuthorizationUse authorizationUse)
	{
		string parentId = string.IsNullOrWhiteSpace(authorizationUse?.RequestId)
			? Guid.NewGuid().ToString("N")
			: authorizationUse.RequestId;
		return $"{parentId}:pass:0";
	}

	private void OnCooldownOver()
	{
		requestAvailable = true;
	}

	private static Vector3 GetUavCenter()
	{
		GameWorld gameWorld = Singleton<GameWorld>.Instance;
		Player player = gameWorld?.MainPlayer;
		return player?.Transform != null ? player.Transform.position : Vector3.zero;
	}
}
