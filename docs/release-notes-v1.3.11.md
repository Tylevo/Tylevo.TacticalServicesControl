# Tylevo's Tactical Services Control v1.3.11 Public Beta

**SPT 4.1.5 / EFT 0.16.9.5.40743 · Prepared, not published**

This candidate requires **UnityToolkit 2.0.2** and **WTT CommonLib 3.0.6**,
installed separately. TSC v1.3.11 and the standalone Toolkit update have not
been published yet. Follow the [TSC releases](https://github.com/Tylevo/Tylevo.TacticalServicesControl/releases)
and [UnityToolkit project](https://forge.sp-tarkov.com/mod/1426/unitytoolkit)
for availability.

This release is being prepared for players moving on from the **SPT 4.0.13 Forge version, TSC v1.0.8**. It includes the work developed across the intermediate GitHub test builds, which were not released on Forge. The changes below compare the prepared update with that last Forge release.

## New features since the 4.0.13 release

### UH-60 Cargo Transfer

Cargo Transfer replaces Priority Exfil. Call the helicopter to a loading zone, use **SEND ITEMS VIA UH-60**, and send loot home while you remain in the raid. Accepted cargo arrives through **UH-60 Pilot** mail after the raid.

The support authorization pays for dispatch. Submitting items has a separate RUB handling fee, paid from carried money or your stash through the **Transfer fee source** setting in F12. The helicopter leaves after a successful paid transfer. Cancelling or failing payment keeps the remaining loading window open. Standard Extraction still extracts your PMC and has separate timing settings.

### Pilot's Services tab

Buy support authorizations at **Traders > Pilot > Services**. Choose from the compact service list on the left and review the selected service's description, price, availability, and held/limit count on the right. The purchase review keeps the same confirmed payment from your PMC stash and the same persistent authorization flow. In-raid phone purchasing and deployment controls are unchanged.

### Phone interface and mouse controls

The purchase phone now uses live UI with redesigned service icons, prices, balances, and availability. Its screens stay horizontal until the final upright swipe. Hold **Left Alt** to browse and left-click to select; release Alt to look around again. The **1 / 2 / 3** keyboard navigation remains available.

Phone zoom now eases in and out instead of snapping into place. Zoom timing and framing are configurable in F12. Deployment and radar views keep your raid FOV.

### Pilot trader and dedicated Uplink slot

The main download opens Pilot immediately and sells the Uplink for **₽50,000**.
Configured services retain their normal prices and limits. The separate,
optional **Pilot Questline add-on** introduces progression using the same client.

With the add-on, Pilot is introduced through **Open Channel**, Mechanic's level-5 handover quest.
**Some Assembly Required** takes a Broken GPhone, Electronic components, and
a Screwdriver. **Back on the Air** sends the player to install a supplied Radio
repeater at Shoreline's weather-station antenna and survive. Completing it
awards the working Uplink and unlocks all configured services. Replacement
phones cost **₽50,000** at Pilot's loyalty level 1, with five per restock.
The phone no longer spawns in TSC's random loot. See the
[questline guide](pilot-questline.md) for quantities, rewards, and survival rules.

An Uplink-only fourth special slot lets you carry the phone without occupying the usual three special slots. Manual support also recognizes a carried Uplink outside that slot.

### Radar display options

Hold **J** to see active reconnaissance on the physical phone, or select **HUD** mode for a compact scanner in a chosen screen corner. The recon session continues while the phone is stowed. Only the requester sees the radar.

### Payment and configuration options

Support authorizations can be priced in **RUB, USD, or EUR**, using carried money or the stash where configured. Cargo's separate handling fee remains RUB-only. Changing currency does not convert your existing price values.

The TerraGroup dashboard now opens from **SIC > Mod pages > Tactical Services Control** in the launcher. SIC's native config editor is also available. The themed dashboard retains its appearance, while both editors validate settings and protect against conflicting or failed saves.

## Improvements and fixes

- Corrected A-10 shot origins and added compensation for EFT's gravity and drag to address impacts falling short of the target.
- Strengthened authorization synchronization, consumption, failed-dispatch refunds, and payment recovery across saves and reconnects.
- Improved phone equip/stow cleanup, radar lifetime handling, and targeting readiness.
- Separated standard Extraction and Cargo Transfer timing, and improved departure handling after a successful cargo submission.
- Repaired the Uplink asset for the SPT 4.1 runtime and updated the client/server build references for SPT 4.1.5.
- Update the required Toolkit version to the standalone **UnityToolkit 2.0.2**
  package for SPT 4.1.5. It includes its own plugin, prepatcher, companion
  libraries, and notices; no separate compatibility overlay is needed.

Phone deployment, camera targeting, A-10 Double Pass, UAV Recon, Focused Sweep, and the original dashboard were already present in the 4.0.13 release. This update expands and improves that setup.

## Dependency distribution change

TSC v1.3.11 does not bundle UnityToolkit. Arys approved updating Toolkit,
added Tylevo as a coauthor on its existing Forge page, and requested a distinct
version number. The update is being prepared there as **UnityToolkit 2.0.2**.

The earlier bundled v1.3.9 and v1.3.10 test releases have been withdrawn and
held as archived drafts. The [permission record](../PERMISSIONS.md) explains
the distribution correction. Arys's authorship, MIT license, and the
companion libraries' licenses remain unchanged.

## Install or update

Once both new packages are published, install **UnityToolkit 2.0.2** from
[Arys's existing project](https://forge.sp-tarkov.com/mod/1426/unitytoolkit)
and **[WTT CommonLib 3.0.6](https://github.com/WelcomeToThursday/WTT-CommonLib/releases/tag/v3.0.6)** separately, including its client, server, and serialization prepatcher components. Extract the full TSC ZIP into your SPT 4.1.5 root while the game, launcher, and server are closed. Merge `BepInEx` and `SPT_Runtime` and replace the old mod files.

**From SPT 4.0.13:** install SPT 4.1.5 in a new folder and create a fresh profile. Keep the old installation as a backup and let TSC create fresh storage. Do not copy the old installation's mods or player ledger into the new setup.

**From SPT 4.1.x:** follow SPT's patch-update instructions. Back up profiles and TSC's complete `config/` and `storage/` directories first; the TSC ZIP does not overwrite those folders.

If a previous TSC ZIP installed Toolkit for you, replace that installation
with the complete standalone 2.0.2 package in the same plugin and patcher
folders. Keep one copy of each component.

See the [installation guide](dependencies.md) and [controls and usage](usage.md).
The TSC release provides the main installable ZIP plus a separate optional
**Pilot Questline add-on ZIP**, with a checksum file covering both. Extract the
add-on into the same SPT root to enable the three-quest introduction on the
server; clients keep the main TSC download. Toolkit and WTT remain separate
dependencies. See the [add-on guide](pilot-questline.md) before installing or
removing progression from an existing profile.

## Testing and limitations

The standalone Toolkit 2.0.2 plugin and prepatcher built against SPT 4.1.5
references with no warnings or errors. The TSC optional questline build passed
295 regression tests, 7 dashboard interaction tests, both package checks, and
115 isolated native server checks. The full build had five existing warnings
and no errors. See the [add-on validation report](validation/pilot-questline-addon.md)
for the tested candidate and scope. Historical Toolkit and TSC candidate
results remain in the [validation record](validation/v1.3.11.md).
Earlier local use of TSC 1.3.10 was reported working; that result does not
validate the new TSC/Toolkit pair.

The move to Pilot's Services tab still needs in-game navigation, layout,
purchase, and recovery testing. See the [manual checklist](pilot-services-testing.md).
The new [Pilot questline](pilot-questline.md#validation) also requires gameplay
acceptance before publication. Its server, Core, and Fika changes must be
installed together; service protocol 2 rejects older manual request peers
before new payment.

**Fika multiplayer on the current SPT/Fika versions has not been tested.** Fika is optional. Cargo Transfer is implemented for the requesting human host as well as solo play; non-host clients and dedicated-headless requesters cannot use it yet. Dedicated-headless A-10 damage remains experimental. See [known issues](known-issues.md).

This is an unpublished public-beta candidate. Its planned archive retains
the `TESTER` suffix. Intermediate GitHub test releases and technical development notes are catalogued in the [archive](archive/README.md).
