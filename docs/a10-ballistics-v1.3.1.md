# A-10 ballistic correction in v1.3.1

The shared v1.3.0 planner pointed directly from the gun at an intended ground
point. EFT applies gravity and drag after launch, so that direction landed
short. The first and last recorded shot geometries predict roughly 37 m and
17 m of shortfall even in a flat-ground, gravity-only example. These are
illustrations, not measured game impacts. The relevant installed 4.1.2 and
4.1.4 code matched; this limitation predates the compatibility port.

## Implementation

- `A10BallisticSolver` searches a bounded low-elevation solution with a 2.5 cm
  vertical tolerance and a 5 cm final endpoint limit. Failure is explicit.
- `A10EftTrajectoryEvaluator` uses native `TrajectoryCalculator` with loaded
  ammo speed times weapon speed factor, mass, diameter, ballistic coefficient,
  lifetime, and the native human/bot trajectory choice. It does not spawn test
  bullets. Every initialized trial returns its trajectory history to EFT's
  pool, including failed trials.
- The solver allows at most 28 evaluations per round, 5 km horizontal range,
  and the smaller of ammo lifetime or 12 seconds of flight. These bounds avoid
  the native 13-second history limit. The production code has no copied drag
  table or map-specific correction constant.
- Collision prediction follows native 0.01-second flight samples with EFT's
  ballistic hit mask and global trigger policy. The final short tracer follows
  the terminal flight direction; replay waits for predicted arrival.
- `DelaySeconds` remains launch scheduling locally. `FlightTimeSeconds` adds
  travel time. The existing packet's final float carries their sum; receivers
  use zero additional flight time, preserving the wire layout without adding
  the travel delay twice.
- Designation and dispatch retain the selected point directly. Nearby surface
  projection uses the designator's terrain/low-poly mask, searches within 24 m
  vertically, and chooses the surface nearest the selected elevation. Curved
  collision checks separately use native ballistic layers.
- Headless visual and shorter compatibility damage paths retain their origins,
  share intended points, and align predicted arrivals. The existing synthetic
  fallback waits until actual firing plus travel plus settling time, and is
  suppressed for invalid/obstructed paths or projectile creation failures.

## Validation and limits

The coordinated regression run and CI-safe verification passed **193 tests,
0 failures**. The native adapter and ammunition/owner parameter mapping were
independently compared with the installed EFT IL; the test fixture's 79 G1
table rows matched the installed float values.

The regression suite includes an independent test-only reproduction of the
inspected native trajectory integrator to exercise the production adapter and
solver without proprietary runtime dependencies. It covers ranges, headings,
height differences, parameter variation, invalid/unreachable shots, history
pool cleanup, curved obstruction, layered surfaces, and arrival timing.

This does not execute Unity's native physics scene. Collision prediction cannot
foresee moving actors, destructible cover, penetration, or ricochets; EFT also
has owner/body exclusion rules beyond the broad collision mask. Diagnostics
therefore distinguish intended targets, predicted endpoints, and measured
first collisions. Polling can miss a shot before EFT recycles it; that reports
measurement unavailable, never a guessed hit. Existing aircraft frame timing
can still slightly separate the visible gun from its precomputed moving origin.

The current 12.9 m decorative arrow has not been rescaled into a 44.1 by 15 m
footprint preview. Its particle scaling and surface projection require visual
validation. Manual impact audio retains the existing timing sequence.

## Live acceptance

1. On flat open ground, call a single strafe and inspect first/middle/last
   `TSC A-10 measured collision` entries. Compare actual positions with both
   intended and predicted points; unavailable entries are inconclusive.
2. Repeat at several distances, rotate the approach 180 degrees, and repeat a
   double pass. Check impact center and spread against the laser.
3. Test a slope, a stacked roof/ground location, and cover along the approach.
   Cover should intercept rounds without synthetic damage at the hidden target.
4. Repeat with human-host and headless Fika authority, checking visible impact
   arrival against damage and cancellation/raid-end cleanup.

Live solo and multiplayer acceptance is pending. Build, regression, and package
evidence accompanies the tester artifact.
