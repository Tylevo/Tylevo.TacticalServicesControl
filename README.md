# Tylevo's Tactical Services Control

Call in an A-10 strike, arrange a helicopter extraction or cargo pickup, and locate nearby contacts with UAV reconnaissance. Control your support from the TerraGroup TSC Uplink phone.

**TSC v1.3.13 development candidate Â· SPT 4.1.5 / EFT 0.16.9.5.40743**

[Candidate notes](docs/release-notes-v1.3.13.md) Â· [Installation guide](docs/dependencies.md) Â· [TSC releases](https://github.com/Tylevo/Tylevo.TacticalServicesControl/releases)

Install **UnityToolkit 2.0.2** and **WTT CommonLib 3.0.6** separately. **Fika support is included, but multiplayer on the current SPT/Fika versions has not been tested.**

## Next version: prices, presets, and payment currencies

The unpublished **1.3.13 candidate** adds **Balanced, Casual, Hardcore, Barter,
and Classic 1.3.12** presets. Preview changes in the dashboard, apply them to
your draft, then Save Config. Save named presets as JSON files on the SPT host
and share your settings through JSON files or complete share codes. The library
works across browsers without a database or online account.

Fresh installs use lower Balanced prices: **Focused Sweep 25,000 RUB, UAV
50,000 RUB, A-10 150,000 RUB, Double Pass 250,000 RUB, Extraction 125,000 RUB,
and Cargo Transfer 75,000 RUB**. Existing installs retain saved prices until
you choose a preset or edit them. The Uplink and quest repeater keep their
existing prices. Buying Cargo Transfer now covers sending your items home;
there is no extra charge at the helicopter.

Select **RUB, USD, EUR, GP coins, or Bitcoin** separately for each service in SIC
or the dashboard. GP/BTC come from the PMC stash, including eligible items in
stash containers; cash uses the configured wallet. Prices are whole units and
changing currency does not convert the amount. See the [candidate notes](docs/release-notes-v1.3.13.md)
and [preset guide](docs/dashboard.md#gameplay-presets-candidate-1313).

Fresh installs and Reset Defaults use **stash roubles**. The dashboard has a
dedicated presets page, aligned sharing panels, wider dropdowns, and the
in-game color theme. Phone payment swipes continue into stowing without the
temporary pause, with a larger arrow following the hand animation.

**Keep your existing Pilot Questline 1.3.12 add-on for SPT 4.1.5.** Main TSC
1.3.13 accepts it; this update needs no new add-on download. Quest content
and progression are unchanged.

## What's new in 1.3.12?

- Upright phones follow the hand during sprint, with gentler walking bob and turning sway.
- The horizontal purchase phone eases out of zoom when you sprint and back in when you stop, with matching fade timing.
- Set UH-60 cargo grid columns and rows in SIC or the dashboard. Each dimension defaults to the native size; custom grids support up to 10 columns and 30 rows.

The original 1.3.12 release required matching main and add-on versions.
The 1.3.13 compatibility change above lets you keep that unchanged add-on.
See the [patch notes](docs/release-notes-v1.3.12.md) for configuration and testing details.

## What's changed since the SPT 4.0.13 release?

If you're updating from the last Forge release, TSC v1.0.8, this version brings together the features and fixes developed across the intervening GitHub test builds.

- **UH-60 Cargo Transfer:** send loot home while you stay in the raid. It replaces Priority Exfil. Cargo arrives through Pilot's mail, with item handling included in the service price.
- **Pilot's Services tab:** buy support authorizations before entering a raid at **Traders > Pilot > Services**. Browse the compact service list on the left and review the selected service's details on the right. Purchases use your PMC stash balance and existing authorization limits.
- **A redesigned phone:** live UI, new service artwork, horizontal purchase screens, and smoother, adjustable zoom. Hold **Left Alt** to select with the mouse; the number-key controls remain available.
- **Optional Pilot Questline add-on:** the main download opens Pilot immediately and sells the Uplink for â‚½50,000. Install the separate add-on to earn access through three quests beginning with Mechanic at level 5. The Uplink has its own fourth special slot.
- **More radar display options:** hold **J** to check active recon on the physical phone, or choose a compact HUD scanner in a screen corner.
- **More payment options and better recovery:** configure support prices in RUB, USD, or EUR. Authorization use and payment recovery have been strengthened across failed requests, reconnects, and server saves.
- **A-10 targeting improvements:** corrected shot origins and trajectory compensation address rounds landing short of the designated target.
- **Launcher configuration:** open the TerraGroup dashboard from SIC's **Mod pages**, or use its native config editor. The dashboard keeps its theme, and saves include validation and protection against conflicting edits.
- **Updated dependencies:** UnityToolkit 2.0.2 supplies the compatible SPT 4.1.5 plugin and prepatcher. Install its standalone package alongside WTT CommonLib.

Phone deployment, camera targeting, A-10 Double Pass, UAV Recon, and Focused Sweep already existed in the 4.0.13 build. They remain part of TSC alongside these additions. The [release notes](docs/release-notes-v1.3.11.md) cover the upgrade in more detail.

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
2. Install the standalone [**UnityToolkit 2.0.2** package](https://github.com/Tylevo/UnityToolkit-New/releases/tag/v2.0.2), then install [WTT CommonLib 3.0.6](https://github.com/WelcomeToThursday/WTT-CommonLib/releases/tag/v3.0.6), including its client, server, and serialization prepatcher components.
3. Extract the **full TSC ZIP** into your SPT 4.1.5 root. Merge the `BepInEx` and `SPT_Runtime` folders and replace old mod files when prompted.
4. If you use quest progression, keep the existing **Pilot Questline 1.3.12 add-on for SPT 4.1.5**, or install it from its existing download. Main TSC 1.3.13 accepts it without an add-on update. It adds server content and uses the same TSC client. Skip it for immediate access through Pilot.
5. Start the SPT server, then the launcher and game.

The TSC ZIP does not include UnityToolkit. Its separate 2.0.2 package supplies the plugin, prepatcher, and companion libraries. Keep one Toolkit installation in the standard plugin and patcher folders; no additional compatibility overlay is needed. TSC replaces SamSWAT Fire Support and Arys Reloaded, so don't install those alongside it.

The [installation guide](docs/dependencies.md) shows the folder layout and upgrade details. `SHA256SUMS.txt` provides optional download verification; GitHub's source archives are not installable mod packages.

## Getting started

With the main download, open **Traders > Pilot > Trading** and buy the **TerraGroup TSC Uplink** for **â‚½50,000**. Pilot and configured services are available immediately, with normal service prices and authorization limits.

With the optional **Pilot Questline add-on**, begin **Open Channel** with Mechanic at level 5, supply Pilot's repair parts, and restore the Shoreline weather-station relay. Completing **Back on the Air** awards the phone and unlocks services and â‚½50,000 replacements. See the [add-on and questline guide](docs/pilot-questline.md).

TSC does not add phones to random loot in either mode. Put your Uplink in the dedicated fourth special slot, or carry it in your inventory.

To buy support before a raid, open **Traders > Pilot > Services**. Select a service from the left-hand list, review its description, price, and held authorizations in the right-hand detail panel, then confirm the purchase. The cost comes from the same PMC stash and grants the same persistent authorization as before.

In-raid phone purchasing and deployment controls are unchanged: use **U** to buy support and **K** to deploy an authorization when you need it.

| Control | Action |
| --- | --- |
| **U** | Open the purchase phone. |
| **Hold Left Alt + left-click** | Browse and select with the phone cursor. Release Alt to look around again. |
| **LMB, then 1 / 2 / 3** | Open Tactical Services from the home screen, then choose UH-60 Services, Fire Support, or UAV Recon. |
| **1 / 2, then Enter** | Choose the standard or alternate service within a category, then confirm its purchase on the review screen. |
| **K** | Open deployment for services you own. Select with Alt and the mouse, or **1â€“6**, then deploy with **LMB / Enter**. |
| **Middle mouse / Enter** | Confirm each camera-targeting step for A-10 or UH-60 support. |
| **Alt + RMB / Backspace** | Cancel camera targeting. |
| **Hold J** | View active UAV recon in Phone display mode. |
| **RMB / Escape** | Go back or close the purchase phone. |
| **F12** | Change keybinds, phone zoom, radar display, and other client settings. |

Purchase confirmation turns the phone upright and plays the swipe automatically. See the [usage guide](docs/usage.md) for the full controls, radar options, cargo fees, and payment settings.

## Dashboard and settings

Start the SPT server, open **SIC** from the launcher, and choose **Tactical Services Control** under **Mod pages**. This opens the themed TerraGroup dashboard.

SIC also has **Config Editor > Mods > Tactical Services Control** for prices, availability, cooldowns, payment settings, and service timing. Candidate gameplay presets use those same fields, with their library and sharing controls in the themed dashboard. Personal phone and radar settings stay in **F12**. The [dashboard guide](docs/dashboard.md) explains previewing, saving, applying, and sharing settings.

## Compatibility and known issues

For the published 1.3.12 release, the maintainer accepted the phone movement and sprint zoom changes in-game on September 8. That build passed 305 C# regression tests and 10 dashboard tests, and cargo settings passed 27 native SIC checks. Cargo grid layout and delivery with custom dimensions still need gameplay coverage. See the [1.3.12 validation record](docs/validation/v1.3.12.md).

The 1.3.13 preset library has native SIC and HTTP validation from development. The release archive's accompanying validation record identifies its exact build and checks. In-game and Fika gameplay acceptance remain separate from automated validation.

The existing [Services checklist](docs/pilot-services-testing.md) and [questline checklist](docs/pilot-questline.md#validation) retain their unrecorded gameplay cases. This patch does not change the questline. Update the main package on all Fika participants and retain the existing 1.3.12 add-on on the server when used.

**Current Fika multiplayer remains untested.** Solo play does not require Fika. Cargo Transfer is available in solo play and is implemented for the requesting human Fika host; other Fika clients and dedicated-headless requesters cannot use it yet. Dedicated-headless A-10 damage is experimental.

See [known issues](docs/known-issues.md) for current limitations and the [validation record](docs/validation/v1.3.12.md) for test details.

## Credits and more information

Based on **SamSWAT's Fire Support** and **Arys Reloaded**, with permission and attribution. Thanks to **Arys** for UnityToolkit and permission to maintain its update on the existing Forge page. Full credits, component licenses, and redistribution details are in [credits](docs/credits.md), [third-party notices](THIRD_PARTY_NOTICES.md), and [permissions](PERMISSIONS.md).

TSC is licensed under **CC BY-NC 4.0**. UnityToolkit remains under MIT, and its companion libraries retain their own licenses.

[All documentation](docs/README.md) Â· [Archived releases and development history](docs/archive/README.md) Â· [Building from source](BUILDING.md)

If you'd like to support development, you can leave an optional tip on [Ko-fi](https://ko-fi.com/tylevo). Tipping does not unlock features, downloads, updates, or priority support.
