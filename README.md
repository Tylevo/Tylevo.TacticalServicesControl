# Tylevo's Tactical Services Control

A BepInEx mod that reworks SamSWAT's Fire Support / Arys Reloaded into a TerraGroup-style tactical support system for SPT and Fika.

> **SPT 4.1.4 tester port:** v1.3.0 targets SPT 4.1.4 / EFT
> 0.16.9.5.40743. The expected test artifact is
> `Tylevo.TacticalServicesControl-v1.3.0-SPT4.1.4-TESTER.zip`.
> The exact-version build, 168 regression tests, final package checks, and
> server/HTTP smoke pass. Menu load, raid behavior, and Fika require validation.
> Current results are recorded in
> `docs/port/SPT-4.1.4-PORT-LOG.md`.

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

## Requirements

- SPT 4.1.4.
- The SPT 4.1 rebuild of UnityToolkit 2.0.1 pinned in the port log. The old
  unmodified v2.0.1/4.0 binary is not a valid substitute.
- WTT Client CommonLib and WTT Server CommonLib 3.0.6, installed separately
  as required dependencies, including its serialization prepatcher.
- Project Fika client 2.4.2 with its compatible server line, optional and
  required only for multiplayer/Fika use. Single-player installs do not need
  Fika; TSC detects it at runtime.

The exact dependency commits, assembly versions, and SHA-256 values used for a
tester are recorded in `docs/port/SPT-4.1.4-PORT-LOG.md`. Use those exact pins
for acceptance testing; compiler compatibility with a different dependency
build is not runtime evidence. Historical SPT 4.1.2 results remain in
`docs/port/SPT-4.1-PORT-LOG.md`.

TSC does not bundle WTT Common Lib. The client and server projects reference the installed WTT dependency DLLs at runtime/build time, so WTT should be listed as a dependency on Forge rather than redistributed inside the TSC package.

Do not install the old SamSWAT Fire Support or Arys Reloaded mod alongside TSC. TSC is a derivative replacement package.

## Installation

1. Back up your profiles before testing the public beta.
2. Install the required dependencies listed above.
3. Extract the release ZIP directly into your SPT root.
4. Confirm these folders exist after extraction:
   - `BepInEx/plugins/Tylevo.TacticalServicesControl/`
   - `SPT_Runtime/user/mods/Tylevo.TacticalServicesControl/`
5. Start SPT normally.

Do not place the ZIP contents inside an extra nested folder.

## How To Use

### Pre-Raid Store

When Seasonal Modifiers is not installed, open **TSC UPLINK** from the main menu directly below **Records**. If the Records entry is unavailable, TSC places itself below **Character** instead. Seasonal Modifiers owns that menu area when its client plugin is loaded, so TSC removes its redundant main-menu row; use the in-raid Uplink purchase flow instead.

1. Open **TSC UPLINK** from the main menu on a standalone TSC install.
2. Wait for the authenticated PMC stash and authorization ledger to load.
3. Select **Buy** for a service, review the service, price, and projected balance in the confirmation dialog, then choose **Confirm Buy**. Cancelling sends no purchase request.
4. Use **Dashboard** to open the active SPT server's local TSC Dashboard.

Pre-raid purchases require persistent authorizations and a server-backed stash payment source. They remain available when the same PMC enters a raid.

### In Raid

1. Bring the **TerraGroup TSC Uplink** into raid.
2. Press `U` to open the Uplink in purchase mode.
3. Press `1`, `2`, or `3` to open UH-60 Services, Fire Support, or UAV Recon. Inside a category, press `1` for the standard service or `2` for its alternate service when available.
4. Press `Enter` on the confirmation screen to pay with the configured currency and wallet source. `RMB` returns to the previous screen and `Escape` closes the phone.
5. When you are ready to use an authorization, press `K` to open the Uplink in deployment mode. Only services you currently own are listed.
6. Press `1`-`6` to select a service, then press `LMB` or `Enter` to deploy it. `RMB`, `Backspace`, or `Escape` stows the phone without spending the authorization.
7. A-10 and UH-60 services use camera-based target designation. Confirm each targeting step with `Mouse 2` (middle mouse) or `Enter`; cancel with `Alt + RMB` or `Backspace`.
8. UAV Recon and Focused Sweep begin directly after deployment. The default `Phone` display mode uses `J`: hold it to raise the Uplink and view the live radar, then release it to return to your weapon. Walking and sprint keys do not lower it while the radar key remains held, and the recon timer keeps running while the phone is stowed. The optional `HUD` display mode keeps only the square live scanner visible in a selected screen corner for the active recon session.
9. UH-60 Cargo Transfer lands at the marked loading zone and provides **SEND ITEMS VIA UH-60**. It never extracts your PMC. The authorization pays for dispatch; EFT calculates a separate RUB-only item-handling fee when cargo is submitted. In `F12`, **Transfer fee source** defaults to `Carried`, preserving EFT's native carried-RUB payment, or can use `Stash` to debit the authenticated PMC stash through the TSC server. This fee is independent of TSC's configured authorization currency. Once EFT confirms the paid items reached its persistent delivery grid, the helicopter departs immediately; cancelling or failing payment leaves the remaining landed window available for retry. Successfully marked cargo returns through post-raid mail from **UH-60 Pilot** without replacing the native **BTR Driver** contact; if TSC routing cannot be completed safely, the accepted native cargo falls back to BTR delivery instead of being discarded.

The `U`, `K`, `J`, and spotter-confirm controls are configurable in the BepInEx configuration manager opened with `F12`. `UAV Radar Display` also provides the `Phone`/`HUD` choice and four HUD positions, while `Helicopter Cargo` provides the `Carried`/`Stash` handling-fee source. Phone framing and optional authorization-screen zoom are available there as well; the `K` deploy view and held `J` radar preserve the current raid FOV and reveal directly in the upright presentation after EFT finishes the concealed equip transaction.

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

## Known Issues

- SPT 4.1.4 compilation passes with 0 errors and four existing warnings;
  168/168 regression tests, final ZIP checks, and packaged-server/HTTP smoke
  pass. Dashboard editing, native config editor, menu, solo raids, and Fika
  require live acceptance. See `docs/port/SPT-4.1.4-VALIDATION.md` for
  each gate; historical SPT 4.1.2 passes do not close these gates.
- The existing Uplink bundle repair and input/menu hardening are retained.
  The repaired equip/stow animation, targeting controls, and menu spacing
  still require a fresh SPT 4.1.4 client run.
- Phone inventory inspect model may still need polish.
- Mortar/artillery support is planned but not included.
- Dedicated-headless Fika A-10 damage is experimental and remains separately gated from the original single-player/human-host path.
- The full human-host, Fika-client, and dedicated-headless live acceptance matrices for the new transactional request flow are not yet complete.
- If both authority-acceptance result paths and cancellation settlement are lost beyond their bounded waits, an authority-executed service can still be refunded and become free.
- Commit/refund retries are held in memory. A client crash, permanent logout, or backend outage that outlasts pending expiry can refund an already delivered service.
- Remote third-person phone animation sync is planned but not included.
- Public beta: back up profiles before testing payment modes.

Stash payment and non-host A-10 tracer visibility are implemented, but the
current automated suite does not exercise either path end to end. Keep both in
the live multiplayer acceptance matrix before public upload.

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
