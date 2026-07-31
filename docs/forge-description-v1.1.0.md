# Forge Main Description Draft

> v1.1.0 release-candidate draft. Do not publish until the final acceptance and package gates pass.

## Short Description

Buy and deploy A-10 strikes, Black Hawk extractions, and UAV recon before or during a raid through the TerraGroup TSC Uplink, with configurable payments and optional Fika integration.

## Main Description

# Tylevo's Tactical Services Control

Bring battlefield support into the raid with the **TerraGroup TSC Uplink**. Purchase a persistent authorization from the main menu or the in-raid phone, save it until you need it, then call in an A-10 strike, Black Hawk extraction, or UAV sweep.

TSC is a derivative rework and expansion of SamSWAT's Fire Support and Arys Reloaded. It replaces the primary rangefinder-and-radial workflow with a tactical phone, adds a server-authoritative support economy, and works in solo SPT with optional Project Fika integration.

## TSC Guide {.tabset}

### Services

- **A-10 Strafe** and **A-10 Double Pass** with aircraft, GAU-8 audio, tracers, and impact effects.
- **UH-60 Black Hawk Extraction** and **UH-60 Cargo Transfer** with separate timing controls. Cargo Transfer sends selected loot through post-raid delivery and never extracts the PMC.
- **UAV Recon** and **Focused Sweep** with a requester-only radar.

### Buy Before A Raid

Open **TSC UPLINK** from the main menu. It appears below **Records** when that entry is installed and otherwise below **Character**.

The store shows the authenticated PMC's stash balance, server price, availability, owned count, and storage limit for every service. Select **BUY**, review the confirmation showing the projected balance, then choose **CONFIRM BUY**.

Pre-raid buying requires:

- Persistent authorizations enabled on the TSC server.
- A stash-backed or hybrid payment source.
- An authenticated PMC profile and a reachable SPT server.

The store never spends carried raid cash. Its **DASHBOARD** button opens the active server's TSC Dashboard.

### Buy And Deploy In Raid

1. Bring the **TerraGroup TSC Uplink** into the raid.
2. Press `U` to open purchase mode.
3. Press `1`, `2`, or `3` to open UH-60 Services, Fire Support, or UAV Recon.
4. Press `1` for the standard service or `2` for its alternate option.
5. Press `Enter` on the confirmation screen to authorize payment.
6. Press `K` when ready to deploy an owned authorization.
7. Press `1`-`6` to select an owned service, then use `LMB` or `Enter` to deploy it.

The deployment phone lists only authorizations you own. `RMB`, `Backspace`, or `Escape` stows it without spending one.

### Targeting And Controls

- `U`: Open in-raid purchase mode.
- `K`: Open deployment mode.
- `J`: Hold the physical Uplink radar in the default UAV `Phone` mode.
- `1`-`6`: Navigate menus and select services.
- `Enter`: Confirm a purchase, deployment, or targeting step.
- `LMB`: Deploy the selected authorization.
- `Mouse 2` / middle mouse: Confirm A-10 and UH-60 targeting.
- `RMB`: Return to the previous phone screen.
- `Alt + RMB` or `Backspace`: Cancel target designation.
- `Escape`: Close or stow the phone.

A-10 and UH-60 targeting uses the player camera. A rangefinder is not required for the primary workflow.

Purchase, deploy, radar-hold, and spotter-confirm keys are configurable in F12. Phone framing and optional purchase-screen zoom are configurable there as well.

### UAV Display

Choose the UAV presentation in **F12 > UAV Radar Display**:

- `Phone` (default): hold `J` to raise the physical Uplink and release it to restore the previous weapon.
- `HUD`: show only the square live scanner in any screen corner during the active recon link.

HUD mode includes the scanner, sweep, orientation labels, player marker, and contacts. It does not include the phone header, status bands, telemetry, footer, or surrounding phone interface.

The radar is private to the requesting player. Other clients do not receive it, and a dedicated headless host creates no phone or HUD.

### Payments And Persistence

- Select RUB, USD, or EUR in the TSC Dashboard.
- Use carried cash, stash cash, or a configured hybrid source during a raid.
- Use the authenticated stash for pre-raid purchases.
- Store each standard and upgraded authorization separately.
- Recover interrupted persistent purchases through stable request IDs without a second debit or grant.

Changing currency does not convert numeric service prices. Review every price before saving a different currency.

### Configuration

Local controls and phone presentation settings are available in the F12 BepInEx configuration manager.

Server and gameplay settings are managed from:

`https://127.0.0.1:6969/tsc/admin`

The dashboard controls prices, currency, payment sources, authorization
limits, service availability, UAV contracts, standard-Extraction timing,
extraction-free Cargo timing, and support behavior. It is localhost-only by
default. Do not expose or port-forward it to the public internet.

### Fika

Project Fika is optional. Solo SPT does not require it.

Install the exact same TSC build on the server, human host, every client, and any dedicated headless host. Host/headless settings are authoritative during the connected raid.

- Solo and human-host raids keep the original Arys-style A-10 runtime and ballistic path.
- Fika clients wait for raid-authority acceptance and remain visual-only for A-10 damage.
- Stable request IDs prevent repeated packets from intentionally executing the same accepted support request twice.
- UAV feeds and functional extraction points belong only to the requester.
- Dedicated-headless A-10 uses a separate experimental damage executor.

**v1.1.0 multiplayer validation notice:** the transactional request, requester-isolation, extraction, and dedicated-headless acceptance matrices are still open. No current headless tester has yet verified real-raid A-10 damage/authorization settlement for this candidate. Dedicated-headless A-10 must be treated as experimental, not as parity with the human-host ballistic path.

### Installation

1. Install **UnityToolkit v2.0.1**.
2. Install **WTT Client Common Lib** and **WTT Server Common Lib**.
3. Install Project Fika only when using multiplayer.
4. Back up profiles and existing TSC config/storage.
5. Close the server, launcher, game, all clients, and any headless process.
6. Extract the TSC ZIP directly into the SPT root.

After extraction, these folders should exist:

`BepInEx/plugins/Tylevo.TacticalServicesControl/`

`SPT/user/mods/Tylevo.TacticalServicesControl/`

Do not install SamSWAT Fire Support or Arys Reloaded alongside TSC. TSC is their derivative replacement and the packages conflict.

### Compatibility And Known Limitations

- Built for **SPT 4.0.13**.
- Project Fika integration is optional and remains beta for the v1.1.0 transaction, UAV, extraction, and headless changes until the live matrices pass.
- Dedicated-headless A-10 damage is separately gated and experimental.
- If both authority-acceptance result paths and cancellation settlement are lost beyond their bounded waits, an executed service can still be refunded.
- Commit/refund retries are held in memory, so a crash, permanent logout, or backend outage beyond pending expiry can refund an already delivered service.
- Stash payment and non-host A-10 tracer delivery are implemented but still require end-to-end live acceptance.
- Remote third-person phone animation sync is not included.
- Phone inventory-inspect presentation may still need polish.
- Mortar and artillery support are not included.
- Back up profiles before testing payment configurations.

### Credits

TSC is derived from **SamSWAT's Fire Support** and **SamSWAT's Fire Support - Arys Reloaded by Arys**, with permission and attribution retained.

Additional credit goes to Tyrian for adapted radar work, danauraborealis for Manimal Hacker Mod material used under the MIT license, and the SPT and Project Fika teams for their platforms and APIs.

[Source code and full notices](https://github.com/Tylevo/Tylevo.TacticalServicesControl)

Optional support: [Ko-fi](https://ko-fi.com/tylevo). Tips are voluntary and do not unlock downloads, features, early access, or support priority.

{.endtabset}
