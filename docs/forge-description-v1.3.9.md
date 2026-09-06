# Tylevo's Tactical Services Control v1.3.9 Public Beta

> **Historical documentation.** These instructions and results describe this
> earlier version. For the prepared TSC v1.3.11 / SPT 4.1.5 update, use the
> [release notes](release-notes-v1.3.11.md) and [installation guide](dependencies.md).
> TSC v1.3.11 requires standalone UnityToolkit 2.0.2; both new packages are
> unpublished. See the [archive index](archive/README.md) for older availability.
>
> The claim of explicit bundling permission below was a maintainer/assistant
> misunderstanding and is withdrawn. See the [corrected permission record](../PERMISSIONS.md).

Published September 5, 2026 for SPT 4.1.4. Call in A-10 strafes, UH-60 extraction and cargo
transfer, or UAV reconnaissance through the TerraGroup TSC Uplink.

This update adds the existing TSC dashboard to SIC's **Mod pages**. It keeps
the TerraGroup theme and adds sidebar links to SIC and its standard config
editor. The two editors now handle runtime changes, disk saves, and stale
edits consistently. See the [release notes](release-notes-v1.3.9.md) and
[dashboard guide](dashboard.md).

Buy the Uplink from UH-60 Pilot. Press **U** to buy support, **K** to deploy
it, and hold **J** to view active recon in Phone mode. Hold **Left Alt** and
left-click to select on the phone. Keyboard controls use **1**, **2**, and
**3** for categories, then **1** or **2** for the service. Review the choice
and confirm with **Enter** or Alt and a click.

Fika multiplayer has not been tested on the current SPT/Fika versions.
The optional integration remains experimental, and SPT 4.1.5 has not been
tested. See the
[README](../README.md) for controls and limitations.

UnityToolkit 2.0.1 is included in the TSC ZIP, with its plugin and prepatcher
rebuilt against SPT 4.1, companion libraries, and license notices. Arys
approved bundling the rebuild, as confirmed by the maintainer on
September 5, 2026. No separate Toolkit or compatibility-overlay download is
needed.

WTT CommonLib 3.0.6 is still required separately, including its client,
server, and serialization prepatcher components. Fika is optional and also
installed separately. Follow the [dependency guide](dependencies.md). Close
the game and server before extracting the full matched package into your
SPT root. Replace existing Toolkit files in their standard folders when
prompted and keep one Toolkit installation. The package preserves
configuration, profiles, and TSC storage. Back those up before updating.

Based on SamSWAT's Fire Support and Arys Reloaded, with permission and full
credit retained. TSC is released under CC BY-NC 4.0; UnityToolkit remains
under MIT and its companion libraries keep their own licenses. See
[credits](credits.md) and [third-party notices](../THIRD_PARTY_NOTICES.md).
