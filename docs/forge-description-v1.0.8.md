# Forge Main Description Draft

## Short Description

Buy and deploy A-10 strikes, Black Hawk extractions, and UAV recon through an in-raid tactical uplink, with configurable payments and optional Fika support.

## Main Description

# Tylevo's Tactical Services Control

Bring battlefield support into the raid with the **TerraGroup TSC Uplink**. Purchase an authorization from the phone, save it until you need it, then call in an A-10 strike, Black Hawk extraction, or UAV sweep without leaving the raid.

TSC is a full rework and expansion of SamSWAT's Fire Support and Arys Reloaded. It replaces the old rangefinder-and-radial workflow with a purpose-built tactical phone, adds an in-raid economy, and supports both solo SPT and Project Fika.

## TSC Guide {.tabset}

### Features

- **A-10 Strafe** and **A-10 Double Pass** with aircraft, GAU-8 audio, tracers, and impact effects.
- **UH-60 Black Hawk Extraction** and **Priority Exfil**.
- **UAV Recon** and **Focused Sweep** with a requester-only radar display.
- Purchase and deploy services from the animated **TerraGroup TSC Uplink**.
- Carried roubles, stash roubles, and configurable hybrid payment behavior.
- Persistent service authorizations with separate counts for every standard and upgraded option.
- Configurable prices, limits, payment rules, support settings, and Fika/headless behavior.
- Optional Project Fika support with host-authoritative requests and synchronized raid visuals.

### How To Use

1. Bring the **TerraGroup TSC Uplink** into the raid.
2. Press `U` to open the phone in purchase mode.
3. Press `1`, `2`, or `3` to open Extraction, Fire Support, or UAV Recon.
4. Inside a category, press `1` for the standard service or `2` for the upgraded option when available.
5. Press `Enter` on the confirmation screen to purchase the selected authorization.
6. When you are ready to use it, press `K` to open the phone in deployment mode.
7. Press `1`-`6` to choose one of your owned services, then press `LMB` or `Enter` to deploy it.

The deploy phone only lists authorizations you currently own. Closing it with `RMB`, `Backspace`, or `Escape` does not consume anything.

### Targeting And Controls

- `U`: Open the Uplink in purchase mode.
- `K`: Open the Uplink in deployment mode.
- `1`-`6`: Navigate phone menus and select owned services.
- `Enter`: Confirm a purchase, deployment, or targeting step.
- `LMB`: Deploy the selected authorization from the phone.
- `Mouse 2` / middle mouse: Confirm A-10 and UH-60 targeting steps.
- `RMB`: Return to the previous phone screen.
- `Alt + RMB` or `Backspace`: Cancel active target designation.
- `Escape`: Close or stow the phone.

A-10 and UH-60 targeting uses the player camera. **A rangefinder is not required.** UAV Recon and Focused Sweep start immediately after deployment.

The purchase, deploy, UAV-radar hold, and spotter-confirm keys can all be changed in `F12`. Phone framing and optional authorization-screen zoom can also be adjusted there; deploy and held radar views preserve the current raid FOV.

### Configuration

Local controls and phone presentation settings are available through the `F12` BepInEx configuration manager.

Server and gameplay settings are managed from the TSC Dashboard:

`https://127.0.0.1:6969/tsc/admin`

The dashboard controls prices, payment sources, authorization limits, support behavior, and Fika/headless settings. It is localhost-only by default. Do not expose or port-forward it to the public internet.

### Fika

Project Fika is optional. Solo SPT does not require it.

For Fika, install the same TSC version on the human host, every client, and the dedicated headless host when one is used. The raid host's settings are authoritative while connected.

- Human-hosted Fika raids retain the original Arys A-10 runtime and damage path.
- Fika clients render synchronized support visuals and do not execute authoritative A-10 damage.
- Dedicated-headless A-10 damage is separately gated and remains **experimental**. Results can vary by map and mod combination.
- UAV radar is displayed only to the player who requested it and is never created on the headless host.

### Installation

1. Install **UnityToolkit v2.0.1** and **WTT CommonLib**.
2. Install Project Fika only when using multiplayer.
3. Extract the TSC release ZIP directly into the SPT root.
4. Start SPT normally.

After extraction, these folders should exist:

`BepInEx/plugins/Tylevo.TacticalServicesControl/`

`SPT/user/mods/Tylevo.TacticalServicesControl/`

Do not install SamSWAT Fire Support or Arys Reloaded alongside TSC. TSC is a derivative replacement and the packages conflict.

### Compatibility And Known Issues

- Built for **SPT 4.0.13**.
- Project Fika is supported but optional.
- Dedicated-headless Fika A-10 damage is experimental and is not claimed to match the original human-host ballistic path in every setup.
- Remote third-person phone animation sync is not currently included.
- The phone's inventory inspect presentation may still need polish.
- Back up profiles before testing new payment configurations.

### Credits

TSC is derived from **SamSWAT's Fire Support** and **SamSWAT's Fire Support - Arys Reloaded by Arys**, with permission and attribution retained.

Additional credit goes to Tyrian for radar work, danauraborealis for Manimal Hacker Mod material used under the MIT license, and the SPT and Project Fika teams for their platforms and APIs.

[Source code and full notices](https://github.com/Tylevo/Tylevo.TacticalServicesControl)

Optional support: [Ko-fi](https://ko-fi.com/tylevo). Tips are voluntary and do not unlock downloads, features, early access, or support priority.

{.endtabset}
