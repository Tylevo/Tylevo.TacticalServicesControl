# Tylevo's Tactical Services Control v1.3.8

**SPT 4.1.4 public beta** with A-10 strafes and double passes, UH-60 extraction
and cargo transfer, and UAV reconnaissance through the TerraGroup TSC Uplink.

Buy the physical Uplink from **UH-60 Pilot for ₽50,000** at loyalty level 1,
with a limit of five per restock. Pilot is unlocked without a quest for now,
uses his new portrait, and remains your helicopter cargo contact. Existing
locked Pilot entries unlock at server startup.

This cumulative update includes:

- A-10 gravity/drag compensation using EFT's native trajectory model, moving gun origins, curved cover checks, and projectile travel timing.
- Native phone panels with live prices, balances, availability, and service details.
- Phone mouse selection while holding Left Alt, alongside keyboard controls.
- Smooth, configurable authorization-phone zoom and framing.
- A redesigned six-card pre-raid store and updated tactical service icons.
- A TSC UPLINK shortcut on the main-menu bottom bar, immediately left of Character.
- The dedicated Uplink special slot and optional Seasonal Modifiers Danger Close integration.

Press **U** to buy authorizations, **K** to deploy them, and hold **J** to view
active recon in Phone mode. Hold **Left Alt** to browse the phone with its
cursor. Review the purchase, then confirm to play the automatic portrait
swipe. F12 provides local controls, phone zoom, cursor, and radar settings;
the TSC Dashboard and native SPT mod-config entry manage server settings.

Cargo Transfer sends items; it never extracts the PMC. Dispatch authorization
and EFT's separate RUB handling fee are distinct charges. Solo and requesting
human Fika hosts are supported for Cargo; non-host/headless requesters remain
unavailable. Full multiplayer acceptance and dedicated-headless A-10 damage
are still beta work.

Requires [WTT CommonLib 3.0.6](https://github.com/WelcomeToThursday/WTT-CommonLib/releases/tag/v3.0.6)
and **UnityToolkit 2.0.1 plus the SPT 4.1 compatibility overlay**. Install
the official UnityToolkit package first, then the separate
`UnityToolkit-v2.0.1-SPT4.1-compat-overlay.zip` attached to the
[v1.3.8 release](https://github.com/Tylevo/Tylevo.TacticalServicesControl/releases/tag/v1.3.8).
The original binaries alone are incompatible. Follow the
[dependency guide](dependencies.md); dependencies remain separate from the
TSC ZIP. Optional multiplayer uses
[Fika client 2.4.2](https://github.com/project-fika/Fika-Plugin/releases/tag/v2.4.2)
and its compatible server component.

Back up profiles and TSC configuration/storage, close the game and server,
and extract `Tylevo.TacticalServicesControl-v1.3.8-SPT4.1.4-TESTER.zip` into the
SPT root. Install the full matched package on all Fika peers. Do not install
SamSWAT Fire Support or Arys Reloaded alongside this derivative replacement.

The implementation passed 216 regression tests, build/package verification,
and installed-server Pilot checks. These do not replace live raid and
multiplayer acceptance. Read the [release notes](release-notes-v1.3.8.md) for
the complete changes, upgrade paths, controls, and remaining checks.

Based on SamSWAT's Fire Support and Arys Reloaded, with permission and full
credit retained. Released under CC BY-NC 4.0; see [credits](credits.md) and
[third-party notices](../THIRD_PARTY_NOTICES.md).
