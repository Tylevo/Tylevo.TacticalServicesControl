using Cysharp.Threading.Tasks;
using EFT.Ballistics;
using System;
using System.Diagnostics;
using System.Threading;
using UnityEngine;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

/// <summary>
/// Best-effort observation of a few native EFT rounds. A planned tracer endpoint
/// is never reported as a measured hit, and pooled shots are never followed.
/// </summary>
public static class A10ShotDiagnostics
{
	private const float MaximumObservationSeconds = 12f;

	public static void Observe(
		Shot bullet,
		A10TracerSegment plan,
		string requestId,
		int pass,
		int index,
		CancellationToken cancellationToken)
	{
		ObserveAsync(bullet, plan, requestId, pass, index, cancellationToken).Forget();
	}

	private static async UniTask ObserveAsync(
		Shot bullet,
		A10TracerSegment plan,
		string requestId,
		int pass,
		int index,
		CancellationToken cancellationToken)
	{
		try
		{
			if (bullet == null)
			{
				LogUnavailable(requestId, pass, index, "shot-not-created");
				return;
			}

			// EFT returns finished shots to a pool. The weapon reuses its ammo
			// item, so its identity alone is insufficient to distinguish rounds.
			object ammo = bullet.Ammo;
			object weapon = bullet.Weapon;
			Shot parent = bullet.Parent;
			Vector3 startPosition = bullet.StartPosition;
			Vector3 startDirection = bullet.Direction;
			int randomSeed = bullet.RandomSeed;
			string owner = bullet.PlayerProfileID;
			float previousShotTime = bullet.TimeSinceShot;
			var elapsed = Stopwatch.StartNew();

			if (ammo == null || weapon == null || !startPosition.Equals(plan.ProjectileOrigin))
			{
				LogUnavailable(requestId, pass, index, "shot-already-released-or-replaced");
				return;
			}

			while (elapsed.Elapsed.TotalSeconds <= MaximumObservationSeconds)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (!ReferenceEquals(ammo, bullet.Ammo) ||
				    !ReferenceEquals(weapon, bullet.Weapon) ||
				    !ReferenceEquals(parent, bullet.Parent) ||
				    !startPosition.Equals(bullet.StartPosition) ||
				    !startDirection.Equals(bullet.Direction) ||
				    randomSeed != bullet.RandomSeed ||
				    owner != bullet.PlayerProfileID ||
				    bullet.TimeSinceShot < previousShotTime)
				{
					LogUnavailable(requestId, pass, index, "shot-released-or-recycled-before-observation");
					return;
				}

				previousShotTime = bullet.TimeSinceShot;
				if (bullet.HasAchievedTarget)
				{
					Vector3 actualHit = bullet.HitPoint;
					Vector3 error = actualHit - plan.IntendedImpact;
					Vector3 approach = new Vector3(startDirection.x, 0f, startDirection.z).normalized;
					Vector3 lateral = Vector3.Cross(Vector3.up, approach);
					float horizontalError = new Vector3(error.x, 0f, error.z).magnitude;
					string collider = bullet.HitCollider != null ? bullet.HitCollider.name : "<unavailable>";
					FireSupportPlugin.LogSource?.LogInfo(
						$"TSC A-10 measured collision requestId={A10AuthorityDiagnostics.ShortId(requestId)} pass={pass} shot={index} intended={A10AuthorityDiagnostics.FormatVector(plan.IntendedImpact)} predicted={A10AuthorityDiagnostics.FormatVector(plan.TracerEnd)} actual={A10AuthorityDiagnostics.FormatVector(actualHit)} predictionError={Vector3.Distance(actualHit, plan.TracerEnd):0.00}m horizontalError={horizontalError:0.00}m alongError={Vector3.Dot(error, approach):0.00}m lateralError={Vector3.Dot(error, lateral):0.00}m verticalError={error.y:0.00}m flightTime={bullet.TimeSinceShot:0.000}s collider={collider}");
					return;
				}

				if (bullet.IsShotFinished)
				{
					LogUnavailable(requestId, pass, index, "shot-finished-without-observable-collision");
					return;
				}

				// UpdateShots may run more than once per rendered frame. If its
				// hit state was already cleared, report unavailable rather than
				// inferring an impact from CurrentPosition or the planned ray.
				await UniTask.NextFrame(PlayerLoopTiming.LastPostLateUpdate, cancellationToken);
			}

			LogUnavailable(requestId, pass, index, "observation-timeout");
		}
		catch (OperationCanceledException)
		{
			LogUnavailable(requestId, pass, index, "observation-cancelled");
		}
		catch (Exception ex)
		{
			LogUnavailable(requestId, pass, index, $"observation-failed-{ex.GetType().Name}");
		}
	}

	private static void LogUnavailable(string requestId, int pass, int index, string reason)
	{
		FireSupportPlugin.LogSource?.LogInfo(
			$"TSC A-10 collision measurement unavailable requestId={A10AuthorityDiagnostics.ShortId(requestId)} pass={pass} shot={index} reason={reason}");
	}
}
