# Tylevo's Tactical Services Control v1.3.1 Public Beta

Targets SPT 4.1.4 / EFT 0.16.9.5.40743 with the existing WTT and UnityToolkit
dependencies. This tester corrects the A-10's uncompensated aim, which could
make rounds land short of the laser marker.

Each shot now uses EFT's native trajectory calculator to account for gravity,
drag, ammunition, and weapon speed. The visible gun origin and repeatable
strike pattern remain. Replay tracers and impact effects use predicted arrival
time, and the selected target is passed directly into the strike.

Surface selection stays near the marked elevation. Curved trajectory checks
can identify cover before the target. Headless arrival timing and fallback
damage checks account for projectile travel and obstructed paths.

Sampled collision logs support live accuracy checks. Predicted positions are
not presented as measured hits. See `a10-ballistics-v1.3.1.md` for limitations
and the in-raid acceptance procedure. Live solo and Fika acceptance is pending.
