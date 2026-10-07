# Extraction cover orchestration tests

This bounded executable links the production extraction-cover partial plugin,
held-trigger cadence, manual key-chord handling, and finite-step safety policy.
It substitutes only the Unity/EFT/config/Harmony environment and targeting,
ballistics, donor-gun preparation, and effects services.

Run from the repository root:

```powershell
dotnet run --project extras/hh60-visual/tests/extraction-cover/ExtractionCoverTests.csproj --configuration Release
```

The 119 checks cover inbound and arrival windows, disabled settings and manual permission,
held trigger with W, release and missing modifiers, edge-triggered gun selection,
focus/menu/pause/frame gating, per-round shot acceptance, effects after accepted
shots, immediate departure cleanup, cancellation, inactive/destroyed points,
raid/caller changes, model changes, HH60 use of existing guns, fault isolation,
off/on recovery, resource-loading waits, replacement pickups, and retention of
native service objects. Both UH60 and HH60 are tested before arrival without an
extraction point. The inbound-to-hover transition preserves gun selection,
runtime/effect ownership, and the original firing cadence. Native request-field
reflection is exercised against typed substitutes for support type, local-service
permission, linked cancellation token, and timing snapshot. Tests also exclude
cargo, nonlocal, visual-only and pre-cancelled requests; verify cancellation-prefix
cleanup; and isolate newer pooled requests from old tokens, arrival callbacks,
and old/null landing-point cleanup. The targeting setter receives inbound bounds
of 70, 80 and 240 for native speed multipliers 0.5, 1 and 3, then 70 at arrival.
Effect iterator disposal is checked after every fixture.

Lifecycle hooks are called by the fixture; actual Harmony patch execution is not
tested. These checks do not replace native target-geometry/ballistics checks,
Unity rendering/audio tests, or a live extraction raid. Donor geometry has its
own installed-payload harness in `../extraction-gun-visuals`. The motion-bound
setter assertions verify orchestration values, not the physical bound itself.
