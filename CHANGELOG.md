# Changelog

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
