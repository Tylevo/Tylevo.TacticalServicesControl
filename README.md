# Tylevo's Tactical Services Control

A BepInEx mod that reworks SamSWAT's Fire Support / Arys Reloaded into a TerraGroup-style tactical support system for SPT and Fika.

> **v1.3.8 public beta · SPT 4.1.4 / EFT 0.16.9.5.40743**
>
> Download the packaged mod from the [v1.3.8 release](https://github.com/Tylevo/Tylevo.TacticalServicesControl/releases/tag/v1.3.8).
> Use `Tylevo.TacticalServicesControl-v1.3.8-SPT4.1.4-TESTER.zip`.
> This is a preview release; the full multiplayer acceptance matrix remains open.
> See the [v1.3.8 release notes](docs/release-notes-v1.3.8.md) for the cumulative update and verification status.

This mod adds a **TerraGroup TSC Uplink** phone that lets you buy support authorizations in raid, then deploy them later from the same device. The phone handles service selection and camera-based target designation, so the rangefinder and YY gesture wheel are no longer required for the primary workflow.

Currently available support options:

- A-10 autocannon strafe.
- A-10 Double Pass.
- UH-60 Black Hawk extraction.
- UH-60 Cargo Transfer.
- UAV Recon.
- Focused Sweep.

Tylevo Seasonal Modifiers can optionally use TSC's versioned Danger Close API
to schedule environmental, host-authoritative ambient A-10 passes. TSC remains fully
standalone when Seasonal Modifiers is absent. See
[`docs/seasonal-modifiers-integration.md`](docs/seasonal-modifiers-integration.md)
for the API v3 dispatch, warning, and Uplink-slot semantics.

This project is derivative of SamSWAT's original Fire Support and SamSWAT's Fire Support - Arys Reloaded by Arys. Public redistribution is prepared with upstream permission recorded in `PERMISSIONS.md`, with full credit retained in `THIRD_PARTY_NOTICES.md` and `docs/credits.md`.

## What's New for SPT 4.1.4

The v1.3.8 package includes every update from v1.3.0 through v1.3.8:

- **Corrected A-10 aim:** each round uses EFT's native gravity and drag model from the moving gun position. Surface and cover checks follow the curved trajectory, and replay effects account for travel time. This corrects the uncompensated aim that could put rounds short of the marker; live impact accuracy remains part of beta testing.
- **Native phone interface:** service cards, descriptions, prices, balances, availability, and recon parameters use live text and panels. Hold **Left Alt** to browse with the phone cursor, then review and confirm. Keyboard controls remain available.
- **Smoother phone zoom:** authorization screens ease their FOV and hand framing into place, with configurable incoming and outgoing timing. Deploy and radar views preserve your raid FOV.
- **Redesigned pre-raid store and icons:** six selectable service cards, a detail panel, and a separate purchase confirmation share the phone's visual style. The service artwork has no pale perimeter frames; card selection outlines remain.
- **Native bottom navigation:** **TSC UPLINK** sits immediately left of **Character** on the main-menu footer. It no longer adds a center-menu row.
- **UH-60 Pilot shop:** buy the physical Uplink from Pilot for **₽50,000**, at loyalty level 1, with a limit of five per restock. Existing locked Pilot entries unlock at server startup. His new portrait also appears on cargo mail.
- **Optional Danger Close integration:** a dedicated Uplink special slot, host-authored A-10 warnings, and the versioned API support Seasonal Modifiers while preserving standalone TSC use.

## Requirements

- [SPT 4.1.4](https://github.com/sp-tushonka/build/releases/tag/4.1.4).
- [UnityToolkit 2.0.1 with the SPT 4.1 compatibility overlay](docs/dependencies.md).
  Install the official upstream package first, then
  `UnityToolkit-v2.0.1-SPT4.1-compat-overlay.zip` from the
  [v1.3.8 release](https://github.com/Tylevo/Tylevo.TacticalServicesControl/releases/tag/v1.3.8).
  The overlay updates the plugin and prepatcher; the unmodified upstream
  binaries alone are not compatible with this target.
- [WTT CommonLib 3.0.6](https://github.com/WelcomeToThursday/WTT-CommonLib/releases/tag/v3.0.6),
  including the client, server, and serialization prepatcher components.
- [Project Fika client 2.4.2](https://github.com/project-fika/Fika-Plugin/releases/tag/v2.4.2)
  with its compatible server component, optional and
  required only for multiplayer/Fika use. Single-player installs do not need
  Fika; TSC detects it at runtime.

The exact dependency commits, assembly versions, and SHA-256 values used for this
beta are recorded in [the port log](docs/port/SPT-4.1.4-PORT-LOG.md). Use those exact pins
for acceptance testing; compiler compatibility with a different dependency
build is not runtime evidence. Historical SPT 4.1.2 results remain in
`docs/port/SPT-4.1-PORT-LOG.md`.

Dependencies are installed separately and are not included in the TSC ZIP.
Follow the [dependency installation guide](docs/dependencies.md) for the
required package order and file locations.

Do not install the old SamSWAT Fire Support or Arys Reloaded mod alongside TSC. TSC is a derivative replacement package.

## Installation

1. Back up your profiles. When updating TSC, also back up its `config/` and complete `storage/` directories.
2. Close EFT, the launcher, the SPT server, and any Fika/headless processes before replacing files.
3. Install the required dependencies, then extract the **full release ZIP** directly into your SPT 4.1.4 root. GitHub's automatically generated source archives are not installable mod packages.
4. Confirm these folders exist after extraction:
   - `BepInEx/plugins/Tylevo.TacticalServicesControl/`
   - `SPT_Runtime/user/mods/Tylevo.TacticalServicesControl/`
5. Start the SPT server, then the launcher and game. Restarting both server and game loads the updated trader, portrait, and cached UI icons.

Do not place the ZIP contents inside an extra nested folder. Install all four
TSC DLLs and the accompanying assets together. Fika peers must use the same TSC
package. Do not leave another copy of an older TSC client/server DLL in a
different mod folder.

### Updating an Existing Installation

The ZIP excludes mutable configuration, storage, admin tokens, and profiles.
An overlay update within the current folders preserves those files. In
particular, keep the complete `storage/` directory: it contains purchased
authorizations and transaction/delivery recovery data. Existing configuration
and ledger formats migrate when the server starts. Restore a matching backup
of both mod files and state when reverting an upgrade.

Older SPT layouts used `SPT/user/mods/Tylevo.TacticalServicesControl/`.
SPT 4.1.4 loads this mod from `SPT_Runtime/user/mods/Tylevo.TacticalServicesControl/`.
If your TSC state still lives in the old folder, preserve a backup and copy
its `config/` and complete `storage/` directories into the new TSC server
folder while the server is stopped. Do this before the first launch, and
keep the new release's DLLs, database, bundles, web files, and artwork. If
both locations already contain state, choose the intended matching backup
instead of merging two ledgers. TSC does not search the old server folder
for configuration or storage automatically.

This transfers TSC's own state only. Follow SPT's version-specific guidance
for moving player profiles between installations; TSC does not perform that
profile transfer. If starting with fresh profiles, let the new installation
create fresh TSC state.

## How To Use

### Getting the Uplink

Buy the **TerraGroup TSC Uplink** from **UH-60 Pilot** in Trading for
**₽50,000**, at loyalty level 1, with the existing limit of five per restock.
The offer has moved from Jaeger. Pilot is available without a quest for now;
existing profiles with a locked Pilot entry are unlocked when the server starts.
The same contact continues to deliver UH-60 cargo mail.

### Pre-Raid Store

When Seasonal Modifiers is not installed, open **TSC UPLINK** on the bottom bar immediately left of **Character** while on the main menu. It uses the native footer layout and does not add a row to the center menu. The shortcut hides when leaving the main menu. When the Seasonal Modifiers client is loaded, TSC suppresses its redundant shortcut; use the in-raid Uplink purchase flow instead.

1. Open **TSC UPLINK** from the main menu on a standalone TSC install.
2. Wait for your stash balance and purchased authorizations to load.
3. Select a service card to see its artwork, description, price, availability, and owned authorizations. Use the purchase review control, then confirm in the dialog after checking the price and projected balance. Cancelling sends no purchase request.
4. Use **Dashboard** to open the active SPT server's local TSC Dashboard.

Pre-raid purchases require persistent authorizations and a server-backed stash payment source. They remain available when the same PMC enters a raid.

### In Raid

1. Bring the **TerraGroup TSC Uplink** into raid.
2. Press `U` to open the Uplink in purchase mode.
3. Hold `Left Alt` to move the phone cursor, open **Tactical Services**, and choose a category and service. Release Alt to restore camera look. For keyboard navigation, tap `LMB` on the home screen to open Tactical Services, then press `1`, `2`, or `3` for UH-60 Services, Fire Support, or UAV Recon. Within a category, `1` chooses the standard service and `2` its alternate, opening its review.
4. Review the selected service, then click the confirmation control while holding Alt or press `Enter` to start the portrait hand-swipe sequence and pay with the configured currency and wallet source. The swipe animation runs automatically; no manual drag is required. `RMB` returns to the previous screen and `Escape` closes the phone. Cancelling after a payment has committed does not undo the purchase.
5. When you are ready to use an authorization, press `K` to open the Uplink in deployment mode. Only services you currently own are listed.
6. Hold `Left Alt` to select a service with the phone cursor and click the deploy control. Alternatively, press `1`-`6` to select, then `LMB` or `Enter` to deploy. `RMB`, `Backspace`, or `Escape` stows the phone without spending the authorization.
7. A-10 and UH-60 services use camera-based target designation. Confirm each targeting step with `Mouse 2` (middle mouse) or `Enter`; cancel with `Alt + RMB` or `Backspace`.
8. UAV Recon and Focused Sweep begin directly after deployment. The default `Phone` display mode uses `J`: hold it to raise the Uplink and view the live radar, then release it to return to your weapon. Walking and sprint keys do not lower it while the radar key remains held, and the recon timer keeps running while the phone is stowed. The optional `HUD` display mode keeps only the square live scanner visible in a selected screen corner for the active recon session.
9. UH-60 Cargo Transfer lands at the marked loading zone and provides **SEND ITEMS VIA UH-60**. It never extracts your PMC. The authorization pays for dispatch; EFT calculates a separate RUB-only item-handling fee when cargo is submitted. In `F12`, **Transfer fee source** defaults to `Carried`, preserving EFT's native carried-RUB payment, or can use `Stash` to debit the authenticated PMC stash through the TSC server. This fee is independent of TSC's configured authorization currency. Once EFT confirms the paid items reached its persistent delivery grid, the helicopter departs immediately; cancelling or failing payment leaves the remaining landed window available for retry. Successfully marked cargo returns through post-raid mail from **UH-60 Pilot** without replacing the native **BTR Driver** contact; if TSC routing cannot be completed safely, the accepted native cargo falls back to BTR delivery instead of being discarded.

The `U`, `K`, `J`, and spotter-confirm controls are configurable in the BepInEx configuration manager opened with `F12`. `UAV Radar Display` also provides the `Phone`/`HUD` choice and four HUD positions, while `Helicopter Cargo` provides the `Carried`/`Stash` handling-fee source. Phone framing and optional authorization-screen zoom are available there as well; the `K` deploy view and held `J` radar preserve the current raid FOV and open upright.

When authorization-screen zoom is enabled, it begins after a 0.08-second
lead-in and eases the camera FOV and hand framing over 0.75 seconds by default.
In `F12`, **Phone zoom in seconds** accepts 0.25-1.5 seconds, and **Phone zoom
out seconds** accepts 0.15-0.8 seconds with a default of 0.35. Closing the phone
restores the original raid FOV; quickly reopening it retains that original
restore target. These settings do not change deploy or radar presentation.

Phone mouse selection can be disabled or adjusted in `F12`, including its
modifier and sensitivity. The cursor is part of the phone display and follows
the handset. Purchase browsing remains landscape until final confirmation;
the portrait swipe animation drives the payment commit as before.

Server/host settings are changed from the local TSC Dashboard:

```text
https://127.0.0.1:6969/tsc/admin
```

The dashboard is localhost-only by default. Do not port-forward it.

SPT's native mod-config editor also lists a curated **Tactical Services
Control** entry for service prices, availability, payment behavior, cooldowns,
persistence limits, UAV range/timing, UH-60 timing, and double-pass delay.
Both editors use TSC's validated, revision-checked `tsc-config.json` save path.
Security and player-specific state stay in TSC's dedicated interfaces.

## Features

- TerraGroup TSC Uplink item.
- Authenticated pre-raid authorization store with an explicit purchase confirmation.
- Phone-based support authorization flow.
- Phone-based support deployment and camera-ray target designation without requiring the rangefinder.
- PhoneAuthorizations and Hybrid payment modes.
- Configurable RUB, USD, or EUR payment from the stash or carried wallet.
- A-10 Strafe and A-10 Double Pass.
- UH-60 Extraction and UH-60 Cargo Transfer.
- Configurable carried- or authenticated-stash RUB payment for EFT's native Cargo Transfer handling fee.
- UAV Recon and Focused Sweep.
- Requester-only UAV radar rendered on the physical Uplink phone or as a scanner-only square corner HUD, plus the UAV A-10 loiter visual.
- Fika support request sync.
- Fika host-authoritative settings sync.
- Local TSC Dashboard configuration.
- Native SPT mod-config editor integration for supported service settings.

## Fika Installation

Install the same TSC version on the host, any headless host, and every client. The host config is authoritative while connected. Client local config does not override host settings during a joined raid, dashboard changes on the host sync to clients, and disconnect clears synced overrides.

UH-60 Cargo Transfer is currently supported for solo players and a requesting
human Fika host. Non-host and dedicated-headless requesters remain fail-closed
until the item-dependent native handling price has an authoritative host
synchronization contract. On a supported requester, `Stash` fee mode requires
the matching TSC server endpoint and fails closed without moving cargo or
charging carried cash when used with an older server.

## Payment Modes

TSC supports RUB, USD, or EUR from carried cash, stash cash, and hybrid payment behavior where configured. Select the server-authoritative currency in the dashboard. The phone displays the active price and balance source, and the server calculates authoritative stash prices. Client-sent prices and currency are not trusted.

Changing the currency does not convert the numeric service prices. Review every service price before saving a different currency.

These settings price the TSC dispatch authorization. UH-60 Cargo Transfer's
separate EFT handling fee remains RUB-only and uses its own local `F12`
`Carried`/`Stash` selector.

Back up profiles before testing payment-source modes.

## Dashboard

The TSC Dashboard is local by default:

- Public health route: `/tsc/health`
- Dashboard route: `/tsc/admin`
- Admin diagnostics route: `/tsc/admin/health`
- Config file: `config/tsc-config.json`
- Token file: `config/tsc-admin-token.txt`

The installer does not overwrite `config/tsc-config.json`. The server creates
the file with current defaults when no canonical or legacy config exists, and
migrates an existing file during upgrades.

Remote dashboard access is disabled by default. If you enable remote access, keep it on a trusted LAN/VPN only and require the admin token for writes. Do not port-forward the dashboard.

See `docs/dashboard.md`, `PRIVACY.md`, and `SECURITY.md`.

## Beta Status and Known Issues

The v1.3.8 implementation passed **216 regression tests**, a full local build
with no errors, and verification of the **169-file package**. Installed-server
checks confirmed Pilot's unlock, Pilot-only Uplink stock, unchanged price and
purchase limit, exact portrait bytes, and preserved profile/trader state.
Those checks did not submit a paid Uplink purchase or inspect its in-game UI.

The native phone and Alt controls received positive in-raid user feedback.
Layout harnesses and automated checks also cover the store and footer
integration, but they do not establish every resolution, animation, combat,
or multiplayer behavior. See the [release notes](docs/release-notes-v1.3.8.md)
for the remaining acceptance work.

- A-10 ballistic compensation is implemented and tested against the native trajectory model; real collision accuracy and replay still need the broader solo/Fika acceptance matrix.
- Pilot's final Trading appearance and a paid Uplink purchase need an in-game acceptance check.
- Phone inventory inspect model may still need polish.
- Mortar/artillery support is planned but not included.
- Dedicated-headless Fika A-10 damage is experimental and remains separately gated from the original single-player/human-host path.
- The full human-host, Fika-client, and dedicated-headless live acceptance matrices for the new transactional request flow are not yet complete.
- If both authority-acceptance result paths and cancellation settlement are lost beyond their bounded waits, an authority-executed service can still be refunded and become free.
- Commit/refund retries are held in memory. A client crash, permanent logout, or backend outage that outlasts pending expiry can refund an already delivered service.
- Remote third-person phone animation sync is planned but not included.
- Public beta: back up profiles before testing payment modes.

Stash payment and non-host A-10 tracer visibility are implemented, but the
automated suite does not exercise either path end to end. Both remain in the
live multiplayer acceptance matrix for this beta.

## Credits

Credits and notices are in `THIRD_PARTY_NOTICES.md` and `docs/credits.md`.

- SamSWAT for the original Fire Support.
- Arys for SamSWAT's Fire Support - Arys Reloaded.
- danauraborealis for Manimal Hacker Mod material used under the MIT license.
- Accurate Circular Radar / Tyrian Radar Standalone for adapted radar HUD material, if those assets/code remain.
- SPT and Project Fika as compatibility targets.

## License And Permissions

Tylevo's Tactical Services Control is released under Creative Commons Attribution-NonCommercial 4.0 International (CC BY-NC 4.0), matching the upstream Arys Reloaded source license. Arys clarified that the Forge/SPT Hub BY-NC 3.0 listing was due to historical site limitations before the Forge migration.

Upstream-derived Fire Support material is redistributed with permission and full attribution. Third-party components keep their own license terms.

## Optional Tip

If you enjoy the project and want to support future work, you can leave a voluntary tip on Ko-fi. This is optional and does not unlock features, early access, or support priority.

https://ko-fi.com/tylevo

This tip link is included with upstream permission for the public beta release. It is voluntary only and does not unlock features, early access, downloads, updates, or support priority.
