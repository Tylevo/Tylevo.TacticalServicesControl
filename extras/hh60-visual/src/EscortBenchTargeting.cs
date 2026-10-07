using System;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.InventoryLogic;
using UnityEngine;

namespace TscHh60Visual
{
    // Local-player gun tracking and per-round authorization. This component
    // never creates projectiles, moves the aircraft, or changes AI relationships.
    internal sealed partial class EscortBenchTargeting : IDisposable
    {
        private const int MaximumPlayers = 256;
        private const int MaximumCandidates = 12;
        private const float ScanInterval = 0.2f;
        private readonly GameObject _aircraft;
        private readonly string _gunMarkerName;
        private readonly Player _caller;
        private readonly Collider[] _hull;
        private readonly RaycastHit[] _hits = new RaycastHit[128];
        private readonly Collider[] _overlaps = new Collider[64];
        private readonly Candidate[] _candidates = new Candidate[MaximumCandidates];
        private readonly EscortGunMount[] _guns = new EscortGunMount[2];
        private readonly bool _movingMode;
        private float _hullMotionSpeed = 70f;
        private readonly int _fixedGunIndex;
        private readonly EscortBenchBallistics _ballistics;
        private readonly EscortBallisticAim _aim;
        private readonly Dictionary<Player, EscortActorMotion> _motion = new Dictionary<Player, EscortActorMotion>();
        private readonly List<Player> _expiredMotion = new List<Player>();
        private GameWorld _motionWorld;
        private GameWorld _world;
        private Player _target;
        private float _untilScan;
        private int _selected, _candidateCount, _aliveAi, _hostile, _withinArc;
        private bool _disposed, _queryFailed;
        private string _scanReason = "waiting for HH60 gun geometry";
        private string _burstTargetId;
        private Vector3 _burstOffset;

        internal EscortBenchTargeting(GameObject aircraft, Player caller)
            : this(aircraft, caller, null, false) { }

        internal EscortBenchTargeting(GameObject aircraft, Player caller, EscortBenchBallistics ballistics, bool movingMode)
            : this(aircraft, caller, ballistics, movingMode, -1) { }

        internal EscortBenchTargeting(GameObject aircraft, Player caller, EscortBenchBallistics ballistics, bool movingMode, int fixedGunIndex,
            string gunMarkerName = Plugin.MarkerName)
        {
            if (fixedGunIndex < -1 || fixedGunIndex > 1) throw new ArgumentOutOfRangeException(nameof(fixedGunIndex));
            if (fixedGunIndex >= 0 && !movingMode) throw new ArgumentException("Independent guns require moving targeting.");
            _aircraft = aircraft != null ? aircraft : throw new ArgumentNullException(nameof(aircraft));
            _gunMarkerName = gunMarkerName ?? throw new ArgumentNullException(nameof(gunMarkerName));
            _caller = caller != null ? caller : throw new ArgumentNullException(nameof(caller));
            _hull = aircraft.GetComponentsInChildren<Collider>(true);
            _movingMode = movingMode;
            _fixedGunIndex = fixedGunIndex;
            _selected = fixedGunIndex >= 0 ? fixedGunIndex : 0;
            _ballistics = ballistics;
            if (movingMode && ballistics == null) throw new ArgumentNullException(nameof(ballistics));
            if (ballistics != null) _aim = new EscortBallisticAim();
            Summary = (_selected == 0 ? "Left" : "Right") +
                (movingMode ? " | waiting for moving gun tracking" : " | waiting for stationary bench");
        }

        internal string Summary { get; private set; }
        internal void SetHullMotionSpeed(float speed)
        {
            if (!EscortBenchSafetyPolicy.Finite(speed) || speed < 70f)
                throw new ArgumentOutOfRangeException(nameof(speed));
            _hullMotionSpeed = speed;
        }
        internal string EligibleTargetId { get; private set; }
        internal int SelectedGunIndex => _selected;
        internal Transform SelectedBarrel => !_disposed ? _guns[_selected]?.BarrelTransform : null;
        internal Vector3? AimPoint { get; private set; }
        internal Vector3? MuzzlePosition
        {
            get
            {
                var gun = _guns[_selected];
                return !_disposed && gun != null && gun.Available ? gun.Muzzle : (Vector3?)null;
            }
        }

        internal void Tick(float dt)
        {
            EligibleTargetId = null;
            if (_disposed) return;
            try
            {
                string reason;
                if (!Ready(out reason)) { ClearTarget(reason); return; }
                if (!EscortBenchSafetyPolicy.Finite(dt) || dt < 0f) { ClearTarget("invalid bench timestep"); return; }
                if (_ballistics != null && !UpdateMotion(out reason)) { ClearTarget(reason); return; }
                _untilScan -= Mathf.Min(dt, 1f);
                bool scanned = false;
                if (_untilScan <= 0f)
                {
                    _untilScan = ScanInterval;
                    Scan();
                    scanned = true;
                }
                if (_target == null) { ClearBurstAim(); return; }
                Vector3 point;
                if (!Alive(_target) || !TryTorso(_target, out point)) { ClearTarget("target no longer available"); _untilScan = 0f; return; }
                if (_fixedGunIndex >= 0 && !ConfirmedHostile(_target))
                { ClearTarget("target no longer a confirmed hostile AI"); _untilScan = 0f; return; }
                if (_burstTargetId != null && !string.Equals(_burstTargetId, _target.ProfileId, StringComparison.Ordinal))
                    ClearBurstAim();
                AimPoint = point;
                var gun = _guns[_selected];
                float yaw, pitch, distance;
                if (_fixedGunIndex >= 0 && !AimGeometry(gun, point, out yaw, out pitch, out distance, out reason))
                { ClearTarget(reason); _untilScan = 0f; return; }
                if (_movingMode)
                {
                    EscortAimSolution solution;
                    Ammo ammo;
                    if (!SolveMovingAim(gun, point, out ammo, out solution, out reason))
                    { SetSummary(reason); return; }
                    distance = (point - gun.Muzzle).magnitude;
                    gun.Angles(solution.Direction, out yaw, out pitch);
                    if (!EscortBenchSafetyPolicy.WithinLimits(distance, yaw, pitch, out reason))
                    { ClearTarget("predicted aim " + reason); _untilScan = 0f; return; }
                    AimPoint = solution.PredictedTarget;
                }
                else if (!AimGeometry(gun, point, out yaw, out pitch, out distance, out reason)) { ClearTarget(reason); _untilScan = 0f; return; }
                if (!string.IsNullOrEmpty(_target.ProfileId)) EligibleTargetId = _target.ProfileId;
                gun.Track(yaw, pitch, Mathf.Min(dt, 0.1f), _aircraft.transform);
                // The expensive checks run at 5Hz during observation, then again
                // synchronously for every requested shot through TryGetShot.
                if (scanned)
                {
                    Vector3 origin, direction;
                    string id;
                    Ammo unused;
                    TryGetShot(_fixedGunIndex >= 0 ? EligibleTargetId : null,
                        out origin, out direction, out id, out unused, out reason);
                }
            }
            catch (Exception error)
            {
                _queryFailed = true;
                ClearTarget("unsupported targeting query: " + error.GetType().Name);
                Plugin.Log?.LogWarning("[EscortBench] Targeting disabled; no shot authorized: " + error);
            }
        }

        internal void CycleGun()
        {
            if (_disposed || _fixedGunIndex >= 0) return;
            _guns[_selected]?.Restore();
            _selected = 1 - _selected;
            _untilScan = 0f;
            ClearTarget("gun changed; waiting for scan");
        }

        internal void Suspend(string reason)
        {
            if (_disposed) return;
            _motion.Clear();
            _untilScan = 0f;
            ClearTarget(reason ?? "tracking suspended");
        }

        internal bool SetBurstAim(string targetId, Vector3 worldOffset, out string reason)
        {
            reason = null;
            if (_disposed || _fixedGunIndex < 0 || !_movingMode || string.IsNullOrEmpty(targetId) ||
                _target == null || !string.Equals(targetId, EligibleTargetId, StringComparison.Ordinal) ||
                !string.Equals(targetId, _target.ProfileId, StringComparison.Ordinal))
            { reason = "burst target is no longer eligible"; return false; }
            if (!Finite(worldOffset) || worldOffset.sqrMagnitude > 0.6f * 0.6f)
            { reason = "burst aim offset must be finite and within0.6m"; return false; }
            _burstTargetId = targetId;
            _burstOffset = worldOffset;
            return true;
        }

        internal void ClearBurstAim()
        {
            _burstTargetId = null;
            _burstOffset = Vector3.zero;
        }

        internal bool TryGetShot(out Vector3 origin, out Vector3 direction, out string targetProfileId, out string reason)
        {
            Ammo unused;
            return TryGetShot(out origin, out direction, out targetProfileId, out unused, out reason);
        }

        internal bool TryGetShot(out Vector3 origin, out Vector3 direction, out string targetProfileId, out Ammo expectedAmmo, out string reason)
        {
            return TryGetShot(null, out origin, out direction, out targetProfileId, out expectedAmmo, out reason);
        }

        internal bool TryGetShot(string requiredTargetId, out Vector3 origin, out Vector3 direction,
            out string targetProfileId, out Ammo expectedAmmo, out string reason)
        {
            origin = direction = Vector3.zero;
            targetProfileId = null;
            expectedAmmo = null;
            reason = null;
            try
            {
                if (_disposed || !Ready(out reason)) return Reject(reason ?? "bench disposed", out reason);
                if (_target == null) return Reject(_scanReason, out reason);
                if ((_fixedGunIndex >= 0 && string.IsNullOrEmpty(requiredTargetId)) ||
                    (!string.IsNullOrEmpty(requiredTargetId) && !string.Equals(requiredTargetId, _target.ProfileId, StringComparison.Ordinal)))
                    return Reject("settled burst target changed; a new settle is required", out reason);
                var players = _world.AllAlivePlayersList;
                if (players == null || players.Count > MaximumPlayers || !players.Contains(_target))
                    return Reject("live player registry changed or exceeds bench limit", out reason);
                if (!ConfirmedHostile(_target)) return Reject("target is no longer a confirmed hostile AI", out reason);
                Vector3 point;
                if (!TryTorso(_target, out point)) return Reject("target torso unavailable", out reason);
                var gun = _guns[_selected];
                if (_movingMode)
                    return TryMovingShot(gun, point, out origin, out direction, out targetProfileId, out expectedAmmo, out reason);
                float yaw, pitch, distance;
                if (!AimGeometry(gun, point, out yaw, out pitch, out distance, out reason)) return Reject(reason, out reason);
                var muzzle = gun.Muzzle;
                var barrel = gun.Barrel;
                float actualYaw, actualPitch;
                Angles(gun, barrel, out actualYaw, out actualPitch);
                if (!EscortBenchSafetyPolicy.WithinLimits(distance, actualYaw, actualPitch, out reason))
                    return Reject("actual barrel " + reason, out reason);
                if (!EscortBenchSafetyPolicy.Aligned(Vector3.Angle(barrel, point - muzzle)))
                    return Reject("tracking; barrel has not reached torso aim", out reason);
                // Player hitboxes and the gun were just moved on the main thread;
                // do not rely on the raid's autoSyncTransforms setting.
                Physics.SyncTransforms();
                if (!MuzzleClear(gun, muzzle, barrel, out reason)) return Reject(reason, out reason);
                if (!TargetIsFirstHit(muzzle, barrel, distance + 1.5f, _target, out reason)) return Reject(reason, out reason);
                if (!FlightClearance(muzzle, barrel, distance, _target, out reason)) return Reject(reason, out reason);
                if (!OtherPlayersClear(muzzle, barrel, _target, out reason)) return Reject(reason, out reason);
                origin = muzzle;
                direction = barrel;
                targetProfileId = _target.ProfileId;
                if (string.IsNullOrEmpty(targetProfileId)) return Reject("target profile unavailable", out reason);
                AimPoint = point;
                SetSummary("READY " + ShortId(targetProfileId) + " " + distance.ToString("F0") + "m; manual shot only");
                return true;
            }
            catch (Exception error)
            {
                _queryFailed = true;
                origin = direction = Vector3.zero;
                targetProfileId = null;
                expectedAmmo = null;
                Plugin.Log?.LogWarning("[EscortBench] Shot blocked by unsupported query: " + error);
                return Reject("unsupported shot query: " + error.GetType().Name, out reason);
            }
        }

        private bool Ready(out string reason)
        {
            reason = null;
            if (_disposed || _queryFailed) { reason = "bench unavailable; reset required"; return false; }
            if (_aircraft == null || !_aircraft.activeInHierarchy || !Alive(_caller) || _caller.IsAI)
            { reason = "aircraft or live human caller unavailable"; return false; }
            _world = Singleton<GameWorld>.Instance;
            if (_world == null || _world.MainPlayer != _caller)
            { reason = "local raid owner unavailable"; return false; }
            if (_fixedGunIndex >= 0)
            {
                if (_guns[_selected] == null || !_guns[_selected].Exists) BindGuns();
            }
            else if (_guns[0] == null || !_guns[0].Exists || _guns[1] == null || !_guns[1].Exists) BindGuns();
            if (_guns[_selected] == null || !_guns[_selected].Available)
            { reason = "HH60 gun geometry is not active/ready"; return false; }
            return true;
        }

        private void BindGuns()
        {
            var marker = Plugin.FindDescendant(_aircraft.transform, _gunMarkerName);
            if (marker == null) return;
            if (_fixedGunIndex < 0 || _fixedGunIndex == 0)
                BindGun(0, marker, "left", new Vector3(0.002512f, 1.059167f, 0.111553f));
            if (_fixedGunIndex < 0 || _fixedGunIndex == 1)
                BindGun(1, marker, "right", new Vector3(0.025021f, 1.059167f, 0.111553f));
        }

        private void BindGun(int index, Transform marker, string side, Vector3 tip)
        {
            if (_guns[index] != null && _guns[index].Exists) return;
            var mount = Plugin.FindDescendant(marker, "helicopter_gun_box_" + side);
            var barrel = Plugin.FindDescendant(marker, "helicopter_gun_" + side);
            if (mount == null || barrel == null || barrel.parent != mount) return;
            _guns[index] = new EscortGunMount(mount, barrel, tip, _aircraft.transform);
        }

        private void Scan()
        {
            _aliveAi = _hostile = _withinArc = _candidateCount = 0;
            _target = null;
            AimPoint = null;
            var players = _world.AllAlivePlayersList;
            if (players == null || players.Count > MaximumPlayers)
            { ClearTarget("live player registry unavailable or exceeds 256-player limit"); return; }
            Physics.SyncTransforms();
            var gun = _guns[_selected];
            for (int i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (!Alive(player) || !player.IsAI) continue;
                _aliveAi++;
                if (!ConfirmedHostile(player)) continue;
                _hostile++;
                Vector3 torso;
                if (!TryTorso(player, out torso)) continue;
                float yaw, pitch, distance;
                string reason;
                if (!AimGeometry(gun, torso, out yaw, out pitch, out distance, out reason)) continue;
                _withinArc++;
                InsertCandidate(new Candidate { Player = player, Point = torso, Distance = distance });
            }
            _scanReason = _hostile == 0 ? "no recorded hostile AI (neutral/unknown protected)"
                : _withinArc == 0 ? "no hostile torso inside this gun's 12-200m aiming arc" : "all candidate torso lines are blocked";
            for (int i = 0; i < _candidateCount; i++)
            {
                var candidate = _candidates[i];
                string reason;
                if (!TargetIsFirstHit(gun.Muzzle, (candidate.Point - gun.Muzzle).normalized,
                    candidate.Distance + 1.5f, candidate.Player, out reason))
                { _scanReason = reason; continue; }
                _target = candidate.Player;
                AimPoint = candidate.Point;
                SetSummary("tracking " + ShortId(_target.ProfileId));
                return;
            }
            SetSummary(_scanReason);
        }

        private void InsertCandidate(Candidate candidate)
        {
            int index = _candidateCount;
            if (index == MaximumCandidates && candidate.Distance >= _candidates[index - 1].Distance) return;
            if (index == MaximumCandidates) index--;
            else _candidateCount++;
            while (index > 0 && _candidates[index - 1].Distance > candidate.Distance)
            {
                _candidates[index] = _candidates[index - 1];
                index--;
            }
            _candidates[index] = candidate;
        }

        private bool ConfirmedHostile(Player player)
        {
            if (!Alive(player) || player == _caller || !player.IsAI || player.ProfileId == _caller.ProfileId) return false;
            var owner = player.AIData?.BotOwner;
            var group = owner?.BotsGroup;
            if (owner == null || group == null) return false;
            bool sameGroup = !string.IsNullOrEmpty(_caller.GroupId)
                && string.Equals(player.GroupId, _caller.GroupId, StringComparison.Ordinal);
            bool ally = group.IsAlly(_caller) || (_caller.BotsGroup != null && _caller.BotsGroup.IsAlly(player));
            // Verified native IsEnemy only reads the existing enemy dictionary.
            // Do not call CheckAndAddEnemy/AddEnemy; even IsPlayerEnemy can reroll
            // backend random hostility through BotSettings.IsPlayerEnemy.
            return EscortBenchSafetyPolicy.ConfirmedHostile(true, true, owner.BotState == EBotState.Active,
                true, group.IsEnemy(_caller), ally, sameGroup);
        }

        private bool AimGeometry(EscortGunMount gun, Vector3 point, out float yaw, out float pitch, out float distance, out string reason)
        {
            Vector3 delta = point - gun.Muzzle;
            distance = delta.magnitude;
            Angles(gun, delta, out yaw, out pitch);
            return EscortBenchSafetyPolicy.WithinLimits(distance, yaw, pitch, out reason);
        }

        private void Angles(EscortGunMount gun, Vector3 direction, out float yaw, out float pitch)
        {
            gun.Angles(direction, out yaw, out pitch);
        }

        private bool MuzzleClear(EscortGunMount gun, Vector3 muzzle, Vector3 barrel, out string reason)
        {
            reason = null;
            int count = Physics.OverlapSphereNonAlloc(muzzle, 0.1f, _overlaps, BallisticsCalculatorConstants.HitMask, QueryTriggerInteraction.Collide);
            if (count == _overlaps.Length) { reason = "muzzle overlap query saturated"; return false; }
            for (int i = 0; i < count; i++)
                if (Relevant(_overlaps[i])) { reason = "muzzle overlaps physical geometry"; return false; }
            var shaft = muzzle - gun.BarrelTransform.position;
            var shaftRay = new Ray(gun.BarrelTransform.position, shaft.normalized);
            for (int i = 0; i < _hull.Length; i++)
            {
                var collider = _hull[i];
                if (!Physical(collider)) continue;
                RaycastHit hit;
                if (collider.Raycast(shaftRay, out hit, shaft.magnitude + 0.05f))
                { reason = "gun barrel crosses original UH60 hull"; return false; }
                var bounds = collider.bounds;
                if (!bounds.Contains(muzzle)) continue;
                // A ray starting inside a nonconvex collider can miss its backfaces.
                // Paired outside-in rays conservatively reject enclosed muzzles.
                float span = bounds.extents.magnitude * 2f + 1f;
                if (collider.Raycast(new Ray(muzzle - barrel * span, barrel), out hit, span + 0.05f)
                    && collider.Raycast(new Ray(muzzle + barrel * span, -barrel), out hit, span + 0.05f))
                { reason = "muzzle is enclosed by original UH60 hull"; return false; }
            }
            return true;
        }

        private bool TargetIsFirstHit(Vector3 origin, Vector3 direction, float length, Player target, out string reason)
        {
            int count = Physics.RaycastNonAlloc(origin, direction, _hits, length, BallisticsCalculatorConstants.HitMask, QueryTriggerInteraction.Collide);
            if (count == _hits.Length) { reason = "body line-of-sight query saturated"; return false; }
            int nearest = Nearest(count);
            if (nearest < 0)
            { reason = "torso ray missed target body; " + DescribeTargetBody(target); return false; }
            var collider = _hits[nearest].collider;
            if (EscortBenchColliderOwner.Resolve(collider, _world) != target)
            {
                reason = (collider.transform.IsChildOf(_aircraft.transform) ? "original UH60 hull blocks torso line" : "world geometry or another body blocks torso line")
                    + "; " + DescribeCollision(collider, _hits[nearest].distance, length - 1.5f, target)
                    + "; " + DescribeTargetBody(target);
                return false;
            }
            reason = null;
            return true;
        }

        private bool FlightClearance(Vector3 origin, Vector3 direction, float distance, Player target, out string reason)
        {
            // Swept clearance, not an alternate damage simulation. The projectile
            // layer fires along the unchanged actual barrel axis with native drop.
            int segments = Mathf.CeilToInt(distance / 25f);
            for (int i = 0; i < segments; i++)
            {
                float from = i * 25f, length = Mathf.Min(25f, distance - from);
                float radius = EscortBenchSafetyPolicy.WorldClearance(from + length);
                var segmentOrigin = origin + direction * from;
                int overlaps = Physics.OverlapSphereNonAlloc(segmentOrigin, radius, _overlaps,
                    BallisticsCalculatorConstants.HitMask, QueryTriggerInteraction.Collide);
                if (overlaps == _overlaps.Length) { reason = "flight-overlap query saturated"; return false; }
                for (int j = 0; j < overlaps; j++)
                    if (Relevant(_overlaps[j]) && EscortBenchColliderOwner.Resolve(_overlaps[j], _world) != target)
                    {
                        reason = "native projectile clearance starts inside another surface; "
                            + DescribeCollision(_overlaps[j], from, distance, target);
                        return false;
                    }
                int count = Physics.SphereCastNonAlloc(segmentOrigin, radius, direction,
                    _hits, length, BallisticsCalculatorConstants.HitMask, QueryTriggerInteraction.Collide);
                if (count == _hits.Length) { reason = "flight-clearance query saturated"; return false; }
                for (int j = 0; j < count; j++)
                    if (Relevant(_hits[j].collider) && EscortBenchColliderOwner.Resolve(_hits[j].collider, _world) != target)
                    {
                        reason = "insufficient clearance around native projectile path; "
                            + DescribeCollision(_hits[j].collider, from + _hits[j].distance, distance, target);
                        return false;
                    }
                // Do not stop at the first target body part: the rest of the
                // corridor to the aim point must remain clear of other surfaces.
            }
            reason = null;
            return true;
        }

        private bool OtherPlayersClear(Vector3 origin, Vector3 direction, Player target, out string reason)
        {
            var players = _world.AllAlivePlayersList;
            if (players == null || players.Count > MaximumPlayers) { reason = "player safety registry unavailable"; return false; }
            for (int i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (player == target || !Alive(player)) continue;
                Bounds bounds;
                if (!TryBodyBounds(player, out bounds)) { reason = "another living player's body bounds unavailable"; return false; }
                var delta = bounds.center - origin;
                float forward = Vector3.Dot(delta, direction);
                float sideways = (delta - direction * forward).magnitude;
                if (!EscortBenchSafetyPolicy.IntersectsProtectedCorridor(forward, sideways, bounds.extents.magnitude)) continue;
                reason = player == _caller ? "caller inside protected flight corridor"
                    : !player.IsAI ? "human inside protected flight corridor"
                    : "another AI inside protected flight corridor (including beyond target)";
                return false;
            }
            reason = null;
            return true;
        }

        private static bool TryBodyBounds(Player player, out Bounds bounds)
        {
            bounds = default;
            var parts = player.PlayerBones?.BodyPartColliders;
            if (parts == null || parts.Length == 0 || parts.Length > 64) return false;
            bool found = false;
            for (int i = 0; i < parts.Length; i++)
            {
                var collider = parts[i]?.Collider;
                // Trigger body parts still protect a player; do not discard them
                // merely because a particular native rig marks them as triggers.
                if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy) continue;
                var item = collider.bounds;
                if (!Finite(item.center) || !Finite(item.extents)) return false;
                if (!found) { bounds = item; found = true; }
                else bounds.Encapsulate(item);
            }
            return found;
        }

        private int Nearest(int count)
        {
            int result = -1;
            float distance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
                if (Relevant(_hits[i].collider) && _hits[i].distance < distance)
                { distance = _hits[i].distance; result = i; }
            return result;
        }

        private bool Relevant(Collider collider)
        {
            if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy) return false;
            if (!collider.isTrigger) return true;
            // Native Shot uses the global trigger setting. Explicitly include
            // player/ballistic triggers here without letting unrelated zones
            // become false body blockers; no global physics setting is changed.
            return EscortBenchSafetyPolicy.RelevantCollision(true,
                EscortBenchColliderOwner.Resolve(collider, _world) != null, collider.GetComponent<BaseBallistic>() != null);
        }

        private string DescribeCollision(Collider collider, float hitDistance, float targetDistance, Player target)
        {
            var owner = EscortBenchColliderOwner.Resolve(collider, _world);
            var node = collider.transform;
            string path = node.name;
            for (int i = 0; i < 3 && node.parent != null; i++)
            {
                node = node.parent;
                path = node.name + "/" + path;
            }
            return "hit=" + path + "#" + collider.GetInstanceID()
                + " layer=" + collider.gameObject.layer + "/" + LayerMask.LayerToName(collider.gameObject.layer)
                + " owner=" + (owner != null ? ShortId(owner.ProfileId) : "none")
                + " target=" + ShortId(target.ProfileId)
                + " at=" + hitDistance.ToString("F1") + "/" + targetDistance.ToString("F1") + "m";
        }

        private static string DescribeTargetBody(Player target)
        {
            var parts = target.PlayerBones?.BodyPartColliders;
            int count = parts != null ? parts.Length : 0, enabled = 0, active = 0;
            if (parts != null)
                for (int i = 0; i < Math.Min(parts.Length, 64); i++)
                {
                    var collider = parts[i]?.Collider;
                    if (collider == null) continue;
                    if (collider.enabled) enabled++;
                    if (collider.enabled && collider.gameObject.activeInHierarchy) active++;
                }
            return "body enabled/active/total=" + enabled + "/" + active + "/" + count
                + " spirit=" + (target.Spirit != null && target.Spirit.IsActive);
        }

        private static bool Alive(Player player)
        {
            return player != null && player.gameObject.activeInHierarchy && player.HealthController != null && player.HealthController.IsAlive;
        }

        private static bool TryTorso(Player player, out Vector3 point)
        {
            point = Vector3.zero;
            var bones = player.PlayerBones;
            if (bones == null || bones.Ribcage == null) return false;
            point = bones.Ribcage.position;
            return Finite(point) && Finite(player.Position) && (point - player.Position).sqrMagnitude < 16f;
        }

        private static bool Finite(Vector3 point)
        {
            return EscortBenchSafetyPolicy.Finite(point.x) && EscortBenchSafetyPolicy.Finite(point.y) && EscortBenchSafetyPolicy.Finite(point.z);
        }

        private static bool Physical(Collider collider)
        {
            return collider != null && collider.enabled && !collider.isTrigger && collider.gameObject.activeInHierarchy;
        }

        private bool Reject(string message, out string reason)
        {
            reason = message ?? "shot not authorized";
            SetSummary(reason);
            return false;
        }

        private void ClearTarget(string reason)
        {
            _target = null;
            EligibleTargetId = null;
            ClearBurstAim();
            AimPoint = null;
            _scanReason = reason;
            SetSummary(reason);
        }

        private void SetSummary(string state)
        {
            Summary = (_selected == 0 ? "Left" : "Right") + " | alive AI " + _aliveAi + ", hostile " + _hostile
                + ", in arc " + _withinArc + " | " + state;
        }

        private static string ShortId(string id)
        {
            return string.IsNullOrEmpty(id) ? "unknown" : id.Substring(Math.Max(0, id.Length - 8));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _aim?.Dispose();
            _motion.Clear();
            for (int i = 0; i < _guns.Length; i++) _guns[i]?.Restore();
            _target = null;
            EligibleTargetId = null;
            ClearBurstAim();
            AimPoint = null;
            Summary = "Bench targeting disposed";
        }

        private struct Candidate
        {
            internal Player Player;
            internal Vector3 Point;
            internal float Distance;
        }

    }
}
