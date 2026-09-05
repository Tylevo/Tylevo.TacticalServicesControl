# Permissions

Permission status: **confirmed by the maintainer**. The existing Fire Support grants remain in place; the v1.3.9 release also includes the UnityToolkit bundling permission recorded below.

This file is the public release record for upstream permission and third-party attribution. Keep the private permission evidence archived outside the release package unless the grantor explicitly approves publishing the conversation.

## Upstream Fire Support Permission

Tylevo's Tactical Services Control is a derivative rework of SamSWAT's original Fire Support and SamSWAT's Fire Support - Arys Reloaded by Arys.

For this release, the maintainer has permission to:

- publish Tylevo's Tactical Services Control as a separate derivative/continuation project;
- distribute modified client, Fika, and server DLLs;
- publish the matching source repository;
- include modified portions of the upstream Fire Support / Arys Reloaded codebase;
- include the required bundled assets that are part of the upstream Fire Support basis;
- credit SamSWAT and Arys as the upstream authors/basis;
- include a voluntary Ko-fi tip link, as long as it does not gate downloads, support, features, updates, or early access.

## Permission Record

| Grantor | Status | Scope | Evidence |
| --- | --- | --- | --- |
| SamSWAT | Granted / confirmed by maintainer | Original Fire Support basis, derivative release credit, redistribution as part of TSC | Maintainer-held private message / permission record |
| Arys | Granted / confirmed by maintainer | Arys Reloaded basis, derivative release credit, redistribution as part of TSC | Maintainer-held private message / permission record |
| Arys / UnityToolkit | Explicit bundling permission confirmed by maintainer on 2026-09-05; MIT license retained | Bundle UnityToolkit 2.0.1 rebuilt against SPT 4.1 inside the TSC release ZIP | Dated maintainer permission record; upstream MIT license in `THIRD_PARTY_NOTICES.md` |
| danauraborealis / Manimal Hacker Mod | Granted; MIT license notice included | Phone/use-device material used under MIT and author permission | Maintainer-held permission record + MIT notice |
| Accurate Circular Radar / Tyrian Radar Standalone | Used under listed license/attribution if radar material remains | Radar HUD/material attribution | Source/license notice in `THIRD_PARTY_NOTICES.md` |

Before each public release, keep a dated private copy of the permission evidence in the maintainer archive. Do not publish private DMs unless the author explicitly allows it.

## UnityToolkit Bundling Permission (2026-09-05)

On September 5, 2026, the maintainer confirmed that Arys explicitly approved bundling the rebuilt UnityToolkit with TSC. The v1.3.9 release includes UnityToolkit 2.0.1 with its plugin and prepatcher rebuilt against SPT 4.1, so users do not need a separate Toolkit or compatibility-overlay download.

UnityToolkit remains licensed under MIT, copyright (c) 2025 Arys. The full upstream license is included in `THIRD_PARTY_NOTICES.md` and the packaged notices. Companion libraries retain their own licenses and notices; the Toolkit permission does not change those terms. WTT CommonLib remains a separate requirement, and optional Fika is not bundled.

This record documents permission to redistribute the rebuilt dependency. It does not claim compatibility testing on SPT 4.1.5 or current Fika versions, or endorsement of TSC by Arys.

## Donation / Ko-fi Note

The Ko-fi link is voluntary only. It must not unlock features, early access, downloads, support priority, or updates.

Recommended wording:

> If you enjoy the project and want to support future work, you can leave a voluntary tip on Ko-fi. This is optional and does not unlock features, early access, downloads, or support priority.

## License Interaction

Tylevo's Tactical Services Control is released under Creative Commons Attribution-NonCommercial 4.0 International (CC BY-NC 4.0), matching the upstream Arys Reloaded source license. Arys clarified that the Forge/SPT Hub BY-NC 3.0 listing was due to historical site limitations before the Forge migration.

Upstream-derived material remains credited to its original authors and is redistributed under the permission described above. Third-party MIT material remains under MIT, and other third-party material keeps its own notices and attribution requirements.

See:

- `LICENSE`
- `THIRD_PARTY_NOTICES.md`
- `docs/credits.md`
