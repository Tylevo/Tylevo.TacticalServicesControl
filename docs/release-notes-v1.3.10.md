# Tylevo's Tactical Services Control v1.3.10 Public Beta

**SPT 4.1.5 / EFT 0.16.9.5.40743 · Published September 5, 2026**

[Download the full TSC ZIP](https://github.com/Tylevo/Tylevo.TacticalServicesControl/releases/download/v1.3.10/Tylevo.TacticalServicesControl-v1.3.10-SPT4.1.5-TESTER.zip)

This is the release for players moving on from the **SPT 4.0.13 Forge version, TSC v1.0.8**. It includes the work developed across the intermediate GitHub test builds, which were not released on Forge. The changes below compare those two player releases.

## New features since the 4.0.13 release

### UH-60 Cargo Transfer

Cargo Transfer replaces Priority Exfil. Call the helicopter to a loading zone, use **SEND ITEMS VIA UH-60**, and send loot home while you remain in the raid. Accepted cargo arrives through **UH-60 Pilot** mail after the raid.

The support authorization pays for dispatch. Submitting items has a separate RUB handling fee, paid from carried money or your stash through the **Transfer fee source** setting in F12. The helicopter leaves after a successful paid transfer. Cancelling or failing payment keeps the remaining loading window open. Standard Extraction still extracts your PMC and has separate timing settings.

### Pre-raid support store

Buy support authorizations from **TSC UPLINK** beside **Character** on the main-menu bottom bar. The store shows service artwork, descriptions, prices, your stash balance, and held authorizations. A review step lets you check the purchase before paying.

### Phone interface and mouse controls

The purchase phone now uses live UI with redesigned service icons, prices, balances, and availability. Its screens stay horizontal until the final upright swipe. Hold **Left Alt** to browse and left-click to select; release Alt to look around again. The **1 / 2 / 3** keyboard navigation remains available.

Phone zoom now eases in and out instead of snapping into place. Zoom timing and framing are configurable in F12. Deployment and radar views keep your raid FOV.

### Pilot trader and dedicated Uplink slot

The physical Uplink is now sold by **UH-60 Pilot**, rather than Jaeger. Pilot is unlocked without a quest requirement and uses the new portrait in Trading and cargo mail. The Uplink costs **₽50,000** at loyalty level 1, with a limit of five per restock.

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
- Bundled the compatible **UnityToolkit 2.0.1** rebuild, companion libraries, and license notices with Arys's permission. No extra Toolkit or overlay download is needed.

Phone deployment, camera targeting, A-10 Double Pass, UAV Recon, Focused Sweep, and the original dashboard were already present in the 4.0.13 release. This update expands and improves that setup.

## Install or update

Install **[WTT CommonLib 3.0.6](https://github.com/WelcomeToThursday/WTT-CommonLib/releases/tag/v3.0.6)** separately, including its client, server, and serialization prepatcher components. Extract the full TSC ZIP into your SPT 4.1.5 root while the game, launcher, and server are closed. Merge `BepInEx` and `SPT_Runtime` and replace the old mod files.

**From SPT 4.0.13:** install SPT 4.1.5 in a new folder and create a fresh profile. Keep the old installation as a backup and let TSC create fresh storage. Do not copy the old installation's mods or player ledger into the new setup.

**From SPT 4.1.x:** follow SPT's patch-update instructions. Back up profiles and TSC's complete `config/` and `storage/` directories first; the TSC ZIP does not overwrite those folders.

See the [installation guide](dependencies.md) and [controls and usage](usage.md). The release has one installable ZIP and an optional `SHA256SUMS.txt` file.

## Testing and limitations

The maintainer reports successful local use on SPT 4.1.5. The release passed its full build, all 238 regression tests, and 26 isolated server checks. The [validation record](validation/v1.3.10.md) and [reference log](port/SPT-4.1.5-PORT-LOG.md) document the tested builds and their limits.

**Fika multiplayer on the current SPT/Fika versions has not been tested.** Fika is optional. Cargo Transfer is implemented for the requesting human host as well as solo play; non-host clients and dedicated-headless requesters cannot use it yet. Dedicated-headless A-10 damage remains experimental. See [known issues](known-issues.md).

This remains a public beta. The existing `TESTER` filename is unchanged. Intermediate GitHub test releases and technical development notes are catalogued in the [archive](archive/README.md).
