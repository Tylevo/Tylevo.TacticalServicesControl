# Tylevo's Tactical Services Control

Call in an A-10 strafe, arrange a helicopter pickup, or check for nearby contacts from a TerraGroup TSC Uplink phone. TSC is a BepInEx mod for SPT, built on SamSWAT's Fire Support and Arys Reloaded.

> **v1.3.10 unreleased candidate · SPT 4.1.5 / EFT 0.16.9.5.40743**
>
> This candidate retains the bundled UnityToolkit 2.0.1 rebuilt against SPT 4.1, with Arys's permission. No separate Toolkit or compatibility-overlay download is needed. WTT CommonLib is still required separately.
> **Initial local use on SPT 4.1.5 is reported working. Multiplayer on the current SPT/Fika versions remains untested.** See the [v1.3.10 candidate notes](docs/release-notes-v1.3.10.md) for test coverage.

The latest published release is **v1.3.9 for SPT 4.1.4**, published September 5, 2026. Download its [full v1.3.9 ZIP](https://github.com/Tylevo/Tylevo.TacticalServicesControl/releases/download/v1.3.9/Tylevo.TacticalServicesControl-v1.3.9-SPT4.1.4-TESTER.zip) from the [v1.3.9 release](https://github.com/Tylevo/Tylevo.TacticalServicesControl/releases/tag/v1.3.9). `SHA256SUMS.txt` is available alongside the ZIP.

The earlier [v1.3.8 release](https://github.com/Tylevo/Tylevo.TacticalServicesControl/releases/tag/v1.3.8) remains available; its Toolkit installation steps are preserved in the [dependency guide](docs/dependencies.md#historical-v138-installation-official-toolkit-plus-overlay).

Press `U` in raid to open the Uplink. Hold `Left Alt` to browse and left-click to select; release Alt to look around again. For keyboard navigation, tap `LMB` on the home screen, then press `1`, `2`, or `3` for UH-60 Services, Fire Support, or UAV Recon. Within a category, use `1` for the standard service or `2` for its alternate. Review your choice, then hold Alt and click confirm, or press `Enter`. Press `K` to open deployment for services you own.

Available services:

- A-10 autocannon strafe
- A-10 Double Pass
- UH-60 Black Hawk extraction
- UH-60 Cargo Transfer
- UAV Recon
- Focused Sweep

This is a derivative of SamSWAT's original Fire Support and SamSWAT's Fire Support - Arys Reloaded by Arys. Upstream permission to redistribute the work is recorded in `PERMISSIONS.md`. Full credits are in `THIRD_PARTY_NOTICES.md` and `docs/credits.md`.

## What's new for SPT 4.1.5

The v1.3.10 candidate targets SPT 4.1.5 and keeps the bundled UnityToolkit, themed SIC dashboard, and separate native config Apply/Save behavior introduced in v1.3.9. The target update adds no gameplay changes. Arys confirmed permission to bundle the rebuilt Toolkit on September 5, 2026.

It also retains the v1.3.0 through v1.3.8 updates:

- A-10 aim now accounts for EFT's gravity and drag from the moving gun position. Surface and cover checks follow the round's curved path, and replay effects account for travel time. This addresses rounds landing short of the marker; impact accuracy still needs broader testing in game.
- The phone uses live text and panels for service cards, descriptions, prices, balances, availability, and recon settings. Hold `Left Alt` to browse with the mouse, or use the keyboard controls.
- Phone zoom eases in and out, with adjustable timing for both the camera FOV and hand framing. Deploy and radar views keep your raid FOV.
- The pre-raid store matches the phone's style, with six service cards, a detail panel, and a separate purchase confirmation dialog. The new icons have no pale outer frames; selected cards still have an outline.
- **TSC UPLINK** is on the main menu's bottom bar, immediately left of **Character**. It uses the game's existing footer layout and no longer adds a row to the center menu.
- **UH-60 Pilot** sells the physical Uplink for **₽50,000** at loyalty level 1, up to five per restock. The server unlocks existing locked Pilot entries at startup. His new portrait also appears on cargo mail.

## Requirements

- [SPT 4.1.5](https://github.com/SP-Tushonka/build/releases/tag/4.1.5). Initial local use of this candidate is reported working.
- [UnityToolkit 2.0.1](docs/dependencies.md), included in the full TSC ZIP since v1.3.9 and retained for this candidate with its plugin and prepatcher rebuilt against SPT 4.1, companion libraries, and license notices. There is no separate Toolkit or overlay download for this candidate.
- [WTT CommonLib 3.0.6](https://github.com/WelcomeToThursday/WTT-CommonLib/releases/tag/v3.0.6), including its client, server, and serialization prepatcher components.
- For experimental multiplayer testing, [Project Fika client 2.4.2](https://github.com/project-fika/Fika-Plugin/releases/tag/v2.4.2) and its corresponding server component. This version is a build reference only; multiplayer compatibility hasn't been tested with this SPT release. You don't need Fika for solo play; TSC detects it at runtime.

Install WTT CommonLib separately, including all of its required components. Fika is optional and is also installed separately. The [dependency installation guide](docs/dependencies.md) has the package order and file locations.

The [SPT 4.1.5 port log](docs/port/SPT-4.1.5-PORT-LOG.md) records the verified reference and dependency hashes. New v1.3.10 checks are tracked in the [candidate notes](docs/release-notes-v1.3.10.md). Earlier port logs remain historical evidence; their results do not establish SPT 4.1.5 gameplay compatibility.

TSC replaces the old SamSWAT Fire Support and Arys Reloaded packages. Don't install them alongside it.

## Installation (v1.3.10 candidate)

1. Back up your profiles. If you're updating TSC, also back up its `config/` and complete `storage/` directories.
2. Close EFT, the launcher, the SPT server, and any Fika or headless processes.
3. Update SPT to 4.1.5 and install WTT CommonLib, then extract the **full v1.3.10 candidate ZIP** into your SPT root folder. It includes UnityToolkit. GitHub's automatically generated source archives aren't installable mod packages.
4. Check that extraction created the folders below. If updating an existing Toolkit installation, replace its files in these standard locations when prompted; don't keep additional copies in other plugin or patcher folders.
5. Start the SPT server, then the launcher and game. Restarting both the server and game loads the updated trader, portrait, and cached UI icons.

The installed folders should be:

- `BepInEx/plugins/Tylevo.TacticalServicesControl/`
- `SPT_Runtime/user/mods/Tylevo.TacticalServicesControl/`
- `BepInEx/plugins/UnityToolkit/`
- `BepInEx/patchers/UnityToolkit/`

Avoid an extra folder around the ZIP contents. Install all four TSC DLLs and their assets together, and remove any older copies of TSC DLLs from other mod folders. Everyone in a Fika session needs the same TSC package.

### Updating an existing installation

Extracting the release over the current TSC folders preserves your configuration and saved state. The ZIP doesn't contain configuration, storage, admin tokens, or profiles. Keep the **complete `storage/` directory**: it holds purchased authorizations and the records needed to recover payments and cargo deliveries. The server migrates existing configuration and ledger formats when it starts. To roll back an upgrade, restore matching backups of both the mod files and saved state.

Older SPT installations used `SPT/user/mods/Tylevo.TacticalServicesControl/`. SPT 4.1.4 and 4.1.5 use `SPT_Runtime/user/mods/Tylevo.TacticalServicesControl/`.

If your TSC state is still in the old folder, back it up and copy its `config/` and complete `storage/` directories into the new TSC server folder. Do this with the server stopped, before the first launch. Keep the new release's DLLs, database, bundles, web files, and artwork. If both folders already contain state, choose the matching backup you want to use; don't merge two ledgers. TSC won't look in the old folder automatically.

This moves TSC's state only. Follow SPT's guidance for your version when moving player profiles. If you're starting with fresh profiles, let TSC create fresh state too.

## How to use

### Getting the Uplink

Buy the **TerraGroup TSC Uplink** from **UH-60 Pilot** in Trading for **₽50,000**. It requires loyalty level 1, with a limit of five per restock. The offer has moved from Jaeger. Pilot has no quest requirement for now, and the server unlocks him for existing profiles at startup. He also delivers your UH-60 cargo mail.

### Pre-raid store

Open **TSC UPLINK** on the main menu's bottom bar, immediately left of **Character**. The shortcut only appears on the main menu.

1. Wait for your stash balance and purchased authorizations to load.
2. Select a service card to see its artwork, description, price, availability, and how many authorizations you own.
3. Open the purchase review, check the price and projected balance, then confirm in the dialog. Cancelling here sends no purchase request.

The **Dashboard** button opens the active SPT server's TSC Dashboard.

The pre-raid store authenticates purchases and requires persistent authorizations and a server-backed stash payment source. Your purchases are available when you enter a raid with the same PMC.

### Buying support in raid

Bring the Uplink into raid and press `U` to open it. Hold `Left Alt` to use the phone cursor, open **Tactical Services**, and choose a category and service. Release Alt to look around again.

To use the keyboard, tap `LMB` on the home screen to open Tactical Services. Press `1`, `2`, or `3` for UH-60 Services, Fire Support, or UAV Recon. Within a category, `1` selects the standard service and `2` selects its alternate, then opens the review screen.

Check the service details, then hold Alt and click the confirmation control, or press `Enter`. The phone turns upright and plays the swipe animation automatically. You don't need to drag anything. The swipe commits the payment using the configured currency and wallet source.

`RMB` goes back and `Escape` closes the phone. Closing it after payment has gone through won't undo the purchase.

### Deploying support

Press `K` to open deployment mode. It lists only the services you own. Hold `Left Alt`, select a service, and click deploy. You can also press `1`-`6` to select a service, then `LMB` or `Enter` to deploy it. `RMB`, `Backspace`, or `Escape` stows the phone without spending an authorization.

For A-10 and UH-60 services, use the camera to mark the target. Confirm each targeting step with `Mouse 2` (middle mouse) or `Enter`. Cancel targeting with `Alt + RMB` or `Backspace`.

### UAV radar

UAV Recon and Focused Sweep start as soon as you deploy them. Only the requester sees the radar. In the default `Phone` display mode, hold `J` to raise the Uplink and see the live radar; release it to return to your weapon. Walking or sprinting won't lower the phone while you hold the key. The recon timer keeps running while the phone is stowed.

The optional `HUD` mode shows only the live square scanner in one of four screen corners for the active recon session. UAV support also includes the A-10 loiter visual.

### Sending cargo

UH-60 Cargo Transfer lands at your marked loading zone and offers **SEND ITEMS VIA UH-60**. It sends items home and never extracts your PMC.

The authorization pays for dispatch. EFT charges a separate item-handling fee when you submit cargo, always in RUB, regardless of your TSC authorization currency. In `F12`, under **Helicopter Cargo**, **Transfer fee source** defaults to `Carried`, which uses EFT's normal carried-cash payment. Choose `Stash` to pay from your authenticated PMC stash through the TSC server.

The helicopter leaves as soon as EFT confirms the paid items reached its saved delivery grid. If you cancel or payment fails, you can try again during the remaining landed time. Cargo arrives after the raid through **UH-60 Pilot** mail. The **BTR Driver** contact stays unchanged; if TSC can't route an accepted delivery through Pilot safely, it falls back to BTR delivery so the items aren't discarded.

Cargo Transfer is enabled for solo players and a human Fika host requesting their own transfer, but the Fika path hasn't been tested on this release. Other Fika clients and dedicated-headless requesters can't use it yet. See the Fika section below for details.

## Controls and phone settings

Open the BepInEx configuration manager with `F12` to change the `U`, `K`, `J`, and spotter-confirm bindings. You can also adjust phone framing, choose `Phone` or `HUD` under **UAV Radar Display**, and select a HUD corner.

Mouse selection has settings for its modifier and sensitivity, and you can turn it off. The cursor stays on the phone display as the handset moves. Purchase screens stay horizontal until the final upright swipe confirmation. The `K` deploy view and held `J` radar open upright and keep your raid FOV.

Optional purchase-screen zoom starts after a 0.08-second delay and eases the camera FOV and hand framing into place over 0.75 seconds by default. In `F12`, **Phone zoom in seconds** accepts 0.25-1.5 seconds. **Phone zoom out seconds** accepts 0.15-0.8 seconds and defaults to 0.35. Closing the phone restores your original raid FOV. If you quickly reopen it, the phone still remembers that original FOV for when you close it again. These settings don't change the deploy or radar views.

## Fika installation

Fika integration is included, but it hasn't been tested on the current SPT/Fika versions. Fika 2.4.2 remains the build reference for this candidate; it does not establish multiplayer compatibility with SPT 4.1.5. Treat the setup below as instructions for testing.

Install the same TSC version on the host, any headless host, and every client. The integration is designed to use the host's settings while connected, sync host dashboard changes and support requests to clients, and clear the settings overrides on disconnect. Those behaviors still need checking in game on this release.

Cargo Transfer is limited to solo players and the requesting human host because its item-based handling fee still needs a way for the host to verify and synchronize prices for other requesters. On supported requesters, `Stash` fee mode requires the matching TSC server endpoint. With an older server, the transfer is blocked without moving cargo or charging carried cash.

## Payment modes

TSC has `PhoneAuthorizations` and `Hybrid` payment modes, with RUB, USD, or EUR payments from carried cash or the stash where configured. Select the currency in the dashboard. The phone shows the active price and payment source; the server sets stash prices and doesn't trust prices or currency sent by a client.

Changing currency doesn't convert the price numbers. Review every service price before saving a different currency.

These settings apply to support authorizations. Cargo Transfer's separate EFT handling fee stays RUB-only and uses its own `Carried`/`Stash` setting in `F12`. Back up your profiles before testing payment modes.

## Dashboard

Since v1.3.9, TSC has **Tactical Services Control** under SIC's
**Mod pages**, opening the same TerraGroup dashboard. Its sidebar links back
to SIC and the native config editor. This addition is not in the v1.3.8 ZIP
linked above. See the [dashboard guide](docs/dashboard.md) for both editors
and their Apply, Save, and reload behavior.

Change server and host settings in the local TSC Dashboard:

```text
https://127.0.0.1:6969/tsc/admin
```

SPT's mod-config editor also has a **Tactical Services Control** entry. It covers service prices and availability, payments, cooldowns, persistence limits, UAV range and timing, UH-60 timing, and the delay between A-10 passes. Both editors validate changes and check the configuration revision before saving `tsc-config.json`. Security settings and player-specific state stay in TSC's dedicated interfaces.

Dashboard routes and files:

- Public health route: `/tsc/health`
- Dashboard route: `/tsc/admin`
- Admin diagnostics route: `/tsc/admin/health`
- Config file: `config/tsc-config.json`
- Token file: `config/tsc-admin-token.txt`

Installation doesn't overwrite `config/tsc-config.json`. The server creates it with current defaults if neither a current nor legacy config exists, and migrates an existing file during upgrades.

The dashboard allows localhost access only by default. If you enable remote access, keep it on a trusted LAN or VPN and require the admin token for writes. Don't port-forward it.

See `docs/dashboard.md`, `PRIVACY.md`, and `SECURITY.md`.

## Beta status and known issues

The v1.3.10 candidate passed its full SPT 4.1.5 build, all 238 regression tests, and 26 isolated server checks covering the dashboard, SIC registration, configuration, and bundles. The maintainer reports that the updated local setup is working. Individual service checks were not listed, and current Fika multiplayer remains untested. See the [candidate validation notes](docs/validation/v1.3.10.md) for the scope and limits; the previous v1.3.9 results remain historical evidence for SPT 4.1.4.

The earlier v1.3.8 release passed **216 regression tests**, a full local build with no errors, and checks on its **169-file package**. These historical checks do not confirm Fika compatibility; live multiplayer testing has not been done on the current SPT/Fika versions. Server checks confirmed Pilot's unlock, the Pilot-only Uplink listing, its price and purchase limit, the exact portrait bytes, and preserved profile and trader state. They didn't cover the Trading screen in game or a paid Uplink purchase.

The phone interface and Alt controls received positive feedback from in-raid use. Layout harnesses and automated checks cover the store and bottom-bar integration. More testing across resolutions, animations, and combat is needed, and Fika testing has not started on the current versions. The [candidate notes](docs/release-notes-v1.3.10.md) track what remains.

- A-10 aim compensation has been tested against EFT's trajectory model. Actual impacts, collisions, and replay effects still need broader solo testing and live Fika testing.
- Pilot's appearance in Trading and a paid Uplink purchase still need checking in game.
- The phone's inventory inspect model may need more polish.
- Mortar/artillery support and remote third-person phone animation sync are planned but aren't included.
- A-10 damage on dedicated-headless Fika hosts is experimental and must be enabled separately from the original solo and human-host path.
- The payment and request flow has not been tested in live sessions with human Fika hosts, Fika clients, or dedicated-headless hosts on the current SPT/Fika versions.
- If both paths for confirming an accepted request fail and cancellation also times out, a service that already ran can still be refunded, making it free.
- Payment commit and refund retries are kept in memory. A client crash, permanent logout, or backend outage lasting beyond the pending transaction's expiry can refund a service that was already delivered.

Stash payments and A-10 tracer visibility for non-host players are implemented, but the automated suite doesn't test either from start to finish. Both still need live multiplayer testing.

## Credits

Full credits and notices are in `THIRD_PARTY_NOTICES.md` and `docs/credits.md`.

- SamSWAT for the original Fire Support.
- Arys for SamSWAT's Fire Support - Arys Reloaded and UnityToolkit, and for permission to bundle the rebuilt Toolkit.
- danauraborealis for Manimal Hacker Mod material used under the MIT license.
- Accurate Circular Radar / Tyrian Radar Standalone for adapted radar HUD material, if those assets/code remain.
- SPT and Project Fika as compatibility targets.

## License and permissions

Tylevo's Tactical Services Control is released under Creative Commons Attribution-NonCommercial 4.0 International (CC BY-NC 4.0), matching the upstream Arys Reloaded source license. Arys clarified that the Forge/SPT Hub BY-NC 3.0 listing was due to historical site limitations before the Forge migration.

Upstream-derived Fire Support material is redistributed with permission and full attribution. UnityToolkit remains under Arys's MIT license; its companion libraries retain their respective licenses. The maintainer confirmed Arys's explicit permission to bundle the rebuilt Toolkit on September 5, 2026. Third-party components keep their own license terms; see [the notices](THIRD_PARTY_NOTICES.md).

## Optional tip

If you'd like to support future work, you can leave a voluntary tip on Ko-fi:

https://ko-fi.com/tylevo

The tip link is included with upstream permission for the public beta. Tipping doesn't unlock features, early access, downloads, updates, or support priority.
