# Tylevo's Tactical Services Control v1.3.12 Public Beta

**September 8, 2026 · SPT 4.1.5 / EFT 0.16.9.5.40743**

This patch improves phone movement and adds configurable UH-60 cargo capacity.
It builds on [1.3.11](release-notes-v1.3.11.md); Pilot Services, purchase history,
and the optional questline carry forward from that release.

## Changes

- **Phone grip during sprint:** upright deployment, radar, and Danger Close
  phones follow the animated left hand in first person.
- **Gentler movement:** reduce walking bob and turning sway while an upright
  phone is held, keeping the phone and hand together.
- **Purchase phone sprint zoom:** the horizontal purchase screen eases back to
  your normal raid FOV and hand framing when you sprint, then zooms in again
  when you stop. Both directions use the existing zoom-in curve and **Phone
  zoom in seconds** setting. Reversing mid-transition starts from the current
  view. Automatic zoom must be enabled; closing still uses its separate
  zoom-out setting.
- **Configurable cargo grid:** set **Cargo Grid Columns** and **Cargo Grid Rows**
  in the dashboard's UH-60 section, or `GridWidth` and `GridHeight` under
  `priorityExfil` in SIC's config editor. Columns allow 0–10 and rows 0–30.
  Zero preserves the native size of that dimension. Settings apply when the
  next cargo screen opens. These are inventory cells: larger items use
  multiple cells, so a 10-by-10 grid is not a fixed 100-item limit.

## Install or update

Install **[UnityToolkit 2.0.2](https://github.com/Tylevo/UnityToolkit-New/releases/tag/v2.0.2)**
and **[WTT CommonLib 3.0.6](https://github.com/WelcomeToThursday/WTT-CommonLib/releases/tag/v3.0.6)**
separately, including their required plugin, server, and prepatcher components.
TSC does not bundle these dependencies.

1. Close the game, launcher, and SPT server. Back up your profiles and TSC's
   complete `config/` and `storage/` directories.
2. Extract **Tylevo.TacticalServicesControl-v1.3.12-SPT4.1.5-TESTER.zip** into
   the SPT root. Merge `BepInEx` and `SPT_Runtime` and replace old mod files.
   Update all TSC DLLs and assets together.
3. If the Pilot Questline add-on is installed, also extract the matching
   **Tylevo.TacticalServicesControl-PilotQuestline-v1.3.12-SPT4.1.5-TESTER.zip**
   before restarting. Its quest behavior is unchanged; the version manifest
   must match the main mod. Keep the add-on installed to retain its quest
   definitions. Skip this ZIP for immediate Pilot access.
4. Restart the server, launcher, and game. Fika participants must use the same
   main TSC version; its server uses the matching add-on when progression is enabled.

The ZIPs contain no player profiles, mutable configuration, or TSC storage.
Existing settings, purchased authorizations, and cargo records are preserved.
New cargo dimensions default to the native grid. `SHA256SUMS.txt` covers both
installable ZIPs; GitHub's automatic source archives are not mod installers.
For older SPT versions and add-on removal, use the [installation guide](dependencies.md)
and [questline guide](pilot-questline.md).

## Validation and limitations

The maintainer accepted the phone grip, movement, and purchase sprint zoom
after in-game testing on September 8. The tested candidate passed **305 C#
regression tests and 10 dashboard tests**. Cargo configuration also passed
**27 checks using SPT 4.1.5's native SIC config editor service**.

Custom cargo grid layout and delivery still need in-game coverage. Broader
phone cases and current Fika multiplayer remain unverified. Cargo Transfer
retains its solo/requesting-human-host scope; other Fika clients and
dedicated-headless requesters cannot use it. The optional questline keeps its
existing public-beta testing limits. See the [validation record](validation/v1.3.12.md)
and [known issues](known-issues.md).
