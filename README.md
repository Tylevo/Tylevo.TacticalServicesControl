# Tylevo's Tactical Services Control

Call in an A-10 strafe, arrange a helicopter pickup, or check for nearby contacts from a TerraGroup TSC Uplink phone. TSC is a BepInEx mod for SPT, with optional Fika support, built on SamSWAT's Fire Support and Arys Reloaded.

> **v1.3.8 public beta · SPT 4.1.4 / EFT 0.16.9.5.40743**
>
> Download `Tylevo.TacticalServicesControl-v1.3.8-SPT4.1.4-TESTER.zip` from the [v1.3.8 release](https://github.com/Tylevo/Tylevo.TacticalServicesControl/releases/tag/v1.3.8).
> Multiplayer testing is still in progress. The [release notes](docs/release-notes-v1.3.8.md) cover the changes and what has been checked so far.

Buy support authorizations before a raid or from the phone during one, then deploy them when you need them. The Uplink handles service selection and camera-based targeting; you don't need the rangefinder or YY gesture wheel.

Available services:

- A-10 autocannon strafe
- A-10 Double Pass
- UH-60 Black Hawk extraction
- UH-60 Cargo Transfer
- UAV Recon
- Focused Sweep

TSC works on its own. If you also use Tylevo Seasonal Modifiers, it can schedule ambient A-10 passes as environmental events through TSC's Danger Close API. The host controls those passes. The [integration guide](docs/seasonal-modifiers-integration.md) explains API v3, warnings, dispatch, and the dedicated Uplink special slot.

This is a derivative of SamSWAT's original Fire Support and SamSWAT's Fire Support - Arys Reloaded by Arys. Upstream permission to redistribute the work is recorded in `PERMISSIONS.md`. Full credits are in `THIRD_PARTY_NOTICES.md` and `docs/credits.md`.

## What's new for SPT 4.1.4

The v1.3.8 package includes the updates from v1.3.0 through v1.3.8:

- A-10 aim now accounts for EFT's gravity and drag from the moving gun position. Surface and cover checks follow the round's curved path, and replay effects account for travel time. This addresses rounds landing short of the marker; impact accuracy still needs broader testing in game.
- The phone uses live text and panels for service cards, descriptions, prices, balances, availability, and recon settings. Hold `Left Alt` to browse with the mouse, or use the keyboard controls.
- Phone zoom eases in and out, with adjustable timing for both the camera FOV and hand framing. Deploy and radar views keep your raid FOV.
- The pre-raid store matches the phone's style, with six service cards, a detail panel, and a separate purchase confirmation dialog. The new icons have no pale outer frames; selected cards still have an outline.
- **TSC UPLINK** is on the main menu's bottom bar, immediately left of **Character**. It uses the game's existing footer layout and no longer adds a row to the center menu.
- **UH-60 Pilot** sells the physical Uplink for **₽50,000** at loyalty level 1, up to five per restock. The server unlocks existing locked Pilot entries at startup. His new portrait also appears on cargo mail.
- Seasonal Modifiers can use the dedicated Uplink special slot, host-issued A-10 warnings, and Danger Close API. It remains optional.

## Requirements

- [SPT 4.1.4](https://github.com/sp-tushonka/build/releases/tag/4.1.4).
- [UnityToolkit 2.0.1 with the SPT 4.1 compatibility overlay](docs/dependencies.md). Install the official upstream package first, then `UnityToolkit-v2.0.1-SPT4.1-compat-overlay.zip` from the [v1.3.8 release](https://github.com/Tylevo/Tylevo.TacticalServicesControl/releases/tag/v1.3.8). The overlay updates the plugin and prepatcher. The upstream binaries alone won't work with this version of SPT.
- [WTT CommonLib 3.0.6](https://github.com/WelcomeToThursday/WTT-CommonLib/releases/tag/v3.0.6), including its client, server, and serialization prepatcher components.
- For multiplayer, [Project Fika client 2.4.2](https://github.com/project-fika/Fika-Plugin/releases/tag/v2.4.2) and its compatible server component. You don't need Fika for solo play; TSC detects it at runtime.

Install dependencies separately; they aren't included in the TSC ZIP. The [dependency installation guide](docs/dependencies.md) has the package order and file locations.

The [port log](docs/port/SPT-4.1.4-PORT-LOG.md) records the exact dependency commits, assembly versions, and SHA-256 hashes used for this beta. Use those versions when testing. A successful build against another dependency version doesn't establish that it works in game. Earlier SPT 4.1.2 results are in `docs/port/SPT-4.1-PORT-LOG.md`.

TSC replaces the old SamSWAT Fire Support and Arys Reloaded packages. Don't install them alongside it.

## Installation

1. Back up your profiles. If you're updating TSC, also back up its `config/` and complete `storage/` directories.
2. Close EFT, the launcher, the SPT server, and any Fika or headless processes.
3. Install the dependencies, then extract the **full release ZIP** into your SPT 4.1.4 root folder. GitHub's automatically generated source archives aren't installable mod packages.
4. Check that extraction created both folders below.
5. Start the SPT server, then the launcher and game. Restarting both the server and game loads the updated trader, portrait, and cached UI icons.

The installed folders should be:

- `BepInEx/plugins/Tylevo.TacticalServicesControl/`
- `SPT_Runtime/user/mods/Tylevo.TacticalServicesControl/`

Avoid an extra folder around the ZIP contents. Install all four TSC DLLs and their assets together, and remove any older copies of TSC DLLs from other mod folders. Everyone in a Fika session needs the same TSC package.

### Updating an existing installation

Extracting the release over the current TSC folders preserves your configuration and saved state. The ZIP doesn't contain configuration, storage, admin tokens, or profiles. Keep the **complete `storage/` directory**: it holds purchased authorizations and the records needed to recover payments and cargo deliveries. The server migrates existing configuration and ledger formats when it starts. To roll back an upgrade, restore matching backups of both the mod files and saved state.

Older SPT installations used `SPT/user/mods/Tylevo.TacticalServicesControl/`. SPT 4.1.4 uses `SPT_Runtime/user/mods/Tylevo.TacticalServicesControl/`.

If your TSC state is still in the old folder, back it up and copy its `config/` and complete `storage/` directories into the new TSC server folder. Do this with the server stopped, before the first launch. Keep the new release's DLLs, database, bundles, web files, and artwork. If both folders already contain state, choose the matching backup you want to use; don't merge two ledgers. TSC won't look in the old folder automatically.

This moves TSC's state only. Follow SPT's guidance for your version when moving player profiles. If you're starting with fresh profiles, let TSC create fresh state too.

## How to use

### Getting the Uplink

Buy the **TerraGroup TSC Uplink** from **UH-60 Pilot** in Trading for **₽50,000**. It requires loyalty level 1, with a limit of five per restock. The offer has moved from Jaeger. Pilot has no quest requirement for now, and the server unlocks him for existing profiles at startup. He also delivers your UH-60 cargo mail.

### Pre-raid store

Open **TSC UPLINK** on the main menu's bottom bar, immediately left of **Character**. The shortcut only appears on the main menu. If the Seasonal Modifiers client is loaded, this shortcut is hidden; buy services through the in-raid Uplink instead.

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

Cargo Transfer currently works for solo players and a human Fika host requesting their own transfer. Other Fika clients and dedicated-headless requesters can't use it yet. See the Fika section below for details.

## Controls and phone settings

Open the BepInEx configuration manager with `F12` to change the `U`, `K`, `J`, and spotter-confirm bindings. You can also adjust phone framing, choose `Phone` or `HUD` under **UAV Radar Display**, and select a HUD corner.

Mouse selection has settings for its modifier and sensitivity, and you can turn it off. The cursor stays on the phone display as the handset moves. Purchase screens stay horizontal until the final upright swipe confirmation. The `K` deploy view and held `J` radar open upright and keep your raid FOV.

Optional purchase-screen zoom starts after a 0.08-second delay and eases the camera FOV and hand framing into place over 0.75 seconds by default. In `F12`, **Phone zoom in seconds** accepts 0.25-1.5 seconds. **Phone zoom out seconds** accepts 0.15-0.8 seconds and defaults to 0.35. Closing the phone restores your original raid FOV. If you quickly reopen it, the phone still remembers that original FOV for when you close it again. These settings don't change the deploy or radar views.

## Fika installation

Install the same TSC version on the host, any headless host, and every client. While you're connected, the host's settings take precedence over your local configuration. Dashboard changes on the host sync to clients, and disconnecting clears those overrides. TSC also syncs support requests between Fika players.

Cargo Transfer is limited to solo players and the requesting human host because its item-based handling fee still needs a way for the host to verify and synchronize prices for other requesters. On supported requesters, `Stash` fee mode requires the matching TSC server endpoint. With an older server, the transfer is blocked without moving cargo or charging carried cash.

## Payment modes

TSC has `PhoneAuthorizations` and `Hybrid` payment modes, with RUB, USD, or EUR payments from carried cash or the stash where configured. Select the currency in the dashboard. The phone shows the active price and payment source; the server sets stash prices and doesn't trust prices or currency sent by a client.

Changing currency doesn't convert the price numbers. Review every service price before saving a different currency.

These settings apply to support authorizations. Cargo Transfer's separate EFT handling fee stays RUB-only and uses its own `Carried`/`Stash` setting in `F12`. Back up your profiles before testing payment modes.

## Dashboard

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

Version 1.3.8 passed **216 regression tests**, a full local build with no errors, and checks on the **169-file package**. Server checks confirmed Pilot's unlock, the Pilot-only Uplink listing, its price and purchase limit, the exact portrait bytes, and preserved profile and trader state. They didn't cover the Trading screen in game or a paid Uplink purchase.

The phone interface and Alt controls received positive feedback from in-raid use. Layout harnesses and automated checks cover the store and bottom-bar integration, but testing across resolutions, animations, combat, and multiplayer is still in progress. The [release notes](docs/release-notes-v1.3.8.md) track what remains.

- A-10 aim compensation has been tested against EFT's trajectory model. Actual impacts, collisions, and replay effects still need broader solo and Fika testing.
- Pilot's appearance in Trading and a paid Uplink purchase still need checking in game.
- The phone's inventory inspect model may need more polish.
- Mortar/artillery support and remote third-person phone animation sync are planned but aren't included.
- A-10 damage on dedicated-headless Fika hosts is experimental and must be enabled separately from the original solo and human-host path.
- The new payment and request flow still needs a full round of live tests with human hosts, Fika clients, and dedicated-headless hosts.
- If both paths for confirming an accepted request fail and cancellation also times out, a service that already ran can still be refunded, making it free.
- Payment commit and refund retries are kept in memory. A client crash, permanent logout, or backend outage lasting beyond the pending transaction's expiry can refund a service that was already delivered.

Stash payments and A-10 tracer visibility for non-host players are implemented, but the automated suite doesn't test either from start to finish. Both still need live multiplayer testing.

## Credits

Full credits and notices are in `THIRD_PARTY_NOTICES.md` and `docs/credits.md`.

- SamSWAT for the original Fire Support.
- Arys for SamSWAT's Fire Support - Arys Reloaded.
- danauraborealis for Manimal Hacker Mod material used under the MIT license.
- Accurate Circular Radar / Tyrian Radar Standalone for adapted radar HUD material, if those assets/code remain.
- SPT and Project Fika as compatibility targets.

## License and permissions

Tylevo's Tactical Services Control is released under Creative Commons Attribution-NonCommercial 4.0 International (CC BY-NC 4.0), matching the upstream Arys Reloaded source license. Arys clarified that the Forge/SPT Hub BY-NC 3.0 listing was due to historical site limitations before the Forge migration.

Upstream-derived Fire Support material is redistributed with permission and full attribution. Third-party components keep their own license terms.

## Optional tip

If you'd like to support future work, you can leave a voluntary tip on Ko-fi:

https://ko-fi.com/tylevo

The tip link is included with upstream permission for the public beta. Tipping doesn't unlock features, early access, downloads, updates, or support priority.
