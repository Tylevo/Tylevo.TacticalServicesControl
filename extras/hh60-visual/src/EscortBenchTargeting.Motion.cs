using EFT;
using EFT.Ballistics;
using EFT.InventoryLogic;
using UnityEngine;

namespace TscHh60Visual
{
    internal sealed partial class EscortBenchTargeting
    {
        private bool UpdateMotion(out string reason, bool forceSample = false)
        {
            reason = null;
            var players = _world.AllAlivePlayersList;
            if (players == null || players.Count > MaximumPlayers)
            { reason = "moving safety registry unavailable"; return false; }
            if (_motionWorld != _world) { _motion.Clear(); _motionWorld = _world; }
            _expiredMotion.Clear();
            foreach (var pair in _motion)
                if (!Alive(pair.Key) || !players.Contains(pair.Key)) _expiredMotion.Add(pair.Key);
            for (int i = 0; i < _expiredMotion.Count; i++) _motion.Remove(_expiredMotion[i]);
            for (int i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (!Alive(player)) continue;
                EscortActorMotion motion;
                if (!_motion.TryGetValue(player, out motion))
                { motion = new EscortActorMotion(); _motion.Add(player, motion); }
                motion.Observe(MotionVector(player.Position), Time.time, forceSample);
            }
            return true;
        }

        private bool TryVelocity(Player player, out Vector3 velocity, out string reason)
        {
            velocity = Vector3.zero;
            EscortActorMotion motion;
            if (!_motion.TryGetValue(player, out motion) || !motion.Ready(Time.time))
            { reason = motion == null ? "collecting actor motion" : motion.Reason; return false; }
            var v = motion.Velocity;
            velocity = new Vector3(v.X, v.Y, v.Z);
            reason = null;
            return true;
        }

        private bool SolveMovingAim(EscortGunMount gun, Vector3 torso, out Ammo ammo,
            out EscortAimSolution solution, out string reason)
        {
            ammo = null;
            solution = default;
            Vector3 velocity;
            if (!TryVelocity(_target, out velocity, out reason)) return false;
            ammo = _ballistics.PeekNextRound();
            if (ammo == null) { reason = "next native round unavailable"; return false; }
            return _aim.TrySolve(ammo, gun.Muzzle, BurstAimPoint(torso), velocity, out solution, out reason);
        }

        private Vector3 BurstAimPoint(Vector3 torso)
        {
            return _target != null && _burstTargetId != null && _burstTargetId == _target.ProfileId
                ? torso + _burstOffset : torso;
        }

        private bool TryMovingShot(EscortGunMount gun, Vector3 point, out Vector3 origin,
            out Vector3 direction, out string targetProfileId, out Ammo expectedAmmo, out string reason)
        {
            origin = direction = Vector3.zero;
            targetProfileId = null;
            expectedAmmo = null;
            if (!UpdateMotion(out reason, true)) return Reject(reason, out reason);
            Ammo ammo;
            EscortAimSolution desired;
            if (!SolveMovingAim(gun, point, out ammo, out desired, out reason)) return Reject(reason, out reason);
            Vector3 muzzle = gun.Muzzle, barrel = gun.Barrel;
            float distance = (point - muzzle).magnitude;
            float yaw, pitch;
            if (_fixedGunIndex >= 0 && !AimGeometry(gun, point, out yaw, out pitch, out distance, out reason))
                return Reject(reason, out reason);
            gun.Angles(desired.Direction, out yaw, out pitch);
            if (!EscortBenchSafetyPolicy.WithinLimits(distance, yaw, pitch, out reason))
                return Reject("predicted aim " + reason, out reason);
            gun.Angles(barrel, out yaw, out pitch);
            if (!EscortBenchSafetyPolicy.WithinLimits(distance, yaw, pitch, out reason))
                return Reject("actual barrel " + reason, out reason);
            if (!EscortBenchSafetyPolicy.Aligned(Vector3.Angle(barrel, desired.Direction)))
                return Reject("tracking; barrel has not reached native lead/drop aim", out reason);
            Vector3 velocity;
            if (!TryVelocity(_target, out velocity, out reason)) return Reject(reason, out reason);
            EscortAimSolution actual;
            if (!_aim.TryEvaluate(ammo, muzzle, barrel, BurstAimPoint(point), velocity, out actual, out reason))
                return Reject("actual barrel native trajectory: " + reason, out reason);
            Physics.SyncTransforms();
            if (!MuzzleClear(gun, muzzle, barrel, out reason)) return Reject(reason, out reason);
            // The present torso must be visible, but the led barrel is deliberately
            // not required to intersect that present-day body position.
            if (!TargetIsFirstHit(muzzle, (point - muzzle).normalized, distance + 1.5f, _target, out reason))
                return Reject(reason, out reason);
            if (!CurvedFlightClearance(actual, _target, out reason)) return Reject(reason, out reason);
            if (!MovingPlayersClear(actual, muzzle, barrel, ammo, _target, out reason)) return Reject(reason, out reason);
            if (string.IsNullOrEmpty(_target.ProfileId)) return Reject("target profile unavailable", out reason);
            origin = muzzle;
            direction = barrel;
            targetProfileId = _target.ProfileId;
            expectedAmmo = ammo;
            AimPoint = actual.PredictedTarget;
            SetSummary("READY " + ShortId(targetProfileId) + " " + distance.ToString("F0") +
                "m native lead " + actual.FlightTime.ToString("F3") +
                (_fixedGunIndex >= 0 ? "s; scheduled round checked" : "s; manual shot only"));
            return true;
        }

        private bool CurvedFlightClearance(EscortAimSolution solution, Player target, out string reason)
        {
            var path = solution.Path;
            if (path == null || path.Length < 2 || path.Length > 1024)
            { reason = "native trajectory samples unavailable or oversized"; return false; }
            if (!Finite(path[0].Position) || path[0].Time != 0f)
            { reason = "native trajectory origin is invalid"; return false; }
            for (int i = 1; i < path.Length; i++)
            {
                var from = path[i - 1];
                var to = path[i];
                if (!Finite(to.Position) || !EscortBenchSafetyPolicy.Finite(to.Time) || to.Time <= from.Time || to.Time > 1f)
                { reason = "native trajectory timing is invalid"; return false; }
                Vector3 delta = to.Position - from.Position;
                float length = delta.magnitude;
                if (!EscortBenchSafetyPolicy.Finite(length) || length < 0.000001f || length > 25f)
                { reason = "native trajectory segment is unsupported"; return false; }
                // Native samples follow the same drag trajectory as CreateShot;
                // a small additional envelope covers the curved span between them.
                float radius = 0.1f + 0.5f * 9.81f * (to.Time - from.Time) * (to.Time - from.Time);
                if (!PathEndpointClear(from.Position, radius, target, out reason) ||
                    !PathEndpointClear(to.Position, radius, target, out reason)) return false;
                int count = Physics.SphereCastNonAlloc(from.Position, radius, delta / length,
                    _hits, length, BallisticsCalculatorConstants.HitMask, QueryTriggerInteraction.Collide);
                if (count >= _hits.Length) { reason = "native path clearance query saturated"; return false; }
                for (int j = 0; j < count; j++)
                    if (Relevant(_hits[j].collider) && EscortBenchColliderOwner.Resolve(_hits[j].collider, _world) != target)
                    { reason = "native curved path blocked; " + DescribeCollision(_hits[j].collider, _hits[j].distance, length, target); return false; }
                if (!MovingHullClear(from.Position, to.Position, from.Time, to.Time, out reason)) return false;
            }
            reason = null;
            return true;
        }

        private bool MovingHullClear(Vector3 start, Vector3 end, float startTime, float endTime, out string reason)
        {
            if (_hullMotionSpeed <= 70f) return MovingHullSegmentClear(start, end, endTime, out reason);
            // Native trajectories are linear between their time samples. A fast
            // inbound aircraft needs finer intervals near the muzzle: applying
            // the whole sample's future expansion at t=0 can reject every shot.
            // Each smaller interval still uses its full END-time motion bound.
            float paddingGrowth = _hullMotionSpeed * (endTime - startTime);
            if (!EscortBenchSafetyPolicy.Finite(paddingGrowth) || paddingGrowth <= 0f || paddingGrowth > 32f)
            { reason = "unsupported inbound hull timing interval"; return false; }
            int pieces = Mathf.Max(1, Mathf.CeilToInt(paddingGrowth / .25f));
            Vector3 previous = start;
            for (int piece = 1; piece <= pieces; piece++)
            {
                float fraction = (float)piece / pieces;
                Vector3 next = Vector3.Lerp(start, end, fraction);
                float arrival = Mathf.Lerp(startTime, endTime, fraction);
                if (!MovingHullSegmentClear(previous, next, arrival, out reason)) return false;
                previous = next;
            }
            reason = null;
            return true;
        }

        private bool MovingHullSegmentClear(Vector3 start, Vector3 end, float arrivalTime, out string reason)
        {
            // Escort flight uses 70m/s surface motion. Native extraction sets a
            // larger clip-derived envelope during approach, then restores the
            // hover bound on arrival. Both use the same 35m body-relative hull.
            // This query includes only the aircraft's own physical colliders;
            // nearby world geometry is handled by the tighter native-path sweep.
            float radius = 0.1f + _hullMotionSpeed * arrivalTime;
            var root = _aircraft.transform;
            Vector3 scale = root.lossyScale;
            float smallestScale = Mathf.Min(scale.x, Mathf.Min(scale.y, scale.z));
            if (!Finite(scale) || smallestScale <= 0f)
            { reason = "moving rotor safety scale unavailable"; return false; }
            if (EscortRotorSafety.Intersects(MotionVector(root.InverseTransformPoint(start)),
                MotionVector(root.InverseTransformPoint(end)), radius / smallestScale))
            { reason = "native path can enter a rotating rotor's swept volume"; return false; }
            // Flight setup caps the complete root-centered hull at35m. Once the
            // segment is outside that envelope plus its possible displacement,
            // world colliders near a distant target cannot be relevant to this
            // own-hull query (or fill its bounded physics buffer unnecessarily).
            double rootDistance = EscortActorMotion.DistanceToSegment(MotionVector(root.position),
                MotionVector(start), MotionVector(end));
            if (double.IsNaN(rootDistance) || double.IsInfinity(rootDistance))
            { reason = "moving hull distance is invalid"; return false; }
            if (rootDistance > 35f + radius) { reason = null; return true; }
            for (int endpoint = 0; endpoint < 2; endpoint++)
            {
                int overlaps = Physics.OverlapSphereNonAlloc(endpoint == 0 ? start : end, radius, _overlaps,
                    ~0, QueryTriggerInteraction.Ignore);
                if (overlaps >= _overlaps.Length) { reason = "moving hull overlap query saturated"; return false; }
                for (int i = 0; i < overlaps; i++)
                    if (OwnPhysicalHull(_overlaps[i])) { reason = "native path can intersect the moving aircraft hull"; return false; }
            }
            Vector3 delta = end - start;
            float length = delta.magnitude;
            if (length <= 0.000001f) { reason = null; return true; }
            int count = Physics.SphereCastNonAlloc(start, radius, delta / length, _hits,
                length, ~0, QueryTriggerInteraction.Ignore);
            if (count >= _hits.Length) { reason = "moving hull sweep query saturated"; return false; }
            for (int i = 0; i < count; i++)
                if (OwnPhysicalHull(_hits[i].collider)) { reason = "native path can cross the moving aircraft hull"; return false; }
            reason = null;
            return true;
        }

        private bool OwnPhysicalHull(Collider collider)
        {
            return Physical(collider) && (collider.transform == _aircraft.transform ||
                collider.transform.IsChildOf(_aircraft.transform));
        }

        private bool PathEndpointClear(Vector3 point, float radius, Player target, out string reason)
        {
            int count = Physics.OverlapSphereNonAlloc(point, radius, _overlaps,
                BallisticsCalculatorConstants.HitMask, QueryTriggerInteraction.Collide);
            if (count >= _overlaps.Length) { reason = "native path overlap query saturated"; return false; }
            for (int i = 0; i < count; i++)
                if (Relevant(_overlaps[i]) && EscortBenchColliderOwner.Resolve(_overlaps[i], _world) != target)
                { reason = "native curved path overlaps another surface; " + DescribeCollision(_overlaps[i], 0, 0, target); return false; }
            reason = null;
            return true;
        }

        private bool MovingPlayersClear(EscortAimSolution solution, Vector3 origin, Vector3 direction,
            Ammo ammo, Player target, out string reason)
        {
            var players = _world.AllAlivePlayersList;
            if (players == null || players.Count > MaximumPlayers)
            { reason = "moving player safety registry unavailable"; return false; }
            if (ammo == null || !EscortBenchSafetyPolicy.Finite(ammo.AmmoLifeTimeSec) || ammo.AmmoLifeTimeSec <= 0f)
            { reason = "native round lifetime unavailable"; return false; }
            var path = solution.Path;
            for (int i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (player == target || !Alive(player)) continue;
                Vector3 observedVelocity;
                if (!TryVelocity(player, out observedVelocity, out reason))
                { reason = "protected actor motion unavailable: " + reason; return false; }
                Bounds bounds;
                if (!TryBodyBounds(player, out bounds))
                { reason = "another living player's body bounds unavailable"; return false; }
                for (int j = 1; j < path.Length; j++)
                {
                    // Protect every possible displacement within the verified
                    // speed bound, rather than assuming an ally keeps its heading.
                    double radius = bounds.extents.magnitude + 0.25 + EscortActorMotion.PositionSlack +
                        EscortActorMotion.MaximumSpeed * path[j].Time;
                    double separation = EscortActorMotion.DistanceToSegment(MotionVector(bounds.center),
                        MotionVector(path[j - 1].Position), MotionVector(path[j].Position));
                    if (double.IsNaN(separation) || double.IsInfinity(separation) || separation <= radius)
                    { reason = "protected actor can enter the native projectile path"; return false; }
                }
                var delta = bounds.center - origin;
                float forward = Vector3.Dot(delta, direction);
                float sideways = (delta - direction * forward).magnitude;
                // Preserve the existing beyond-target safety corridor. Widen for
                // movement near a conservative arrival time, not the entire round
                // lifetime for nearby bodies unrelated to that distant interval.
                float arrival = Mathf.Min(ammo.AmmoLifeTimeSec, Mathf.Max(0f, forward) / 300f + 0.05f);
                float bodyRadius = bounds.extents.magnitude + EscortActorMotion.PositionSlack +
                    EscortActorMotion.MaximumSpeed * arrival;
                if (EscortBenchSafetyPolicy.IntersectsProtectedCorridor(forward, sideways, bodyRadius))
                { reason = "another living player can enter the protected beyond-target corridor"; return false; }
            }
            reason = null;
            return true;
        }

        private static FlightVector MotionVector(Vector3 value) { return new FlightVector(value.x, value.y, value.z); }
    }
}
