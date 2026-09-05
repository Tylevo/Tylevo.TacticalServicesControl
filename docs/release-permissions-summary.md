# Release Permissions Summary

The maintainer has confirmed the upstream permission recorded in `PERMISSIONS.md`. On 2026-09-05, the maintainer also confirmed Arys's explicit approval to bundle UnityToolkit 2.0.1 rebuilt against SPT 4.1 in the v1.3.9 unreleased candidate.

Tylevo's Tactical Services Control is released under Creative Commons Attribution-NonCommercial 4.0 International (CC BY-NC 4.0), matching the upstream Arys Reloaded source license. Arys clarified that the Forge/SPT Hub BY-NC 3.0 listing was due to historical site limitations before the Forge migration.

## What This Covers

- Public Forge release.
- Public GitHub source repository.
- Modified client, Fika, and server DLL distribution.
- Derivative use of SamSWAT's Fire Support and Arys Reloaded code/assets that remain in TSC.
- Full attribution to SamSWAT and Arys.
- Bundled UnityToolkit plugin and prepatcher rebuilt against SPT 4.1, with the complete Arys MIT license. Users of the v1.3.9 candidate do not need a separate Toolkit or overlay download.
- UnityToolkit companion libraries with their own license terms and notices retained.
- MIT notice and attribution for Manimal Hacker Mod material.
- Optional voluntary Ko-fi tip link, with no paid features, early access, downloads, or support priority.

## What Must Stay True

- Keep `THIRD_PARTY_NOTICES.md` in every release archive.
- Include the Toolkit MIT license and the companion-library notices with the bundled dependency; TSC's project license does not replace their licenses.
- Keep `PERMISSIONS.md` updated if the permission scope changes.
- Do not publish private DMs unless the author explicitly approves.
- If an author asks for credit wording changes, update README, Forge description, and notices before the next release.

WTT CommonLib is still separately required. Fika remains optional and separately installed; current multiplayer compatibility and SPT 4.1.5 have not been tested. Redistribution permission is not a compatibility test result.
