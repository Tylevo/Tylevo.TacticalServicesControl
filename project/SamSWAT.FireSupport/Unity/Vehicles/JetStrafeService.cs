using Cysharp.Threading.Tasks;
using System;
using System.Threading;
using UnityEngine;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public sealed class JetStrafeService(
	FireSupportSpotter spotter,
	int maxRequests,
	ESupportType supportType = ESupportType.Strafe) : FireSupportService(maxRequests)
{
	public override ESupportType SupportType => supportType;

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
		ESupportType effectiveSupportType = authorizationUse.ConsumedAuthorization
			? authorizationUse.ConsumedAuthorizationType
			: SupportType;
		if (consumedBaseRequest)
		{
			availableRequests--;
		}

		bool doublePass = effectiveSupportType == ESupportType.DoubleStrafe;
		bool firstPassAccepted = false;
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
			FireSupportAudio.Instance.PlayVoiceover(EVoiceoverType.StationStrafeRequest);
			await UniTask.WaitForSeconds(8f, cancellationToken: cancellationToken);

			Vector3 pos = (strafeStartPos + strafeEndPos) / 2;
			Vector3 dir = (strafeEndPos - strafeStartPos).normalized;
			FireSupportNetworkRequestResult firstPass = await ExecuteStrafePass(
				effectiveSupportType,
				pos,
				dir,
				passIndex: 0,
				BuildDispatchRequestId(authorizationUse, 0),
				cancellationToken);
			if (!firstPass.Accepted)
			{
				failureFinalized = true;
				await RestoreFailedDispatch(
					authorizationUse,
					consumedBaseRequest,
					firstPass);
				return;
			}

			firstPassAccepted = true;
			FireSupportPayment.CommitConsumedAuthorization(authorizationUse);
			TryPlayJetArrivalAudio();
			controller
				.StartCooldown(FireSupportTuningSettings.GetRequestCooldown(), cancellationToken, OnCooldownOver)
				.Forget();

			if (doublePass && !cancellationToken.IsCancellationRequested)
			{
				float delay = Mathf.Max(0f, FireSupportTuningSettings.GetDoubleStrafeSecondPassDelay());
				FireSupportPlugin.LogSource.LogInfo($"A-10 double pass authorized; second pass in {delay:0.0}s.");
				await UniTask.WaitForSeconds(delay, cancellationToken: cancellationToken);
				FireSupportNetworkRequestResult secondPass = await ExecuteStrafePass(
					effectiveSupportType,
					pos,
					-dir,
					passIndex: 1,
					BuildDispatchRequestId(authorizationUse, 1),
					cancellationToken);
				if (!secondPass.Accepted)
				{
					// Pass zero has already been accepted and delivered, so the
					// authorization is terminally committed. A failed second pass
					// is observable but cannot refund an already-delivered strike.
					FireSupportPlugin.LogSource.LogWarning(
						$"A-10 double-pass second pass did not start. state={secondPass.State}, reason={secondPass.Reason}, requestId={authorizationUse.RequestId}.");
				}
				else
				{
					TryPlayJetArrivalAudio();
				}
			}
		}
		catch (OperationCanceledException)
		{
			if (!firstPassAccepted && !failureFinalized)
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
			if (!firstPassAccepted && !failureFinalized)
			{
				await RestoreFailedDispatch(
					authorizationUse,
					consumedBaseRequest,
					FireSupportNetworkRequestResult.Reject("LocalDispatchException"));
			}
		}
	}

	private static async UniTask<FireSupportNetworkRequestResult> ExecuteStrafePass(
		ESupportType supportType,
		Vector3 position,
		Vector3 direction,
		int passIndex,
		string supportRequestId,
		CancellationToken cancellationToken)
	{
		FireSupportNetworkRequestResult networkResult =
			await FireSupportNetworking.TryHandleSupportRequestAsync(
				supportType,
				position,
				direction,
				Vector3.zero,
				cancellationToken,
				passIndex: passIndex,
				supportRequestId: supportRequestId);
		if (networkResult.Handled)
		{
			return networkResult;
		}

		if (cancellationToken.IsCancellationRequested)
		{
			return FireSupportNetworkRequestResult.Cancel();
		}

		bool localSuccess = await FireSupportRuntime.TryProcessRequest(
			supportType,
			position,
			direction,
			Vector3.zero,
			visualOnly: false,
			visualSeed: Environment.TickCount,
			cancellationToken: cancellationToken,
			passIndex: passIndex);
		return localSuccess
			? FireSupportNetworkRequestResult.Accept("LocalRuntimeStarted")
			: FireSupportNetworkRequestResult.Reject("LocalRuntimeStartFailed");
	}

	private static void TryPlayJetArrivalAudio()
	{
		try
		{
			FireSupportAudio.Instance?.PlayVoiceover(EVoiceoverType.JetArriving);
		}
		catch (Exception ex)
		{
			// Dispatch has already been accepted. Presentation failures are
			// observable, but can never roll back a delivered authorization.
			FireSupportPlugin.LogSource?.LogWarning(
				$"A-10 arrival voiceover failed after dispatch acceptance. {ex}");
		}
	}

	private async UniTask RestoreFailedDispatch(
		FireSupportAuthorizationUse authorizationUse,
		bool consumedBaseRequest,
		FireSupportNetworkRequestResult result)
	{
		await FireSupportPayment.RefundConsumedAuthorizationAsync(authorizationUse);
		if (consumedBaseRequest)
		{
			availableRequests++;
		}

		requestAvailable = true;
		FireSupportController.Instance?.CanCallSupport(true);
		FireSupportPlugin.LogSource.LogWarning(
			$"A-10 dispatch did not start. state={result.State}, reason={result.Reason}, requestId={authorizationUse.RequestId}.");
	}

	private static string BuildDispatchRequestId(FireSupportAuthorizationUse authorizationUse, int passIndex)
	{
		string parentId = string.IsNullOrWhiteSpace(authorizationUse?.RequestId)
			? Guid.NewGuid().ToString("N")
			: authorizationUse.RequestId;
		return $"{parentId}:pass:{Mathf.Max(0, passIndex)}";
	}

	private void OnCooldownOver()
	{
		requestAvailable = true;
	}
}
