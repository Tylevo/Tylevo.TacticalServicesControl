# Building

This repository does not include proprietary EFT or SPT assemblies. Provide local references from your own SPT install.

## Requirements

- .NET SDK 10.0.201, pinned by `global.json` and the CI workflow with SDK
  roll-forward disabled.
- SPT 4.1.4 reference assemblies from a clean installation.
- WTT Client CommonLib and WTT Server CommonLib `3.0.6`, installed separately
  from the official `v3.0.6` release, including its serialization prepatcher.
- UnityToolkit `2.0.1` rebuilt for SPT 4.1 from tag/commit
  `3c27a9798dc4396ca0b3dc765448a4221ff3007b` with the documented SPT 4.1
  configuration, reference, deploy-guard, and player-loop target adaptations.
  The unmodified pre-4.1 binary is not a substitute.
- Project Fika client `2.4.2` from the official `v2.4.2` release when building
  the optional Fika interop. Multiplayer validation also requires its
  compatible server component.

Exact dependency assembly identities, byte lengths, and SHA-256 values are in
`docs/port/SPT-4.1.4-PORT-LOG.md`. The initial UnityToolkit input is the existing
SPT 4.1.2 rebuild; its 4.1.4 compatibility still needs validation. Historical
4.1.2 pins and results remain in `docs/port/SPT-4.1-PORT-LOG.md`; fresh 4.1.4
compile and runtime evidence is required.

TSC references WTT Common Lib from the local SPT dependency install. Do not copy WTT Common Lib source or binaries into the TSC source tree or release archive.

## Reference Paths

Create a local `Shared.User.props` or pass MSBuild properties:

- `SptDir`: path to a local SPT 4.1.4 root used for reference lookup and
  optional post-build output. The server runtime is below `SPT_Runtime/`.
- `SptSharedAssembliesDir`: folder containing the versioned SPT reference
  assemblies. For `SPT-4.1 Release`, `410x/hollowed.dll` is the compile-only
  `Assembly-CSharp` reference produced by the matching SPT.Modules
  `Shared/Hollowed` project. It must match the exact EFT build and must never be
  shipped in the mod. SPT 4.1.4 changes serialized fields despite retaining
  EFT build 40743, so the old 4.1.2 reference cannot certify this update.
  Record the exact 4.1.4 assembly identities, provenance, and SHA-256 values in
  `docs/port/SPT-4.1.4-PORT-LOG.md`. Historical reference hashes remain in
  `docs/port/SPT-4.1-PORT-LOG.md`.

Use forward slashes or quote paths carefully when paths contain spaces.

## Verification Layers

TSC has two intentionally separate verification layers.

### CI-safe verification

The CI-safe layer requires .NET 10, Node.js, Git, and PowerShell, but no EFT,
SPT, Fika, UnityToolkit, or WTT assemblies:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\verify-ci.ps1
```

It runs the zero-dependency regression runner, checks changed-line whitespace,
validates the solution and deploy guards, parses shipped JSON, checks dashboard
JavaScript syntax, verifies release/version metadata, checks tracked-file
hygiene, and validates the declarative package inputs. GitHub Actions runs this
same command. CI must never download, cache, upload, or redistribute
proprietary reference assemblies.

When called by CI, `-BaseSha` and `-HeadSha` make the whitespace check cover the
entire pushed or pull-request range. A local invocation checks both unstaged and
staged changes.

### Full local verification

The full runtime build remains local because all four runtime projects require
assemblies from a legally obtained local install:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\verify-local.ps1 `
  -SptDir "C:\Path\To\SPT" `
  -SptSharedAssembliesDir "C:\Path\To\SPT Assemblies"
```

`verify-local.ps1` first runs the CI-safe checks, validates every project
reference path, and then builds the normal solution using the
`SPT-4.1 Release` configuration. The solution includes Core, Server, Fika
Interop, the Fika bootstrap, and the regression runner. The build always passes
`SkipTscDeploy=true`; it reads local references but does not deploy to or alter
the supplied SPT installation. Use `-Configuration` only when intentionally
checking another configured target.

The historical SPT 4.1.2 port passed this command end to end against its pinned
reference/dependency root: CI-safe checks, release metadata, package-source
inventory, 160/160 regression tests, the five-project solution, and all four
fresh runtime outputs. This is compile and static-verification evidence only;
the disposable exact-version server bootstrap and public health-route smoke
also pass. Dashboard schema/config/authentication exercise, final-package boot
cleanliness, client load, solo raids, and Fika sessions remain separate gates
in `docs/port/SPT-4.1-PORT-LOG.md`. These results do not validate SPT 4.1.4.
The historical v1.3.0 exact-version SPT 4.1.4 five-project build passed with 0 errors and
four existing warnings, and the combined regression suite passes 168/168.
Clean-commit build evidence, complete CI/package checks, packaged-server
bootstrap, and seven HTTP checks pass. The artifact revision and remaining
client/raid/Fika gates are in `docs/port/SPT-4.1.4-VALIDATION.md`.

The active v1.3.2 candidate adds authorization-phone zoom easing and preserves
the v1.3.1 ballistic correction. Its regression suite passes 198/198 tests.
Build and package results are recorded in the candidate's external evidence
sidecars; in-game phone acceptance remains pending.
The native purchase-interface concept is not part of this release. See
`docs/release-notes-v1.3.2.md` for the zoom settings and acceptance scope.

### SPT 4.1 client and server contracts

`InputManagerUtil` resolves only the public four-parameter
`InputManager.Create(KeyGroup[], AxisGroup[], float, bool)` overload. A missing
overload fails patch initialization, and `FireSupportSpotter.Load` times out
after five seconds if the manager is not captured.

`MainMenuSlotStepPolicy` prefers measured adjacent native rows, then a cached
row interval, accepting those trusted measurements up to 160 pixels. The
ambiguous Play-to-Character gap is a later fallback capped at 90 pixels. Keep
the Seasonal Modifiers menu handoff when applying these placement rules.

`FireSupportServerConfigService` and `FireSupportAuthorizationLedger` must
remain explicit DI singletons: their paths, revision, and transaction state
are shared by load callbacks, HTTP handlers, and purchase consumers. The native
SPT config editor uses the same validation and atomic save path as the TSC
dashboard.

Historical SPT 4.1.2 client runs exposed the repaired input/menu issues and the
Uplink animator layer/default-state mismatch. The earlier corrected DLL
deployment was not a completed client acceptance run; its hashes and smoke
details remain in `docs/port/SPT-4.1-PORT-LOG.md`.

The retained v1.3.1 A-10 regression seams cover the deterministic 50-round
impact plan, moving muzzle, native gravity/drag evaluation, and terminal
replay timing. Solo and human-host projectiles originate from the visible
moving aircraft. The dedicated-headless executor retains its shorter
experimental damage origin and aligns predicted visual/damage arrivals. Its
fallback waits for projectile travel and is suppressed for invalid or
obstructed paths. Actual solo and multiplayer collision accuracy remains a
live acceptance check; see `docs/a10-ballistics-v1.3.1.md`.

The former MSBuild `CreateReleaseZip` and release-cleanup targets were removed
because they read and modified the live `SptDir` tree and updated an existing
ZIP in place. Normal `PostBuild` developer deployment remains, but release
builds must keep `SkipTscDeploy=true`.
`tools/New-ReleasePackage.ps1` is the only supported release archive path.

The Core and Fika bootstrap retain their historical internal assembly
identities. Their build outputs are
`SamSWAT.FireSupport.ArysReloaded.Core.dll` and
`SamSWAT.FireSupport.ArysReloaded.Fika.dll`; clean release staging must rename
the files to `Tylevo.TacticalServicesControl.Core.dll` and
`Tylevo.TacticalServicesControl.Fika.dll` without changing their internal
assembly names.

## Package Layout Verification

`tools/package-layout.allowlist.json` is the closed package layout, source, and
artifact-provenance contract. Its `archiveRoots` declare the only permitted
top-level ZIP folders, while `installRoots` declare the exact TSC destinations
below them. Every mirrored source file and reviewed source-only exclusion is
listed individually. The checker fails on a missing listed file, an untracked
listed file, or any unreviewed extra below either `CopyToOutput` tree.
Validate the source mappings without building:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-PackageLayout.ps1 -ValidateSourceInputs
```

Validate either a clean staging directory or a ZIP:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-PackageLayout.ps1 -Path "C:\Path\To\package-stage"
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-PackageLayout.ps1 -Path "C:\Path\To\TSC.zip"
```

The checker normalizes and de-duplicates paths, requires the exact 154-file
reviewed mirror inventory, exactly four TSC DLL mappings, and exactly eight
named asset bundles. It asserts the exact archive-root set and rejects
proprietary dependencies, profiles, storage, logs, build artifacts, archives,
and `.gitkeep` files from the package.

The v1.3.2 package contract follows the verified public v1.0.8 artifact:

- Extract the ZIP directly into the SPT installation root.
- The archive contains exactly `BepInEx/` and `SPT_Runtime/` at top level.
- TSC files install only into
  `BepInEx/plugins/Tylevo.TacticalServicesControl/` and
  `SPT_Runtime/user/mods/Tylevo.TacticalServicesControl/`.
- The mutable `config/tsc-config.json` is intentionally not shipped. A new
  installation creates schema-3 defaults on first server start; an upgrade
  therefore preserves and migrates the administrator's existing file.
- Root-level README, changelog, license, and release-note files are not part of
  the installer.

## Clean Release Staging

Build and verify from the exact clean commit that will identify the candidate.
For a release candidate, write the build evidence to a new file outside the
repository:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\verify-local.ps1 `
  -SptDir "C:\Path\To\SPT" `
  -SptSharedAssembliesDir "C:\Path\To\SPT Assemblies" `
  -EvidencePath "C:\External\TSC\v1.3.2-build-evidence.json"
```

`-EvidencePath` must not already exist and must be outside the repository. Once
that command succeeds, create the package in a separate new or empty external
directory:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\New-ReleasePackage.ps1 `
  -BaselineAssetArchive "C:\Path\To\Tylevo.TacticalServicesControl-v1.0.8-SPT4.0.13.zip" `
  -OutputDirectory "C:\External\TSC\v1.3.2-candidate" `
  -BuildEvidencePath "C:\External\TSC\v1.3.2-build-evidence.json"
```

The baseline archive is an explicit input for the eight historical Unity
bundles. The packager requires the verified public v1.0.8 ZIP SHA-256 recorded
in the manifest and verifies every baseline entry. Seven bundles are staged
unchanged. The Uplink container is replaced by the tracked, reviewed SPT 4.1
repair at `tools/assets/spt-4.1.2/uav_uplink_container.bundle`; the historical
directory name records the repair's origin. Its length and SHA-256 are pinned
independently in the manifest. It never accepts or
reads a live SPT directory as package content.

The baseline ZIP remains immutable: its required name is
`Tylevo.TacticalServicesControl-v1.0.8-SPT4.0.13.zip`, its byte length is
41,236,560, and its SHA-256 is
`C3C0390CC6641E5F82C99C3E57D8AEDA358FD0278E667E6616E53963EB2FCB8D`.
Its two server-side bundle entries retain their historical
`SPT/user/mods/...` source paths in the allowlist. The packager verifies those
exact entries and stages them at the 4.1.4 `SPT_Runtime/user/mods/...`
destinations. The Uplink container uses the separately pinned override, while
the loot bundle stays byte-identical. The baseline archive and its recorded
history are never rewritten.

`tools/Repair-UplinkBundle.py` reproduces the override from the exact baseline
ZIP with UnityPy 1.25.0. It repairs the two-layer controller's default states
and the nonzero `OutUse` animation-event hash. It rejects source/output drift,
overwrites, changes outside the two approved Unity objects, or changed streamed
resource bytes. The 4.1.4 serialized-field audit and equip/stow acceptance are
separate checks recorded in `docs/port/SPT-4.1.4-PORT-LOG.md`.

The four DLLs come only from the fixed project build-output paths recorded in
the manifest. All four must have the reviewed assembly name,
`AssemblyVersion`/`FileVersion` `1.3.2.0`, and
`AssemblyInformationalVersion` `1.3.2+<current-clean-HEAD>`. This rejects old,
mixed, or locally modified build outputs. The packager requires the external
build evidence and matches its HEAD/tree, SDK, configuration, output paths,
sizes, SHA-256 values, and assembly metadata against those four DLLs. Run the
full local verification after committing packaging/source changes so the
binaries and evidence identify that exact candidate revision.

The output directory must be outside the entire repository and must be new or
completely empty. The command creates a fresh `stage/`, validates it, creates a
new archive without updating any prior ZIP, validates the ZIP directly,
extracts it into a fresh `verify-extracted/`, and independently hashes all 168
files to require exact path/size/SHA-256 equality with the stage.
The repository root, manifest, checker, and build-output mappings are fixed by
the tracked packaging script; release inputs must be tracked, clean, and equal
to `HEAD`, with no external manifest or repository override. Mirrored and
copied source content is read from exact `HEAD` blobs rather than mutable
working-tree bytes, and the clean HEAD/tree identity is checked again before
success.

For this port the generated archive name is exactly
`Tylevo.TacticalServicesControl-v1.3.2-SPT4.1.4-TESTER.zip`. The `TESTER`
suffix must remain until the 4.1.4 runtime acceptance gates are complete.

The command also writes a new external `*.content-evidence.json` sidecar with
the source HEAD/tree, manifest identity, verified baseline archive, complete
168-file content inventory, DLL identities and versions, bundle pins, archive
identity, and exact file/DLL/bundle counts. The sidecar is not included in the
installer ZIP and is never overwritten.

ZIP entries are sorted ordinally and receive one fixed timestamp, so identical
inputs on the same toolchain produce the same SHA-256. To audit
reproducibility, run the command twice with two different empty external output
directories and compare the reported hashes.

Verify the release identity independently:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-ReleaseMetadata.ps1
```

Never include EFT/SPT/Fika/WTT/UnityToolkit assemblies, local profiles, logs,
build caches, source-only prompt files, or local machine paths in a release.
