using System;
using System.Globalization;
using EFT;
using UnityEngine;

namespace TscHh60Visual
{
    public enum EscortPatrolPattern { Mixed, Orbit, Racetrack }

    // Shared local patrol controller. All movement belongs to the outer aircraft
    // root; native body correction, rotor nodes, colliders and weapons are intact.
    internal sealed class EscortFlight
    {
        private const float VerifiedEnvelopeRadius = 12f;
        private const float CenterDeadband = 15f;
        private const float CenterSpeed = 8f;
        private const float Acceleration = 7f;
        private const float TurnRate = 30f * Mathf.Deg2Rad;
        private const float ClimbSpeed = 8f;
        private const float DescentSpeed = 4f;
        private const float MaxBlockedSeconds = 12f;
        private readonly Transform _aircraft;
        private readonly Player _caller;
        private readonly float _radius, _height, _speed, _duration, _envelope;
        private readonly EscortPatrolPattern _pattern;
        private readonly RaycastHit[] _castHits = new RaycastHit[96];
        private RaycastHit[] _altitudeHits = new RaycastHit[96];
        private readonly Collider[] _overlaps = new Collider[96];
        private Vector3 _center, _velocity, _lastPosition, _departureStart, _patrolAxis;
        private float _yaw, _bank, _direction, _elapsed, _departureTime, _departureYaw;
        private float _clearanceTimer, _requiredAltitude, _blockedSeconds;
        private float _avoidYaw, _avoidUntil, _nextAvoidSearch;
        private float _halfLeg, _patternTime, _callerDistance;
        private bool _departing, _finished, _groundKnown, _transiting, _racetrack;
        private bool _trackingAvailable;
        private string _status = "Joining patrol orbit";
        private string _groundDiagnostic = "not sampled";

        internal EscortFlight(GameObject aircraft, Player caller, float radius = 100f,
            float height = 65f, float speed = 30f, float duration = 180f,
            EscortPatrolPattern pattern = EscortPatrolPattern.Mixed)
        {
            if (aircraft == null || caller == null) throw new ArgumentNullException("A patrol aircraft and caller are required.");
            if (!Finite(radius) || !Finite(height) || !Finite(speed) || !Finite(duration))
                throw new ArgumentException("Patrol settings must be finite.");
            _aircraft = aircraft.transform;
            _caller = caller;
            _radius = Mathf.Clamp(radius, 70f, 200f);
            _height = Mathf.Clamp(height, 45f, 150f);
            _speed = Mathf.Clamp(speed, 5f, 40f);
            _duration = Mathf.Clamp(duration, 10f, 600f);
            if (!Enum.IsDefined(typeof(EscortPatrolPattern), pattern)) throw new ArgumentOutOfRangeException(nameof(pattern));
            _pattern = pattern;
            _racetrack = pattern != EscortPatrolPattern.Orbit;
            if (!Finite(_aircraft.position) || !Finite(caller.Transform.Original.position))
                throw new ArgumentException("Initial patrol positions must be finite.");
            Vector3 scale = _aircraft.lossyScale;
            if (!Finite(scale) || scale.x <= 0 || scale.y <= 0 || scale.z <= 0)
                throw new ArgumentException("The patrol aircraft has an unsupported scale.");
            Physics.SyncTransforms();
            _envelope = MeasureEnvelope(aircraft, Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z)));
            if (!CanOccupy(_aircraft.position))
                throw new InvalidOperationException("The initial patrol clearance envelope is obstructed.");
            _center = Horizontal(caller.Transform.Original.position);
            _lastPosition = _aircraft.position;
            Vector3 forward = Horizontal(_aircraft.forward);
            if (!Finite(forward) || forward.sqrMagnitude < 0.0001f)
                throw new ArgumentException("The patrol aircraft must start with a valid upright heading.");
            forward.Normalize();
            _yaw = Mathf.Atan2(forward.x, forward.z);
            _patrolAxis = forward;
            Vector3 radial = Horizontal(_aircraft.position) - _center;
            _callerDistance = radial.magnitude;
            if (radial.sqrMagnitude < 1f) radial = Vector3.Cross(forward, Vector3.up) * _radius;
            // Prefer the orbit direction already closest to the initial heading.
            _direction = Vector3.Dot(Vector3.Cross(Vector3.up, radial), forward) >= 0 ? 1f : -1f;
            _requiredAltitude = _aircraft.position.y;
            Plugin.Log?.LogInfo("[EscortBench] Patrol prepared: radius=" + _radius +
                "m height=" + _height + "m speed=" + _speed + "m/s duration=" + _duration +
                "s rootEnvelope=" + _envelope + "m pattern=" + _pattern +
                " transitEnter=" + TransitEnterDistance + "m transitExit=" + TransitExitDistance +
                "m. Weapon authorization is handled separately by the selected test mode.");
        }

        internal string Status => _status;
        internal string Telemetry => string.Format(CultureInfo.InvariantCulture,
            "speed={0:F1}m/s orbitDistance={1:F1}m desiredRadius={2:F0}m altitudeError={3:F1}m ground={4} route={5} callerDistance={6:F1}m halfLeg={7:F1}m",
            _velocity.magnitude, (Horizontal(_lastPosition) - _center).magnitude, _radius,
            _requiredAltitude + (_departing ? 8f : 0f) - _lastPosition.y, _groundDiagnostic,
            PatrolPhase, _callerDistance, _halfLeg);
        internal bool IsDeparting => _departing;
        internal bool CanTrackTargets => _trackingAvailable && !_departing && !_transiting && !_finished;
        internal bool IsTransiting => _transiting;
        internal string PatrolPhase => _departing ? "Departure" : _transiting ? "Transit" : _racetrack ? "Racetrack" : "Orbit";
        private float TransitEnterDistance => Mathf.Max(300f, _radius * 3f);
        private float TransitExitDistance => _radius * 1.5f;

        // False requests caller-owned cleanup. Never translates to an unchecked
        // position, catches query failures, and never catches up a long frame.
        internal bool Tick(float dt)
        {
            _trackingAvailable = false;
            if (_finished) return false;
            if (_aircraft == null || _caller == null || _caller.Destroyed) return Finish("Patrol ended: aircraft or caller unavailable");
            dt = EscortFlightMath.LimitDeltaTime(dt);
            if (dt == 0) return true;
            try
            {
                Vector3 position = _aircraft.position;
                Vector3 callerPosition = _caller.Transform.Original.position;
                if (!Finite(position) || !Finite(callerPosition) || !Finite(_velocity))
                    return Finish("Patrol aborted: non-finite flight state");
                if ((position - _lastPosition).sqrMagnitude > 4f)
                    return Finish("Patrol aborted: aircraft root moved outside the flight controller");
                _elapsed += dt;
                if (!_departing && _elapsed >= _duration) BeginDeparture();
                if (_departing && ((_elapsed - _departureTime >= 15f && (position - _departureStart).sqrMagnitude >= 40000f) ||
                    _elapsed - _departureTime >= 25f)) return Finish("Patrol departure complete");

                UpdatePatrol(position, callerPosition, dt);
                Physics.SyncTransforms();
                // The spherical envelope covers every yaw/bank/rotor angle. An
                // already-overlapping starting pose has no proven escape sweep.
                if (!CanOccupy(position)) return Hold(dt, "Aircraft clearance envelope is obstructed");

                Vector3 guidance = _departing ? Heading(_departureYaw) : _transiting
                    ? (Horizontal(callerPosition) - Horizontal(position)).normalized : PatrolGuidance(position);
                float desiredYaw = Mathf.Atan2(guidance.x, guidance.z);
                _clearanceTimer -= dt;
                if (_clearanceTimer <= 0)
                {
                    _clearanceTimer = 0.25f;
                    _groundKnown = SampleAltitude(position, Heading(_yaw), callerPosition.y, out float sampledAltitude);
                    _requiredAltitude = sampledAltitude;
                }

                float altitudeError = _requiredAltitude + (_departing ? 8f : 0f) - position.y;
                float desiredVertical = Mathf.Clamp(altitudeError * 0.4f, -DescentSpeed, ClimbSpeed);
                // Ground sampling chooses cruise height; the full-aircraft
                // occupancy/sweep below authorizes every move. A missing distant
                // ground sample cannot freeze a clear patrol or its recall.
                if (!_groundKnown) desiredVertical = Mathf.Max(0f, desiredVertical);
                float lookahead = Mathf.Max(_envelope * 2f, _speed * 1.5f);
                bool forwardClear = CanMove(position, position + Heading(_yaw) * lookahead);
                if (!forwardClear && _elapsed >= _nextAvoidSearch && _elapsed >= _avoidUntil)
                {
                    _nextAvoidSearch = _elapsed + 0.75f;
                    float escapeYaw;
                    if (FindOpenHeading(position, lookahead, out escapeYaw))
                    {
                        _avoidYaw = escapeYaw;
                        _avoidUntil = _elapsed + 5f;
                    }
                }
                if (_elapsed < _avoidUntil) desiredYaw = _avoidYaw;
                float previousYaw = _yaw;
                float nextYaw = EscortFlightMath.TurnHeading(_yaw, desiredYaw, TurnRate, dt);
                float remainingTurn = Mathf.Abs(EscortFlightMath.WrapRadians(desiredYaw - nextYaw));
                // Slow for large heading changes and climb before advancing at a
                // tall obstacle. The per-step sweep is still the final authority.
                float turnScale = Mathf.Clamp01(1f - remainingTurn / (100f * Mathf.Deg2Rad));
                float climbScale = Mathf.Clamp01(1f - Mathf.Max(0, altitudeError - 10f) / 40f);
                float desiredSpeed = forwardClear ? CruiseSpeed(position) * turnScale * climbScale : 0f;
                Vector3 desiredVelocity = Heading(nextYaw) * desiredSpeed + Vector3.up * desiredVertical;
                Vector3 nextVelocity = Unity(EscortFlightMath.MoveVelocity(Math(_velocity), Math(desiredVelocity), Acceleration, dt));
                // Stop residual descent too, including between 4 Hz samples.
                if (!_groundKnown) nextVelocity.y = Mathf.Max(0f, nextVelocity.y);
                Vector3 nextPosition = position + nextVelocity * dt;
                bool moved = false;
                if (Finite(nextPosition) && CanMove(position, nextPosition))
                {
                    moved = (nextPosition - position).sqrMagnitude > 0.000001f;
                    _velocity = nextVelocity;
                }
                else
                {
                    // A safety stop can brake more sharply than ordinary flight.
                    // Try only a checked vertical climb; never nudge through a wall.
                    float climb = Mathf.MoveTowards(Mathf.Max(0, _velocity.y), Mathf.Max(0, desiredVertical), Acceleration * dt);
                    Vector3 upward = position + Vector3.up * climb * dt;
                    if (climb > 0.01f && CanMove(position, upward))
                    {
                        nextPosition = upward;
                        _velocity = Vector3.up * climb;
                        moved = true;
                    }
                    else { nextPosition = position; _velocity = Vector3.zero; }
                }
                _yaw = nextYaw;
                float horizontalSpeed = Horizontal(_velocity).magnitude;
                float turnPerSecond = EscortFlightMath.WrapRadians(_yaw - previousYaw) / dt;
                float desiredBank = horizontalSpeed > 1f ? Mathf.Clamp(-turnPerSecond * horizontalSpeed * 2f, -18f, 18f) : 0;
                _bank = Mathf.MoveTowards(_bank, desiredBank, 12f * dt);
                _aircraft.SetPositionAndRotation(nextPosition,
                    Quaternion.LookRotation(Heading(_yaw), Vector3.up) * Quaternion.AngleAxis(_bank, Vector3.forward));
                _lastPosition = nextPosition;
                _callerDistance = (Horizontal(nextPosition) - Horizontal(callerPosition)).magnitude;
                if (moved) _blockedSeconds = 0;
                else _blockedSeconds += dt;
                if (_blockedSeconds >= MaxBlockedSeconds) return Finish("Patrol aborted: no clear movement route");
                _trackingAvailable = moved && forwardClear && !_departing && !_transiting;
                _status = !forwardClear ? "Holding or climbing for obstacle clearance" : _departing ? "Departing" :
                    _transiting ? "Flying to the caller's new area" :
                    _halfLeg > 10f ? "Patrolling with broadside passes and rounded turns" : "Orbiting the caller's area";
                return true;
            }
            catch (Exception error)
            {
                Plugin.Log?.LogWarning("[EscortBench] Patrol stopped after a clearance/flight error: " + error.GetBaseException().Message);
                return Finish("Patrol aborted: flight or clearance query failed");
            }
        }

        internal void BeginDeparture()
        {
            if (_departing || _finished) return;
            _departing = true;
            _trackingAvailable = false;
            _transiting = false;
            _departureTime = _elapsed;
            _departureStart = _aircraft == null ? _lastPosition : _aircraft.position;
            _departureYaw = _yaw;
            _avoidUntil = 0;
            _status = "Departing";
        }

        private void UpdatePatrol(Vector3 position, Vector3 callerPosition, float dt)
        {
            Vector3 callerArea = Horizontal(callerPosition);
            _callerDistance = (Horizontal(position) - callerArea).magnitude;
            if (_departing) return;
            if (!_transiting && _callerDistance > TransitEnterDistance)
            {
                _transiting = true;
                Plugin.Log?.LogInfo("[EscortBench] Patrol leaving its pattern to catch up with the caller.");
            }
            if (_transiting)
            {
                if (_callerDistance >= TransitExitDistance) return;
                // Only the guidance center moves instantly. Actual pose, heading,
                // bank and velocity remain continuous through the normal limits.
                _transiting = false;
                _center = callerArea;
                _halfLeg = 0f;
                _patternTime = 0f;
                _racetrack = _pattern != EscortPatrolPattern.Orbit;
                _patrolAxis = Heading(_yaw);
                Plugin.Log?.LogInfo("[EscortBench] Caller area reached; re-establishing " + _pattern + " patrol.");
            }
            _center = Unity(EscortFlightMath.MoveCenter(Math(_center), Math(callerArea), CenterDeadband, CenterSpeed, dt));
            _patternTime += dt;
            if (_pattern == EscortPatrolPattern.Mixed && _patternTime >= (_racetrack ? 70f : 35f))
            {
                _patternTime = 0f;
                _racetrack = !_racetrack;
                // The track axis changes only while the shape is circular.
                if (_racetrack) _patrolAxis = Heading(_yaw);
                Plugin.Log?.LogInfo("[EscortBench] Patrol transitioning to " + (_racetrack ? "broadside passes" : "orbit") + ".");
            }
            // A circle is a racetrack with a zero-length centerline. Extending
            // that line gradually avoids a discontinuous path/waypoint switch.
            _halfLeg = Mathf.MoveTowards(_halfLeg, _racetrack ? _radius * 1.1f : 0f, _radius * 0.16f * dt);
        }

        private Vector3 PatrolGuidance(Vector3 position)
        {
            Vector3 offset = Horizontal(position) - _center;
            float along = Vector3.Dot(offset, _patrolAxis);
            Vector3 radial = offset - _patrolAxis * Mathf.Clamp(along, -_halfLeg, _halfLeg);
            float distance = radial.magnitude;
            Vector3 outward = distance > 0.1f ? radial / distance : Vector3.Cross(Heading(_yaw), Vector3.up) * _direction;
            Vector3 tangent = Vector3.Cross(Vector3.up, outward) * _direction;
            float correction = Mathf.Clamp((distance - _radius) / (_radius * 0.5f), -0.85f, 0.85f);
            return (tangent - outward * correction).normalized;
        }

        private float CruiseSpeed(Vector3 position)
        {
            if (_departing || _transiting || _halfLeg < 0.01f) return _speed;
            float along = Mathf.Abs(Vector3.Dot(Horizontal(position) - _center, _patrolAxis));
            // Leave acceleration available to round the caps; speed returns on
            // the long legs and during transit. Begin slowing before the bend.
            float capWeight = 1f - Mathf.Clamp01((_halfLeg - along) / 60f);
            float turnSpeed = Mathf.Min(_speed, Mathf.Sqrt(Acceleration * _radius * 0.8f));
            return Mathf.Lerp(_speed, turnSpeed, capWeight);
        }

        private bool SampleAltitude(Vector3 position, Vector3 forward, float callerY, out float altitude)
        {
            // Preserve the verified flight level and previous target when the
            // map has gaps, dense detail, or an unusable remote cast contact.
            altitude = Mathf.Max(position.y, _requiredAltitude);
            float highest = float.NegativeInfinity;
            float lookahead = Mathf.Max(_envelope * 2f, _speed * 3f);
            float startY = Mathf.Max(position.y, callerY) + 250f;
            int validSamples = 0;
            bool complete = true;
            string counts = string.Empty, detail = null;
            for (int sample = 0; sample < 3; sample++)
            {
                Vector3 probe = position + forward * (lookahead * sample * 0.5f);
                probe.y = startY;
                int count;
                // This is a broad map-height query, not the short movement
                // sweep. Retry dense results with a separate bounded buffer.
                while (true)
                {
                    count = Physics.SphereCastNonAlloc(probe, _envelope, Vector3.down, _altitudeHits,
                        800f, ~0, QueryTriggerInteraction.Ignore);
                    if (count < _altitudeHits.Length || _altitudeHits.Length >= 1536) break;
                    _altitudeHits = new RaycastHit[_altitudeHits.Length * 4];
                }
                counts += (sample == 0 ? "" : "/") + count;
                if (count >= _altitudeHits.Length)
                {
                    complete = false;
                    if (detail == null) detail = "probe=" + sample + " saturated=" + count;
                    // NonAlloc does not promise nearest hits when saturated.
                    // No downward target can be justified from this subset.
                    continue;
                }
                bool found = false;
                float sampleHeight = float.NegativeInfinity;
                for (int i = 0; i < count; i++)
                {
                    var hit = _altitudeHits[i];
                    if (Own(hit.collider)) continue;
                    if (hit.collider == null || !Finite(hit.distance) || hit.distance <= 0.001f || !Finite(hit.point))
                    {
                        complete = false;
                        if (detail == null) detail = "probe=" + sample + " unusable=" +
                            (hit.collider == null ? "null" : hit.collider.name + ":layer" + hit.collider.gameObject.layer) +
                            " distance=" + hit.distance.ToString("F3", CultureInfo.InvariantCulture) + " point=" + hit.point;
                        continue;
                    }
                    sampleHeight = Mathf.Max(sampleHeight, hit.point.y);
                    found = true;
                }
                if (!found)
                {
                    complete = false;
                    if (detail == null) detail = "probe=" + sample + " no external surface";
                    continue;
                }
                validSamples++;
                highest = Mathf.Max(highest, sampleHeight);
            }
            complete &= validSamples == 3;
            if (validSamples > 0)
            {
                float target = highest + Mathf.Max(_height, _envelope + 10f);
                if (Finite(target)) altitude = complete ? target : Mathf.Max(altitude, target);
                else { complete = false; detail = "non-finite ground height"; }
            }
            _groundDiagnostic = (complete ? "complete" : "partial/no-descent") + " " + validSamples +
                "/3 hits=" + counts + (detail == null ? "" : " " + detail);
            return complete;
        }

        private bool FindOpenHeading(Vector3 position, float distance, out float yaw)
        {
            for (int i = 1; i <= 4; i++)
                for (int side = -1; side <= 1; side += 2)
                {
                    float candidate = EscortFlightMath.WrapRadians(_yaw + side * i * 45f * Mathf.Deg2Rad);
                    if (CanMove(position, position + Heading(candidate) * distance)) { yaw = candidate; return true; }
                }
            yaw = _yaw;
            return false;
        }

        private bool CanMove(Vector3 start, Vector3 end)
        {
            if (!Finite(start) || !Finite(end)) return false;
            Vector3 delta = end - start;
            float distance = delta.magnitude;
            if (!Finite(distance)) return false;
            if (distance > 0.0001f)
            {
                int count = Physics.SphereCastNonAlloc(start, _envelope, delta / distance, _castHits,
                    distance, ~0, QueryTriggerInteraction.Ignore);
                if (count >= _castHits.Length) return false;
                for (int i = 0; i < count; i++) if (!Own(_castHits[i].collider)) return false;
            }
            return CanOccupy(end);
        }

        private bool CanOccupy(Vector3 position)
        {
            int count = Physics.OverlapSphereNonAlloc(position, _envelope, _overlaps,
                ~0, QueryTriggerInteraction.Ignore);
            if (count >= _overlaps.Length) return false;
            for (int i = 0; i < count; i++) if (!Own(_overlaps[i])) return false;
            return true;
        }

        private bool Own(Collider collider)
        {
            return collider != null && (collider.transform == _aircraft || collider.transform.IsChildOf(_aircraft));
        }

        private float MeasureEnvelope(GameObject aircraft, float scale)
        {
            // Offline vertex/rotor audit proves 12m encloses the shipped visual.
            // Live collider AABBs can only enlarge that rotation-invariant sphere.
            float radius = VerifiedEnvelopeRadius * scale;
            var colliders = aircraft.GetComponentsInChildren<Collider>(true);
            if (colliders.Length == 0) throw new InvalidOperationException("Patrol collision hull is missing.");
            foreach (var collider in colliders)
            {
                if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy) continue;
                Bounds bounds = collider.bounds;
                if (!Finite(bounds.center) || !Finite(bounds.extents)) throw new InvalidOperationException("Patrol collider bounds are not finite.");
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                    radius = Mathf.Max(radius, (point - _aircraft.position).magnitude + 0.5f);
                }
            }
            if (!Finite(radius) || radius < 1f || radius > 35f) throw new InvalidOperationException("Patrol aircraft envelope is unsupported.");
            return radius;
        }

        private bool Hold(float dt, string reason)
        {
            _velocity = Vector3.zero;
            _blockedSeconds += dt;
            _status = "Holding: " + reason;
            if (_blockedSeconds >= MaxBlockedSeconds) return Finish("Patrol aborted: " + reason);
            return true;
        }

        private bool Finish(string reason)
        {
            _finished = true;
            _status = reason;
            Plugin.Log?.LogInfo("[EscortBench] " + reason);
            return false;
        }

        private static Vector3 Heading(float yaw) { return new Vector3(Mathf.Sin(yaw), 0, Mathf.Cos(yaw)); }
        private static Vector3 Horizontal(Vector3 value) { return new Vector3(value.x, 0, value.z); }
        private static FlightVector Math(Vector3 value) { return new FlightVector(value.x, value.y, value.z); }
        private static Vector3 Unity(FlightVector value) { return new Vector3(value.X, value.Y, value.Z); }
        private static bool Finite(float value) { return EscortFlightMath.IsFinite(value); }
        private static bool Finite(Vector3 value) { return Finite(value.x) && Finite(value.y) && Finite(value.z); }
    }
}
