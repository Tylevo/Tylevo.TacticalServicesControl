# Tylevo's Tactical Services Control v1.3.10 Public Beta

Unreleased candidate for SPT 4.1.5. Call in A-10 strafes, UH-60 extraction
and cargo transfer, or UAV reconnaissance through the TerraGroup TSC Uplink.
The maintainer reports successful local use on this target. Individual service
checks are not documented; multiplayer on the current SPT/Fika versions
remains untested.

This candidate updates TSC's declared SPT target and retains the gameplay
and configuration features from v1.3.9. Open **Tactical Services Control**
under SIC's **Mod pages** for the themed dashboard. The native config editor
also remains available, with separate runtime Apply, disk Save, and Load Disk
actions. See the [candidate notes](release-notes-v1.3.10.md) and
[dashboard guide](dashboard.md).

UnityToolkit 2.0.1 is bundled with its plugin and prepatcher rebuilt against
SPT 4.1, companion libraries, and license notices, with Arys's permission.
No separate Toolkit or compatibility-overlay download is needed. WTT
CommonLib 3.0.6 is still required separately, including its client, server,
and serialization prepatcher components. Fika is optional; version 2.4.2
remains a build reference rather than a multiplayer compatibility claim.

Close the game and server, back up profiles and TSC configuration/storage,
update SPT to 4.1.5, and extract the full v1.3.10 candidate ZIP into the SPT
root. Replace existing Toolkit files in their standard folders and keep one
installation. Follow the [dependency guide](dependencies.md). The published
v1.3.9 package remains available for SPT 4.1.4.

Buy the Uplink from UH-60 Pilot. Press **U** to buy support, **K** to deploy
it, and hold **J** to view active recon in Phone mode. Hold **Left Alt** and
left-click to select, then review and confirm the service.

Based on SamSWAT's Fire Support and Arys Reloaded, with permission and full
credit retained. TSC is released under CC BY-NC 4.0; UnityToolkit remains
under MIT and its companion libraries keep their own licenses. See
[credits](credits.md) and [third-party notices](../THIRD_PARTY_NOTICES.md).
