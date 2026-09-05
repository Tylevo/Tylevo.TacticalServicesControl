# Tylevo's Tactical Services Control

Call in an A-10 strike, arrange a helicopter extraction or cargo pickup, and locate nearby contacts with UAV reconnaissance. Control your support from the TerraGroup TSC Uplink phone.

**TSC v1.3.10 · SPT 4.1.5 / EFT 0.16.9.5.40743**

**[Download TSC for SPT 4.1.5](https://github.com/Tylevo/Tylevo.TacticalServicesControl/releases/download/v1.3.10/Tylevo.TacticalServicesControl-v1.3.10-SPT4.1.5-TESTER.zip)** · [Release notes](docs/release-notes-v1.3.10.md) · [Installation guide](docs/dependencies.md)

The full ZIP includes UnityToolkit. Install **WTT CommonLib 3.0.6** separately. **Fika support is included, but multiplayer on the current SPT/Fika versions has not been tested.**

## What's changed since the SPT 4.0.13 release?

If you're updating from the last Forge release, TSC v1.0.8, this version brings together the features and fixes developed across the intervening GitHub test builds.

- **UH-60 Cargo Transfer:** send loot home while you stay in the raid. It replaces Priority Exfil. Cargo arrives through Pilot's mail, and the separate item-handling fee can use carried roubles or your stash.
- **A pre-raid support store:** buy authorizations before entering a raid. Open **TSC UPLINK** beside **Character** on the main menu's bottom bar to browse service cards, see your balance, and review purchases.
- **A redesigned phone:** live UI, new service artwork, horizontal purchase screens, and smoother, adjustable zoom. Hold **Left Alt** to select with the mouse; the number-key controls remain available.
- **UH-60 Pilot and a dedicated phone slot:** Pilot now sells the physical Uplink, has a new portrait, and is unlocked without a quest requirement. The Uplink has its own fourth special slot.
- **More radar display options:** hold **J** to check active recon on the physical phone, or choose a compact HUD scanner in a screen corner.
- **More payment options and better recovery:** configure support prices in RUB, USD, or EUR. Authorization use and payment recovery have been strengthened across failed requests, reconnects, and server saves.
- **A-10 targeting improvements:** corrected shot origins and trajectory compensation address rounds landing short of the designated target.
- **Launcher configuration:** open the TerraGroup dashboard from SIC's **Mod pages**, or use its native config editor. The dashboard keeps its theme, and saves include validation and protection against conflicting edits.
- **A simpler dependency install:** the compatible UnityToolkit rebuild and its companion libraries are bundled with Arys's permission.

Phone deployment, camera targeting, A-10 Double Pass, UAV Recon, and Focused Sweep already existed in the 4.0.13 build. They remain part of TSC alongside these additions. The [release notes](docs/release-notes-v1.3.10.md) cover the upgrade in more detail.

## Available support

| Service | What it does |
| --- | --- |
| A-10 Strafe | One autocannon pass over your designated target. |
| A-10 Double Pass | Two passes, with a configurable delay between them. |
| UH-60 Extraction | Land at your chosen pickup zone and extract your PMC. |
| UH-60 Cargo Transfer | Send items home without ending your raid. |
| UAV Recon | Scan for contacts and view them on the phone or HUD. |
| UAV Focused Sweep | Use the alternate, focused recon service. |

## Installation

**Coming from SPT 4.0.13? Install SPT 4.1.5 in a new folder and start a fresh profile.** Keep your old installation as a backup and let TSC create fresh storage for the new profile. The in-place patch-update instructions for SPT 4.1.x do not apply to 4.0.13. Follow [SPT's installation and upgrade guidance](https://wiki.sp-tushonka.com/en/SPT_4x/Updating_SPT).

For an existing SPT 4.1.x installation, back up your profiles and TSC's complete `config/` and `storage/` directories before updating to 4.1.5. The TSC ZIP does not overwrite those folders.

1. Close the game, launcher, and SPT server.
2. Install [WTT CommonLib 3.0.6](https://github.com/WelcomeToThursday/WTT-CommonLib/releases/tag/v3.0.6), including its client, server, and serialization prepatcher components.
3. Extract the **full TSC ZIP** into your SPT 4.1.5 root. Merge the `BepInEx` and `SPT_Runtime` folders and replace old mod files when prompted.
4. Start the SPT server, then the launcher and game.

UnityToolkit is already included. Keep its bundled files in the standard plugin and patcher folders; no separate Toolkit or compatibility overlay is needed. TSC replaces SamSWAT Fire Support and Arys Reloaded, so don't install those alongside it.

The [installation guide](docs/dependencies.md) shows the folder layout and upgrade details. `SHA256SUMS.txt` on the release page is optional download verification; GitHub's source archives are not installable mod packages.

## Getting started

Buy the **TerraGroup TSC Uplink** from **UH-60 Pilot** for **₽50,000** at loyalty level 1. Put it in the dedicated fourth special slot, or carry it in your inventory. Pilot has no quest requirement for now.

Buy support through the main-menu **TSC UPLINK** store or through the phone in raid, then deploy the authorization when you need it.

| Control | Action |
| --- | --- |
| **U** | Open the purchase phone. |
| **Hold Left Alt + left-click** | Browse and select with the phone cursor. Release Alt to look around again. |
| **LMB, then 1 / 2 / 3** | Open Tactical Services from the home screen, then choose UH-60 Services, Fire Support, or UAV Recon. |
| **1 / 2, then Enter** | Choose the standard or alternate service within a category, then confirm its purchase on the review screen. |
| **K** | Open deployment for services you own. Select with Alt and the mouse, or **1–6**, then deploy with **LMB / Enter**. |
| **Middle mouse / Enter** | Confirm each camera-targeting step for A-10 or UH-60 support. |
| **Alt + RMB / Backspace** | Cancel camera targeting. |
| **Hold J** | View active UAV recon in Phone display mode. |
| **RMB / Escape** | Go back or close the purchase phone. |
| **F12** | Change keybinds, phone zoom, radar display, and other client settings. |

Purchase confirmation turns the phone upright and plays the swipe automatically. See the [usage guide](docs/usage.md) for the full controls, radar options, cargo fees, and payment settings.

## Dashboard and settings

Start the SPT server, open **SIC** from the launcher, and choose **Tactical Services Control** under **Mod pages**. This opens the themed TerraGroup dashboard. The in-game store's **Dashboard** button opens the same page.

SIC also has **Config Editor > Mods > Tactical Services Control** for prices, availability, cooldowns, payment settings, and service timing. Personal phone and radar settings stay in **F12**. The [dashboard guide](docs/dashboard.md) explains saving and applying changes.

## Compatibility and known issues

The maintainer reports the updated local setup working on SPT 4.1.5. Automated build and server checks have passed; broader gameplay testing is ongoing.

**Current Fika multiplayer remains untested.** Solo play does not require Fika. Cargo Transfer is available in solo play and is implemented for the requesting human Fika host; other Fika clients and dedicated-headless requesters cannot use it yet. Dedicated-headless A-10 damage is experimental.

See [known issues](docs/known-issues.md) for current limitations and the [validation record](docs/validation/v1.3.10.md) for test details.

## Credits and more information

Based on **SamSWAT's Fire Support** and **Arys Reloaded**, with permission and attribution. Thanks to **Arys** for UnityToolkit and permission to bundle its compatible rebuild. Full credits, component licenses, and redistribution details are in [credits](docs/credits.md), [third-party notices](THIRD_PARTY_NOTICES.md), and [permissions](PERMISSIONS.md).

TSC is licensed under **CC BY-NC 4.0**. UnityToolkit remains under MIT, and its companion libraries retain their own licenses.

[All documentation](docs/README.md) · [Archived releases and development history](docs/archive/README.md) · [Building from source](BUILDING.md)

If you'd like to support development, you can leave an optional tip on [Ko-fi](https://ko-fi.com/tylevo). Tipping does not unlock features, downloads, updates, or priority support.
