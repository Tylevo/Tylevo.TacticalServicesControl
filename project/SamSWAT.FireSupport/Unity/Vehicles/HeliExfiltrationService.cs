using Cysharp.Threading.Tasks;
using System;
using System.Threading;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public sealed class HeliExfiltrationService(FireSupportSpotter spotter, int maxRequests)
	: FireSupportService(maxRequests)
{
	public override ESupportType SupportType => ESupportType.Extract;

	public override async UniTaskVoid PlanRequest(CancellationToken cancellationToken)
	{
		SetLocationResult locationResult = await spotter.SetLocation(checkSpace: true, cancellationToken);

		if (locationResult.Success)
		{
			await spotter.ConfirmLocation(cancellationToken);
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
		if (consumedBaseRequest)
		{
			availableRequests--;
		}

		FireSupportController.Instance.CanCallSupport(false);

		FireSupportAudio.Instance.PlayVoiceover(EVoiceoverType.StationExtractionRequest);
		await UniTask.WaitForSeconds(GetDispatchDelay(effectiveSupportType), cancellationToken: cancellationToken);

		var randomEulerAngles = new Vector3(0, Random.Range(0, 360), 0);
		if (!FireSupportNetworking.TryHandleSupportRequest(
			    effectiveSupportType,
			    position,
			    Vector3.zero,
			    randomEulerAngles,
			    cancellationToken))
		{
			bool success = await FireSupportRuntime.TryProcessRequest(
				effectiveSupportType,
				position,
				Vector3.zero,
				randomEulerAngles,
				visualOnly: false,
				visualSeed: 0,
				cancellationToken: cancellationToken);
			if (!success)
			{
				if (authorizationUse.ConsumedAuthorization)
				{
					FireSupportPayment.RefundConsumedAuthorization(authorizationUse);
				}

				if (consumedBaseRequest)
				{
					availableRequests++;
				}

				requestAvailable = true;
				FireSupportController.Instance.CanCallSupport(true);
				return;
			}

			FireSupportAudio.Instance.PlayVoiceover(EVoiceoverType.SupportHeliArrivingToPickup);
		}

		FireSupportPayment.CommitConsumedAuthorization(authorizationUse);

		await UniTask.WaitForSeconds(GetCompletionDelay(effectiveSupportType),
			cancellationToken: cancellationToken);

		FireSupportController.Instance
			.StartCooldown(FireSupportTuningSettings.GetRequestCooldown(), cancellationToken, OnCooldownOver)
			.Forget();
	}

	private void OnCooldownOver()
	{
		requestAvailable = true;
	}

	private static float GetDispatchDelay(ESupportType supportType)
	{
		return supportType == ESupportType.PriorityExfil
			? FireSupportTuningSettings.GetPriorityExfilDispatchDelay()
			: 8f;
	}

	private static float GetCompletionDelay(ESupportType supportType)
	{
		return supportType == ESupportType.PriorityExfil
			? 25f + FireSupportTuningSettings.GetHelicopterWaitTime(ESupportType.PriorityExfil)
			: 35f + FireSupportTuningSettings.GetHelicopterWaitTime(ESupportType.Extract);
	}
}
