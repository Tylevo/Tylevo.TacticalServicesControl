using Comfort.Common;
using Cysharp.Threading.Tasks;
using EFT;
using EFT.Communications;
using System.Threading;
using UnityEngine;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public sealed class UavReconService(int maxRequests) : FireSupportService(maxRequests)
{
	public override ESupportType SupportType => ESupportType.Uav;

	public override UniTaskVoid PlanRequest(CancellationToken cancellationToken)
	{
		ConfirmRequest(cancellationToken).Forget();
		return default;
	}

	private async UniTaskVoid ConfirmRequest(CancellationToken cancellationToken)
	{
		requestAvailable = false;
		FireSupportController.Instance.CanCallSupport(false);

		FireSupportAuthorizationUse authorizationUse =
			await FireSupportPayment.TryPayForDeploymentAsync(SupportType);
		if (!authorizationUse.Ok)
		{
			FireSupportController.Instance.CanCallSupport(true);
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

		if (cancellationToken.IsCancellationRequested)
		{
			if (authorizationUse.ConsumedAuthorization)
			{
				FireSupportPayment.RefundConsumedAuthorization(authorizationUse);
			}

			FireSupportController.Instance.CanCallSupport(true);
			requestAvailable = true;
			if (consumedBaseRequest)
			{
				availableRequests++;
			}

			return;
		}

		if (UavDeviceActivationController.TryPlay(
			() => StartUavRecon(
				    effectiveSupportType,
				    durationSeconds,
				    scanInterval,
				    rangeMeters,
				    uavCenter,
				    publishActivationPhoneVisual,
				    authorizationUse,
				    cancellationToken),
			    cancellationToken))
		{
			if (publishActivationPhoneVisual)
			{
				UavPhoneVisualNetworkService.PublishLocal(
					effectiveSupportType,
					UavPhoneVisualPhase.StartActivationPhone,
					duration: 2.2f,
					cancellationToken: cancellationToken);
			}

			return;
		}

		FireSupportPlugin.LogSource.LogInfo("UAV activation device animation did not start; using immediate radar fallback.");
		UavWristPhoneController.Play(cancellationToken);
		if (publishActivationPhoneVisual)
		{
			UavPhoneVisualNetworkService.PublishLocal(
				effectiveSupportType,
				UavPhoneVisualPhase.StartActivationPhone,
				duration: 1.1f,
				cancellationToken: cancellationToken);
		}

		StartUavRecon(
			effectiveSupportType,
			durationSeconds,
			scanInterval,
			rangeMeters,
			uavCenter,
			publishActivationPhoneVisual,
			authorizationUse,
			cancellationToken);

		return;
	}

	private void StartUavRecon(
		ESupportType effectiveSupportType,
		int durationSeconds,
		float scanInterval,
		float rangeMeters,
		Vector3 uavCenter,
		bool publishActivationPhoneVisual,
		FireSupportAuthorizationUse authorizationUse,
		CancellationToken cancellationToken)
	{
		if (cancellationToken.IsCancellationRequested)
		{
			return;
		}

		if (publishActivationPhoneVisual)
		{
			UavPhoneVisualNetworkService.PublishLocal(
				effectiveSupportType,
				UavPhoneVisualPhase.Authorized,
				duration: 0.9f,
				success: true,
				cancellationToken: cancellationToken);
		}

		if (!FireSupportNetworking.TryHandleSupportRequest(
			    effectiveSupportType,
			    Vector3.zero,
			    Vector3.zero,
			    Vector3.zero,
			    cancellationToken,
			    durationSeconds))
		{
			UavReconOverlay.Activate(
				durationSeconds,
				cancellationToken,
				playActivationVisual: false,
				scanInterval,
				rangeMeters);
		}

		UavAircraftLoiterController.StartConfigured(uavCenter, durationSeconds, cancellationToken);
		FireSupportPayment.CommitConsumedAuthorization(authorizationUse);

		NotificationManagerClass.DisplayMessageNotification(
			$"{FireSupportPayment.GetSupportName(effectiveSupportType)} active for {durationSeconds}s.",
			ENotificationDurationType.Default,
			ENotificationIconType.Default,
			null);

		FireSupportController.Instance
			.StartCooldown(FireSupportTuningSettings.GetRequestCooldown(), cancellationToken, OnCooldownOver)
			.Forget();
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
