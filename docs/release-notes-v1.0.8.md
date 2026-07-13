# Tylevo's Tactical Services Control v1.0.8 Public Beta

Install-ready beta package for SPT 4.0.13. Extract it into the SPT root while the game and server are closed.

This is the first Forge release after v1.0.6. Version 1.0.7 was used for GitHub/test builds but was never published on Forge, so v1.0.8 includes all 1.0.7 changes as well as the newer fixes below.

## Included From The Unpublished v1.0.7 Build

- The F12 "Configure TSC at" address now opens the dashboard at `/tsc/admin` instead of displaying the raw config endpoint.
- TSC config polling now runs only during raids and defaults to every 60 seconds, greatly reducing menu/hideout request traffic and server log spam while still refreshing at raid start.

## Highlights

- Support deployment now happens from the TSC Uplink phone. Buy an authorization, press the configurable deploy key, select the exact service you purchased, and designate the target without carrying the rangefinder.
- The phone can automatically zoom and reframe itself while raised. FOV and horizontal/vertical framing are configurable in F12 and restore cleanly when the phone is stowed.
- Purchase, deploy, and spotter-confirm keybinds are now visible in F12.
- A-10 single and double passes stay separate from purchase through deployment, so selecting one no longer spends or launches the other.

## Payment And Authorizations

- Fixed authorizations being duplicated, not consumed, or restored with the wrong service type.
- Stash purchases are serialized and transactional. TSC restores the inventory if profile saving or authorization persistence fails, preventing lost roubles without a matching authorization.
- `PreferStashThenCarried` now falls back to carried roubles when stash payment is unavailable.
- The purchase endpoint accepts plain, zlib, and deflate request bodies and returns a controlled failure instead of crashing on malformed or unexpected input.
- The authorization ledger now uses atomic saves, backups, corrupt-file preservation, and mutation rollback.

## Fika And A-10

- Exactly one raid-world authority executes an A-10 strike. Single-player and human Fika hosts retain the original Arys runtime/ballistic behavior; clients are visual-only.
- Every support request carries a unique ID. In-flight and completed request gates prevent duplicate network packets from firing projectiles or consuming authorizations twice.
- Fika clients wait for the host/headless acceptance broadcast before starting A-10 visuals.
- Tracers and impact effects are aligned to the visible aircraft firing pass and keyed to the request, seed, and pass.
- Dedicated-headless A-10 damage is available through the experimental headless executor and Fika damage packets. This remains intentionally gated and experimental.
- UAV radar overlays are requester-local and are not created on a dedicated headless host.
- Fika integration startup/shutdown and duplicate-request retention are bounded and safe across repeated raid initialization.

## Stability And Compatibility

- Fixed concurrent AssetBundle loading, including the UAV radar HUD double-load race.
- Added compatibility with Manimal's HackerMod phone assets by reusing its complete phone bundle set when present.
- Fixed null-reference teardown paths in `SimpleSpinBlur`, the fire-support pool, UI/controller cleanup, and phone camera restoration.
- Hardened UH-60 extraction trigger cleanup and Fika extraction routing to reduce stuck black-screen exits.
- Restored the loud A-10 strike flyover while keeping UAV loiter audio quiet and non-looping.

## Updating

Extract over the existing installation. Back up `SPT/user/mods/Tylevo.TacticalServicesControl/config/tsc-config.json` first if it contains custom dashboard settings.

Required dependencies are not bundled: UnityToolkit v2.0.1, WTT Client Common Lib, and WTT Server Common Lib. Project Fika is optional and only required for multiplayer.

## Known Issues

- Dedicated-headless Fika A-10 damage remains experimental. It is not claimed to match the original player-host ballistic path on every map or mod combination.
- Remote third-person phone animation sync is not included.
- Phone inventory inspect presentation may still need polish.
