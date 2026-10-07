namespace TscHh60Visual
{
    // A held trigger requests one fresh authorization at most every 0.12s.
    // Releasing or losing permission immediately stops requests, without
    // resetting the rate limit or accumulating catch-up rounds.
    internal sealed class EscortHeldTrigger
    {
        internal const float Interval = 0.12f;
        private float _lastTime, _nextAttempt;
        private bool _hasTime, _hasAttempt;

        internal bool Tick(float now, bool held, bool eligible)
        {
            if (!EscortBenchSafetyPolicy.Finite(now) || now < 0f) return false;
            if (_hasTime && now < _lastTime)
            {
                _nextAttempt = now + Interval;
                _hasAttempt = true;
                _lastTime = now;
                return false;
            }
            _hasTime = true;
            _lastTime = now;
            if (!held || !eligible || (_hasAttempt && now < _nextAttempt)) return false;
            _hasAttempt = true;
            _nextAttempt = now + Interval;
            return true;
        }

        internal void Reset() { _hasTime = _hasAttempt = false; _lastTime = _nextAttempt = 0f; }
    }
}
