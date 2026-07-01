using Cysharp.Threading.Tasks;
using System;
using System.Threading;
using UnityEngine;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public sealed class JetStrafeService(FireSupportSpotter spotter, int maxRequests) : FireSupportService(maxRequests)
{
	public override ESupportType SupportType => ESupportType.Strafe;

	public override async UniTaskVoid PlanRequest(CancellationToken cancellationToken)
	{
		SetLocationResult locationResult = await spotter.SetLocation(checkSpace: false, cancellationToken);

		if (!locationResult.Success) return;

		SetDirectionResult directionResult = await spotter.SetSupportDirection(cancellationToken);

		if (directionResult.Success)
		{
			await spotter.ConfirmLocation(cancellationToken);
			FireSupportAuthorizationUse authorizationUse =
				await FireSupportPayment.TryPayForDeploymentAsync(SupportType);
			if (!authorizationUse.Ok)
			{
				return;
			}

			ConfirmRequest(
					strafeStartPos: directionResult.StartPosition,
					strafeEndPos: directionResult.EndPosition,
					authorizationUse: authorizationUse,
					cancellationToken)
				.Forget();
		}
	}

	private async UniTaskVoid ConfirmRequest(Vector3 strafeStartPos, Vector3 strafeEndPos,
		FireSupportAuthorizationUse authorizationUse,
		CancellationToken cancellationToken)
	{
		requestAvailable = false;
		bool consumedBaseRequest = !authorizationUse.ConsumedAuthorization;
		if (consumedBaseRequest)
		{
			availableRequests--;
		}

		FireSupportController.Instance.CanCallSupport(false);

		FireSupportController.Instance
			.StartCooldown(FireSupportTuningSettings.GetRequestCooldown(), cancellationToken, OnCooldownOver)
			.Forget();
		FireSupportAudio.Instance.PlayVoiceover(EVoiceoverType.StationStrafeRequest);
		await UniTask.WaitForSeconds(8f, cancellationToken: cancellationToken);

		Vector3 pos = (strafeStartPos + strafeEndPos) / 2;
		Vector3 dir = (strafeEndPos - strafeStartPos).normalized;
		bool doublePass = authorizationUse.ConsumedAuthorizationType == ESupportType.DoubleStrafe;
		bool success = await ExecuteStrafePass(pos, dir, passIndex: 0, cancellationToken: cancellationToken);
		if (success)
		{
			FireSupportPayment.CommitConsumedAuthorization(authorizationUse);
		}

		if (success && doublePass && !cancellationToken.IsCancellationRequested)
		{
			float delay = Mathf.Max(0f, FireSupportTuningSettings.GetDoubleStrafeSecondPassDelay());
			FireSupportPlugin.LogSource.LogInfo($"A-10 double pass authorized; second pass in {delay:0.0}s.");
			await UniTask.WaitForSeconds(delay, cancellationToken: cancellationToken);
			success = await ExecuteStrafePass(pos, -dir, passIndex: 1, cancellationToken: cancellationToken);
		}

		if (!success && authorizationUse.ConsumedAuthorization)
		{
			FireSupportPayment.RefundConsumedAuthorization(authorizationUse);
		}

		if (!success)
		{
			if (consumedBaseRequest)
			{
				availableRequests++;
			}

			requestAvailable = true;
			FireSupportController.Instance.CanCallSupport(true);
		}
	}

	private static async UniTask<bool> ExecuteStrafePass(
		Vector3 position,
		Vector3 direction,
		int passIndex,
		CancellationToken cancellationToken)
	{
		FireSupportAudio.Instance.PlayVoiceover(EVoiceoverType.JetArriving);
		int visualSeed = Environment.TickCount;
		if (FireSupportNetworking.TryHandleSupportRequest(
			    ESupportType.Strafe,
			    position,
			    direction,
			    Vector3.zero,
			    cancellationToken,
			    passIndex: passIndex))
		{
			return true;
		}

		return await FireSupportRuntime.TryProcessRequest(
			ESupportType.Strafe,
			position,
			direction,
			Vector3.zero,
			visualOnly: false,
			visualSeed: visualSeed,
			cancellationToken: cancellationToken,
			passIndex: passIndex);
	}

	private void OnCooldownOver()
	{
		requestAvailable = true;
	}
}
