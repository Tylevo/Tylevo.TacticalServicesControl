# Changelog

## 0.9.8 - Public Beta (released as v1.0.8)

### Added

- Added a complete phone-based deployment workflow. Purchased authorizations are now selected and deployed from the vertical TSC Uplink interface instead of requiring the YY gesture wheel.
- Added configurable F12 keybinds for opening the purchase phone, opening the deployment phone, and confirming spotter targets.
- Added optional automatic phone zoom with FOV, vertical framing, and horizontal framing controls. The previous camera FOV and viewmodel offset are restored when the phone is stowed.
- Added explicit A-10 authority roles and unique support request IDs for Fika. The raid authority tracks in-flight and completed requests so duplicate packets cannot fire the same strike twice.
- Added an experimental dedicated-headless A-10 damage executor. It uses the headless raid authority and Fika damage packets while clients remain visual-only.

### Fixed

- Fixed authorization counts becoming duplicated, not being consumed, or returning as the wrong A-10 option. Single-pass and double-pass authorizations now remain separate through purchase, deployment, consume, commit, and refund.
- Hardened stash purchases into a serialized transaction. Invalid quantities are rejected, profile inventory is restored if saving or authorization persistence fails, and failed mutations no longer leave the player charged without an authorization.
- Fixed `PreferStashThenCarried` purchases failing when stash payment is unavailable even though the player has enough carried roubles.
- Fixed compressed `/tsc/purchase` request bodies failing JSON parsing. The server now accepts plain JSON plus zlib/deflate request bodies and returns a controlled denial if purchase handling throws.
- Made the authorization ledger durable with atomic file replacement, backup recovery, corrupt-file preservation, and rollback when a ledger write fails.
- Fixed Fika A-10 clients starting prediction before the host accepted a strike. Clients now wait for the authority broadcast, render the accepted visual pass, and never execute authoritative damage.
- Synchronized Fika tracer and impact replay to the visible A-10 firing pass. Replay is keyed by support request, seed, and pass so double passes do not create early fake bursts or mismatched impacts.
- Fixed UAV HUD ownership in Fika: only the requesting client creates the radar overlay, and a dedicated headless host does not create client HUD objects.
- Fixed concurrent or repeated AssetBundle loads, including the UAV radar HUD double-load failure. A per-bundle load gate now reuses a load that won the race.
- Added HackerMod phone-bundle compatibility. When Manimal's complete HackerMod phone bundle set is installed, TSC reuses it instead of loading a conflicting duplicate phone asset.
- Fixed `SimpleSpinBlur`, fire-support pools, UI controllers, and phone zoom teardown paths that could throw null-reference errors during raid shutdown or repeated initialization.
- Hardened UH-60 extraction trigger cleanup and Fika extraction routing to reduce stuck black-screen exits and stale extraction coroutines.
- Restored the full-volume A-10 strike flyover sound while keeping the UAV loiter aircraft quiet and non-looping.

### Changed

- The YY gesture wheel is retired from the main workflow. Deployment now goes through the TSC Uplink phone: after purchasing an authorization, a notification shows the deploy key, and pressing it (default `K`, configurable as "Open deploy key") pulls the phone out already vertical with a deploy selector listing only the authorizations you currently hold, styled to match the purchase screens. The phone is held one-handed (the free hand is tucked out of view; "Deploy hide right hand" config). Number keys (1-6) select a service, tapping (LMB, or Enter) deploys it — the spotter or UAV starts within half a second while the phone stows — and Backspace/Escape/RMB puts the phone away. A short arming delay after opening prevents stray clicks from spending an authorization, and the selector shows a "Station busy" countdown while the support cooldown runs. The authorization is only consumed when the deployment actually starts, exactly as before. UAV deploys from the Uplink no longer replay the activation-device phone animation; the radar starts immediately.
- The rangefinder is no longer required as a target designator. A-10 and UH-60 targeting uses the same spotter view raycast from your camera with any item in hands; Enter confirms each targeting step (LMB still works, but fires a held weapon), and Alt+RMB or Backspace cancels. Purchase and deploy remain separate phone states, so buying a double pass and deploying it can no longer disagree about which A-10 option is used.
- The old YY radial and its rangefinder flow are still available behind the new "Enable legacy YY radial" config toggle (default off) for this release, and will be removed once the deploy phone is stable.
- Fika A-10 authority is now explicit: single-player and a human Fika host keep the original Arys runtime/ballistic path; a Fika client is visual-only; a dedicated headless host may use only the gated experimental damage path.
- A-10 requester attribution is tracked separately from projectile ownership, with detailed authority, owner, candidate, tracer, and fallback diagnostics for headless testing.
- Increased the default automatic phone zoom FOV from 42 to 45 for a slightly wider, more natural view. Existing custom FOV values are preserved; only the previous untouched 42 default is migrated.

### Known Issues

- Dedicated-headless Fika A-10 damage remains experimental. It has been tested successfully, but it is intentionally gated and is not claimed to be identical to the original player-host ballistic path on every map or mod combination.
- Remote third-person phone animation sync is still not included.
- Phone inventory inspect presentation may still need polish.

## 0.9.7 - Public Beta (released as v1.0.7)

### Fixed

- The F12 "Configure TSC at" address now points at the dashboard (`/tsc/admin`) instead of the config endpoint (`/tsc`), which returned raw JSON. Opening the shown address now loads the dashboard directly.
- Reduced TSC server request logging and idle memory/CPU use. The config was polled every 10 seconds continuously, including in the menu and hideout where it is never used, and since v1.0.6 each poll also logged a request line. Polling now runs only during a raid and defaults to every 60 seconds, cutting the request volume and log spam by roughly 50-100x for a typical session. Dashboard edits still apply within a minute in-raid, and a fresh config is loaded at the start of every raid.

## 0.9.6 - Public Beta (released as v1.0.6)

### Fixed

- Stash purchases now work for every Fika client with no network configuration. TSC calls to the server (purchases and config/stash sync) now route through SPT's own backend connection instead of a separately configured HTTP URL, so they automatically reach the correct server for the host and for clients on any network (LAN, Radmin VPN, direct) and charge the right player's stash. The Server Config URL setting is no longer needed and can be left at its default; a wrong value no longer causes "Check the TSC server and dashboard connection."

## 0.9.5 - Public Beta (released as v1.0.5)

### Fixed

- Fika clients can now use stash-based purchases when they point their TSC server URL at the host. A host running default config broadcasts its own loopback address (127.0.0.1), which used to override each client's configured host address and send the purchase to the client's own machine. A loopback host broadcast is now ignored so the client's own Server Config URL takes effect. (For zero-config clients, the host can instead set its Server Config URL to its LAN IP; or use the CarriedRoubles payment source, which is fully client-side.)
- UH-60 extraction as a Fika host no longer strands the lobby. Extraction routed through EFT's session stop instead of Fika's extract flow, so a host extracting first killed the hosted session while other players kept playing into a dead lobby. Extraction now goes through Fika's extract path (host to spectate, session stays alive); solo and non-Fika installs are unchanged.

### Changed

- Phone navigation simplified. Number keys (1-3) now open a category directly instead of only highlighting it, RMB steps back one screen, and Escape closes the phone. Enter still only confirms on the final screen so stray input cannot spend money.
- The TerraGroup TSC Uplink now uses the special-equipment look: orange grid background and the orange SPEC tag, matching the rangefinder, so it sorts and reads as special-slot gear.

## 0.9.4 - Public Beta (released as v1.0.4)

### Fixed

- Carried-rouble purchases no longer lose your money. Authorizations bought with carried roubles used to vanish within seconds of purchase — the service showed AUTH REQ again unless you deployed it almost immediately, and the roubles were spent either way. These purchases now persist for the whole raid and can be deployed whenever you're ready.
- Non-host Fika players now see A-10 tracers reliably. Tracer playback was scheduled against the host's clock, which is unrelated to the client's; depending on which machine had more uptime, tracers rendered all at once or never. Clients now anchor playback to their own packet arrival time.
- Non-host Fika players now see the GAU-8 impact explosions. Only the host simulates the A-10 ballistics, so detonation effects existed only there; clients now emit the same big_smoky_explosion effect at each round's impact point during tracer playback, matching the host's view.
- Potentially fixed a freeze (movement and camera locked, weapon still usable) affecting loot pickups after the phone had been opened from its special slot and cancelled with the uplink hotkey. Two hand-restore flows raced; quick-use sessions are now restored by the game alone. The race was intermittent by nature, so please report if it still occurs on this version.
- Carried-rouble payment now counts money stored in the secure container. The previous inventory scan excluded it, so purchases failed with "Carried Roubles: 0" despite cash being on the character.
- Stash balance now syncs outside raids too. Config requests previously carried no profile id in menus, the hideout, or the first seconds of a raid, so stash-based payment sources displayed carried-only balances until an in-raid sync completed.

### Changed

- Rebalanced default prices for new installs: Extraction 300k (was 50k), Priority Exfil 450k (was 150k), UAV 125k (was 100k), Focused Sweep 90k (was 75k). A-10 Strafe and Double Pass unchanged. Max stored authorizations per service reduced from 3 to 2. Existing configs keep their saved values.
- TSC now declares a BepInEx incompatibility with SamSWAT's Fire Support: Arys Reloaded (requested by Arys). TSC is its derivative replacement and the two cannot run together; BepInEx now skips TSC with a clear message instead of letting them corrupt each other.
- Dashboard is easier to find: opening `/tsc` in a browser now redirects to the dashboard at `/tsc/admin` (the game's config polling is unaffected), the dashboard asset error now names the folder it expects so missing installs are self-diagnosable, and the README dashboard URL was corrected.

## 0.9.3 - Public Beta (released as v1.0.3)

### Fixed

- Rebuilt all eight asset bundles with unique internal archive (CAB) identities. The phone bundles previously shared CAB IDs with Manimal's Hacker mod, the radar HUD bundle was a byte-copy of Tyrian Radar Standalone's bundle, and the FireSupport-lineage bundles shared IDs with the original SamSWAT Fire Support. Unity refuses to load a bundle whose archive ID is already loaded, so running TSC alongside any of those mods broke the phone: missing inventory icon, red ERROR model (inventory and inspect screen), and crashes or failed raid loads with the phone equipped.
- Picking a dropped TSC Uplink off the ground with F no longer freezes the player. The equip patches previously intercepted items that were not yet in the player's inventory, breaking EFT's pickup interaction.
- Quick-using meds, grenades, or other items while the phone session is active no longer freezes the player; the swap is now declined cleanly instead of leaving EFT waiting forever.
- The phone no longer reacts to mouse clicks, number keys, or cancel input while the inventory screen is open.
- The uplink hotkey is ignored while the inventory screen is open.

## 0.9.2 - Public Beta (released as v1.0.2)

### Fixed

- Fixed infinite loading on installs without Fika, introduced in 0.9.1. The Fika plugin DLL contained packet types referencing Fika.Core; once the plugin started loading on non-Fika installs, other mods' assembly-wide type scans (for example WTT Client Common Lib) crashed with ReflectionTypeLoadException. All Fika-typed code now lives in `Tylevo.TacticalServicesControl.Fika.Interop.dll`, which is only loaded after Fika is confirmed present, so it stays invisible to type scans on single-player installs.

## 0.9.1 - Public Beta (released as v1.0.1)

### Fixed

- Fika is now a soft dependency. The TSC Fika plugin loads cleanly on installs without Fika and no longer logs a missing `com.fika.core` dependency error; multiplayer sync simply stays disabled.

## 0.9.0 - Public Beta

### Added

- TerraGroup TSC Uplink item.
- TerraGroup phone authorization flow.
- PhoneAuthorizations and Hybrid payment modes.
- Stash rouble payment support.
- Carried rouble payment support.
- A-10 Strafe support authorization.
- A-10 Double Pass support authorization.
- UH-60 Extraction support authorization.
- Priority Exfil support authorization.
- UAV Recon support authorization.
- Focused Sweep support authorization.
- Fika support request sync.
- Fika host-authoritative settings sync.
- Dynamic phone UI pricing.
- Opaque LCD backplate renderer.
- Local dashboard configuration UI.
- UAV radar overlay.
- UAV A-10 loiter visual.

### Changed

- Support purchase and support deployment are separated.
- Phone buys prepaid authorizations.
- YY/rangefinder deploys targeted support later.
- Base request amounts can be set to 0 without blocking prepaid phone authorizations.
- Public-facing branding now uses Tylevo's Tactical Services Control / TSC.

### Fixed

- Phone no longer shows white screen during startup.
- Phone LCD no longer shows world/xray transparency.
- Previous weapon restores after phone close.
- Non-host A-10 tracer visibility confirmed in Fika testing.

### Known Issues

- Phone inventory inspect model may still need polish.
- Mortar/artillery support is deferred.
- Phone-as-designator is deferred.
- Remote third-person phone animation sync is deferred.
