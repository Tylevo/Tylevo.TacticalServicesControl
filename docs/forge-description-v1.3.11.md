# Tylevo's Tactical Services Control v1.3.11 Public Beta

**For SPT 4.1.5 / EFT 0.16.9.5.40743**

> Prepared page copy: TSC v1.3.11 and its required standalone UnityToolkit
> 2.0.2 update have not yet been published.

Call in an A-10 strike, arrange a helicopter extraction or cargo pickup, and find nearby contacts with UAV reconnaissance. The TerraGroup TSC Uplink phone puts support selection, purchases, and targeting in your hands.

This update is being prepared for players coming from the **SPT 4.0.13 Forge release, TSC v1.0.8**. It includes the features and fixes developed through the intervening GitHub test builds.

## What's new since 4.0.13?

- **UH-60 Cargo Transfer** replaces Priority Exfil. Send loot home through Pilot's mail without ending your raid. The dispatch authorization and item-handling fee are separate; the handling fee can use carried roubles or your stash.
- **A pre-raid support store** lets you buy authorizations from **TSC UPLINK**, beside **Character** on the main menu's bottom bar. Browse service cards, check your balance, and review purchases before paying.
- **A redesigned phone** brings live prices and availability, new service icons, horizontal purchase screens, and smoother zoom. Hold **Left Alt** and left-click to make selections, or keep using the number keys.
- **UH-60 Pilot** now sells the physical Uplink, has a new portrait, and is unlocked without a quest requirement. The phone also has a dedicated fourth special slot.
- **Radar display options** let you hold **J** to check active recon on the physical phone or use a compact HUD scanner in a screen corner.
- **RUB, USD, or EUR support pricing** gives you more ways to configure payments. Authorization synchronization, failed-dispatch refunds, and payment recovery have also been improved.
- **A-10 targeting corrections** address rounds landing short by correcting shot origins and compensating for the bullet trajectory.
- **SIC integration** opens the themed TerraGroup dashboard from the launcher's **Mod pages**. A native config editor is also available, with validation and protection against conflicting saves.
- **Updated Toolkit dependency:** install UnityToolkit 2.0.2 separately. It is
  being prepared on Arys's existing Forge page and includes the compatible
  plugin, prepatcher, and companion libraries. No extra compatibility overlay
  is needed.

Phone deployment, camera targeting, A-10 Double Pass, UAV Recon, and Focused Sweep were already in the 4.0.13 version. They remain available alongside these additions. See the [full release notes](https://github.com/Tylevo/Tylevo.TacticalServicesControl/blob/main/docs/release-notes-v1.3.11.md) for details.

## Available support

- **A-10 Strafe:** one autocannon pass over your designated target.
- **A-10 Double Pass:** two passes, with a configurable delay between them.
- **UH-60 Extraction:** a helicopter pickup that extracts your PMC.
- **UH-60 Cargo Transfer:** a helicopter pickup that sends your items home while you stay in raid.
- **UAV Recon:** a contact scan displayed on the phone or HUD.
- **UAV Focused Sweep:** the alternate focused recon service.

## Install

Once the new packages are published, use **SPT 4.1.5** and install
**UnityToolkit 2.0.2** from [Arys's UnityToolkit project](https://forge.sp-tarkov.com/mod/1426/unitytoolkit)
and **[WTT CommonLib 3.0.6](https://github.com/WelcomeToThursday/WTT-CommonLib/releases/tag/v3.0.6)** separately, including its client, server, and serialization prepatcher components. Fika is optional.

1. Close the game, launcher, and SPT server.
2. Extract the **full TSC ZIP** into your SPT 4.1.5 root.
3. Merge the `BepInEx` and `SPT_Runtime` folders, replacing old mod files when prompted.
4. Start the server, then the launcher and game.

UnityToolkit is a separate dependency and is not included in the TSC ZIP.
Keep one installation in its standard plugin and patcher folders. TSC replaces SamSWAT Fire Support and Arys Reloaded; don't install those alongside it.

**Coming from SPT 4.0.13:** install SPT 4.1.5 in a new folder and start a fresh profile. Keep the old installation as a backup and let TSC create fresh storage. Do not copy the old mods or player ledger into the new setup.

**Already on SPT 4.1.x:** follow SPT's patch-update instructions. Back up your profiles and TSC's complete `config/` and `storage/` directories first. The TSC ZIP does not overwrite those directories.

[Installation guide](https://github.com/Tylevo/Tylevo.TacticalServicesControl/blob/main/docs/dependencies.md) · [TSC release availability](https://github.com/Tylevo/Tylevo.TacticalServicesControl/releases)

## Use the Uplink

Buy the **TerraGroup TSC Uplink** from **UH-60 Pilot** for **₽50,000** at loyalty level 1. Carry it in your inventory or use its dedicated fourth special slot.

- **U:** open the purchase phone. Left-click from the home screen to open Tactical Services.
- **Hold Left Alt + left-click:** browse and select with the cursor. Release Alt to look around.
- **1 / 2 / 3:** choose UH-60 Services, Fire Support, or UAV Recon. Use **1 / 2** for the service variant, then **Enter** on the review screen to buy.
- **K:** open deployment. Select with Alt and the mouse or **1–6**, then deploy with **LMB / Enter**.
- **Middle mouse / Enter:** confirm each A-10 or UH-60 targeting step. **Alt + RMB / Backspace** cancels.
- **Hold J:** view active recon in Phone display mode.
- **F12:** adjust keybinds, phone zoom, radar display, and other client settings.

The final purchase confirmation turns the phone upright and plays the swipe automatically. The [usage guide](https://github.com/Tylevo/Tylevo.TacticalServicesControl/blob/main/docs/usage.md) covers cargo fees, radar, and the full controls.

## Configure TSC

With the SPT server running, open **SIC > Mod pages > Tactical Services Control** from the launcher to use the themed dashboard. For the native editor, open **Config Editor > Mods > Tactical Services Control**. Personal phone and radar settings stay in **F12**.

## Testing and Fika

The new TSC v1.3.11 and Toolkit 2.0.2 pair is being prepared for testing.
Earlier local use of TSC 1.3.10 on SPT 4.1.5 was reported working, but does not
validate this candidate. See the validation record for current results.

**Multiplayer on the current SPT/Fika versions has not been tested.** Solo play does not require Fika. Cargo Transfer is implemented for solo play and the requesting human Fika host; non-host clients and dedicated-headless requesters cannot use it yet. Dedicated-headless A-10 damage remains experimental.

[Known issues](https://github.com/Tylevo/Tylevo.TacticalServicesControl/blob/main/docs/known-issues.md) · [Validation record](https://github.com/Tylevo/Tylevo.TacticalServicesControl/blob/main/docs/validation/v1.3.11.md)

## Credits

Based on **SamSWAT's Fire Support** and **Arys Reloaded**, with permission and full credit retained. Thanks to **Arys** for UnityToolkit and permission to maintain its update on
the existing Forge page.

TSC is released under **CC BY-NC 4.0**. UnityToolkit remains under MIT, and its companion libraries retain their own licenses.

[Credits](https://github.com/Tylevo/Tylevo.TacticalServicesControl/blob/main/docs/credits.md) · [Third-party notices](https://github.com/Tylevo/Tylevo.TacticalServicesControl/blob/main/THIRD_PARTY_NOTICES.md)
