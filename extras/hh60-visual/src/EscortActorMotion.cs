using System;

namespace TscHh60Visual
{
    // World-space actor motion only. Aircraft motion is intentionally excluded:
    // the native direct CreateShot API does not inherit platform velocity.
    internal sealed class EscortActorMotion
    {
        internal const float MaximumSpeed = 15f;
        internal const float PositionSlack = 0.30f;
        private const float MaximumAcceleration = 80f;
        private FlightVector _position, _velocity;
        private readonly FlightVector[] _positions = new FlightVector[32];
        private readonly float[] _times = new float[32];
        private int _head, _historyCount;
        private float _sampleTime, _startTime;
        private int _samples;
        private bool _hasPosition;

        internal FlightVector Velocity => _velocity;
        internal string Reason { get; private set; } = "collecting actor motion";

        internal bool Ready(float now)
        {
            return EscortFlightMath.IsFinite(now) && _hasPosition && _samples >= 2 &&
                now - _startTime >= 0.1f && now >= _sampleTime && now - _sampleTime <= 0.15f;
        }

        internal void Observe(FlightVector position, float now, bool forceSample = false)
        {
            if (!EscortFlightMath.IsFinite(position) || !EscortFlightMath.IsFinite(now))
            {
                _hasPosition = false;
                _samples = 0;
                Reason = "non-finite actor motion";
                return;
            }
            if (!_hasPosition) { Reset(position, now, "collecting actor motion"); return; }
            float elapsed = now - _sampleTime;
            double distance = Distance(position, _position);
            if (elapsed < 0f || elapsed > 0.25f)
            { Reset(position, now, "actor motion is stale"); return; }
            if (elapsed == 0f)
            {
                if (distance > 0.05) Reset(position, now, "actor moved without a timed sample");
                return;
            }
            // Fixed-step actor transforms can advance between render samples.
            // A bounded position slack covers that quantization; the actual
            // velocity limit is measured across a full100ms history window.
            if (distance > MaximumSpeed * elapsed + PositionSlack || distance > 4f)
            { Reset(position, now, "actor moved discontinuously or exceeds 15m/s"); return; }
            if (!forceSample && elapsed < 0.01f) return;
            _position = position;
            _sampleTime = now;
            _head = (_head + 1) % _positions.Length;
            _positions[_head] = position;
            _times[_head] = now;
            _historyCount = Math.Min(_historyCount + 1, _positions.Length);
            int anchor = -1;
            for (int i = 1; i < _historyCount; i++)
            {
                int index = (_head - i + _positions.Length) % _positions.Length;
                if (now - _times[index] >= 0.1f - 0.00001f) { anchor = index; break; }
            }
            if (anchor < 0) { Reason = "collecting actor motion"; return; }
            elapsed = now - _times[anchor];
            var previous = _positions[anchor];
            float speed = (float)(Distance(position, previous) / elapsed);
            if (!EscortFlightMath.IsFinite(speed) || speed > MaximumSpeed)
            { Reset(position, now, "actor moved discontinuously or exceeds 15m/s"); return; }
            var velocity = new FlightVector((position.X - previous.X) / elapsed,
                (position.Y - previous.Y) / elapsed, (position.Z - previous.Z) / elapsed);
            if (_samples > 0 && Distance(velocity, _velocity) > MaximumAcceleration * elapsed)
            { Reset(position, now, "actor acceleration changed abruptly"); return; }
            _velocity = velocity;
            _samples++;
            Reason = Ready(now) ? "motion ready" : "collecting actor motion";
        }

        private void Reset(FlightVector position, float now, string reason)
        {
            _position = position;
            _sampleTime = _startTime = now;
            _velocity = FlightVector.Zero;
            _samples = 0;
            _head = 0;
            _historyCount = 1;
            _positions[0] = position;
            _times[0] = now;
            _hasPosition = true;
            Reason = reason;
        }

        internal static double DistanceToSegment(FlightVector point, FlightVector start, FlightVector end)
        {
            if (!EscortFlightMath.IsFinite(point) || !EscortFlightMath.IsFinite(start) || !EscortFlightMath.IsFinite(end))
                return double.NaN;
            double x = (double)end.X - start.X, y = (double)end.Y - start.Y, z = (double)end.Z - start.Z;
            double px = (double)point.X - start.X, py = (double)point.Y - start.Y, pz = (double)point.Z - start.Z;
            double lengthSquared = x * x + y * y + z * z;
            double t = lengthSquared > 0 ? Math.Max(0, Math.Min(1, (px * x + py * y + pz * z) / lengthSquared)) : 0;
            x = px - x * t; y = py - y * t; z = pz - z * t;
            return Math.Sqrt(x * x + y * y + z * z);
        }

        private static double Distance(FlightVector left, FlightVector right)
        {
            double x = (double)left.X - right.X, y = (double)left.Y - right.Y, z = (double)left.Z - right.Z;
            return Math.Sqrt(x * x + y * y + z * z);
        }
    }
}
