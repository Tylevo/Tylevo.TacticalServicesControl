# Tylevo Tactical Services Control Development Handoff

Last updated: 2026-07-13

## Start Here

This is the active local development checkout:

`C:\Users\tylev\Desktop\RaidOps\build_source\Tylevo.TacticalServicesControl-github-1.0.7`

[GitHub Releases](https://github.com/Tylevo/Tylevo.TacticalServicesControl/releases), the matching release tag, and the attached artifact are authoritative for what was publicly shipped. GitHub's default branch is authoritative for committed unreleased work after it is fetched. Uncommitted files in this checkout are development candidates only.

Read these files before making changes:

1. `docs/HANDOFF.md`
2. `README.md`
3. `docs/release-notes-v1.0.8.md`
4. `CHANGELOG.md`
5. `docs/fika.md`
6. `docs/dashboard.md`
7. `docs/known-issues.md`

Do not work from the older experimental branches, `.codex` snapshots, `tmp` package stages, or previous patch-kit folders.

## Repository State

- Branch: `main`
- Current committed tip: `b7835ea` (`Document v1.0.8 controls and release history (#4)`)
- `main`, `origin/main`, and tag `v0.9.8` point to that commit.
- Product display version: **1.0.8 Public Beta**
- Internal assembly/tag version: **0.9.8 / 0.9.8.0**
- Target: **SPT 4.0.13**

Uncommitted work at this checkpoint:

- `README.md` is modified to replace the obsolete YY/rangefinder instructions with the current phone deploy workflow.
- `docs/forge-description-v1.0.8.md` is a new paste-ready Forge main-description draft.
- `docs/HANDOFF.md` is this new checkpoint.
- `docs/roadmap.md` now separates shipped 1.0.8 work from possible future features.
- UAV Recon now offers two local F12 presentation modes: the default hold-to-view physical phone, or a persistent corner HUD that renders the exact phone radar texture. HUD position is independently configurable.
- The configurable radar hold key defaults to `J`; unrelated movement/sprint keys no longer count as a release, and releasing the configured chord safely restores the weapon even during asynchronous phone equip.
- Deploy and radar sessions conceal EFT's landscape equip transaction, then reveal the existing phone directly in its upright pose with the free right arm tucked. The animator is not frozen before EFT's completion callback.
- Standard UAV defaults are 480 seconds / 200 m / 5-second sweeps. Focused Sweep defaults are 90 seconds / 100 m / 0.75-second sweeps.
- The requester phone link and loiter aircraft now use the same host/server-authoritative duration. A one-time migration updates untouched legacy local timing defaults (`45/1` and `30/0.5`) without replacing custom values.
- Fika recon sessions are requester-owned: a human host cannot view a client's feed and a dedicated headless host creates no feed.
- Matching server defaults, dashboard ranges, and packaged config templates are updated.

The current code and documentation changes are build-verified and installed as one matched local test set in `D:\SPT`, but they have not been live-raid tested, packaged, committed, or published.

## Published Release

GitHub release:

`https://github.com/Tylevo/Tylevo.TacticalServicesControl/releases/tag/v0.9.8`

Installer:

`https://github.com/Tylevo/Tylevo.TacticalServicesControl/releases/download/v0.9.8/Tylevo.TacticalServicesControl-v1.0.8-SPT4.0.13.zip`

Local matching installer:

`C:\Users\tylev\Desktop\RaidOps\dist\Tylevo.TacticalServicesControl-v1.0.8-SPT4.0.13.zip`

Verified release facts:

- SHA256: `C3C0390CC6641E5F82C99C3E57D8AEDA358FD0278E667E6616E53963EB2FCB8D`
- Size: `41,236,560` bytes, about 39.33 MiB.
- Archive roots: `BepInEx` and `SPT` only.
- File entries: 172.
- Asset bundles: 8.
- GitHub release is published, not a draft, and not marked prerelease.

The Forge page was still serving the 1.0.7 release when this handoff was written. The new 1.0.8 main-description draft has not been pasted into Forge yet.

## What Is Implemented

### Uplink Purchase And Deployment

- The TerraGroup TSC Uplink is the primary interface for both purchasing and deploying support.
- `U` opens purchase mode.
- `K` opens deployment mode.
- Purchase categories use `1`-`3`; standard/upgraded variants use `1` and `2` inside the category.
- Deploy mode lists only authorizations the player currently owns.
- Deploy selection uses `1`-`6`, then `LMB` or `Enter`.
- Closing deploy mode with `RMB`, `Backspace`, or `Escape` does not consume an authorization.
- A-10 and UH-60 target designation is camera-based. The rangefinder is no longer required.
- `Mouse 2`/middle mouse or `Enter` confirms targeting steps. `Alt + RMB` or `Backspace` cancels.
- The old YY radial and rangefinder flow remains behind the hidden legacy setting and is disabled by default.

### Phone Presentation

- Phone auto zoom remains available for authorization purchase screens and defaults to FOV 45.
- Deploy-menu and held UAV-radar phone views preserve the current raid FOV with no deliberate zoom.
- Their landscape equip animation remains active but concealed until EFT completes the hand transaction; the first visible frame is the upright phone, with the free right arm tucked below view.
- Default horizontal framing is `-0.004`.
- Default vertical framing is `0.09`.
- Authorization-screen FOV and phone framing are adjustable in F12. Deploy/radar views keep raid FOV unchanged, and any changed presentation state is restored when the phone is stowed.
- One-time migration flags update untouched old defaults while preserving custom user values.
- Purchase, deploy, and spotter-confirm keybinds are configurable in F12.

### Available Services

- A-10 Strafe.
- A-10 Double Pass.
- UH-60 Black Hawk Extraction.
- Priority Exfil.
- UAV Recon.
- Focused Sweep.

Single and upgraded service variants remain distinct through purchase, storage, selection, consumption, commit, and refund.

### Payments And Authorization Ledger

- Carried roubles, stash roubles, and hybrid/preferred payment modes are implemented.
- `PreferStashThenCarried` falls back to carried roubles when stash payment cannot complete.
- Stash purchases are serialized and transactional.
- Failed profile saves or authorization persistence restore the inventory state.
- Purchase requests accept plain, zlib, and deflate bodies and reject malformed payloads cleanly.
- Server-calculated prices are authoritative; client prices are not trusted.
- The authorization ledger uses atomic saves, backups, corrupt-file preservation, and mutation rollback.
- Duplicate grants, wrong-variant consumption, authorization count drift, and authorizations not disappearing after use were addressed.

### Dashboard And Settings

- The SPT server owns configuration, payment, and authorization-ledger operations.
- The local dashboard is at `https://127.0.0.1:6969/tsc/admin`.
- Remote access is disabled by default and should never be publicly port-forwarded.
- In Fika, host/headless settings are authoritative during the connected raid and sync to clients.
- Dashboard/payment/config work is available in solo SPT; Project Fika is not required for base mod loading.

### Fika Authority Model

Do not move raid damage simulation into the SPT HTTP server. The server handles config, payment, and the ledger; the active Unity/Fika raid-world authority handles gameplay damage.

A-10 executor selection is explicit:

- Single-player: `A10VisualRuntimeExecutor`, original Arys-style authoritative runtime/ballistic path.
- Human Fika host: `A10VisualRuntimeExecutor`, authoritative runtime/ballistic path.
- Fika client: visual-only after authority acceptance; never authoritative damage.
- Dedicated Fika headless: `A10HeadlessDamageExecutor` only while headless mode is `ExperimentalDamageOnly`.
- If no raid authority exists, the request is rejected/refunded. A random client is not elected.

Every networked support request has a `SupportRequestId`. Host/headless in-flight and completed request gates prevent duplicate packets from firing or consuming twice. The ID is carried through A-10 request, accepted support, tracer, and replay context.

Fika clients wait for authority acceptance before starting the A-10 visual pass. Tracer and impact playback is keyed by request ID, seed, and pass and is scheduled against the client-visible firing pass.

Dedicated-headless A-10 remains experimental. Its fallback can use the active EFT health controller locally or Fika damage packets for remote human targets. This is intentionally gated and must not be described as identical to the original human-host ballistic path.

### UAV, Audio, Stability, And Compatibility

- UAV radar defaults to the physical Uplink phone. Hold the configurable radar key (`J` by default) to raise it while walking or sprinting, and release it to restore the previous weapon; the recon timer continues while the phone is stowed. F12 can instead select a persistent requester-local HUD and one of four screen corners.
- The old mismatched corner HUD and its AssetBundle loading remain disabled. HUD mode displays the exact phone radar render texture, while the shared scan/timer backend continues supplying pooled contact snapshots.
- Standard UAV defaults are 480 seconds, 200 m, and 5-second sweeps. Focused Sweep defaults are 90 seconds, 100 m, and 0.75-second sweeps.
- Fika host authority reads the server/dashboard duration before legacy local config, so the requester recon link and loiter aircraft use one lifetime.
- Only one local recon link can be active at a time; overlapping or duplicate activation does not extend or mutate the current link.
- Fika recon links are requester-only. Human hosts do not receive a client's feed, nonrequester clients do not receive it, and dedicated headless hosts create none.
- Phone release is latched until EFT's asynchronous `SpawnController` callback completes, preventing a release-during-equip hand-swap race.
- The horizontal equip remains fully animated for EFT but its renderers are temporarily forced off; they are restored only after the safe upright pose is sampled, or during teardown if the session ends early.
- The UAV radar AssetBundle double/concurrent-load race was fixed.
- The loud A-10 strike flyover was restored in `A10Behaviour.PlayStrikeFlyoverAudio()`.
- UAV loiter audio remains quiet and non-looping; keep these audio paths separate.
- A-10 tracer and marker-anchored impact replay were added for Fika clients.
- `SimpleSpinBlur`, pool, UI/controller, and phone camera teardown null paths were hardened.
- UH-60 extraction trigger cleanup and Fika extraction routing were hardened against stuck black-screen exits.
- Manimal HackerMod phone-bundle coexistence was added by reusing the complete compatible phone bundle set when present.
- Fika integration startup/shutdown and retained duplicate-request state are bounded across repeated raids.

## Important Boundaries

- Preserve the original Arys A-10 path for solo and non-headless human hosts.
- Keep dedicated-headless A-10 separately gated and labeled experimental.
- Never let a Fika client execute authoritative support damage.
- Never make the SPT HTTP/config server simulate raid damage.
- Preserve `RequesterProfileId` and `SupportRequestId` in network contracts.
- Keep requester attribution separate from projectile/damage ownership.
- Do not remove tracer/impact replay while changing damage logic.
- Do not create HUD or phone presentation objects on dedicated headless.
- Do not install old SamSWAT Fire Support or Arys Reloaded alongside TSC; TSC is the derivative replacement.
- Do not touch `D:\SPT` unless the user explicitly asks for a live install.
- Do not publish, tag, upload, or replace release assets unless the user explicitly asks.

## Live SPT Installation

On July 13, 2026, the current UAV phone-radar work was built and installed into `D:\SPT` as one matched local test set:

- Core client DLL: `0.9.8.0`
- Fika Interop DLL: `0.9.8.0`
- Fika DLL: `0.9.8.0`
- Server DLL: `0.9.8.0`
- All four live DLL SHA-256 hashes match their fresh build outputs.
- No duplicate internal-name Core or Fika DLLs are present in the TSC plugin folder.
- The `K` deploy and held `J` radar paths preserve the current raid FOV; their phone framing is still applied and restored independently.
- Held `J` now ignores unrelated movement/sprint keys, and the deploy/radar landscape equip is concealed before the upright reveal.
- The Fika authority duration path now matches the requester link to the loiter aircraft lifetime.
- The customized primary config was preserved with only the UAV values changed to 480 seconds / 200 m / 5 seconds and Focused Sweep to 90 seconds / 100 m / 0.75 seconds.
- The legacy config and admin token were left unchanged.

The complete pre-install client/server copy is at `C:\Users\tylev\Desktop\RaidOps\install_backups\TSC-before-uav-phone-20260713-091746`. The prior zooming Core DLL is at `C:\Users\tylev\Desktop\RaidOps\install_backups\TSC-core-before-nozoom-20260713-093459`. The immediately previous Core, Fika Interop, and client config from before the lifetime/movement/upright refinement are at `C:\Users\tylev\Desktop\RaidOps\install_backups\TSC-before-uav-sync-walk-vertical-20260713-095746`. This is a local test installation, not a packaged or published release, and it still requires live-raid validation.

## Build And Verification

Run builds sequentially because parallel builds can lock shared Core `obj` outputs:

```powershell
dotnet build .\SamSWAT.FireSupport.ArysReloaded.sln -c "SPT-4.0 Release"
dotnet build .\project\SamSWAT.FireSupport.Fika.Interop\SamSWAT.FireSupport.Fika.Interop.csproj -c "SPT-4.0 Release"
dotnet build .\project\SamSWAT.FireSupport.Fika\SamSWAT.FireSupport.Fika.csproj -c "SPT-4.0 Release"
```

All three passed for the published 1.0.8 source. The remaining warnings were existing obsolete InventoryController calls and SPT 4.1 ConfigServer migration/constructor-capture warnings; there were no errors.

The normal build targets contain post-build hooks that deploy into `$(SptDir)` and create a release ZIP. The current project files support `SkipTscDeploy=true` so full builds can run without modifying the live install:

```powershell
dotnet build .\project\SamSWAT.FireSupport\SamSWAT.FireSupport.Core.csproj -c "SPT-4.0 Release" -p:SptDir=D:\SPT\ -p:SkipTscDeploy=true
dotnet build .\project\SamSWAT.FireSupport.Server\SamSWAT.FireSupport.Server.csproj -c "SPT-4.0 Release" -p:SptDir=D:\SPT\ -p:SkipTscDeploy=true
dotnet build .\project\SamSWAT.FireSupport.Fika.Interop\SamSWAT.FireSupport.Fika.Interop.csproj -c "SPT-4.0 Release" -p:SptDir=D:\SPT\ -p:SkipTscDeploy=true -p:BuildProjectReferences=false
dotnet build .\project\SamSWAT.FireSupport.Fika\SamSWAT.FireSupport.Fika.csproj -c "SPT-4.0 Release" -p:SptDir=D:\SPT\ -p:SkipTscDeploy=true -p:BuildProjectReferences=false
```

All four deploy-suppressed builds pass for the current UAV phone-radar work, and their matched 0.9.8.0 outputs are installed locally in `D:\SPT`. Source/output hashes match all four live DLLs. Live validation is still required for walking/sprinting while held, concealed upright reveal timing, rapid hold/release, prior-weapon/FOV restoration, repeated opens, matching phone/aircraft expiry, inventory/death/raid teardown, radar orientation/cadence, and the full Fika requester matrix. `git diff --check` passes.

For any new support feature, test at minimum:

1. Solo purchase, deploy, target, consume, and refund behavior.
2. Human-hosted Fika with one client.
3. Duplicate packet/request ID behavior.
4. Dedicated-headless request rejection when disabled.
5. Dedicated-headless behavior when experimental mode is enabled.
6. Requester-only HUD/UI behavior.
7. Raid end, phone stow, disconnect, and repeated-raid teardown.
8. Full tester ZIP contents, required DLL hashes, two top-level roots, and all eight existing bundles.

## Good Next Feature Areas

These are not implemented yet and should remain separate from shipped behavior:

- Mortar or artillery support using a proven EFT/Fika projectile or shelling API.
- Remote third-person phone/uplink visuals for other Fika players.
- Purpose-built Unity phone animations and improved deploy poses.
- Phone inventory-inspect model/presentation polish.
- Additional aircraft, helicopter, recon, or extraction service types.
- Broader service balancing after the next feature is selected.

Start a new feature by defining its solo authority, human-host authority, headless behavior, client visuals, packet identity/deduplication, payment/authorization behavior, cancellation/refund behavior, and teardown before editing code.

## Ready-To-Paste Next Chat Prompt

```text
Continue development of Tylevo's Tactical Services Control from the current source-of-truth repo:

C:\Users\tylev\Desktop\RaidOps\build_source\Tylevo.TacticalServicesControl-github-1.0.7

Read docs/HANDOFF.md first, then README.md, docs/release-notes-v1.0.8.md, CHANGELOG.md, docs/fika.md, docs/dashboard.md, and docs/known-issues.md.

The published release is v1.0.8 Public Beta, tag/internal version v0.9.8, at commit b7835ea. There is newer uncommitted, compile-verified UAV work: hold J (configurable) raises a live physical-phone radar while movement keys remain usable, release restores the weapon, the landscape equip is concealed before an upright/no-right-arm reveal, the old corner HUD is disabled, the phone and loiter aircraft share the authority duration, and the recon timer keeps running while stowed. Preserve the phone purchase/deploy workflow, transactional payments and authorization ledger, explicit Fika A-10 authority model, SupportRequestId duplicate protection, requester-owned UAV feed, tracer/impact replay, UH-60 extraction fixes, HackerMod compatibility, and teardown safety.

Important architecture boundary: the SPT HTTP server handles config/payment/ledger only. Unity/Fika raid authority handles gameplay damage. Solo and human Fika hosts keep the original Arys A-10 path, clients are visual-only, and dedicated-headless A-10 remains gated and experimental. Do not elect random clients or move raid damage into the HTTP server.

There are uncommitted code and documentation changes for the UAV phone radar plus the prior README.md, docs/forge-description-v1.0.8.md, docs/HANDOFF.md, and docs/roadmap.md work. Do not discard them. A matched 0.9.8.0 local test build is installed in D:\SPT with the latest focused backup at C:\Users\tylev\Desktop\RaidOps\install_backups\TSC-before-uav-sync-walk-vertical-20260713-095746 and the earlier full backup at C:\Users\tylev\Desktop\RaidOps\install_backups\TSC-before-uav-phone-20260713-091746. Do not replace or otherwise modify the live install unless I explicitly ask.

I want to implement more features next. First inspect the handoff and current code around the feature I name, explain the smallest compatible design, then implement it end to end with focused tests and all three required release builds. Do not publish, tag, package, or touch D:\SPT unless I explicitly ask.
```
