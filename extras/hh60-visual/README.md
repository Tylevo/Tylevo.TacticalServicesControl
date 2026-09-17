# Optional Icebreaker HH-60 visual companion

This is the source of an **opt-in visual replacement**, tested locally with
TSC **1.3.13**, SPT **4.1.5**, and EFT **0.16.9.5-40743**. It attaches the
Black Division-associated HH-60 from the Icebreaker cutscene to TSC's existing
UH-60 controller. The normal TSC solution, release package, and default UH-60
are unchanged. It is not an additional helicopter service.

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
| Door gunners | 2 | Fixed poses, do not fire |
| Cabin support crew member | 1 | Fixed pose |

All five are **visual meshes only**: no AI, dialogue, interaction, combat,
damage model, boarding, or rappelling. No rope geometry or rope animation is
enabled. They are not multiplayer entities.

## What changes, and what does not

- One Harmony postfix on `UH60Behaviour.OnAwake`; no AssetLoader/AssetBundle
  hooks and no replacement Unity bundle loaded at runtime.
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

On the local installation, the user explicitly confirmed:

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

The runtime sources match the tested 0.2.1 implementation; only build paths,
offline-tool arguments, and test infrastructure have been made portable.
The companion deliberately accepts only the tested 1.3.13 Core SHA-256:

```text
9144491E2C1A359E909148C55817905D00F1330DD783155E5809D5C712E0BF7E
```

A different/rebuilt Core fails closed to the original model. This pin is not
a general compatibility claim. Maintainers must validate and intentionally
update it before distributing a different build.

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

Use the repository's pinned .NET SDK. Building reads local game references but
never deploys automatically:

```powershell
$game = Read-Host 'Full path to your SPT installation'
./extras/hh60-visual/build.ps1 -GameRoot $game
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

The setting defaults to **off**. Start once to create
`BepInEx/config/local.tsc.hh60visual.cfg`, close the client, set
`[Visual] Enabled = true`, and restart. To return to the original TSC model,
close the client, set it to `false`, and restart (or remove only this companion).
Do not install old experimental asset-bundle redirect versions alongside it.

Run the proprietary-free tests from the repository root:

```powershell
dotnet run --project extras/hh60-visual/tests/payload/PayloadTests.csproj -c Release
dotnet run --project extras/hh60-visual/tests/glass/GlassPolicyTests.csproj -c Release
& ./.venv-hh60/Scripts/python.exe -m unittest discover -s extras/hh60-visual/tools -p test_export_safety.py
```

The default suites use synthetic input only: 30 payload checks, 45 glass-policy
checks, and 12 exporter safety checks. For optional local real-data validation,
append `-- --validate "$work/payload/scene.json"` to the payload test command or
`-- --input "$work/payload/scene.json"` to the glass test command (50 checks).
CI must never load or publish donor assets.
