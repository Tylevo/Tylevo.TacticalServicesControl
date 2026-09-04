# Tylevo's Tactical Services Control v1.3.0 Public Beta

This SPT 4.1.2 tester keeps TSC fully standalone while adding the optional
Danger Close API v3 integration used by Tylevo Seasonal Modifiers.

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

The proprietary-free suite passes 160 regression tests, and all four runtime
projects compile against the pinned SPT 4.1.2, Fika, WTT, and UnityToolkit
references with deployment disabled. Live solo/Fika warnings, profile UI, and
raid impact behavior remain tester acceptance gates.
