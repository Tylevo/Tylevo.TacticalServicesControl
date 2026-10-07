# Tylevo's Tactical Services Control v1.3.12 Public Beta

**For SPT 4.1.5 / EFT 0.16.9.5.40743**

Call in A-10 fire support, arrange a helicopter extraction or cargo pickup,
and locate contacts with UAV reconnaissance from the TerraGroup TSC Uplink.
Buy authorizations in **Traders > Pilot > Services** or through the phone in raid.

## New in 1.3.12

- Upright phones stay attached to the animated hand during sprint.
- Walking bob and turning sway are gentler while holding the upright phone.
- The horizontal purchase phone eases out of zoom during sprint and back in
  when you stop, with matching fade timing.
- Configure UH-60 cargo grid columns and rows in SIC or the dashboard. Zero
  uses the native dimension; custom grids support up to 10 columns and 30
  rows and apply when the next cargo screen opens.

Pilot Services, purchase history, radar display options, configurable service
prices, and the optional three-quest Pilot introduction carry forward from 1.3.11.
The main download provides immediate Pilot access and the ₽50,000 Uplink.

## Install

Install [UnityToolkit 2.0.2](https://github.com/Tylevo/UnityToolkit-New/releases/tag/v2.0.2)
and [WTT CommonLib 3.0.6](https://github.com/WelcomeToThursday/WTT-CommonLib/releases/tag/v3.0.6)
separately, including their required components. Close the game, launcher,
and server, back up profiles and TSC's `config/` and `storage/`, then extract
the full 1.3.12 TSC ZIP into your SPT root. Merge `BepInEx` and `SPT_Runtime`.
Update all TSC components together. Existing profiles, configuration, and
TSC storage are not included in or overwritten by the archive.

If you use the optional Pilot Questline, update it with the separate matching
1.3.12 add-on ZIP before restarting the server. Its quests are unchanged;
the add-on's version must match the main mod. `SHA256SUMS.txt` verifies both ZIPs.

[Downloads](https://github.com/Tylevo/Tylevo.TacticalServicesControl/releases/tag/v1.3.12)
· [Full release notes](https://github.com/Tylevo/Tylevo.TacticalServicesControl/blob/main/docs/release-notes-v1.3.12.md)
· [Installation guide](https://github.com/Tylevo/Tylevo.TacticalServicesControl/blob/main/docs/dependencies.md)

## Controls and configuration

- **U:** purchase phone. **K:** deploy an authorization. **Hold J:** physical-phone radar.
- **Hold Left Alt + left-click:** browse and select on the phone; number keys remain available.
- **F12:** keybinds, phone zoom, and radar display settings.
- **SIC > Mod pages > Tactical Services Control:** themed dashboard.
- **SIC > Config Editor > Mods > Tactical Services Control:** native config editor.

## Testing

The maintainer accepted the phone movement and sprint zoom in-game. The tested
candidate passed 305 C# regression tests and 10 dashboard tests, with 27 native
SIC checks for cargo settings. Custom cargo grid gameplay and current Fika
multiplayer remain unverified. Cargo Transfer is limited to solo play and a
requesting human Fika host. The optional questline retains its existing beta
testing limits. See [known issues](https://github.com/Tylevo/Tylevo.TacticalServicesControl/blob/main/docs/known-issues.md).

Based on SamSWAT's Fire Support and Arys Reloaded, with permission and credit
retained. TSC uses CC BY-NC 4.0; UnityToolkit remains under MIT and is distributed separately.
