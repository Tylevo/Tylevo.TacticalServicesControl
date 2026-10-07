# Optional Icebreaker HH-60 visual companion

This is the source of an **opt-in visual replacement**, tested locally with
TSC **1.3.13**, SPT **4.1.5**, and EFT **0.16.9.5-40743**. It attaches the
Black Division-associated HH-60 from the Icebreaker cutscene to TSC's existing
UH-60 controller. The normal TSC solution, release package, and default UH-60
are unchanged. It is not an additional helicopter service.

Choose **F12 > TSC Helicopter Appearance > Visual > Helicopter model**:

- **UH60** uses the original helicopter and is the default for a fresh install.
- **HH60** uses the Icebreaker aircraft with five static crew members.

Model changes apply without restarting, including to existing and pooled
helicopters. The first HH60 selection may take several seconds to load; the
original stays visible until it is ready. Subsequent switches reuse the loaded
visual. Switching to UH60 while loading keeps the original visible. Existing
`Enabled = true` configurations migrate to HH60 automatically.

## Manual extraction cover fire

Version **0.10.1** provides **F12 > TSC Helicopter Appearance > Extraction showcase >
Enable manual cover fire**. This optional mode uses the actual pickup helicopter
during its inbound approach and pickup window. It uses the existing
**Escort bench (experimental) > Manual trigger**, **Select other gun**, and
**Permit manual live fire** settings. The separate escort test need not be
enabled or summoned. Default controls are **hold Right Ctrl + Page Down** to
fire and **Right Ctrl + Page Up** to switch guns; custom bindings are retained.

The selected gun tracks hostile AI within its normal arc. Hold the trigger to
fire along the visible barrel, including when no target is available; release
to stop. Movement keys can remain held. Existing projectile, hull, rotor and
protected-player checks apply to each round. Shots use the same native
player-owned ballistics, muzzle flash and sound as the escort test.

Cover fire starts with the local extraction dispatch. The same guns and
held-fire cadence continue through arrival into the pickup window. **Cover fire
stops at departure; outbound departure coverage is not implemented.** It also
stops on cancellation, lost raid/player, or disabling either permission. Menus,
lost focus and pause suspend firing. The extraction helicopter takes priority
over the separately summoned escort's manual trigger during this window.
UH60 borrows only the two visible HH60 gun assemblies; its body remains selected.
HH60 uses its existing door guns. The native controller retains aircraft
animation, engine audio, extraction countdown and cargo interaction. This
companion does not implement rope deployment or climbing; private rope work
is outside this contribution.

The option and manual live-fire permission default off in a fresh configuration.
Fika and additional human actors remain blocked; multiplayer firing is unverified.

## Local escort development tests

Version **0.10.1** provides four opt-in solo-raid modes under **F12 > TSC Helicopter
Appearance > Escort bench (experimental)**. Enable **Enable escort test**, then
choose **Test mode**. An existing `Enable stationary test` value migrates to the
new enable setting. Mode changes apply when the next helicopter is summoned.
Close the settings/inventory before using the controls:

- **Right Ctrl + Insert:** summon or recall the selected HH-60 test above and to your right.
  Face along the desired helicopter heading before summoning in an open outdoor area.
- **Right Ctrl + Page Up:** select the other door gun for manual firing.
- **Hold Right Ctrl + Page Down:** fire along the selected gun's current visible
  barrel every 0.12 seconds. Release to stop. Enable **Permit manual live fire**
  first (off by default). This works in **StationaryShots**, **PatrolManualShots**,
  and **PatrolAutomaticBursts**, including when no enemy is available.
- **Right Ctrl + Delete:** hold or resume both guns in automatic mode.

**Show escort test HUD** is off by default for clean recordings. Enable it in
the same F12 section to show instructions, status, target labels and shot
notifications. It applies immediately; controls and logging remain active while
the HUD is hidden.

The previous Alt+F8/F9/F10 defaults migrate to these bindings on startup to avoid
Field Attachments' inclusive F-key handlers. Custom escort bindings are retained.
The previous **Permit single live shots** and **Fire one test round** settings
migrate to **Permit manual live fire** and **Manual trigger**, preserving their
values unless the new settings already exist.

The manual trigger accepts its configured key/modifiers while movement keys
are held, including custom single-key or multi-key bindings. Window focus,
menus and pause still gate input; the HUD distinguishes paused input from a
blocked shot. If manual fire and automatic hold share a key, manual fire takes
priority without also toggling automatic hold.

Holding the manual trigger pauses both automatic burst controllers. Releasing
it lets automatic targeting begin a fresh settling period. The manual trigger
does not require target acquisition or alignment with an enemy, and can fire
into ordinary terrain. Each round still checks the actual muzzle, native bullet
path, helicopter hull/rotors and protected actors. Patrol firing remains paused
during catch-up, departure, menus and obstructed flight. Delayed frames and rapid
tapping cannot produce a backlog or bypass the 0.12-second cadence.

Weapon-enabled summons retain and load the native NSV tail sound bank through
the game's asset manager, including on maps without a mounted gun. Loading is
bounded and does not prevent projectile simulation if audio fails. The retain
is released on cancellation or aircraft cleanup. A bounded pool lets the native
tails overlap during held fire without cutting off an earlier tail.

Both door guns now show the native NSV muzzle jet and a brief warm light for
each accepted round. Flashes attach to the achieved barrel tip and follow gun
aiming and aircraft movement, with independent timing/materials for each gun.
They work for manual fire and automatic bursts, including with the test HUD
hidden. Pulses expire after 0.12 seconds and stop on pause or cleanup. Native
assets are retained explicitly; unavailable effects do not stop firing.

**PatrolAutomaticBursts** operates both door guns independently after enabling
**Permit automatic bursts**. Manual permission alone cannot enable autonomous fire.
Each gun settles on the same target for 0.6 seconds, fires up to three rounds
spaced at least 0.12 seconds apart, cools for 2.5 seconds, then settles again.
Target loss, a refused shot, hold fire, menus, pause, transit or departure interrupts
the burst. Returning to combat always requires fresh settling; delayed frames never
emit a backlog of rounds. This test limits each gun to **60 accepted rounds per
summon** for autonomous fire, with remaining usage shown in the HUD. Manually
held fire has its own cadence and does not consume the automatic burst budget.

A fresh random aim offset, at most 0.55 m from the target's predicted torso, is
chosen per burst. The visible gun moves toward it during settling. The actual
barrel trajectory, gun limits, current torso visibility, other actors and hull
clearance are rechecked before every round; no spread is applied after those
checks. Both guns share the native ammo sequence, so every fifth accepted round
across the aircraft remains M21. Their targeting, burst state and pose restoration
are independent. Crew hands remain static.

**PatrolManualShots** combines the flight pattern with visible tracking of the
selected door gun. Start with manual fire permission off to observe aiming, then
enable it and hold the manual trigger to fire. Targeting suspends during catch-up,
departure, pause, and blocked flight. Firing in this mode remains manual.

Moving aim uses the exact next ammunition item and EFT's native trajectory
calculator for target lead and bullet drop. No trial projectiles are fired.
For autonomous fire, the gun must physically reach the solution: each request re-evaluates the
achieved barrel axis and current muzzle, target motion, world clearance and
other living actors before emission. The direct native shot API does not add
platform velocity; prediction follows that same launch model using the current
world-space muzzle and direction. Unstable or discontinuous target movement
is rejected until fresh motion samples are established.

**PatrolFlightOnly** follows the player's area using **Patrol pattern**:

- **Mixed** (default) alternates long broadside passes with occasional orbits.
- **Racetrack** keeps parallel passes joined by rounded turns.
- **Orbit** keeps the original circular patrol.

The pattern is captured on summon. Mixed gradually extends the circular path
into a racetrack rather than jumping between waypoints. It spends 70 seconds
in its racetrack phase and 35 seconds in its orbit phase; that timer pauses
during catch-up. The track axis stays fixed during each elongated pattern.

All patterns leave their patrol to fly toward the caller when the horizontal
separation exceeds 300 m at the default radius. They establish a new patrol
within 150 m. Only the guidance center is recentered: aircraft position, heading,
bank and velocity stay continuous. Nearby walking still uses the gradual area
follow. The helicopter slows for the racetrack's turns and accelerates along
its straight legs. It uses bounded acceleration, turning and banking, terrain
lookahead, and a swept clearance envelope covering the complete
aircraft through rotor rotation and banking. Obstructions cause a hold and, if
they persist, termination. The helicopter does not teleport through obstacles.
Recall starts departure; a second press removes the aircraft immediately.
**PatrolFlightOnly** keeps its guns disabled for the entire session, even if
settings change while it is active. It constructs no targeting or ballistic
emitter. Select **PatrolManualShots** before a new summon to test moving guns.

Version **0.4.1** fixes the stationary hover followed by a 12-second despawn when
the terrain-height scan cannot fully sample map geometry. Height sampling retries
dense results with a bounded buffer and retains usable contacts. Incomplete
sampling preserves the current flight level and prevents descent; it does not
block a clear patrol or recall. Full-envelope collision checks still authorize
every movement. Diagnostics distinguish missing ground, saturated results, and
unusable remote contacts.

The HUD reports the selected gun, candidate/relationship state and firing blocks.
Version **0.3.1** identifies native body/armor hitboxes through their ballistic
owner rather than the movement-controller registry. A target's own hitbox no
longer counts as an unknown obstruction. Requested shots also show a native
notification when blocked or submitted, with collider details retained in diagnostics.
Version **0.4.0** also resolves the immediate child boxes of native armor plates,
following the native ballistic lookup order without accepting unknown owners.
Unknown or neutral relationships do not authorize automatic shots. The rig reads existing
native enemy/ally records rather than repeatedly calling the probabilistic
`IsPlayerEnemy` policy query. Automatic targeting is limited to 200 m and
revalidated for each round. Manually held fire uses the current barrel even
without a target, while humans and AI without confirmed hostility remain
protected obstructions.

Real rounds use the caller's registered identity, a transient native weapon
descriptor and M33 ammunition, with M21 every fifth accepted shot. This is an
ownership baseline: **kills, quests, XP and faction consequences may apply**.
`fireIndex = -1` is not proof of statistics isolation. Native impact observations
are logged separately from accepted submissions with the `[EscortBench]` prefix
in `BepInEx/LogOutput.log`. There is no extra scripted damage application.

The aircraft is prepared while inactive, with transport controllers removed
before activation. It has its own lifetime and never leases the extraction pool.
Shared visual assets and TSC's asset cache remain shared; the transport appearance
selector cannot hide the test rig's gun hierarchy. The rig is removed on recall,
raid/player changes, player death, disabling the bench, or after five minutes.
Patrols begin automatic departure after three minutes.
Outstanding shot observations remain tracked until completion or raid teardown.

These are development tests. Fika and additional
human actors are blocked until ownership/network routing is validated. The crew
are static meshes; their hands do not follow the gun motion. Retained UH60 hull
geometry and conservative firing checks do not establish complete HH60 collision
coverage or a guarantee against penetration/ricochet friendly damage. In-game
flight, attribution, visual alignment, sound and cleanup still need acceptance.
Stationary version 0.3.1 produced eight accepted rounds, six positive native
health-damage observations and five death transitions in the local test log.
This establishes real native damage, but does not isolate quest, XP or faction effects.

## Inspect colliders and gun positions

Version 0.2.3 adds **F12 > TSC Helicopter Appearance > Debug > Show helicopter
overlay**, off by default. Enable it while a helicopter is nearby; select HH60
to see its door-gun markers. Changes to the debug settings apply immediately.

- Green lines show collision-mesh boundary and crease edges, with coplanar
  triangulation removed. Orange boxes are approximate bounds when local surface
  data is unavailable or does not match. Yellow shows optional attached triggers.
- Cyan marks the left muzzle; pink marks the right. White lines follow the actual
  barrel axes. Positions are measured at the final barrel rings in the pinned donor.
- The fans preview adjustable horizontal and vertical limits. Defaults are a
  110-degree outward sector, 65 degrees down and 10 degrees up, displayed for 12 m.
  They are proposed limits for inspection, not an active targeting or weapon system.
- **See through helicopter** draws through scenery as well; disable it for normal
  occlusion. The overlay is drawn in the main game view, within 600 m of the aircraft.

HH60 continues to use the original UH60 collision hull. The overlay makes the
relationship visible; it does not change collision geometry, gun poses or service
behavior. A future gun controller needs both traverse limits and line-of-sight
checks against the hull and environment. Arcs alone do not check shot clearance.

The stock collision meshes cannot be read at runtime. For their surface outlines,
prepare one optional **local-only** file using the same Python environment:

```powershell
& ./.venv-hh60/Scripts/python.exe ./extras/hh60-visual/tools/export_collider_overlay.py `
  --bundle "$game/BepInEx/plugins/Tylevo.TacticalServicesControl/assets/content/vehicles/uh60_blackhawk.bundle" `
  --output "$work/payload/collider-overlay.json"
```

Copy that JSON alongside the installed `payload/scene.json`. The exporter and
runtime verify the stock bundle hash; runtime additionally matches each mesh's
name, vertex count and bounds. Missing or mismatched data falls back to orange
bounds. The generated file is game-derived data and must stay out of Git and CI.
Line meshes are cached for drawing and released on companion shutdown. Native
rendering and camera visibility still require an in-game check.

## Why this aircraft?

We think this HH-60's equipped, special-operations/extraction appearance is a
good visual fit for the **TerraGroup TSC Uplink** and its contracted helicopter
services. The occupied cockpit and door-gunner stations make the service feel
like a staffed tactical aircraft rather than an empty transport. This is an
art-direction suggestion, **not a claim that the game's lore canonically
assigns this exact aircraft to TerraGroup**, or that the original UH-60 is
incorrect. The maintainer can keep it optional or choose a different direction.

The five visible crew are:

| Role | Count | Behavior |
| --- | --- | --- |
| Pilots | 2 | Fixed poses sampled from the source cutscene |
| Door gunners | 2 | Fixed poses; optional guns move independently |
| Cabin support crew member | 1 | Fixed pose |

All five crew figures are **visual meshes only**: no AI, dialogue, interaction, combat,
damage model, boarding, or rappelling. They are not multiplayer entities. Private
rope geometry, deployment and climbing experiments are not included.

## What changes, and what does not

- Normal transport appearance uses one Harmony postfix on `UH60Behaviour.OnAwake`.
  Four additional hooks observe extraction dispatch, pickup creation, request
  cancellation and landing-point cleanup. They follow the native inbound/pickup
  lifecycle without replacing the service methods. There are no AssetLoader or
  AssetBundle hooks and no replacement Unity bundle loaded at runtime.
  Optional weapon modes also install filtered native impact observers.
- Validated decoded mesh/texture data is uploaded using ordinary Unity APIs.
  A persistent host builds a shared resource cache once, reused by pooled aircraft.
- The HH-60 follows `b_vhc_main`; main/tail rotor rotations follow the existing
  TSC rotor nodes. The source cutscene's flight track is not played.
- The original Animator, flight/arrival behavior, extraction trigger, cargo
  interaction, sounds, colliders, and light components stay in place.
  No service prices, timing, inventory, profiles, or server code are changed.
- Original meshes are hidden with `Renderer.forceRenderingOff` **only after**
  the replacement is completely ready. Managed failures restore their original
  flags. This is not a promise to recover from native Unity/GPU crashes.
- Exactly three aircraft window slots borrow the already-loaded
  `MI_VH_BlackHawk_Glass` shared material and its
  `Global Fog/Transparent Reflective Specular` shader. Original material state
  is not mutated or owned by the companion. Mixed-material door metal, crew
  eyewear, and weapon optics are untouched. Existing material-property blocks
  are copied per slot at construction time; later per-renderer block updates
  are not mirrored.

The original TSC lights/flicker components are retained; this does **not** add
a new day/night lighting system.

## Validation and limits

Historical in-raid acceptance of the original **0.2.1** companion confirmed:

- The visible HH-60 replacement is the intended model.
- Calling the helicopter and completing extraction work.
- Cargo transfer works.
- Night lights and visible aircraft animation work.
- The 0.2.1 cockpit/door glass correction looks correct in game.

Local runtime logs also recorded successful native asset construction, five
baked crew, two rotor bridges, and three original-glass bindings. No private
logs, account details, save data, photos, or game-derived payload are included here.

This does **not** establish Fika/multiplayer compatibility, all-map clearance,
long-session stability, or UAV behavior. TSC's UAV code can instantiate the same
UH-60 prefab, so the shared `OnAwake` hook could also affect that visual path;
that interaction needs separate testing. The original colliders remain sized
for the UH-60, not newly generated HH-60 collision geometry.

The current companion source is version **0.10.1**. The historical observations
above do not establish acceptance of the current extraction-cover or escort modes.
Recorded local validation includes all **12 proprietary-free C# suites** passing,
with the following focused coverage:

| Validation | Passed | Scope |
| --- | --- | --- |
| Model selection | 53 checks | Production selection/build lifecycle with synthetic Unity, cache and scheduler; includes repeated changes and pooled reuse |
| Extraction cover | 122 checks | Production orchestration, input and cadence with targeting, ballistics and effects substitutes |
| Muzzle flash | 58 checks | Production effect loading, hierarchy selection, pulse and cleanup with synthetic Unity/assets; fresh UH60 and cached HH60/UH60 switching |
| Manual input | 23 checks | Synthetic key/shortcut types; the optional installed BepInEx/Unity input mode separately passed 38 checks |
| Extraction gun visuals | 97 checks | Local donor payload hierarchy and production gun lifecycle with managed Unity doubles |
| Core identity | 6 scenarios | Generated hash A, generated hash B, fallback after override, malformed rejection, fallback after rejection, and pre-build mismatch rejection without cleaning; each of the four successful selection runs passed 56 checks |
| Native Core audit | 40 checks | Metadata signatures and lifecycle/API contract against local game references; no live Harmony execution |

The strict companion/Core verifier has **107 checks** and must pass for the exact
final DLL pair before distribution. Its report records the candidate hashes;
metadata checks do not establish runtime rendering, physics or gameplay. Native
muzzle rendering, sound, projectile attribution and cleanup remain unverified for
the current candidate in a live raid.

Complete these in-raid checks with **both UH60 and HH60 appearances** before
claiming current-version acceptance:

- Check a fresh aircraft, repeated model changes and pooled reuse, including gun
  placement, glass, lights, animation and debug drawing.
- During approach and arrival, check both guns, target tracking, held fire with
  and without a target, visible muzzle flashes, sound and projectile direction.
  Confirm the native flight and pickup transition remain intact.
- Release the trigger, pause, open menus and disable each permission; confirm
  firing stops. Cancel and request another extraction, including reused aircraft,
  and check for stale shots, effects or input ownership.
- Complete extraction and cargo transfer through their native interactions.
  Confirm cargo requests do not open the extraction-cover window.
- Hold the trigger through pickup completion and departure; confirm cover fire
  stops as the pickup window closes and does not continue outbound.

These checks do not include private rope deployment or climbing integrations.
The historical source fallback deliberately accepts only this previously verified
1.3.13 Core SHA-256:

```text
3B5CF9B9F8647E5C3DD9D701C1C6B1C19054B81FB4B0C12F22441AC25FA57F63
```

A different/rebuilt Core fails closed to the original model. This fallback is not
a general compatibility claim. Canonical builds use the explicitly audited hash
override described below; do not edit the source fallback to chase each build.

## Assets and attribution

**No EFT models, textures, animations, donor bundles, decoded payloads, or game
assemblies are distributed by this contribution.** The aircraft and crew remain
Battlestate Games' assets. Local preparation was tested against the Icebreaker
scene bundle supplied with [ManimalIcebreaker 1.1.3](https://github.com/danauraborealis/ManimalIcebreaker).
Credit to that project for making the Icebreaker content available in its mod,
and to Tylevo, SamSWAT, and Arys for the existing service/controller work.

The repository's code license does **not** grant redistribution rights to these
assets. This PR makes no claim of permission from Battlestate Games or the
Icebreaker mod author. Any future asset-inclusive distribution requires a
separate permissions review. Keep locally extracted output out of Git and CI.

## Prepare the payload locally

Use a donor file you are authorized to use. The tools do not download assets
and reject files that do not match the tested bundle SHA-256:

```text
F3948D1FB4252D2A756F1A5249D7220A500B2510955CEB77E0C89CC8096245C1
```

Its release-relative location is:

```text
BepInEx/plugins/ManimalIcebreaker/streamingassets/Windows/assets/content/locations/icebreaker/icebreaker_scenes.bundle
```

Use Python 3.11+ and a virtual environment; keep `$work` outside this repository:

```powershell
python -m venv .venv-hh60
& ./.venv-hh60/Scripts/python.exe -m pip install -r ./extras/hh60-visual/tools/requirements.txt

# Set these to your own existing donor file and a new local working directory.
$bundle = Read-Host 'Full path to icebreaker_scenes.bundle'
$work = Read-Host 'Full path to a new working directory outside this repository'
& ./.venv-hh60/Scripts/python.exe ./extras/hh60-visual/tools/crew_pose.py `
  --bundle $bundle --output "$work/crew-poses.json"
& ./.venv-hh60/Scripts/python.exe ./extras/hh60-visual/tools/export_payload.py `
  --bundle $bundle --poses "$work/crew-poses.json" --output "$work/payload"
```

The tools are version-specific, not a general Unity exporter. The expected
result is schema 2, 154 nodes, 117 meshes/renderers, 166 textures, 61 materials,
and the five crew entries. The installed decoded data is about 357 MB before
JSON overhead; native/GPU memory use is additional. Initial creation can take
several seconds. Lower-detail/LOD optimization is future work.

## Build, install, and revert

Use the repository's pinned .NET SDK and build the final Core from the intended
Git revision first. Core's ProductVersion includes that revision, so a subsequent
commit or merge can change its bytes and SHA-256. The companion therefore accepts
an explicit `Hh60ValidatedCoreSha256` build property instead of committing a new
hash into source for every Core build. The exact runtime hash guard stays enabled.

Audit the intended Core and retain the reports in a local working directory
outside the repository. The audit checks native signatures and lifecycle wiring;
it does not establish live Harmony, flight, collision, damage or rendering behavior.
Review the Core's provenance and audit before entering its approved hash:

```powershell
$game = Read-Host 'Full path to your local game references'
$core = Read-Host 'Full path to the final compiled TSC Core DLL'
$work = Read-Host 'Existing local report directory outside the repository'
./extras/hh60-visual/tools/verify-native-contract.ps1 -AuditCore `
  -CoreDll $core -GameRoot $game -Report "$work/core-contract.json"
$approvedHash = Read-Host 'SHA-256 of the reviewed final Core'
./extras/hh60-visual/build.ps1 -GameRoot $game -CoreDll $core `
  -ValidatedCoreSha256 $approvedHash -ValidationReport "$work/companion-contract.json"
```

The build helper refuses a mismatched explicit hash, audits the Core before
compilation, generates the identity under `obj`, then verifies the resulting
companion against that exact Core. It does not infer trust from an installed DLL,
change game files, or deploy. Reports identify the companion, Core and game assembly
hashes. Keep generated identities, reports, game references and payloads local.
Direct builds without the override retain the historical fallback above and will
reject other Core builds. Invalid override values fail compilation.

The proprietary-free build regression also checks generated identities, malformed
hash rejection, runtime equality, and pre-build mismatch rejection:

```powershell
./extras/hh60-visual/tests/core-identity/Test-CoreIdentity.ps1 -OutputDirectory "$work/core-identity-tests"
```

With the client closed, back up any existing companion DLL/configuration. Copy
`extras/hh60-visual/src/bin/Release/netstandard2.1/TscHh60Visual.dll` and your generated `payload/`
into `BepInEx/plugins/TscHh60Visual/`. Do not replace TSC's original bundle or Core.
The file layout must be:

```text
TscHh60Visual/
  TscHh60Visual.dll
  payload/
    scene.json
    meshes/...
    textures/...
```

Start the game after installing the companion, then use the F12 selector to
choose **HH60**. To return to the original TSC model, choose **UH60**. Neither
selection requires a restart. The saved setting is `[Visual] Helicopter model`
in `BepInEx/config/local.tsc.hh60visual.cfg`. Replacing the companion DLL itself
still requires closing and restarting the game.
Do not install old experimental asset-bundle redirect versions alongside it.

Run the proprietary-free tests from the repository root:

```powershell
dotnet run --project extras/hh60-visual/tests/payload/PayloadTests.csproj -c Release
dotnet run --project extras/hh60-visual/tests/glass/GlassPolicyTests.csproj -c Release
dotnet run --project extras/hh60-visual/tests/selection/SelectionTests.csproj -c Release
dotnet run --project extras/hh60-visual/tests/escort-targeting/EscortTargetingTests.csproj -c Release
dotnet run --project extras/hh60-visual/tests/flight/FlightMathTests.csproj -c Release
dotnet run --project extras/hh60-visual/tests/escort-mode/ModePolicyTests.csproj -c Release
dotnet run --project extras/hh60-visual/tests/escort-motion/EscortMotionTests.csproj -c Release
dotnet run --project extras/hh60-visual/tests/escort-burst/EscortBurstTests.csproj -c Release
dotnet run --project extras/hh60-visual/tests/escort-held-trigger/HeldTriggerTests.csproj -c Release
dotnet run --project extras/hh60-visual/tests/escort-input/EscortInputTests.csproj -c Release
dotnet run --project extras/hh60-visual/tests/extraction-cover/ExtractionCoverTests.csproj -c Release
dotnet run --project extras/hh60-visual/tests/muzzle-flash/MuzzleFlashTests.csproj -c Release
& ./.venv-hh60/Scripts/python.exe -m unittest discover -s extras/hh60-visual/tools -p test_export_safety.py
& ./.venv-hh60/Scripts/python.exe -m unittest discover -s extras/hh60-visual/tools -p test_collider_overlay.py
```

The default suites use synthetic input only: 30 payload checks, 45 glass-policy
checks, 12 exporter safety checks, and a model-selection suite exercising the
runtime controller with lightweight Unity and BepInEx substitutes. This covers
loading, repeated changes, pooled instances, renderer-state restoration, and
failure recovery without claiming in-game rendering validation.
For optional local real-data validation,
append `-- --validate "$work/payload/scene.json"` to the payload test command or
`-- --input "$work/payload/scene.json"` to the glass test command (50 checks).
CI must never load or publish donor assets.

The input suite uses synthetic key/shortcut types by default. For optional local
checks of installed BepInEx parsing and shortcut behavior, pass
`-p:NativeInput=true -p:GameRoot=<local-reference-directory>`. The extraction gun
geometry suite also requires local data:
`dotnet run --project extras/hh60-visual/tests/extraction-gun-visuals/ExtractionGunVisualTests.csproj -c Release -- <local-payload>/scene.json`.
Neither optional check belongs in proprietary-free CI.
