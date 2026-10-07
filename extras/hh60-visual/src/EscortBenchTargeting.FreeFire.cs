using System;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;

namespace TscHh60Visual
{
    internal sealed partial class EscortBenchTargeting
    {
        // Held-trigger authorization is independent of acquisition. The emitter
        // receives exactly this achieved barrel and cached ammunition snapshot.
        internal bool TryGetFreeShot(out Vector3 origin, out Vector3 direction,
            out Ammo expectedAmmo, out string reason)
        {
            origin = direction = Vector3.zero;
            expectedAmmo = null;
            reason = null;
            try
            {
                if (_disposed || !Ready(out reason)) return Reject(reason ?? "bench disposed", out reason);
                if (_ballistics == null || _aim == null) return Reject("held-trigger emitter unavailable", out reason);
                var gun = _guns[_selected];
                Vector3 muzzle = gun.Muzzle, barrel = gun.Barrel;
                if (!Finite(muzzle) || !Finite(barrel) || barrel.sqrMagnitude < 0.999f || barrel.sqrMagnitude > 1.001f)
                    return Reject("current barrel geometry is invalid", out reason);
                float yaw, pitch;
                gun.Angles(barrel, out yaw, out pitch);
                if (!EscortBenchSafetyPolicy.WithinLimits(EscortBenchSafetyPolicy.MaximumDistance, yaw, pitch, out reason))
                    return Reject("actual barrel " + reason, out reason);
                if (!UpdateMotion(out reason, true)) return Reject(reason, out reason);
                var ammo = _ballistics.PeekNextRound();
                if (ammo == null) return Reject("next native round unavailable", out reason);
                EscortAimSolution path;
                if (!_aim.TryTrace(ammo, muzzle, barrel, EscortBenchSafetyPolicy.MaximumDistance, out path, out reason))
                    return Reject("held-trigger native path: " + reason, out reason);
                Physics.SyncTransforms();
                if (!MuzzleClear(gun, muzzle, barrel, out reason)) return Reject(reason, out reason);
                if (!FreeFlightHullClear(path, out reason)) return Reject(reason, out reason);
                if (!FreeProtectedPlayersClear(path, muzzle, barrel, ammo, out reason)) return Reject(reason, out reason);
                origin = muzzle;
                direction = barrel;
                expectedAmmo = ammo;
                SetSummary("READY selected barrel; held-trigger round checked");
                return true;
            }
            catch (Exception error)
            {
                _queryFailed = true;
                origin = direction = Vector3.zero;
                expectedAmmo = null;
                Plugin.Log?.LogWarning("[EscortBench] Held-trigger shot blocked by unsupported query: " + error);
                return Reject("unsupported held-trigger query: " + error.GetType().Name, out reason);
            }
        }

        private bool FreeFlightHullClear(EscortAimSolution solution, out string reason)
        {
            var path = solution.Path;
            if (path == null || path.Length < 2 || path.Length > 1024 || path[0].Time != 0f || !Finite(path[0].Position))
            { reason = "held-trigger native path unavailable"; return false; }
            for (int i = 1; i < path.Length; i++)
            {
                var from = path[i - 1];
                var to = path[i];
                if (!Finite(to.Position) || !EscortBenchSafetyPolicy.Finite(to.Time) || to.Time <= from.Time || to.Time > 1f)
                { reason = "held-trigger native timing invalid"; return false; }
                Vector3 delta = to.Position - from.Position;
                float length = delta.magnitude;
                if (!EscortBenchSafetyPolicy.Finite(length) || length <= 0.000001f || length > 25f)
                { reason = "held-trigger native segment unsupported"; return false; }
                // A body, armor plate or ordinary surface can be penetrated.
                // Keep own-hull/rotor checks over the entire native trace; these
                // queries ignore other world geometry, so terrain impacts remain
                // allowed without classifying any contact as a terminal stop.
                if (_movingMode)
                {
                    if (!MovingHullClear(from.Position, to.Position, from.Time, to.Time, out reason)) return false;
                }
                else if (!StationaryFreeHullClear(from.Position, to.Position, out reason)) return false;
            }
            reason = null;
            return true;
        }

        private bool StationaryFreeHullClear(Vector3 start, Vector3 end, out string reason)
        {
            const float radius = 0.1f;
            Transform root = _aircraft.transform;
            Vector3 scale = root.lossyScale;
            float smallestScale = Mathf.Min(scale.x, Mathf.Min(scale.y, scale.z));
            if (!Finite(scale) || smallestScale <= 0f)
            { reason = "stationary rotor safety scale unavailable"; return false; }
            if (EscortRotorSafety.Intersects(MotionVector(root.InverseTransformPoint(start)),
                MotionVector(root.InverseTransformPoint(end)), radius / smallestScale))
            { reason = "held-trigger path crosses a rotating rotor's swept volume"; return false; }
            for (int endpoint = 0; endpoint < 2; endpoint++)
            {
                int count = Physics.OverlapSphereNonAlloc(endpoint == 0 ? start : end, radius, _overlaps,
                    ~0, QueryTriggerInteraction.Ignore);
                if (count >= _overlaps.Length) { reason = "held-trigger hull overlap query saturated"; return false; }
                for (int i = 0; i < count; i++)
                    if (OwnPhysicalHull(_overlaps[i])) { reason = "held-trigger path intersects the aircraft hull"; return false; }
            }
            Vector3 delta = end - start;
            float length = delta.magnitude;
            if (length > 0.000001f)
            {
                int count = Physics.SphereCastNonAlloc(start, radius, delta / length, _hits,
                    length, ~0, QueryTriggerInteraction.Ignore);
                if (count >= _hits.Length) { reason = "held-trigger hull sweep query saturated"; return false; }
                for (int i = 0; i < count; i++)
                    if (OwnPhysicalHull(_hits[i].collider)) { reason = "held-trigger path crosses the aircraft hull"; return false; }
            }
            reason = null;
            return true;
        }

        private bool FreeProtectedPlayersClear(EscortAimSolution solution, Vector3 origin,
            Vector3 direction, Ammo ammo, out string reason)
        {
            var players = _world.AllAlivePlayersList;
            if (players == null || players.Count > MaximumPlayers)
            { reason = "held-trigger player safety registry unavailable"; return false; }
            if (ammo == null || !EscortBenchSafetyPolicy.Finite(ammo.AmmoLifeTimeSec) || ammo.AmmoLifeTimeSec <= 0f)
            { reason = "held-trigger round lifetime unavailable"; return false; }
            var path = solution.Path;
            for (int i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (!Alive(player) || ConfirmedHostile(player)) continue;
                Vector3 velocity;
                if (!TryVelocity(player, out velocity, out reason))
                { reason = "protected actor motion unavailable: " + reason; return false; }
                Bounds bounds;
                if (!TryBodyBounds(player, out bounds))
                { reason = "protected actor body bounds unavailable"; return false; }
                for (int j = 1; j < path.Length; j++)
                {
                    double radius = bounds.extents.magnitude + 0.25 + EscortActorMotion.PositionSlack +
                        EscortActorMotion.MaximumSpeed * path[j].Time;
                    double separation = EscortActorMotion.DistanceToSegment(MotionVector(bounds.center),
                        MotionVector(path[j - 1].Position), MotionVector(path[j].Position));
                    if (double.IsNaN(separation) || double.IsInfinity(separation) || separation <= radius)
                    { reason = "protected actor can enter the held-trigger native path"; return false; }
                }
                Vector3 delta = bounds.center - origin;
                float forward = Vector3.Dot(delta, direction);
                float sideways = (delta - direction * forward).magnitude;
                float arrival = Mathf.Min(ammo.AmmoLifeTimeSec, Mathf.Max(0f, forward) / 300f + 0.05f);
                float bodyRadius = bounds.extents.magnitude + EscortActorMotion.PositionSlack +
                    EscortActorMotion.MaximumSpeed * arrival;
                if (EscortBenchSafetyPolicy.IntersectsProtectedCorridor(forward, sideways, bodyRadius))
                { reason = "protected actor can enter the held-trigger beyond-impact corridor"; return false; }
            }
            reason = null;
            return true;
        }
    }
}
