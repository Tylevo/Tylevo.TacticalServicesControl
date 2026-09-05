# Tylevo's Tactical Services Control v1.3.8 Public Beta

> **Historical documentation.** These instructions and results describe this
> earlier version. For TSC v1.3.10 on SPT 4.1.5, use the
> [current release notes](release-notes-v1.3.10.md) and
> [installation guide](dependencies.md). See the [archive index](archive/README.md)
> for earlier release availability.

**Target:** SPT 4.1.4 / EFT 0.16.9.5.40743.

**Installable archive:** `Tylevo.TacticalServicesControl-v1.3.8-SPT4.1.4-TESTER.zip`.

This cumulative preview includes the SPT 4.1.4 compatibility update and all
changes from v1.3.0 through v1.3.8. It is suitable for beta testing; complete
solo and multiplayer acceptance is still in progress.

## Included Changes

- **SPT 4.1.4 and optional Danger Close integration — v1.3.0.** Retains the repaired Uplink bundle, native configuration support, and input fixes. Adds an Uplink-only fourth special slot and the versioned Seasonal Modifiers API. With that optional mod, an equipped Uplink in the dedicated slot enables advance A-10 warnings, while the final inbound warning reaches players without a device. Standalone TSC remains supported.
- **A-10 ballistic correction — v1.3.1.** Fixes uncompensated aim that could make rounds land short of the laser marker. Every round uses EFT's native trajectory model, including gravity, drag, ammunition properties, and weapon speed, from the moving gun position. Target-surface and cover checks follow the curved path; tracer/impact replay accounts for projectile travel time. Dedicated-headless timing and fallback checks were updated, while that damage mode remains experimental. See the [ballistic analysis and acceptance procedure](a10-ballistics-v1.3.1.md).
- **Smoother authorization-phone zoom — v1.3.2.** FOV and hand framing ease into place after a short lead-in. The default incoming transition is 0.75 seconds and the outgoing restoration is 0.35 seconds, both configurable in F12. Closing or quickly reopening preserves the original raid FOV as the restore target. Deploy and held-radar views keep the current raid FOV.
- **Native phone screens and Alt mouse selection — v1.3.3.** Live panels and text show service prices, currency, balances, owned authorizations, availability, and recon parameters. Hold Left Alt to browse with a cursor on the handset; release it to look around. Keyboard controls remain available. Purchase browsing stays landscape, with the final confirmation using the portrait hand-swipe animation.
- **Redesigned pre-raid store — v1.3.4.** Six selectable service cards, a detail panel, and a separate confirmation dialog share the phone's visual style. Prices and balances come from the active server. Unavailable services, purchase limits, insufficient funds, loading, and interrupted-purchase recovery remain visible without changing payment authority or recovery rules.
- **Updated tactical artwork — v1.3.5–v1.3.6.** The store and phone use the redesigned service and status icons. Pale rounded perimeter frames were removed from the six service images; card borders and selection highlights remain. Extraction and Cargo Transfer have distinct helicopter symbols.
- **Main-menu bottom-bar entry — v1.3.7.** TSC UPLINK now sits immediately left of Character in the native footer, without inserting a center-menu row. Its navigation state is separate from Character, and it hides when leaving the main menu. Seasonal Modifiers still suppresses this redundant shortcut when its client is loaded.
- **Unlocked UH-60 Pilot shop and portrait — v1.3.8.** The physical TerraGroup TSC Uplink has moved from Jaeger to Pilot for **₽50,000**, at **loyalty level 1**, with the existing **five-per-restock** limit. Pilot is unlocked without a quest for now and uses the new portrait in Trading and cargo mail.

The six services remain A-10 Strafe, A-10 Double Pass, UH-60 Extraction,
UH-60 Cargo Transfer, UAV Recon, and Focused Sweep. Mortar/artillery support
is not included.

## Controls

| Default control | Action |
| --- | --- |
| `U` | Open the carried Uplink to buy authorizations. |
| Hold `Left Alt` | Move the phone cursor; click a category, service, or its review/confirmation control. Release Alt to restore camera look. |
| `LMB`, then `1`–`3` | From the purchase home screen, tap LMB to open Tactical Services, then choose UH-60, Fire Support, or UAV. Within a category, `1`/`2` opens the standard/alternate service review. |
| `Enter` on purchase review | Start the automatic portrait confirmation sequence. No manual swipe gesture is required. |
| `K` | Open the owned-authorization deployment list. Use Alt and the deploy button, or `1`–`6` followed by `LMB`/`Enter`. |
| Middle mouse or `Enter` | Confirm camera-based A-10/UH-60 targeting steps. |
| `Left Alt` + `RMB`, or `Backspace` | Cancel target designation. |
| Hold `J` | Raise the phone radar during active recon in Phone display mode; release to return to the weapon. |
| `RMB` / `Escape` | Go back / close purchase browsing. In deployment, RMB, Backspace, or Escape stows the phone without spending the authorization. |

F12 provides the Uplink/deploy/radar/target-confirm bindings, phone cursor
modifier and sensitivity, automatic zoom, framing, transition times, and
Phone/HUD recon display. Zoom-in accepts 0.25–1.5 seconds; zoom-out accepts
0.15–0.8 seconds. These zoom settings affect authorization screens only.

## Requirements and Installation

- [SPT 4.1.4](https://github.com/sp-tushonka/build/releases/tag/4.1.4).
- [UnityToolkit 2.0.1 plus the SPT 4.1 compatibility overlay](dependencies.md). Install the official upstream package first, then `UnityToolkit-v2.0.1-SPT4.1-compat-overlay.zip` from the [v1.3.8 release](https://github.com/Tylevo/Tylevo.TacticalServicesControl/releases/tag/v1.3.8). The overlay updates the plugin and prepatcher; the unmodified upstream binaries alone are incompatible.
- [WTT CommonLib 3.0.6](https://github.com/WelcomeToThursday/WTT-CommonLib/releases/tag/v3.0.6), including its client, server, and serialization prepatcher components.
- For multiplayer only, [Fika client 2.4.2](https://github.com/project-fika/Fika-Plugin/releases/tag/v2.4.2) and its compatible server component. Install the same TSC package on every participating machine, including a headless host.

Close EFT, the launcher, the SPT server, and any Fika/headless processes.
Back up profiles and existing TSC state, then extract the **full release ZIP**
into the SPT installation root. The resulting TSC folders must be:

```text
BepInEx/plugins/Tylevo.TacticalServicesControl/
SPT_Runtime/user/mods/Tylevo.TacticalServicesControl/
```

Install all four matching DLLs and accompanying assets. The source archives
generated by GitHub are not installable packages. Dependencies are separate
from the TSC ZIP; follow the [dependency installation guide](dependencies.md).
Do not install the old SamSWAT Fire Support or Arys Reloaded alongside TSC.
Start the server, then launch the game. Restarting both processes refreshes
the cached icons and Pilot portrait.

For an existing installation, preserve TSC's `config/` and complete `storage/`
directories; the ZIP does not overwrite them. If they still live under the
old `SPT/user/mods/Tylevo.TacticalServicesControl/` path, back them up and copy
them into the current `SPT_Runtime/user/mods/Tylevo.TacticalServicesControl/`
folder before starting the server. Keep the new package's code and assets.
Do not merge separate ledgers or copy old DLLs over the new package. This is
TSC state migration only: use SPT's guidance for player-profile compatibility
and transfer between installations. See the [full upgrade instructions](../README.md#updating-an-existing-installation).

## Pilot Migration and Cargo

Existing profiles with a locked Pilot entry are migrated at server startup.
Only Pilot's unlocked flag changes; loyalty, standing, sales, cargo dialogue,
and other traders are preserved. Profiles without a Pilot entry use SPT's
normal trader initialization. A failed save restores the prior flag. No
manual profile edit or quest completion is needed.

The existing assortment filename, `jaeger_uav_uplink.json`, is retained so an
overlay update replaces the old listing rather than leaving duplicate offer
files. Its trader destination is now Pilot. The native BTR Driver and cargo
fallback remain separate, and Pilot retains the same trader identity for
delivery mail and future quest work.

UH-60 Cargo Transfer sends items and **does not extract your PMC**. Its TSC
authorization pays for dispatch. EFT charges a separate **RUB-only handling
fee** when cargo is loaded, using the F12 Carried/Stash selector. Cargo is
supported in solo and for a requesting human Fika host; non-host and
dedicated-headless requesters remain unavailable pending authoritative
handling-fee synchronization.

## Verification and Remaining Beta Checks

The v1.3.8 implementation passed **216 regression tests** and a full local
five-project build with **zero errors and four pre-existing warnings**.
The verified package contains **169 files**, including four matched TSC DLLs
and eight pinned bundles. Layout harnesses exercised the actual phone,
store, and footer construction, including large prices and varied viewports.

After installation, five native server read requests passed **22 checks**:
Pilot was unlocked, only Pilot sold the Uplink, its original purchase terms
were retained, the portrait bytes matched, and inventory, quests, BTR state,
and trader progress were preserved. These checks did **not** submit a paid
Uplink purchase or validate the Trading screen in game.

The phone interface and Alt controls received positive in-raid user feedback.
Broader acceptance remains open for Pilot's final appearance and purchase,
footer navigation across resolutions, A-10 real collision accuracy, and the
full human-host/client/headless multiplayer matrix. Automated trajectory
predictions and layout previews are not measured game impacts or rendered
in-game acceptance results.

Dedicated-headless A-10 damage remains experimental. Request settlement also
has known failure cases: loss of both acceptance and cancellation settlement,
or a crash/outage beyond pending expiry, can refund a service that already
executed. Remote third-person phone animation sync is not included. See
[known issues](known-issues.md) and [Fika guidance](fika.md) before multiplayer
testing. Preserve backups while testing this preview.
