# TSC Release Stabilization Execution Plan

Last updated: 2026-07-24
Target: the next public beta after the currently published 1.0.8 release
Status: Phase 0 complete; Phase 1 next

This plan converts the current TSC audit and community bug reports into an ordered implementation and validation sequence. Priority describes risk; phase order also accounts for dependencies. Do not advance to the next phase until the current phase's exit gate is satisfied.

## Sources of Truth

Use these authorities in this order:

1. [GitHub Releases](https://github.com/Tylevo/Tylevo.TacticalServicesControl/releases) is the source of truth for what is publicly shipped. Verify the latest release through GitHub release metadata, its tag, and its attached artifact rather than relying only on a cached rendered page.
2. The release tag and attached ZIP are the source of truth for the exact code/artifact represented by a published version.
3. The GitHub default branch is the source of truth for committed unreleased development after it has been fetched and compared locally.
4. `C:\Users\tylev\Desktop\RaidOps\build_source\Tylevo.TacticalServicesControl-github-1.0.7` is the active local working checkout. Its uncommitted files are development candidates, not evidence of what has shipped.

At the time this plan was written, GitHub's release API reported:

- Release: **Tylevo's Tactical Services Control v1.0.8 Public Beta**
- Tag: `v0.9.8`
- Published: 2026-07-13 03:08:41 UTC
- Artifact: `Tylevo.TacticalServicesControl-v1.0.8-SPT4.0.13.zip`
- Artifact size: 41,236,560 bytes
- Release URL: `https://github.com/Tylevo/Tylevo.TacticalServicesControl/releases/tag/v0.9.8`

Re-query GitHub at the start of Phase 0 and Phase 7. If local documentation or the local checkout disagrees with GitHub about what was published, GitHub release metadata, the release tag, and the attached artifact win. Investigate and document the discrepancy before continuing.

## Operating Rules

- Use the local checkout above as the only working directory for this plan, while keeping its role distinct from the published and committed sources of truth.
- Preserve the existing uncommitted physical-phone UAV work. Do not discard, overwrite, or mix it with unrelated fixes.
- Keep each phase in its own reviewable commit or clearly labeled checkpoint.
- Treat Fika host-global settings and per-profile authorization state as different authority domains.
- Preserve `SupportRequestId`, `RequesterProfileId`, duplicate protection, and the original solo/human-host A-10 execution path.
- Never give a Fika client authoritative raid damage.
- Run deploy-suppressed builds while developing. Do not modify `D:\SPT`, create a release, tag, upload, or publish without explicit approval.
- Record live-test evidence for every supported topology: solo, human Fika host, Fika client, and dedicated headless where applicable.

## Execution Order

| Phase | Priority | Outcome |
| --- | --- | --- |
| 0 | Release safety | Preserve the current UAV work and establish a reproducible baseline |
| 1 | P1 | Fix authorization hydration and server-ledger consumption |
| 2 | P1 | Make Fika request acceptance, consumption, commit, and refund transactional |
| 3 | P1 | Complete the physical-phone UAV live acceptance matrix |
| 4 | P2 | Correct Extraction and Priority Exfil timing configuration |
| 5 | P2 | Add regression coverage and complete build integration |
| 6 | Release safety | Reconcile versions, documentation, configuration, and the working tree |
| 7 | Release gate | Produce and validate a clean release candidate |

## Phase 0 - Preserve Work and Establish the Baseline

### Goal

Protect the current physical-phone UAV changes and prove that the starting point is understood before changing authorization, Fika, or extraction behavior.

### Tasks

- [x] Query GitHub Releases and record the latest release name, tag, publication state, timestamp, target commit, and asset list.
- [x] Fetch the release tag and default branch, then compare them with the local committed base.
- [x] Download or inspect the published artifact and record its SHA-256, size, roots, entry count, and included component versions.
- [x] Capture the complete working-tree status and diff.
- [x] Separate the existing UAV work from later bug fixes with a branch, checkpoint commit, or other recoverable snapshot.
- [x] Confirm the committed base, product display version, assembly version, and target SPT version.
- [x] Confirm the intended local reference paths without copying proprietary EFT, SPT, Fika, or WTT assemblies into the repository.
- [x] Run `git diff --check`.
- [x] Run all four deploy-suppressed builds: Core, Server, Fika Interop, and Fika bootstrap.
- [x] Record build results and any pre-existing warnings.
- [x] Identify the authoritative package entry points and resolve whether release archives intentionally contain only `BepInEx` and `SPT` roots or also root documentation.

### Exit Gate

- The published baseline is tied to verified GitHub release metadata, its tag, and its attached artifact.
- The current UAV work is recoverable.
- The working changes are attributable to a known checkpoint.
- All four projects build, or every baseline failure is documented before new code is written.
- No live installation or release artifact was modified.

### Completion Evidence - 2026-07-24

- GitHub release `v0.9.8`, local tag `v0.9.8`, `origin/main`, and the original local `main` all resolve to commit `b7835ea7995fef08ab7c95cfe8c9bf8af2be6c0c`.
- The published v1.0.8 asset is 41,236,560 bytes with SHA-256 `C3C0390CC6641E5F82C99C3E57D8AEDA358FD0278E667E6616E53963EB2FCB8D`.
- The published ZIP contains 207 entries: 172 files and 35 directory entries. Its only top-level roots are `BepInEx/` and `SPT/`.
- The package contains Core, Server, Fika Interop, and Fika bootstrap DLLs at assembly version `0.9.8.0`, plus eight Unity asset bundles.
- The local development work is preserved on branch `codex/tsc-stabilization-20260724` in five separate checkpoint commits:
  - `89f1fdc` - physical-phone UAV implementation and authority timing;
  - `5fcc718` - deploy-suppressed build guards;
  - `24a5846` - physical-phone UAV workflow documentation;
  - `5dc254e` - development handoff and stabilization plan;
  - `03aa54b` - isolated, unvalidated Forge-description draft.
- `git diff --check v0.9.8..HEAD` passes, and the checkpoint branch is clean.
- Core build: succeeded with 28 warnings and 0 errors.
- Server build: succeeded with 9 warnings and 0 errors.
- Fika Interop build: succeeded with 0 warnings and 0 errors.
- Fika bootstrap build: succeeded with 0 warnings and 0 errors.
- The live `D:\SPT` DLL timestamps remain dated 2026-07-13, confirming that the verification builds did not deploy.
- The published artifact and current ZIP target establish `BepInEx/` plus `SPT/` as the package-root contract. The root-document requirement in `BUILDING.md` is stale and must be corrected in Phase 6.
- Packaging still reads from live `SptDir` folders and updates an existing archive in place. Phase 7 must replace that with a clean, allowlisted stage to prevent live-state or stale-entry leakage.

## Phase 1 - Authorization Hydration and Ledger Consumption

### Goal

Eliminate the empty-tablet and all-services-maxed soft-lock while ensuring that a deployed persistent authorization is consumed exactly once from the correct player's server ledger.

### Implementation Tasks

- [ ] Split host-global configuration authority from per-profile state synchronization.
- [ ] Keep authenticated per-profile refresh active for Fika clients, either through `/tsc/config` or a dedicated player-state endpoint.
- [ ] Synchronize at least:
  - authorization counts;
  - purchase-persistence state;
  - stash balance when required by the selected payment mode.
- [ ] Keep Fika host values authoritative for shared prices, service availability, timing, and other raid-global settings.
- [ ] Decouple player-state hydration from the hidden legacy `Use server config URL` toggle, or retire the obsolete toggle.
- [ ] Define whether response authorization data is present and authoritative. Prefer a nullable value or explicit `AuthorizationsIncluded` flag so "omitted" and "authoritative empty" are distinguishable.
- [ ] Apply included ledger state before returning from `AuthorizationLimitReached` and other responses that intentionally carry it.
- [ ] Do not place per-player authorization counts in the broadcast Fika host-settings packet.
- [ ] Refresh the deploy menu if hydration completes while the tablet is already open.
- [ ] Ensure persistent deployment uses the server-backed begin/commit/refund path rather than decrementing only the client mirror.

### Validation Matrix

- [ ] Solo player starts a new raid with stored credits and sees them without purchasing.
- [ ] Human Fika host starts with stored credits and sees them without purchasing.
- [ ] Fika client joins with stored credits and sees them without purchasing.
- [ ] Two Fika clients receive only their own ledger counts.
- [ ] A player with every service at the storage limit can immediately deploy an owned service.
- [ ] A limit-denied purchase hydrates the tablet rather than leaving it locked.
- [ ] An authoritative empty ledger clears stale client state without creating phantom credits.
- [ ] Purchase -> deploy -> consume -> commit decrements the server ledger once.
- [ ] Cancellation or failed execution refunds once when required by the configured persistence policy.
- [ ] Reconnect and next-raid hydration preserve the decremented count.
- [ ] A legacy saved `Use server config URL = false` value cannot suppress player-state hydration.
- [ ] Opening the deploy tablet during initial synchronization either waits safely or updates without requiring it to be reopened.

### Exit Gate

- No topology requires a purchase to reveal previously owned authorizations.
- A maxed ledger cannot soft-lock deployment.
- Client display, server ledger, and persistence mode agree after purchase, deployment, cancellation, reconnect, and raid transition.

## Phase 2 - Transactional Fika Request Acceptance and Refunds

### Goal

Make transport delivery, authority acceptance, execution start, authorization consumption, commit, and refund explicit and idempotent.

### Implementation Tasks

- [ ] Document the current state machine for every networked support request.
- [ ] Treat packet send success only as transport delivery, not gameplay acceptance.
- [ ] Add or confirm an authority response keyed by `SupportRequestId` that communicates accepted or rejected state to the requester.
- [ ] Start requester-side visuals and advance the payment lifecycle only after the authoritative transition required by the configured `ConsumeOn` policy.
- [ ] Do not broadcast an accepted support event until the selected host/headless executor has accepted the request.
- [ ] Return a deterministic rejection when no valid executor exists or the relevant headless feature is disabled.
- [ ] Refund on rejection, timeout, cancellation, or executor-start failure when the authorization has already entered a pending/consumed state.
- [ ] Make acceptance, commit, refund, replay, and duplicate packets idempotent.
- [ ] Bound pending requests and clear them across death, disconnect, raid end, and repeated raids.
- [ ] Preserve the original solo and human-host A-10 ballistic path.

### Validation Matrix

- [ ] Solo support requests execute and consume once.
- [ ] Human-host requests execute and consume once.
- [ ] Fika-client requests wait for host/headless acceptance.
- [ ] Duplicate request packets neither execute nor consume twice.
- [ ] Lost or duplicate acceptance packets converge on one final state.
- [ ] Unavailable or disabled dedicated-headless execution rejects and refunds cleanly.
- [ ] Requester disconnect, death, or raid teardown cannot strand a pending authorization.
- [ ] A-10 single pass, A-10 double pass, both extraction services, UAV Recon, and Focused Sweep follow the intended lifecycle.

### Exit Gate

- No authorization is lost merely because a packet was sent.
- No accepted request can execute or consume twice.
- Every rejection and timeout reaches a deterministic, observable final state.

## Phase 3 - Physical-Phone UAV Live Acceptance

### Goal

Validate the existing uncommitted physical-phone UAV work in live raids and correct only observed failures.

### Solo Tests

- [ ] Standard UAV and Focused Sweep purchase, deployment, activation, expiry, and repeat use.
- [ ] Hold the configured radar key while stationary, walking, sprinting, aiming, and changing direction.
- [ ] Release during the asynchronous phone equip and verify the previous weapon restores.
- [ ] Rapid press/release and repeated open/close cycles do not duplicate or strand the phone.
- [ ] Raid FOV, weapon state, hand state, camera state, and phone renderers restore correctly.
- [ ] Inventory, death, extraction, raid end, and scene teardown leave no overlay, camera, renderer, or input state behind.
- [ ] Radar orientation, scan cadence, range, contact pooling, and countdown remain correct.
- [ ] Phone-link duration and loiter-aircraft duration expire together.
- [ ] Stowing the phone does not pause or extend the recon contract.

### Fika Tests

- [ ] Human-host requester sees only the host's feed.
- [ ] Fika-client requester sees the feed while the host and other clients do not.
- [ ] Two clients requesting at different times remain isolated by requester identity.
- [ ] A dedicated headless host creates no phone presentation or radar UI.
- [ ] Disconnect, death, and raid teardown remove requester-owned recon state.
- [ ] Duplicate or overlapping activation cannot extend or mutate the active link.
- [ ] Host-synchronized duration, range, and scan cadence match the dashboard.

### Exit Gate

- The physical phone is stable through the full input and teardown matrix.
- Requester isolation holds for human-host, client, and dedicated-headless sessions.
- Recon timing is server/host-authoritative and does not drift between the phone and aircraft.

## Phase 4 - Extraction Timing Configuration

### Goal

Make the dashboard contract match runtime behavior for standard Extraction and Priority Exfil.

### Product Decision

The current dashboard promises separate extraction-zone countdowns. Unless the product intentionally wants one shared hold time, implement separate values. If a shared value is intentional, remove the dead priority field and label the remaining setting as common.

### Implementation Tasks

- [ ] Make extraction-zone countdown tuning support-type-aware.
- [ ] Pass both `extraction.extractTimeSeconds` and `priorityExfil.extractTimeSeconds` through the server-config client.
- [ ] Carry both countdowns through the Fika settings packet and its serialization.
- [ ] Account for packet compatibility or require a matched client set for the new protocol.
- [ ] Initialize `HeliExfiltrationPoint` with `Extract` or `PriorityExfil`.
- [ ] Use the matching countdown when the player enters or re-enters the zone.
- [ ] Wire `extraction.dispatchDelaySeconds`, currently represented in the dashboard but hardcoded to eight seconds at runtime, or remove the misleading field.
- [ ] Preserve the already-correct per-service helicopter wait windows and animation-speed multipliers.
- [ ] Validate `waitTimeSeconds >= extractTimeSeconds` and return an actionable dashboard/config error for invalid combinations.
- [ ] Replace fixed completion estimates with lifecycle/event-driven completion, or make the estimate account for configured animation speed.

### Validation Matrix

- [ ] Standard and priority countdowns can be configured to visibly different values in solo.
- [ ] Standard and priority countdowns remain distinct for a Fika client receiving host settings.
- [ ] Leaving and re-entering the zone resets the correct service-specific value.
- [ ] Standard and priority dispatch delays match their configured values.
- [ ] Invalid wait/countdown combinations are rejected before a paid service becomes impossible.
- [ ] Default behavior remains unchanged unless an intentional migration is documented.

### Exit Gate

- Every exposed extraction timing field either affects gameplay as labeled or has been removed.
- Priority Exfil cannot silently inherit the standard extraction countdown.
- A valid configuration cannot make the helicopter leave before the extraction countdown can complete.

## Phase 5 - Regression Coverage and Build Integration

### Goal

Turn the repaired behavior into repeatable checks so later phone, Fika, payment, and extraction work cannot silently reintroduce it.

### Tasks

- [ ] Add focused tests for authorization-response presence semantics.
- [ ] Add ledger lifecycle tests covering purchase, limit denial, begin, commit, refund, reconnect, and two-profile isolation.
- [ ] Add request-state-machine tests for acceptance, rejection, timeout, duplicate packets, and teardown.
- [ ] Add Fika settings-packet round-trip tests, including both extraction timers and dispatch values.
- [ ] Add tuning-precedence tests for local, server, and host-synchronized values.
- [ ] Add extraction-trigger timer initialization/reset tests.
- [ ] Add config validation tests for wait/countdown combinations.
- [ ] Add package-layout allowlist checks.
- [ ] Add Fika Interop and the Fika bootstrap project to the normal solution/build verification path.
- [ ] Provide one documented local verification command or script that builds all required projects with deployment disabled.
- [ ] Add CI checks that do not require redistributing proprietary assemblies. Document any full-build checks that must remain local.
- [ ] Run `git diff --check` as part of the verification path.

### Exit Gate

- The confirmed authorization and extraction regressions fail under test when their fixes are removed.
- All four projects participate in the documented verification path.
- Package-layout and repository hygiene checks are repeatable.

## Phase 6 - Version, Documentation, and Repository Reconciliation

### Goal

Convert the accumulated work into an intentional, reviewable next-release state.

### Tasks

- [ ] Review every modified and untracked file and assign it to a phase/commit.
- [ ] Remove generated, obsolete, or accidental files without discarding intentional user work.
- [ ] Choose the next public display version, internal assembly version, and release/tag version.
- [ ] Update version metadata consistently across build properties, plugin metadata, server metadata, config templates, and release filenames.
- [ ] Update `CHANGELOG.md`.
- [ ] Update `README.md`, `docs/fika.md`, `docs/dashboard.md`, `docs/known-issues.md`, and `docs/roadmap.md`.
- [ ] Update the handoff with the final authority model, validation evidence, and remaining limitations.
- [ ] Create release notes and a Forge description for the chosen version.
- [ ] Reconcile the package-root contradiction between current build documentation and the verified 1.0.8 archive.
- [ ] Confirm all shipped configuration templates match server defaults and dashboard ranges.
- [ ] Confirm dedicated-headless A-10 remains clearly labeled experimental.
- [ ] Confirm no secrets, local paths, profiles, logs, proprietary binaries, or temporary artifacts are tracked.

### Exit Gate

- The working tree contains only intentional release changes.
- Version numbers and release naming are internally consistent.
- Documentation describes tested behavior rather than planned behavior.
- Known limitations are explicit.

## Phase 7 - Clean Release Candidate and Final Acceptance

### Goal

Produce a reproducible release candidate from a clean source state and prove that it is safe to hand to testers.

### Build and Package Tasks

- [ ] Build from a clean worktree or clean clone at the intended release commit.
- [ ] Run all four deploy-suppressed release builds.
- [ ] Run the complete automated verification suite.
- [ ] Stage the package from an explicit allowlist.
- [ ] Verify the final archive roots against the resolved package contract.
- [ ] Verify required Core, Server, Fika Interop, Fika bootstrap, assets, config, and metadata files are present and version-matched.
- [ ] Verify WTT Common Lib, EFT/SPT/Fika proprietary assemblies, profiles, logs, caches, source prompts, and local paths are absent.
- [ ] Extract the archive into an empty temporary directory and validate its layout.
- [ ] Calculate and record the archive SHA-256, size, entry count, and asset-bundle count.
- [ ] Perform a clean-install smoke test only after explicit approval to modify a test SPT installation.

### Final Live Acceptance

- [ ] Repeat the Phase 1 persistent-authorization matrix.
- [ ] Repeat the Phase 2 authority/commit/refund matrix.
- [ ] Repeat the critical Phase 3 UAV requester and teardown matrix.
- [ ] Repeat the Phase 4 standard/priority extraction timing matrix.
- [ ] Verify clean server and client logs for startup, purchase, deployment, extraction, teardown, and repeated raids.
- [ ] Confirm upgrade behavior from the published 1.0.8 configuration and ledger format.

### Release Gate

- The release candidate is reproducible from the recorded commit.
- All P1 issues are closed with evidence.
- P2 issues are closed or explicitly accepted and documented.
- The archive passes content, clean-install, and live-smoke validation.
- Publishing, tagging, uploading, replacing assets, or changing Forge remains a separate user-approved action.

## Standard Development Verification

Use the repository's configured reference paths. Keep deployment disabled:

```powershell
dotnet build .\project\SamSWAT.FireSupport\SamSWAT.FireSupport.Core.csproj -c "SPT-4.0 Release" "-p:SptDir=D:/SPT/" "-p:SptSharedAssembliesDir=C:/Users/tylev/Desktop/RaidOps/SPT Assemblies/" -p:SkipTscDeploy=true
dotnet build .\project\SamSWAT.FireSupport.Server\SamSWAT.FireSupport.Server.csproj -c "SPT-4.0 Release" "-p:SptDir=D:/SPT/" "-p:SptSharedAssembliesDir=C:/Users/tylev/Desktop/RaidOps/SPT Assemblies/" -p:SkipTscDeploy=true
dotnet build .\project\SamSWAT.FireSupport.Fika.Interop\SamSWAT.FireSupport.Fika.Interop.csproj -c "SPT-4.0 Release" "-p:SptDir=D:/SPT/" "-p:SptSharedAssembliesDir=C:/Users/tylev/Desktop/RaidOps/SPT Assemblies/" -p:SkipTscDeploy=true -p:BuildProjectReferences=false
dotnet build .\project\SamSWAT.FireSupport.Fika\SamSWAT.FireSupport.Fika.csproj -c "SPT-4.0 Release" "-p:SptDir=D:/SPT/" "-p:SptSharedAssembliesDir=C:/Users/tylev/Desktop/RaidOps/SPT Assemblies/" -p:SkipTscDeploy=true -p:BuildProjectReferences=false
git diff --check
```

These commands may read locally installed references but must not deploy into or modify the live SPT installation.
