# Tylevo's Tactical Services Control v1.3.0 Public Beta

> **Historical documentation.** These instructions and results describe this
> earlier version. For the prepared TSC v1.3.11 / SPT 4.1.5 update, use the
> [release notes](release-notes-v1.3.11.md) and [installation guide](dependencies.md).
> TSC v1.3.11 requires standalone UnityToolkit 2.0.2; both new packages are
> unpublished. See the [archive index](archive/README.md) for older availability.

This SPT 4.1.4 tester keeps TSC fully standalone while adding the optional
Danger Close API v3 integration used by Tylevo Seasonal Modifiers.

The compatibility update retains TSC v1.3.0, targets EFT 0.16.9.5.40743, and
uses WTT CommonLib 3.0.6 and optional Fika client 2.4.2. UnityToolkit remains
the existing SPT 4.1 rebuild of 2.0.1. The source audit found no direct or
string-based references to the fields renamed by SPT 4.1.4; bundle and
exact-version build validation are tracked in
`docs/port/SPT-4.1.4-PORT-LOG.md`.

TSC now adds a dedicated `SpecialSlot4` to both supported pockets templates.
Only the TerraGroup TSC Uplink fits there. The slot does not grant, force, or
lock the device; players still obtain and equip it normally. Manual TSC service
calls continue to recognize an Uplink carried elsewhere.

When Danger Close is active, an Uplink in slot 4 enables the 90-second advance
forecast. An accepted A-10 pass produces a universal final inbound alert even
for players without the device. In Fika, the listen host publishes reliable
warning events and every peer evaluates its own slot independently. Clients
cannot originate those events.

Existing profiles receive a conservative one-time migration: one Uplink
directly equipped in stock special slots moves to slot 4. Stash and backpack
items are untouched, and occupied, ambiguous, foreign-slot, or failed-save
cases preserve the original item location.

Ambient A-10 requests remain host-authoritative, payment-free, and distinct
from manual service requests. Their projectiles now require a validated real
EFT player bridge, preventing the impact callback failure caused by synthetic
ballistic owners.

A-10 projectile origins now follow the visible moving aircraft in solo and on
a human Fika host. The first round starts at the current muzzle instead of the
old point 515 metres ahead, and subsequent rounds advance with the aircraft
through the burst. Dedicated-headless Fika keeps its shorter experimental
damage origin for reliability; its damage and client replay share the same
deterministic intended impacts, but exact ballistic paths and arrival timing
can still differ.

When the Seasonal Modifiers client is loaded, it owns the main-menu presentation
and TSC removes its separate **TSC UPLINK** row. The native menu spacing is
restored, while all in-raid Uplink, UAV, UH-60, and Danger Close behavior remains
available. Standalone TSC installs retain the pre-raid storefront.

Historical SPT 4.1.2 verification passed 160 regression tests and compiled all
four runtime projects against its pinned references with deployment disabled;
that evidence remains in `docs/port/SPT-4.1-PORT-LOG.md`. The combined SPT 4.1.4
candidate now passes 168/168 regression tests and its exact-version five-project
build with 0 errors and four existing warnings. Final package validation,
packaged-server startup, and seven HTTP checks pass. Live solo/Fika warnings,
profile UI, and raid impact behavior remain pending tester acceptance gates.
See `docs/port/SPT-4.1.4-VALIDATION.md` for the exact tested artifact.
