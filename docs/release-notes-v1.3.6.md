# Tylevo's Tactical Services Control v1.3.6 Public Beta

> **Historical documentation.** These instructions and results describe this
> earlier version. For the prepared TSC v1.3.11 / SPT 4.1.5 update, use the
> [release notes](release-notes-v1.3.11.md) and [installation guide](dependencies.md).
> TSC v1.3.11 requires standalone UnityToolkit 2.0.2; both new packages are
> unpublished. See the [archive index](archive/README.md) for older availability.

Targets SPT 4.1.4 / EFT 0.16.9.5.40743 with the existing dependencies.

The six service icon images no longer have pale rounded perimeter frames.
This affects the store, native phone service screens, and Cargo Transfer's
Pilot avatar. The service-card outlines and green selection highlight remain.
Runtime code, layouts, controls, and gameplay behavior are unchanged.

Both normal and selected asset slots use the edited artwork. The pickup box,
cargo crate, aircraft details, rotor rings, radar arcs, and amber targeting
brackets remain part of the symbols. The cargo alias `supply_evac.png` matches
`priority_exfil.png`. Other status and supporting icons are unchanged.

The six images were edited using the built-in image tool and retain its
1254 x 1254 opaque PNG output. The loader already accepts square PNGs at this
resolution; UI dimensions remain the same. Generative editing introduces
minor changes to fine shading and antialiasing, so original pixel identity
is not claimed. Prompts and output hashes are retained in external evidence.

The expected archive is
`Tylevo.TacticalServicesControl-v1.3.6-SPT4.1.4-TESTER.zip`. Install its assets
and four matched DLLs with EFT closed. Restart EFT to clear its cached icons.
Restart the SPT server to refresh the cached Pilot avatar.

Build, asset, package, and installation results are recorded in external
evidence sidecars. Visual previews check icon clarity and removal of the
outer frame; final appearance on the phone remains an in-game check.
