using System;
using TscHh60Visual;

internal static class Program
{
    private static int _passed;
    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        _passed++;
    }
    private static bool Fire(EscortTestMode mode, bool permit = true, bool emitter = true,
        bool targeting = true, bool flight = true, bool ready = true) =>
        EscortBenchModePolicy.CanFire(mode, permit, emitter, targeting, flight, ready, out _);
    private static bool Auto(EscortTestMode mode, bool permit = true, bool hold = false, bool gameplay = true,
        bool emitter = true, bool targeting = true, bool flightReady = true) =>
        EscortBenchModePolicy.CanAutomaticFire(mode, permit, hold, gameplay, emitter, targeting, flightReady, out _);

    private static void Main()
    {
        Check(!EscortBenchModePolicy.HasWeapons(EscortTestMode.PatrolFlightOnly), "flight-only sessions never construct weapon helpers");
        Check(!Fire(EscortTestMode.PatrolFlightOnly), "live-shot permission cannot arm flight-only mode");
        Check(Fire(EscortTestMode.StationaryShots, flight: false), "stationary manual shot remains permitted");
        Check(!Fire(EscortTestMode.StationaryShots), "stationary mode refuses an unexpected flight controller");
        Check(Fire(EscortTestMode.PatrolManualShots), "active local moving-gun patrol can request one shot");
        Check(!Fire(EscortTestMode.PatrolManualShots, permit: false), "tracking-only patrol cannot fire");
        Check(!Fire(EscortTestMode.PatrolManualShots, flight: false), "destroyed or missing flight controller cannot fire");
        Check(!Fire(EscortTestMode.PatrolManualShots, ready: false), "transit, departure, pause or obstruction cannot fire");
        Check(!Fire(EscortTestMode.PatrolManualShots, emitter: false), "missing emitter cannot fire");
        Check(!Fire(EscortTestMode.PatrolManualShots, targeting: false), "missing targeting cannot fire");
        var captured = EscortTestMode.PatrolFlightOnly;
        var nextSelection = EscortTestMode.PatrolManualShots;
        Check(nextSelection != captured && !Fire(captured), "changing next summon selection cannot arm a captured flight-only session");
        Check(EscortBenchModePolicy.HasFlight(EscortTestMode.PatrolAutomaticBursts) && EscortBenchModePolicy.HasWeapons(EscortTestMode.PatrolAutomaticBursts), "automatic mode has flight and both gun controllers");
        Check(Fire(EscortTestMode.PatrolAutomaticBursts), "automatic patrol permits a manually held trigger");
        Check(!Fire(EscortTestMode.PatrolAutomaticBursts, permit: false), "automatic permission cannot substitute for manual permission");
        Check(!Fire(EscortTestMode.PatrolAutomaticBursts, flight: false), "automatic patrol manual trigger requires flight controller");
        Check(!Fire(EscortTestMode.PatrolAutomaticBursts, ready: false), "automatic patrol manual trigger stops during transit or departure");
        Check(Auto(EscortTestMode.PatrolAutomaticBursts), "automatic mode with explicit permission can schedule rounds");
        Check(!Auto(EscortTestMode.PatrolAutomaticBursts, permit: false), "manual permission does not substitute for automatic permission");
        Check(!Auto(EscortTestMode.PatrolAutomaticBursts, hold: true), "hold-fire stops both guns");
        Check(!Auto(EscortTestMode.PatrolAutomaticBursts, gameplay: false), "menus and pause stop automatic fire");
        Check(!Auto(EscortTestMode.PatrolAutomaticBursts, emitter: false), "missing native emitter blocks automation");
        Check(!Auto(EscortTestMode.PatrolAutomaticBursts, targeting: false), "missing gun controller blocks automation");
        Check(!Auto(EscortTestMode.PatrolAutomaticBursts, flightReady: false), "transit/departure/obstruction blocks automatic fire");
        foreach (var manual in new[] { EscortTestMode.StationaryShots, EscortTestMode.PatrolManualShots, EscortTestMode.PatrolFlightOnly })
            Check(!Auto(manual), "automatic permission cannot arm another captured mode");
        foreach (var unknown in new[] { (EscortTestMode)(-1), (EscortTestMode)4, (EscortTestMode)999 })
        {
            Check(!EscortBenchModePolicy.Valid(unknown), "invalid mode rejected before summon");
            Check(!EscortBenchModePolicy.HasWeapons(unknown) && !EscortBenchModePolicy.HasFlight(unknown) && !Fire(unknown) && !Auto(unknown), "unknown mode has no capabilities");
        }
        Console.WriteLine(_passed + " escort mode capability checks passed.");
    }
}
