using Comfort.Common;
using Cysharp.Threading.Tasks;
using EFT;
using EFT.Ballistics;
using EFT.HealthSystem;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using UnityEngine;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public static class A10DamageOnlyPass
{
	private const float ImpactProbeRadius = 6f;
	private const float PathProbeRadius = 1.4f;
	private const float GeometricImpactProbeRadius = 10f;
	private const float GeometricPathProbeRadius = 5f;
	private const float GeometricTargetCenterProbeRadius = 35f;
	private const int MaxLoggedNearestPlayerLogs = 6;
	private const int MaxLoggedFireFailureLogs = 3;
	private const int MaxLoggedHitCandidateLogs = 8;
	private const int MaxLoggedUnresolvedColliderLogs = 5;
	private const float FallbackTimeBetweenShots = 1f / 65f;
	private const float DirectFallbackFullDamageDistance = 8f;
	private const float DirectFallbackMaxDamageDistance = 35f;
	private const float DirectFallbackMaxDamage = 300f;
	private const float DirectFallbackMinDamage = 90f;
	private const float DirectFallbackPenetrationPower = 100f;
	private const float DirectFallbackArmorDamage = 150f;
	private const float DirectFallbackStaminaBurnRate = 0.432f;

	public static async UniTask<bool> ExecuteAsync(A10StrikeRequest request, CancellationToken cancellationToken)
	{
		int seed = request.VisualSeed != 0 ? request.VisualSeed : Environment.TickCount;
		GameWorld gameWorld = Singleton<GameWorld>.Instance;
		if (gameWorld == null)
		{
			A10AuthorityDiagnostics.LogWarning(
				$"TSC A-10 authoritative damage skipped role={request.Role} requestId={A10AuthorityDiagnostics.ShortId(request.SupportRequestId)} pass={request.PassIndex} seed={seed}; GameWorld is unavailable.");
			return false;
		}

		OwnerResolution owner = ResolveOwner(gameWorld, request.RequesterProfileId);
		A10ProjectileOwnerMode ownerMode = FireSupportTuningSettings.GetA10HeadlessProjectileOwnerMode();
		string shotOwnerProfileId = ResolveShotOwnerProfileId(gameWorld, request, owner, ownerMode);
		LogOwnerResolution(request, seed, owner, ownerMode, shotOwnerProfileId);
		if (string.IsNullOrWhiteSpace(shotOwnerProfileId))
		{
			A10AuthorityDiagnostics.LogWarning(
				$"TSC A-10 authoritative damage continuing role={request.Role} requestId={A10AuthorityDiagnostics.ShortId(request.SupportRequestId)} pass={request.PassIndex} seed={seed}; requester ProfileId and fallback owner ProfileId are unavailable.");
		}

		VehicleWeapon weapon;
		try
		{
			weapon = new VehicleWeapon(shotOwnerProfileId, ItemConstants.GAU8_WEAPON_TPL, ItemConstants.GAU8_AMMO_TPL);
		}
		catch (Exception ex)
		{
			A10AuthorityDiagnostics.LogWarning(
				$"TSC A-10 authoritative damage skipped role={request.Role} requestId={A10AuthorityDiagnostics.ShortId(request.SupportRequestId)} pass={request.PassIndex} seed={seed}; weapon/ballistics unavailable. {ex.Message}");
			return false;
		}

		float timeBetweenShots = weapon?.timeBetweenShots > 0f ? weapon.timeBetweenShots : FallbackTimeBetweenShots;
		Vector3 visualOrigin = A10ShotPlanner.GetOriginalGau8VisualOrigin(request.Position, request.Direction);
		Vector3 damageOrigin = A10ShotPlanner.GetHeadlessDamageOrigin(request.Position, request.Direction);
		List<A10TracerSegment> visualShotPlan = A10ShotPlanner.BuildImpactAnchoredReplay(visualOrigin, request.Position, request.Direction, seed, timeBetweenShots);
		List<A10TracerSegment> damageShotPlan = A10ShotPlanner.Build(damageOrigin, request.Position, seed, timeBetweenShots);
		A10TracerSegment[] visualSegments = visualShotPlan.Where(static segment => segment.IsValid).ToArray();
		A10TracerSegment[] damageSegments = damageShotPlan.Where(static segment => segment.IsValid).ToArray();
		HitAccounting hitAccounting = ProbeHitCandidates(gameWorld, damageSegments, owner, request.Position, request.Direction);
		bool publishTracerBurst = A10TracerNetworking.IsNetworkAuthorityActive && visualSegments.Length > 0;

		FireSupportPlugin.LogSource?.LogInfo(
			$"TSC A-10 damage context role={request.Role} requestId={A10AuthorityDiagnostics.ShortId(request.SupportRequestId)} requester={A10AuthorityDiagnostics.ShortId(owner.RequesterProfileId)} projectileOwnerMode={ownerMode} shotOwner={A10AuthorityDiagnostics.ShortId(shotOwnerProfileId)} resolvedOwner={owner.OwnerPlayer?.GetType().Name ?? "<none>"}:{A10AuthorityDiagnostics.ShortId(owner.OwnerProfileId)}");
		FireSupportPlugin.LogSource?.LogInfo(
			$"TSC A-10 shot plan role={request.Role} requestId={A10AuthorityDiagnostics.ShortId(request.SupportRequestId)} pass={request.PassIndex} seed={seed} damageOnly=True visualReplayMode=MarkerAnchored shots={damageShotPlan.Count} visualSegments={visualSegments.Length} damageSegments={damageSegments.Length} colliderHits={hitAccounting.ColliderHitCount} ownerCandidatePlayers={hitAccounting.OwnerPlayerHitCount} nonOwnerCandidatePlayers={hitAccounting.NonOwnerPlayerHitCount} aliveOwnerCandidatePlayers={hitAccounting.AliveOwnerPlayerHitCount} aliveNonOwnerCandidatePlayers={hitAccounting.AliveNonOwnerPlayerHitCount} totalCandidatePlayersIncludingOwner={hitAccounting.TotalPlayerHitCount} colliderResolvedPlayers={hitAccounting.ColliderResolvedPlayerHitCount} geometricCandidatePlayers={hitAccounting.GeometricPlayerHitCount} unresolvedColliderHits={hitAccounting.UnresolvedColliderHitCount} visualOriginDistance={Vector3.Distance(visualOrigin, request.Position):0.0}m damageOriginDistance={Vector3.Distance(damageOrigin, request.Position):0.0}m damageApplications=unavailable damageConfirmed=unavailable");

		if (hitAccounting.TotalPlayerHitCount == hitAccounting.OwnerPlayerHitCount && hitAccounting.OwnerPlayerHitCount > 0 && !FireSupportTuningSettings.IsA10HeadlessRequesterSelfDamageEnabled())
		{
			FireSupportPlugin.LogSource?.LogInfo(
				$"TSC A-10 strike intersected requester only; self-damage is disabled. requestId={A10AuthorityDiagnostics.ShortId(request.SupportRequestId)} ownerCandidates={hitAccounting.OwnerPlayerHitCount}");
		}

		if (publishTracerBurst)
		{
			var burst = new A10TracerBurst(
				A10TracerNetworking.NextBurstId(),
				request.SupportRequestId,
				seed,
				request.PassIndex,
				Time.time,
				visualSegments);
			A10TracerNetworking.PublishBurst(burst);
			FireSupportPlugin.LogSource?.LogInfo(
				$"TSC A-10 tracer burst published role={request.Role} requestId={A10AuthorityDiagnostics.ShortId(request.SupportRequestId)} pass={request.PassIndex} seed={seed} burst={burst.BurstId} segments={visualSegments.Length}");
		}

		float fireDelaySeconds = A10TracerNetworking.ClientVisualFireDelaySeconds;
		if (fireDelaySeconds > 0f)
		{
			FireSupportPlugin.LogSource?.LogInfo(
				$"TSC A-10 authoritative fire delayed role={request.Role} requestId={A10AuthorityDiagnostics.ShortId(request.SupportRequestId)} pass={request.PassIndex} seed={seed} delay={fireDelaySeconds:0.0}s reason=align-with-client-visual-pass");
			try
			{
				await UniTask.WaitForSeconds(fireDelaySeconds, cancellationToken: cancellationToken);
			}
			catch (OperationCanceledException)
			{
				return false;
			}
		}

		int fired = 0;
		int fireFailures = 0;
		FireSupportPlugin.LogSource?.LogInfo(
			$"TSC A-10 authoritative fire started role={request.Role} requestId={A10AuthorityDiagnostics.ShortId(request.SupportRequestId)} pass={request.PassIndex} seed={seed} shots={damageShotPlan.Count} damageOrigin={A10AuthorityDiagnostics.FormatVector(damageOrigin)} damageOriginDistance={Vector3.Distance(damageOrigin, request.Position):0.0}m nonOwnerCandidates={hitAccounting.NonOwnerPlayerHitCount} ownerCandidates={hitAccounting.OwnerPlayerHitCount} damageConfirmed=unavailable");
		try
		{
			foreach (A10TracerSegment shot in damageShotPlan)
			{
				if (cancellationToken.IsCancellationRequested)
				{
					break;
				}

				try
				{
					weapon.FireProjectile(shot.ProjectileOrigin, shot.ProjectileDirection);
					fired++;
				}
				catch (Exception ex)
				{
					fireFailures++;
					if (fireFailures <= MaxLoggedFireFailureLogs)
					{
						A10AuthorityDiagnostics.LogWarning(
							$"TSC A-10 authoritative fire failed role={request.Role} requestId={A10AuthorityDiagnostics.ShortId(request.SupportRequestId)} pass={request.PassIndex} seed={seed} shot={fired + fireFailures} origin={A10AuthorityDiagnostics.FormatVector(shot.ProjectileOrigin)} direction={A10AuthorityDiagnostics.FormatVector(shot.ProjectileDirection)}. {ex.Message}");
					}
				}

				await UniTask.WaitForSeconds(timeBetweenShots, cancellationToken: cancellationToken);
			}
		}
		catch (OperationCanceledException)
		{
		}

		DirectDamageFallbackResult fallbackResult = ApplyDirectDamageFallback(request, seed, fired, hitAccounting, damageOrigin);
		FireSupportPlugin.LogSource?.LogInfo(
			$"TSC A-10 authoritative fire complete role={request.Role} requestId={A10AuthorityDiagnostics.ShortId(request.SupportRequestId)} pass={request.PassIndex} seed={seed} fired={fired}/{damageShotPlan.Count} fireProjectileFailures={fireFailures} colliderHits={hitAccounting.ColliderHitCount} ownerCandidatePlayers={hitAccounting.OwnerPlayerHitCount} nonOwnerCandidatePlayers={hitAccounting.NonOwnerPlayerHitCount} aliveOwnerCandidatePlayers={hitAccounting.AliveOwnerPlayerHitCount} aliveNonOwnerCandidatePlayers={hitAccounting.AliveNonOwnerPlayerHitCount} colliderResolvedPlayers={hitAccounting.ColliderResolvedPlayerHitCount} geometricCandidatePlayers={hitAccounting.GeometricPlayerHitCount} unresolvedColliderHits={hitAccounting.UnresolvedColliderHitCount} directFallbackApplied={fallbackResult.AppliedCount} directFallbackCommanded={fallbackResult.CommandedCount} directFallbackFailures={fallbackResult.FailureCount} damageApplications={(fallbackResult.Attempted ? fallbackResult.AppliedCount + fallbackResult.CommandedCount : 0)} damageConfirmed=unavailable");
		return fired > 0;
	}

	private static OwnerResolution ResolveOwner(GameWorld gameWorld, string requesterProfileId)
	{
		string requestedProfileId = requesterProfileId?.Trim() ?? string.Empty;
		Player mainPlayer = gameWorld.MainPlayer;
		Player ownerPlayer = null;
		int playerCount = 0;
		bool requesterPlayerResolved = false;

		if (gameWorld.AllPlayersEverExisted != null)
		{
			foreach (Player player in gameWorld.AllPlayersEverExisted)
			{
				if (player == null)
				{
					continue;
				}

				playerCount++;
				if (ownerPlayer == null &&
				    !string.IsNullOrWhiteSpace(requestedProfileId) &&
				    string.Equals(player.ProfileId, requestedProfileId, StringComparison.OrdinalIgnoreCase))
				{
					ownerPlayer = player;
					requesterPlayerResolved = true;
				}
			}
		}

		if (ownerPlayer == null &&
		    mainPlayer != null &&
		    !string.IsNullOrWhiteSpace(requestedProfileId) &&
		    string.Equals(mainPlayer.ProfileId, requestedProfileId, StringComparison.OrdinalIgnoreCase))
		{
			ownerPlayer = mainPlayer;
			requesterPlayerResolved = true;
		}

		if (ownerPlayer == null)
		{
			ownerPlayer = mainPlayer;
		}

		string ownerProfileId = ownerPlayer?.ProfileId ?? requestedProfileId;
		return new OwnerResolution(
			requestedProfileId,
			ownerPlayer,
			ownerProfileId ?? string.Empty,
			mainPlayer?.ProfileId ?? string.Empty,
			playerCount,
			mainPlayer == null,
			requesterPlayerResolved);
	}

	private static string ResolveShotOwnerProfileId(
		GameWorld gameWorld,
		A10StrikeRequest request,
		OwnerResolution owner,
		A10ProjectileOwnerMode ownerMode)
	{
		return ownerMode switch
		{
			A10ProjectileOwnerMode.NeutralSupport => "TSC_A10_SUPPORT",
			A10ProjectileOwnerMode.AuthorityProfile => gameWorld.MainPlayer?.ProfileId ?? owner.OwnerProfileId ?? request.RequesterProfileId ?? string.Empty,
			_ => owner.OwnerProfileId ?? request.RequesterProfileId ?? string.Empty
		};
	}

	private static void LogOwnerResolution(
		A10StrikeRequest request,
		int seed,
		OwnerResolution owner,
		A10ProjectileOwnerMode ownerMode,
		string shotOwnerProfileId)
	{
		FireSupportPlugin.LogSource?.LogInfo(
			$"TSC A-10 owner resolution role={request.Role} requestId={A10AuthorityDiagnostics.ShortId(request.SupportRequestId)} pass={request.PassIndex} seed={seed} requestedProfileId={A10AuthorityDiagnostics.ShortId(owner.RequesterProfileId)} requesterPlayerResolved={owner.RequesterPlayerResolved} ownerPlayerResolved={owner.OwnerPlayer != null} ownerType={owner.OwnerPlayer?.GetType().FullName ?? string.Empty} ownerProfileId={A10AuthorityDiagnostics.ShortId(owner.OwnerProfileId)} gameWorldPlayers={owner.GameWorldPlayerCount} mainPlayerNull={owner.MainPlayerNull} projectileOwnerMode={ownerMode} shotOwnerProfileId={A10AuthorityDiagnostics.ShortId(shotOwnerProfileId)}");
	}

	private static HitAccounting ProbeHitCandidates(
		GameWorld gameWorld,
		A10TracerSegment[] damageSegments,
		OwnerResolution owner,
		Vector3 targetCenter,
		Vector3 strafeDirection)
	{
		if (gameWorld == null || damageSegments == null || damageSegments.Length == 0)
		{
			return default;
		}

		var collector = new HitCandidateCollector(owner);
		foreach (A10TracerSegment segment in damageSegments)
		{
			SafeProbeImpactPoint(segment, collector);
			SafeProbeImpactPath(segment, collector);
		}

		SafeProbeGeometricPlayerCandidates(gameWorld, damageSegments, collector);
		SafeProbeTargetCenterCandidates(gameWorld, targetCenter, strafeDirection, collector);
		collector.LogNearestPlayersIfNoCandidates(gameWorld, targetCenter);
		return collector.ToAccounting();
	}

	private static void SafeProbeImpactPoint(A10TracerSegment segment, HitCandidateCollector collector)
	{
		try
		{
			ProbeImpactPoint(segment, collector);
		}
		catch (Exception ex)
		{
			A10AuthorityDiagnostics.LogWarning(
				$"TSC A-10 impact point probe failed type={ex.GetType().Name} message={ex.Message}");
		}
	}

	private static void SafeProbeImpactPath(A10TracerSegment segment, HitCandidateCollector collector)
	{
		try
		{
			ProbeImpactPath(segment, collector);
		}
		catch (Exception ex)
		{
			A10AuthorityDiagnostics.LogWarning(
				$"TSC A-10 impact path probe failed type={ex.GetType().Name} message={ex.Message}");
		}
	}

	private static void SafeProbeGeometricPlayerCandidates(GameWorld gameWorld, A10TracerSegment[] damageSegments, HitCandidateCollector collector)
	{
		try
		{
			ProbeGeometricPlayerCandidates(gameWorld, damageSegments, collector);
		}
		catch (Exception ex)
		{
			A10AuthorityDiagnostics.LogWarning(
				$"TSC A-10 geometric candidate probe failed type={ex.GetType().Name} message={ex.Message}");
		}
	}

	private static void SafeProbeTargetCenterCandidates(
		GameWorld gameWorld,
		Vector3 targetCenter,
		Vector3 strafeDirection,
		HitCandidateCollector collector)
	{
		try
		{
			ProbeTargetCenterCandidates(gameWorld, targetCenter, strafeDirection, collector);
		}
		catch (Exception ex)
		{
			A10AuthorityDiagnostics.LogWarning(
				$"TSC A-10 target-center candidate probe failed type={ex.GetType().Name} message={ex.Message}");
		}
	}

	private static void ProbeImpactPoint(A10TracerSegment segment, HitCandidateCollector collector)
	{
		Collider[] colliders = Physics.OverlapSphere(segment.TracerEnd, ImpactProbeRadius, ~0, QueryTriggerInteraction.Collide);
		foreach (Collider collider in colliders)
		{
			AddColliderCandidate(collider, collector, segment.TracerEnd, "impact");
		}
	}

	private static void ProbeImpactPath(A10TracerSegment segment, HitCandidateCollector collector)
	{
		float distance = Vector3.Distance(segment.ProjectileOrigin, segment.TracerEnd);
		if (distance <= 0.001f)
		{
			return;
		}

		RaycastHit[] hits = Physics.SphereCastAll(segment.ProjectileOrigin, PathProbeRadius, segment.ProjectileDirection, distance, ~0, QueryTriggerInteraction.Collide);
		foreach (RaycastHit hit in hits)
		{
			AddColliderCandidate(hit.collider, collector, hit.point, "path");
		}
	}

	private static void AddColliderCandidate(Collider collider, HitCandidateCollector collector, Vector3 hitPoint, string source)
	{
		if (collider == null || collector == null)
		{
			return;
		}

		try
		{
			collector.AddCollider(collider);
			Player player = ResolvePlayerFromCollider(collider);
			if (player == null)
			{
				collector.AddUnresolvedCollider(collider, source);
				return;
			}

			collector.AddPlayer(player, hitPoint, source, resolvedFromCollider: true);
		}
		catch (Exception ex)
		{
			collector.AddUnresolvedCollider(collider, $"{source}:exception:{ex.GetType().Name}");
			A10AuthorityDiagnostics.LogWarning(
				$"TSC A-10 collider candidate probe failed source={source} collider={SafeColliderName(collider)} type={ex.GetType().Name} message={ex.Message}");
		}
	}

	private static string SafeColliderName(Collider collider)
	{
		try
		{
			return collider == null ? string.Empty : $"{collider.GetType().Name}/{collider.gameObject?.name ?? string.Empty}";
		}
		catch
		{
			return collider?.GetType().Name ?? string.Empty;
		}
	}

	private static bool IsKnownWorldCollider(Collider collider)
	{
		if (collider == null)
		{
			return false;
		}

		string typeName = collider.GetType().FullName ?? collider.GetType().Name ?? string.Empty;
		if (typeName.IndexOf("Terrain", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return true;
		}

		try
		{
			string objectName = collider.gameObject?.name ?? string.Empty;
			return objectName.IndexOf("Terrain", StringComparison.OrdinalIgnoreCase) >= 0 ||
			       objectName.IndexOf("Grass", StringComparison.OrdinalIgnoreCase) >= 0 ||
			       objectName.IndexOf("Terrains", StringComparison.OrdinalIgnoreCase) >= 0;
		}
		catch
		{
			return false;
		}
	}

	private static Player ResolvePlayerFromCollider(Collider collider)
	{
		if (collider == null)
		{
			return null;
		}

		if (IsKnownWorldCollider(collider))
		{
			return null;
		}

		Player direct = collider.GetComponentInParent<Player>();
		if (direct != null)
		{
			return direct;
		}

		Component[] parents;
		try
		{
			parents = collider.GetComponentsInParent<Component>(includeInactive: true);
		}
		catch
		{
			return null;
		}

		foreach (Component component in parents)
		{
			Player reflected = TryResolvePlayerFromObject(component, depth: 0);
			if (reflected != null)
			{
				return reflected;
			}
		}

		return null;
	}

	private static Player TryResolvePlayerFromObject(object instance, int depth)
	{
		if (instance == null || depth > 2)
		{
			return null;
		}

		if (instance is Player player)
		{
			return player;
		}

		if (instance is Component component)
		{
			Player parentPlayer = component.GetComponentInParent<Player>();
			if (parentPlayer != null)
			{
				return parentPlayer;
			}
		}

		Type type = instance.GetType();
		const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
		foreach (FieldInfo field in type.GetFields(flags))
		{
			if (!ShouldInspectMember(field.FieldType, field.Name))
			{
				continue;
			}

			object value = TryReadField(field, instance);
			Player resolved = TryResolvePlayerFromReflectedValue(value, depth);
			if (resolved != null)
			{
				return resolved;
			}
		}

		foreach (PropertyInfo property in type.GetProperties(flags))
		{
			if (property.GetIndexParameters().Length != 0 || !property.CanRead || !ShouldInspectMember(property.PropertyType, property.Name))
			{
				continue;
			}

			object value = TryReadProperty(property, instance);
			Player resolved = TryResolvePlayerFromReflectedValue(value, depth);
			if (resolved != null)
			{
				return resolved;
			}
		}

		return null;
	}

	private static Player TryResolvePlayerFromReflectedValue(object value, int depth)
	{
		if (value == null)
		{
			return null;
		}

		if (value is Player player)
		{
			return player;
		}

		if (value is Component component)
		{
			Player parentPlayer = component.GetComponentInParent<Player>();
			if (parentPlayer != null)
			{
				return parentPlayer;
			}
		}

		return depth < 2 && LooksLikePlayerWrapper(value.GetType())
			? TryResolvePlayerFromObject(value, depth + 1)
			: null;
	}

	private static bool ShouldInspectMember(Type memberType, string memberName)
	{
		if (memberType == null)
		{
			return false;
		}

		if (typeof(Player).IsAssignableFrom(memberType) || typeof(Component).IsAssignableFrom(memberType))
		{
			return true;
		}

		return LooksLikePlayerWrapper(memberType) || LooksLikePlayerMemberName(memberName);
	}

	private static bool LooksLikePlayerWrapper(Type type)
	{
		if (type == null || type.IsPrimitive || type == typeof(string))
		{
			return false;
		}

		string fullName = type.FullName ?? type.Name ?? string.Empty;
		return fullName.IndexOf("Player", StringComparison.OrdinalIgnoreCase) >= 0 ||
		       fullName.IndexOf("BodyPartCollider", StringComparison.OrdinalIgnoreCase) >= 0 ||
		       fullName.IndexOf("Hitbox", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private static bool LooksLikePlayerMemberName(string memberName)
	{
		if (string.IsNullOrWhiteSpace(memberName))
		{
			return false;
		}

		return memberName.IndexOf("player", StringComparison.OrdinalIgnoreCase) >= 0 ||
		       memberName.IndexOf("owner", StringComparison.OrdinalIgnoreCase) >= 0 ||
		       memberName.IndexOf("profile", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private static object TryReadField(FieldInfo field, object instance)
	{
		try
		{
			return field.GetValue(instance);
		}
		catch
		{
			return null;
		}
	}

	private static object TryReadProperty(PropertyInfo property, object instance)
	{
		try
		{
			return property.GetValue(instance, null);
		}
		catch
		{
			return null;
		}
	}

	private static void ProbeGeometricPlayerCandidates(GameWorld gameWorld, A10TracerSegment[] damageSegments, HitCandidateCollector collector)
	{
		if (gameWorld?.AllPlayersEverExisted == null || damageSegments == null || collector == null)
		{
			return;
		}

		foreach (Player player in gameWorld.AllPlayersEverExisted)
		{
			if (player == null)
			{
				continue;
			}

			Vector3 playerPosition = GetPlayerProbePosition(player);
			float bestDistance = float.MaxValue;
			Vector3 bestPoint = playerPosition;
			bool matched = false;
			foreach (A10TracerSegment segment in damageSegments)
			{
				float impactDistance = Vector3.Distance(playerPosition, segment.TracerEnd);
				float pathDistance = DistancePointToSegment(playerPosition, segment.ProjectileOrigin, segment.TracerEnd);
				float distance = Mathf.Min(impactDistance, pathDistance);
				if (distance < bestDistance)
				{
					bestDistance = distance;
					bestPoint = impactDistance <= pathDistance ? segment.TracerEnd : ClosestPointOnSegment(playerPosition, segment.ProjectileOrigin, segment.TracerEnd);
				}

				if (impactDistance <= GeometricImpactProbeRadius || pathDistance <= GeometricPathProbeRadius)
				{
					matched = true;
				}
			}

			if (matched)
			{
				collector.AddPlayer(player, bestPoint, "geometry", resolvedFromCollider: false, explicitDistance: bestDistance);
			}
		}
	}

	private static void ProbeTargetCenterCandidates(
		GameWorld gameWorld,
		Vector3 targetCenter,
		Vector3 strafeDirection,
		HitCandidateCollector collector)
	{
		if (gameWorld?.AllPlayersEverExisted == null || collector == null)
		{
			return;
		}

		Vector3 safeDirection = strafeDirection.sqrMagnitude > 0.0001f ? strafeDirection.normalized : Vector3.forward;
		Vector3 corridorStart = targetCenter - safeDirection * GeometricTargetCenterProbeRadius;
		Vector3 corridorEnd = targetCenter + safeDirection * GeometricTargetCenterProbeRadius;
		foreach (Player player in gameWorld.AllPlayersEverExisted)
		{
			if (player == null)
			{
				continue;
			}

			Vector3 playerPosition = GetPlayerProbePosition(player);
			float centerDistance = Vector3.Distance(playerPosition, targetCenter);
			float corridorDistance = DistancePointToSegment(playerPosition, corridorStart, corridorEnd);
			float bestDistance = Mathf.Min(centerDistance, corridorDistance);
			if (centerDistance <= GeometricTargetCenterProbeRadius || corridorDistance <= GeometricTargetCenterProbeRadius * 0.6f)
			{
				collector.AddPlayer(player, targetCenter, "targetCenter", resolvedFromCollider: false, explicitDistance: bestDistance);
			}
		}
	}

	private static Vector3 GetPlayerProbePosition(Player player)
	{
		try
		{
			if (player?.Transform != null)
			{
				return player.Transform.position + Vector3.up * 1.1f;
			}
		}
		catch
		{
		}

		return Vector3.zero;
	}

	private static float DistancePointToSegment(Vector3 point, Vector3 start, Vector3 end)
	{
		return Vector3.Distance(point, ClosestPointOnSegment(point, start, end));
	}

	private static Vector3 ClosestPointOnSegment(Vector3 point, Vector3 start, Vector3 end)
	{
		Vector3 segment = end - start;
		float lengthSquared = segment.sqrMagnitude;
		if (lengthSquared <= 0.0001f)
		{
			return start;
		}

		float t = Vector3.Dot(point - start, segment) / lengthSquared;
		t = Mathf.Clamp01(t);
		return start + segment * t;
	}


	private static DirectDamageFallbackResult ApplyDirectDamageFallback(
		A10StrikeRequest request,
		int seed,
		int fired,
		HitAccounting hitAccounting,
		Vector3 damageOrigin)
	{
		bool fallbackEnabled = FireSupportTuningSettings.IsA10HeadlessDirectDamageFallbackEnabled();
		if (!fallbackEnabled || request.Role != A10AuthorityRole.FikaHeadlessHost)
		{
			return default;
		}

		string unavailableReason = GetDirectFallbackUnavailableReason(hitAccounting);
		if (!string.IsNullOrEmpty(unavailableReason))
		{
			FireSupportPlugin.LogSource?.LogInfo(
				$"TSC A-10 headless direct damage fallback armed role={request.Role} requestId={A10AuthorityDiagnostics.ShortId(request.SupportRequestId)} pass={request.PassIndex} seed={seed} fired={fired} ownerCandidates={hitAccounting.OwnerPlayerHitCount} nonOwnerCandidates={hitAccounting.NonOwnerPlayerHitCount} fallbackApplied=False fallbackReason={unavailableReason}");
			return new DirectDamageFallbackResult(attempted: true, appliedCount: 0, commandedCount: 0, failureCount: 0);
		}

		int applied = 0;
		int commanded = 0;
		int failures = 0;
		int skipped = 0;
		IReadOnlyList<HitCandidateSnapshot> candidates = hitAccounting.Candidates ?? Array.Empty<HitCandidateSnapshot>();
		foreach (HitCandidateSnapshot candidate in candidates)
		{
			if (candidate.Player == null)
			{
				skipped++;
				continue;
			}

			if (!candidate.Alive)
			{
				skipped++;
				continue;
			}

			if (candidate.IsOwner && !FireSupportTuningSettings.IsA10HeadlessRequesterSelfDamageEnabled())
			{
				skipped++;
				continue;
			}

			int targetNetId = TryGetPlayerNetId(candidate.Player);
			float damage = CalculateDirectFallbackDamage(candidate.Distance);
			EBodyPart bodyPart = EBodyPart.Chest;
			EBodyPartColliderType colliderType = EBodyPartColliderType.RibcageUp;
			DamageInfoStruct damageInfo = BuildDirectDamageInfo(
				request,
				candidate,
				damageOrigin,
				damage,
				colliderType);

			if (TryApplyActiveHealthDamage(candidate.Player, damageInfo, bodyPart, out float appliedDamage, out string localReason))
			{
				applied++;
				FireSupportPlugin.LogSource?.LogInfo(
					$"TSC A-10 headless direct damage applied method=ActiveHealthController requestId={A10AuthorityDiagnostics.ShortId(request.SupportRequestId)} target={A10AuthorityDiagnostics.ShortId(candidate.ProfileId)} netId={targetNetId} type={candidate.Player.GetType().Name} source={candidate.Source} distance={candidate.Distance:0.0}m bodyPart={bodyPart} collider={colliderType} damage={damage:0.0} applied={appliedDamage:0.0}");
				continue;
			}

			if (targetNetId > 0 &&
			    A10HeadlessDamageCommandDispatcher.TryDispatch(
				    new A10HeadlessDamageCommand
				    {
					    SupportRequestId = request.SupportRequestId,
					    TargetProfileId = candidate.ProfileId,
					    TargetNetId = targetNetId,
					    DamageInfo = damageInfo,
					    BodyPart = bodyPart,
					    ColliderType = colliderType,
					    ArmorPlateCollider = default,
					    MaterialType = MaterialType.Body,
					    Absorbed = 0f
				    },
				    out string commandReason))
			{
				commanded++;
				FireSupportPlugin.LogSource?.LogInfo(
					$"TSC A-10 headless direct damage commanded method=FikaDamagePacket requestId={A10AuthorityDiagnostics.ShortId(request.SupportRequestId)} target={A10AuthorityDiagnostics.ShortId(candidate.ProfileId)} netId={targetNetId} type={candidate.Player.GetType().Name} source={candidate.Source} distance={candidate.Distance:0.0}m bodyPart={bodyPart} collider={colliderType} damage={damage:0.0} reason={commandReason}");
				continue;
			}

			failures++;
			FireSupportPlugin.LogSource?.LogInfo(
				$"TSC A-10 headless direct damage failed requestId={A10AuthorityDiagnostics.ShortId(request.SupportRequestId)} target={A10AuthorityDiagnostics.ShortId(candidate.ProfileId)} netId={targetNetId} type={candidate.Player.GetType().Name} source={candidate.Source} distance={candidate.Distance:0.0}m localReason={localReason}");
		}

		FireSupportPlugin.LogSource?.LogInfo(
			$"TSC A-10 headless direct damage fallback result role={request.Role} requestId={A10AuthorityDiagnostics.ShortId(request.SupportRequestId)} pass={request.PassIndex} seed={seed} fired={fired} candidates={candidates.Count} applied={applied} commanded={commanded} skipped={skipped} failures={failures} fallbackApplied={(applied + commanded) > 0}");
		return new DirectDamageFallbackResult(attempted: true, applied, commanded, failures);
	}

	private static string GetDirectFallbackUnavailableReason(HitAccounting hitAccounting)
	{
		if (hitAccounting.TotalPlayerHitCount <= 0)
		{
			return hitAccounting.UnresolvedColliderHitCount > 0 ? "UnresolvedColliderCandidates" : "NoPlayerCandidates";
		}

		if (hitAccounting.OwnerPlayerHitCount > 0 &&
		    hitAccounting.NonOwnerPlayerHitCount == 0 &&
		    !FireSupportTuningSettings.IsA10HeadlessRequesterSelfDamageEnabled())
		{
			return "RequesterOnlySelfDamageDisabled";
		}

		return string.Empty;
	}

	private static DamageInfoStruct BuildDirectDamageInfo(
		A10StrikeRequest request,
		HitCandidateSnapshot candidate,
		Vector3 damageOrigin,
		float damage,
		EBodyPartColliderType colliderType)
	{
		Vector3 hitPoint = candidate.HitPoint != Vector3.zero ? candidate.HitPoint : GetPlayerProbePosition(candidate.Player);
		Vector3 direction = hitPoint - damageOrigin;
		if (direction.sqrMagnitude <= 0.0001f)
		{
			direction = request.Direction.sqrMagnitude > 0.0001f ? request.Direction.normalized : Vector3.down;
		}
		else
		{
			direction.Normalize();
		}

		return new DamageInfoStruct
		{
			DamageType = EFT.EDamageType.Artillery,
			Damage = damage,
			PenetrationPower = DirectFallbackPenetrationPower,
			ArmorDamage = DirectFallbackArmorDamage,
			Direction = direction,
			HitPoint = hitPoint,
			MasterOrigin = damageOrigin,
			HitNormal = Vector3.up,
			FireIndex = -1,
			StaminaBurnRate = DirectFallbackStaminaBurnRate,
			BodyPartColliderType = colliderType
		};
	}

	private static bool TryApplyActiveHealthDamage(
		Player player,
		DamageInfoStruct damageInfo,
		EBodyPart bodyPart,
		out float appliedDamage,
		out string reason)
	{
		appliedDamage = 0f;
		reason = string.Empty;
		if (player == null)
		{
			reason = "PlayerNull";
			return false;
		}

		if (!IsLocalOrAiDamageTarget(player))
		{
			reason = "RemoteHumanRequiresFikaDamagePacket";
			return false;
		}

		ActiveHealthController activeHealthController;
		try
		{
			activeHealthController = player.ActiveHealthController;
		}
		catch (Exception ex)
		{
			reason = $"ActiveHealthControllerReadFailed:{ex.GetType().Name}:{ex.Message}";
			return false;
		}

		if (activeHealthController == null)
		{
			reason = "NoActiveHealthController";
			return false;
		}

		try
		{
			appliedDamage = activeHealthController.ApplyDamage(bodyPart, damageInfo.Damage, damageInfo);
			if (appliedDamage <= 0f)
			{
				reason = "ActiveHealthControllerAppliedZero";
				return false;
			}

			return true;
		}
		catch (Exception ex)
		{
			reason = $"ActiveHealthControllerApplyFailed:{ex.GetType().Name}:{ex.Message}";
			return false;
		}
	}

	private static bool IsLocalOrAiDamageTarget(Player player)
	{
		if (player == null)
		{
			return false;
		}

		try
		{
			return player.IsAI || player.IsYourPlayer;
		}
		catch
		{
			return false;
		}
	}

	private static float CalculateDirectFallbackDamage(float distance)
	{
		if (distance <= DirectFallbackFullDamageDistance)
		{
			return DirectFallbackMaxDamage;
		}

		float t = Mathf.InverseLerp(DirectFallbackFullDamageDistance, DirectFallbackMaxDamageDistance, distance);
		return Mathf.Lerp(DirectFallbackMaxDamage, DirectFallbackMinDamage, Mathf.Clamp01(t));
	}

	private static int TryGetPlayerNetId(Player player)
	{
		if (player == null)
		{
			return 0;
		}

		int netId = TryReadIntMember(player, "NetId");
		if (netId > 0)
		{
			return netId;
		}

		try
		{
			return player.Id;
		}
		catch
		{
			return 0;
		}
	}

	private static int TryReadIntMember(object instance, string memberName)
	{
		if (instance == null || string.IsNullOrWhiteSpace(memberName))
		{
			return 0;
		}

		const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
		Type type = instance.GetType();
		FieldInfo field = type.GetField(memberName, flags);
		if (field != null)
		{
			try
			{
				object value = field.GetValue(instance);
				return value is int intValue ? intValue : 0;
			}
			catch
			{
			}
		}

		PropertyInfo property = type.GetProperty(memberName, flags);
		if (property != null && property.CanRead && property.GetIndexParameters().Length == 0)
		{
			try
			{
				object value = property.GetValue(instance, null);
				return value is int intValue ? intValue : 0;
			}
			catch
			{
			}
		}

		return 0;
	}

	private readonly struct OwnerResolution
	{
		public readonly string RequesterProfileId;
		public readonly Player OwnerPlayer;
		public readonly string OwnerProfileId;
		public readonly string AuthorityProfileId;
		public readonly int GameWorldPlayerCount;
		public readonly bool MainPlayerNull;
		public readonly bool RequesterPlayerResolved;

		public OwnerResolution(string requesterProfileId, Player ownerPlayer, string ownerProfileId, string authorityProfileId, int gameWorldPlayerCount, bool mainPlayerNull, bool requesterPlayerResolved)
		{
			RequesterProfileId = requesterProfileId;
			OwnerPlayer = ownerPlayer;
			OwnerProfileId = ownerProfileId;
			AuthorityProfileId = authorityProfileId;
			GameWorldPlayerCount = gameWorldPlayerCount;
			MainPlayerNull = mainPlayerNull;
			RequesterPlayerResolved = requesterPlayerResolved;
		}
	}

	private sealed class HitCandidateCollector
	{
		private readonly OwnerResolution _owner;
		private readonly HashSet<int> _colliderIds = new();
		private readonly HashSet<int> _unresolvedColliderIds = new();
		private readonly HashSet<string> _ownerPlayerIds = new(StringComparer.OrdinalIgnoreCase);
		private readonly HashSet<string> _nonOwnerPlayerIds = new(StringComparer.OrdinalIgnoreCase);
		private readonly HashSet<string> _aliveOwnerPlayerIds = new(StringComparer.OrdinalIgnoreCase);
		private readonly HashSet<string> _aliveNonOwnerPlayerIds = new(StringComparer.OrdinalIgnoreCase);
		private readonly HashSet<string> _colliderResolvedPlayerIds = new(StringComparer.OrdinalIgnoreCase);
		private readonly HashSet<string> _geometricPlayerIds = new(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, HitCandidateSnapshot> _candidates = new(StringComparer.OrdinalIgnoreCase);
		private int _loggedCandidates;
		private int _loggedUnresolvedColliders;

		public HitCandidateCollector(OwnerResolution owner)
		{
			_owner = owner;
		}

		public void AddCollider(Collider collider)
		{
			if (collider != null)
			{
				_colliderIds.Add(collider.GetInstanceID());
			}
		}

		public void AddUnresolvedCollider(Collider collider, string source)
		{
			if (collider == null || !_unresolvedColliderIds.Add(collider.GetInstanceID()))
			{
				return;
			}

			if (_loggedUnresolvedColliders++ < MaxLoggedUnresolvedColliderLogs)
			{
				FireSupportPlugin.LogSource?.LogInfo(
					$"TSC A-10 unresolved collider source={source} collider={collider.GetType().Name} gameObject={collider.gameObject?.name ?? string.Empty} layer={collider.gameObject?.layer ?? -1} parent={collider.transform?.parent?.name ?? string.Empty}");
			}
		}

		public void AddPlayer(Player player, Vector3 hitPoint, string source, bool resolvedFromCollider, float? explicitDistance = null)
		{
			if (player == null)
			{
				return;
			}

			bool isOwner = IsOwnerCandidate(player, _owner);
			string profileId = GetPlayerKey(player);
			HashSet<string> playerSet = isOwner ? _ownerPlayerIds : _nonOwnerPlayerIds;
			HashSet<string> aliveSet = isOwner ? _aliveOwnerPlayerIds : _aliveNonOwnerPlayerIds;
			bool firstSeen = playerSet.Add(profileId);
			bool alive = player.HealthController?.IsAlive == true;
			if (alive)
			{
				aliveSet.Add(profileId);
			}

			if (resolvedFromCollider)
			{
				_colliderResolvedPlayerIds.Add(profileId);
			}
			else
			{
				_geometricPlayerIds.Add(profileId);
			}

			Vector3 playerPosition = GetPlayerProbePosition(player);
			float distance = explicitDistance ?? Vector3.Distance(playerPosition, hitPoint);
			if (!_candidates.TryGetValue(profileId, out HitCandidateSnapshot existing) ||
			    distance < existing.Distance ||
			    (resolvedFromCollider && !existing.ResolvedFromCollider))
			{
				_candidates[profileId] = new HitCandidateSnapshot(
					player,
					profileId,
					isOwner,
					alive,
					resolvedFromCollider || existing.ResolvedFromCollider,
					distance < existing.Distance || string.IsNullOrWhiteSpace(existing.Source) ? source : existing.Source,
					Mathf.Min(distance, existing.Distance > 0f ? existing.Distance : distance),
					distance < existing.Distance || existing.HitPoint == Vector3.zero ? hitPoint : existing.HitPoint);
			}

			if (firstSeen && _loggedCandidates++ < MaxLoggedHitCandidateLogs)
			{
				FireSupportPlugin.LogSource?.LogInfo(
					$"TSC A-10 hit candidate source={source} profile={A10AuthorityDiagnostics.ShortId(profileId)} name={SafeNickname(player)} type={player.GetType().Name} isOwner={isOwner} alive={alive} distance={distance:0.0}m pos={A10AuthorityDiagnostics.FormatVector(playerPosition)}");
			}
		}

		public void LogNearestPlayersIfNoCandidates(GameWorld gameWorld, Vector3 targetCenter)
		{
			if (TotalPlayerCount > 0 || gameWorld?.AllPlayersEverExisted == null)
			{
				return;
			}

			var nearest = new List<NearestPlayerDistance>();
			foreach (Player player in gameWorld.AllPlayersEverExisted)
			{
				if (player == null)
				{
					continue;
				}

				Vector3 position = GetPlayerProbePosition(player);
				nearest.Add(new NearestPlayerDistance(player, Vector3.Distance(position, targetCenter)));
			}

			nearest.Sort((left, right) => left.Distance.CompareTo(right.Distance));
			int count = Mathf.Min(MaxLoggedNearestPlayerLogs, nearest.Count);
			for (int index = 0; index < count; index++)
			{
				Player player = nearest[index].Player;
				FireSupportPlugin.LogSource?.LogInfo(
					$"TSC A-10 nearest player to target index={index} profile={A10AuthorityDiagnostics.ShortId(GetPlayerKey(player))} name={SafeNickname(player)} type={player.GetType().Name} isOwner={IsOwnerCandidate(player, _owner)} alive={player.HealthController?.IsAlive == true} distance={nearest[index].Distance:0.0}m pos={A10AuthorityDiagnostics.FormatVector(GetPlayerProbePosition(player))}");
			}
		}

		private int TotalPlayerCount => _ownerPlayerIds.Count + _nonOwnerPlayerIds.Count;

		public HitAccounting ToAccounting()
		{
			return new HitAccounting(
				_colliderIds.Count,
				_unresolvedColliderIds.Count,
				_ownerPlayerIds.Count,
				_nonOwnerPlayerIds.Count,
				_aliveOwnerPlayerIds.Count,
				_aliveNonOwnerPlayerIds.Count,
				_colliderResolvedPlayerIds.Count,
				_geometricPlayerIds.Count,
				_candidates.Values.ToList());
		}
	}

	private static bool IsOwnerCandidate(Player player, OwnerResolution owner)
	{
		if (player == null)
		{
			return false;
		}

		if (owner.OwnerPlayer != null && ReferenceEquals(player, owner.OwnerPlayer))
		{
			return true;
		}

		string profileId = player.ProfileId;
		return !string.IsNullOrWhiteSpace(profileId) &&
		       ((!string.IsNullOrWhiteSpace(owner.RequesterProfileId) && string.Equals(profileId, owner.RequesterProfileId, StringComparison.OrdinalIgnoreCase)) ||
		        (!string.IsNullOrWhiteSpace(owner.OwnerProfileId) && string.Equals(profileId, owner.OwnerProfileId, StringComparison.OrdinalIgnoreCase)));
	}

	private static string GetPlayerKey(Player player)
	{
		return !string.IsNullOrWhiteSpace(player?.ProfileId) ? player.ProfileId : player?.GetHashCode().ToString() ?? string.Empty;
	}

	private static string SafeNickname(Player player)
	{
		try
		{
			return player?.Profile?.Nickname ?? string.Empty;
		}
		catch
		{
			return string.Empty;
		}
	}

	private readonly struct NearestPlayerDistance
	{
		public readonly Player Player;
		public readonly float Distance;

		public NearestPlayerDistance(Player player, float distance)
		{
			Player = player;
			Distance = distance;
		}
	}

	private readonly struct DirectDamageFallbackResult
	{
		public readonly bool Attempted;
		public readonly int AppliedCount;
		public readonly int CommandedCount;
		public readonly int FailureCount;

		public DirectDamageFallbackResult(bool attempted, int appliedCount, int commandedCount, int failureCount)
		{
			Attempted = attempted;
			AppliedCount = appliedCount;
			CommandedCount = commandedCount;
			FailureCount = failureCount;
		}
	}

	private readonly struct HitCandidateSnapshot
	{
		public readonly Player Player;
		public readonly string ProfileId;
		public readonly bool IsOwner;
		public readonly bool Alive;
		public readonly bool ResolvedFromCollider;
		public readonly string Source;
		public readonly float Distance;
		public readonly Vector3 HitPoint;

		public HitCandidateSnapshot(
			Player player,
			string profileId,
			bool isOwner,
			bool alive,
			bool resolvedFromCollider,
			string source,
			float distance,
			Vector3 hitPoint)
		{
			Player = player;
			ProfileId = profileId ?? string.Empty;
			IsOwner = isOwner;
			Alive = alive;
			ResolvedFromCollider = resolvedFromCollider;
			Source = source ?? string.Empty;
			Distance = distance;
			HitPoint = hitPoint;
		}
	}

	private readonly struct HitAccounting
	{
		public readonly int ColliderHitCount;
		public readonly int UnresolvedColliderHitCount;
		public readonly int OwnerPlayerHitCount;
		public readonly int NonOwnerPlayerHitCount;
		public readonly int AliveOwnerPlayerHitCount;
		public readonly int AliveNonOwnerPlayerHitCount;
		public readonly int ColliderResolvedPlayerHitCount;
		public readonly int GeometricPlayerHitCount;
		public readonly IReadOnlyList<HitCandidateSnapshot> Candidates;
		public int TotalPlayerHitCount => OwnerPlayerHitCount + NonOwnerPlayerHitCount;

		public HitAccounting(
			int colliderHitCount,
			int unresolvedColliderHitCount,
			int ownerPlayerHitCount,
			int nonOwnerPlayerHitCount,
			int aliveOwnerPlayerHitCount,
			int aliveNonOwnerPlayerHitCount,
			int colliderResolvedPlayerHitCount,
			int geometricPlayerHitCount,
			IReadOnlyList<HitCandidateSnapshot> candidates)
		{
			ColliderHitCount = colliderHitCount;
			UnresolvedColliderHitCount = unresolvedColliderHitCount;
			OwnerPlayerHitCount = ownerPlayerHitCount;
			NonOwnerPlayerHitCount = nonOwnerPlayerHitCount;
			AliveOwnerPlayerHitCount = aliveOwnerPlayerHitCount;
			AliveNonOwnerPlayerHitCount = aliveNonOwnerPlayerHitCount;
			ColliderResolvedPlayerHitCount = colliderResolvedPlayerHitCount;
			GeometricPlayerHitCount = geometricPlayerHitCount;
			Candidates = candidates ?? Array.Empty<HitCandidateSnapshot>();
		}
	}

}
