# Permissions

The existing Fire Support grants remain in place. **The previous record of
explicit UnityToolkit bundling permission was incorrect.** The maintainer and
assistant misunderstood permission to update Toolkit as permission to bundle
it. The corrected distribution plan is recorded below.

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
| Arys / UnityToolkit | Update permission confirmed by maintainer; Tylevo added as coauthor on the existing Toolkit page | Maintain and publish the standalone SPT update on Arys's existing project page, with a distinct 2.0.2 version; Toolkit is not bundled in TSC v1.3.11 | Maintainer-held permission record; upstream MIT license retained |
| danauraborealis / Manimal Hacker Mod | Granted; MIT license notice included | Phone/use-device material used under MIT and author permission | Maintainer-held permission record + MIT notice |
| Accurate Circular Radar / Tyrian Radar Standalone | Used under listed license/attribution if radar material remains | Radar HUD/material attribution | Source/license notice in `THIRD_PARTY_NOTICES.md` |

Before each public release, keep a dated private copy of the permission evidence in the maintainer archive. Do not publish private DMs unless the author explicitly allows it.

## UnityToolkit permission correction

Earlier documentation for TSC v1.3.9 and v1.3.10 said Arys had explicitly
approved bundling UnityToolkit inside TSC. That statement was a
maintainer/assistant misunderstanding and is withdrawn.

Arys approved updating Toolkit, added Tylevo as a coauthor on its existing
mod page, and requested a distinct version for the update. The prepared
release is **UnityToolkit 2.0.2**, distributed separately through that project.
**TSC v1.3.11 requires Toolkit separately and does not include its binaries or
companion libraries.** The earlier bundled TSC packages are held as archived
drafts. The new TSC and Toolkit packages have not yet been published.

Arys remains the author of UnityToolkit. It remains licensed under MIT,
copyright (c) 2025 Arys; the complete upstream notice is reproduced in
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). Companion libraries keep their
own licenses. This correction concerns the recorded approval and distribution
plan; it does not change those license terms or claim compatibility testing
or endorsement of TSC.

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
