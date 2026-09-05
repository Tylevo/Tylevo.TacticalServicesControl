# Tylevo Tactical Services Control Development Handoff

Last updated: 2026-07-27

> **Historical SPT 4.0.13 snapshot — superseded.** This handoff preserves the
> v1.0.8/v1.1.0-beta.1 stabilization and packaging evidence as it existed on
> the date above. It is not the active port guide. For the current SPT 4.1.4
> target, branch, dependency status, build gates, tester filename, and
> `SPT_Runtime/user/mods` package layout, use
> `docs/release-notes-v1.3.8.md`, `docs/validation/v1.3.8.md`, and
> `docs/port/SPT-4.1.4-PORT-LOG.md`. Do not apply the 4.0.13 target,
> archive names, `SPT/user/mods` paths, or commands below to the 4.1.4 tester.
> The remaining historical details are intentionally retained unchanged.

## Start Here

GitHub Releases, the matching tag, and the attached archive are authoritative
for published TSC builds. The active development branch is
`codex/tsc-stabilization-20260724`; its Phase 6 checkpoint is
`25fdaeb3fc65d6352408285f4c970bbde7b8006c`. The Phase 7 packaging-tooling
checkpoint is the commit containing this handoff.

Read these files before changing release behavior:

1. `docs/HANDOFF.md`
2. `docs/release-stabilization-plan.md`
3. `README.md`
4. `CHANGELOG.md`
5. `docs/release-notes-v1.1.0.md`
6. `docs/fika.md`
7. `docs/dashboard.md`
8. `docs/known-issues.md`

Do not publish, tag, upload, replace a release asset, or modify a live SPT
installation without explicit user approval.

## Release Identity

Published source of truth:

- Display release: **Tylevo's Tactical Services Control v1.0.8 Public Beta**
- Git tag and internal version: `v0.9.8` / `0.9.8.0`
- Release commit: `b7835ea7995fef08ab7c95cfe8c9bf8af2be6c0c`
- Archive: `Tylevo.TacticalServicesControl-v1.0.8-SPT4.0.13.zip`
- SHA-256: `C3C0390CC6641E5F82C99C3E57D8AEDA358FD0278E667E6616E53963EB2FCB8D`
- Size: `41,236,560` bytes
- Archive roots: `BepInEx/` and `SPT/`
- Release status: published, non-draft, non-prerelease

The anonymous GitHub releases HTML can omit the newer entries. An authenticated
GitHub release query on 2026-07-27 confirmed v1.0.8 and its matching asset.

Next-release identity:

- Display: **Tylevo's Tactical Services Control v1.1.0 Beta 1**
- MSBuild, plugin, server, assembly, and file version: `1.1.0` / `1.1.0.0`
- Published tester tag: `v1.1.0-beta.1`
- Reserved final tag: `v1.1.0`
- Intended archive: `Tylevo.TacticalServicesControl-v1.1.0-SPT4.0.13.zip`
- Target: SPT `4.0.13`

Beta 1 is published as a separate GitHub prerelease; no final `v1.1.0` release
or Forge update exists yet. All four runtime DLLs must be rebuilt and
distributed together because the Fika assemblies reference the Core/Interop
assembly versions.

## Stabilization Work Completed

### Phase 1 - Persistent Authorization State

- Authenticated snapshots distinguish omitted player state from an
  authoritative empty ledger.
- Stored counts hydrate at raid start and can refresh an already-open deploy
  phone.
- A limit-denied purchase can still return the authoritative ledger state.
- Persistent use goes through server-backed begin, commit, and refund rather
  than decrementing only a client mirror.
- Player authorization counts remain per-profile and are not broadcast in
  host-global Fika settings.

### Phase 1A - Pre-Raid Store

- **TSC UPLINK** appears under **Records**, or under **Character** when Records
  is unavailable, without overlapping or displacing menu entries incorrectly.
- The standalone store uses the authenticated SPT session and does not create
  raid hands, cameras, or phone objects.
- It displays server prices, availability, stash balance, stored counts, and
  the configured storage limit.
- Purchases require an explicit confirmation with service, price, and
  projected balance.
- Request IDs make retries idempotent; quote changes fail before debit.
- The Dashboard button derives `/tsc/admin` from the active SPT backend host.

### Phase 2 - Transactional Fika Requests

- Transport delivery is no longer treated as gameplay acceptance.
- Requests, acceptance, rejection, commit, refund, tracer, and replay state are
  keyed by `SupportRequestId`.
- Requester visuals and payment settlement wait for the raid authority's
  accepted transition.
- Rejection, timeout, cancellation, and executor-start failure converge on one
  idempotent settlement path.
- Solo and human-host A-10 retain the original Arys runtime/ballistic executor.

### Phase 3 - UAV Phone And Requester Ownership

- UAV duration, range, scan cadence, requester identity, and parent request
  identity are authority-originated.
- The requester phone link and loiter aircraft use one contract lifetime.
- Phone equip/release, release during asynchronous equip, death, raid end,
  render textures, input state, and weapon restoration have explicit cleanup.
- `Phone` mode raises the physical Uplink while the configurable radar key is
  held. `HUD` mode renders only the scanner square in a selected corner.
- Dedicated headless hosts and non-requesters create no local recon UI.
- `K` and `J` reveal the phone directly in its upright presentation after the
  concealed EFT equip transaction.

### Phase 4 - UH-60 Timing And Cargo

- Standard Extraction retains its dispatch, wait, extraction-countdown, and
  animation-speed contract.
- UH-60 Cargo Transfer replaces the Priority Exfil product while keeping its
  enum value, saved `PriorityExfil` key, credits, and artwork compatible.
- Cargo keeps the legacy slot's dispatch, wait, and speed values but never
  starts an extraction countdown or ends the raid.
- The requester loads cargo through EFT's native mid-raid transfer screen.
  Standard Extraction never exposes that interaction.
- After EFT verifies a paid item move in its persistent transfer grid, Cargo
  immediately uses the successful-pickup departure. Cancel and payment failure
  preserve the remaining wait; human-host Fika publishes one accepted-request
  departure packet so observer visuals leave once with the host.
- Successfully marked cargo is delivered by an isolated **UH-60 Pilot**
  messenger. The stock **BTR Driver** identity and unmarked native deliveries
  remain untouched; missing/rejected markers and routing failures fall back to
  stock BTR delivery so an accepted item tree is not intentionally discarded.
- Cargo markers are authenticated, profile/session-bound, durable across an
  SPT restart, and route connected item trees as one unit.
- Non-host Fika clients fail closed before Cargo purchase, authorization
  consumption, or dispatch. Solo SPT and a human Fika host remain supported.
- Completion timing is derived from the configured service snapshot instead of
  one fixed estimate.

### Additional Candidate Features

- Server-authoritative RUB, USD, and EUR payment across carried cash, stash
  debit, pre-raid purchase, journal retry/replay, and Fika synchronization.
- Currency changes do not convert numeric prices; accepted/prepared requests
  retain the currency in which they were quoted.
- A scanner-only corner HUD alternative to the physical radar phone.
- Main-menu styling, layout, confirmation, and stash/control alignment approved
  through iterative user screenshots.

### Phase 5 - Regression And Build Integration

- A proprietary-free regression runner covers authorization presence, ledger
  lifecycle, two-profile isolation, request acceptance/rejection/timeout,
  duplicate packets, teardown, settings round trips, tuning precedence, and
  extraction timing.
- Core, Server, Fika Interop, Fika bootstrap, and regression projects are all
  in the normal solution path.
- CI checks JSON and JavaScript syntax, runtime source wiring, solution
  mappings, package layout, tracked-file hygiene, whitespace, and tests without
  redistributing EFT/SPT/Fika/WTT assemblies.
- Local verification builds the full solution with deployment disabled.

### Phase 6 - Release Reconciliation

- One v1.1.0 identity now drives public naming and all binary metadata.
- Published v1.0.8 release notes are preserved; v1.1.0 release/Forge drafts are
  separate and explicitly unpublished.
- The release archive contract is exactly two top-level roots:
  `BepInEx/` and `SPT/`.
- The obsolete legacy config template is no longer shipped, while migration of
  an existing legacy filename remains supported.
- The canonical config source, server defaults, and dashboard ranges have been
  reconciled.
- User docs distinguish implemented/automated behavior from open live
  acceptance work.

### Phase 7 - Packaging Readiness

- GitHub release metadata was re-queried and the local public v1.0.8 archive
  was verified against its published 41,236,560-byte SHA-256 identity.
- .NET SDK `9.0.314` is pinned for both local and CI release verification.
- Package manifest schema 3 is a closed inventory: 154 reviewed source files,
  four fresh build outputs, eight pinned Unity bundles, and two third-party
  notice copies, for exactly 168 installer files.
- The schema-3 reference config is tracked outside deployable source trees.
  A clean install creates defaults on first server start, while an overlay
  upgrade leaves the administrator's existing config available for migration.
- The eight bundles are imported only from the verified public v1.0.8 archive;
  their paths, sizes, and hashes are pinned. Live SPT files and obsolete
  package-stage folders are not release inputs.
- Clean-build evidence binds the exact Git commit/tree, SDK, proprietary
  reference hashes, output paths, versions, and hashes before packaging.
- The release packager requires that evidence, creates a new deterministic
  archive, validates the stage and a fresh extraction, and writes an external
  per-file content-evidence sidecar.
- The former update-in-place 7-Zip release targets were removed. Normal
  developer deployment remains separately guarded by `SkipTscDeploy`.
- Phase 2-4 and multi-currency tester matrices were reconciled with the current
  schemas and request identity contract. Their live rows remain open.

## Authority Boundaries

The SPT HTTP server owns configuration, authenticated profile payment, and the
persistent authorization ledger. It must not simulate raid damage.

The active Unity/Fika raid-world authority owns gameplay:

- Solo: original Arys visual/runtime A-10 executor and authoritative ballistics.
- Human Fika host: the same original authoritative path.
- Fika client: accepted visuals only; never authoritative support damage.
- Dedicated Fika headless: separately gated experimental damage executor.
- No valid executor: deterministic rejection and refund; never elect a random
  client.

Keep requester attribution separate from projectile/damage ownership. Preserve
`RequesterProfileId` and `SupportRequestId` across every relevant packet.

Dedicated-headless A-10 remains **experimental**. Do not describe it as
equivalent to the human-host ballistic path, and do not remove the gate until
matched-version live testing proves execution, duplicate protection,
authorization settlement, damage attribution, and teardown.

## Configuration And Upgrade Contract

- Product version: `1.1.0`; config schema: `3`; ledger schema: `5`.
- Product-version changes do not force data-schema changes.
- `config/tsc-config.json` is not shipped in the installer. The server creates
  schema-3 defaults only when the canonical and legacy config files are absent.
- An overlay upgrade preserves an existing canonical config instead of
  replacing it with release defaults.
- Existing `config/raidops-firesupport.json` files migrate when the canonical
  file is absent.
- Existing schema-less/schema-1 extraction settings migrate to the historical
  effective standard dispatch delay.
- Pre-currency configs migrate to RUB without converting their saved numeric
  prices.
- Current-schema invalid currency values fail closed.
- Back up profiles and custom `tsc-config.json` before acceptance testing.

Upgrade behavior from the published v1.0.8 config and ledger is still a Phase 7
test gate.

## Verification Evidence

Completed:

- The current 39-test regression suite passed, including published-v1.0.8
  config and ledger migration coverage.
- The preceding 35-test baseline passed 20 consecutive test-suite
  repetitions: 700/700 executions.
- The deploy-suppressed `SPT-4.0 Release` solution build passed for all five
  projects with zero errors.
- Remaining warnings are known obsolete InventoryController usage and SPT 4.1
  migration/constructor-capture warnings.
- Phase-specific validation matrices exist under `docs/validation/`.

User-reported smoke coverage:

- Solo SPT is generally working.
- The pre-raid store and confirmation flow work in the tested solo profile.
- Main-menu placement and styling were visually approved.
- The direct upright `K`/`J` phone draw fix was confirmed.

Still open:

- Two independent detached clean builds with matching four-DLL hashes, followed
  by two matching unpublished v1.1.0 RC archives.
- Human-host and Fika-client persistent-authorization matrices.
- Two-client per-profile isolation.
- Duplicate/lost/late Fika acceptance and settlement in live raids.
- Full requester-owned UAV Phone/HUD lifecycle and teardown matrix.
- Standard extraction and host-only cargo-transfer timing in solo and Fika.
- UH-60 Pilot versus native BTR sender isolation, mixed-package tree
  partitioning, restart persistence, wrong-profile/auth rejection, fallback,
  and duplicate/loss accounting.
- RUB/USD/EUR live carried/stash boundary cases.
- Dedicated-headless duplication, authorization, damage, and teardown testing.
- Clean-install and v1.0.8-upgrade smoke tests from the final v1.1.0 archive.

Do not convert an automated pass into a claim of live multiplayer acceptance.

## Package Contract

The v1.1.0 archive must:

- Be created from a clean source state and a new empty staging directory.
- Contain only top-level `BepInEx/` and `SPT/`.
- Install into:
  - `BepInEx/plugins/Tylevo.TacticalServicesControl/`
  - `SPT/user/mods/Tylevo.TacticalServicesControl/`
- Contain exactly four matched TSC DLLs and eight expected asset bundles.
- Exclude `.gitkeep`, symbols, logs, profiles, storage, caches, build outputs,
  source prompts, old archives, and dependency assemblies.
- Keep WTT Common Lib, Fika, EFT/SPT, UnityToolkit, and other proprietary or
  separately distributed dependencies out of the archive.
- Be validated both as a stage directory and again after ZIP extraction.

Root-level README, changelog, license, and release-note files are not part of
the installer. Publish those through the repository, GitHub release, and Forge.

Manifest schema 3 lists every installer file. Source validation fails when a
reviewed file is missing or untracked, when an unreviewed extra appears below a
`CopyToOutput` tree, or when an expected exclusion changes. The four DLLs come
only from an exact clean-build evidence record. The eight bundles come only
from the verified public v1.0.8 asset and must match their pinned size and
SHA-256. The deterministic packager stages into a new external directory,
creates rather than updates the ZIP, validates both stage and extraction, and
requires per-file path, size, and hash equality.

## Verification Commands

CI-safe:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\verify-ci.ps1
```

Full local, deploy-suppressed, with release-build evidence:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\verify-local.ps1 `
  -SptDir "C:\Path\To\SPT" `
  -SptSharedAssembliesDir "C:\Path\To\SPT Assemblies" `
  -EvidencePath "C:\External\Evidence\build.json"
```

Package source/layout:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-PackageLayout.ps1 -ValidateSourceInputs
```

Release identity:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-ReleaseMetadata.ps1
```

Closed deterministic package:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\New-ReleasePackage.ps1 `
  -BaselineAssetArchive "C:\External\Tylevo.TacticalServicesControl-v1.0.8-SPT4.0.13.zip" `
  -BuildEvidencePath "C:\External\Evidence\build.json" `
  -OutputDirectory "C:\External\Package"
```

## Phase 7 Next Steps

1. Commit and review the Phase 7 manifest, evidence, SDK, and packaging tools.
2. Create two detached clean worktrees at that exact commit.
3. Run deploy-suppressed `verify-local.ps1 -EvidencePath` in each worktree.
4. Require identical hashes for all four freshly built `1.1.0.0` DLLs.
5. Package independently from each worktree into new external directories and
   require identical archive hashes.
6. Retain the primary unpublished archive, build evidence, package evidence,
   component versions/hashes, archive size/counts, and clean logs.
7. Run the published-v1.0.8 upgrade and clean-install smoke tests only with
   explicit approval to modify a test SPT installation.
8. Complete the critical solo/Fika/headless matrices or clearly mark remaining
   blockers before requesting approval to publish.
