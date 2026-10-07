namespace TscHh60Visual
{
    public enum EscortTestMode { StationaryShots, PatrolFlightOnly, PatrolManualShots, PatrolAutomaticBursts }

    // Session capability depends on the mode captured at summon, not mutable UI.
    internal static class EscortBenchModePolicy
    {
        internal static bool Valid(EscortTestMode mode) =>
            mode == EscortTestMode.StationaryShots || mode == EscortTestMode.PatrolFlightOnly || mode == EscortTestMode.PatrolManualShots || IsAutomatic(mode);
        internal static bool IsAutomatic(EscortTestMode mode) => mode == EscortTestMode.PatrolAutomaticBursts;
        internal static bool HasFlight(EscortTestMode mode) =>
            mode == EscortTestMode.PatrolFlightOnly || mode == EscortTestMode.PatrolManualShots || IsAutomatic(mode);
        internal static bool HasWeapons(EscortTestMode mode) =>
            mode == EscortTestMode.StationaryShots || mode == EscortTestMode.PatrolManualShots || IsAutomatic(mode);

        internal static bool CanFire(EscortTestMode mode, bool permitted, bool emitter, bool targeting,
            bool flight, bool flightReady, out string reason)
        {
            if (!Valid(mode) || !HasWeapons(mode))
            { reason = "Flight-only mode cannot fire. Recall and summon a weapon-enabled test mode."; return false; }
            if (!permitted)
            { reason = "Enable Permit manual live fire in F12 to use the held trigger."; return false; }
            if (!emitter || !targeting)
            { reason = "Manual shot controller is unavailable; recall and summon again."; return false; }
            if (mode == EscortTestMode.StationaryShots && flight)
            { reason = "Stationary shot mode cannot authorize a moving aircraft."; return false; }
            if (HasFlight(mode) && (!flight || !flightReady))
            { reason = "Moving shots require an active local patrol; catch-up, departure, pause and blocked flight cannot fire."; return false; }
            reason = null;
            return true;
        }

        internal static bool CanAutomaticFire(EscortTestMode mode, bool permitted, bool holdFire,
            bool gameplayActive, bool emitter, bool targeting, bool flightReady, out string reason)
        {
            if (!IsAutomatic(mode)) { reason = "This session is not an automatic patrol."; return false; }
            if (!permitted) { reason = "Automatic bursts are disabled in F12."; return false; }
            if (holdFire) { reason = "Hold fire."; return false; }
            if (!gameplayActive) { reason = "Automatic fire paused while gameplay input is unavailable."; return false; }
            if (!emitter || !targeting) { reason = "Automatic weapon controllers unavailable."; return false; }
            if (!flightReady) { reason = "Automatic fire suspended outside active local patrol."; return false; }
            reason = null;
            return true;
        }
    }
}
