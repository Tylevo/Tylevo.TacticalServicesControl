# Building

The current source is TSC v1.3.10 public beta for SPT 4.1.5, published
September 5, 2026.
The maintainer reports successful local use. Individual service checks are
not documented, and current Fika multiplayer remains untested. The published
v1.3.9 release continues to target SPT 4.1.4.

This repository does not include proprietary EFT or SPT assemblies. Provide local references from your own SPT install.

## Requirements

- .NET SDK 10.0.201, pinned by `global.json` and the CI workflow with SDK
  roll-forward disabled.
- SPT 4.1.5 reference assemblies from a clean installation.
- WTT Client CommonLib and WTT Server CommonLib `3.0.6`, installed separately
  from the official `v3.0.6` release, including its serialization prepatcher.
- UnityToolkit `2.0.1` rebuilt for SPT 4.1 from tag/commit
  `3c27a9798dc4396ca0b3dc765448a4221ff3007b` with the documented SPT 4.1
  build settings, references, deployment guards, and string-based lookup for
  the existing player-loop target. The unmodified pre-4.1 binary is not a
  substitute: the SPT 4.1.4 and 4.1.5 prepatch validators reject its older
  SPT assembly reference at startup.
- Project Fika client `2.4.2` from the official `v2.4.2` release when building
  the optional Fika interop. Multiplayer validation also requires its
  compatible server component.

The verified SPT 4.1.5 archive, compile-reference identities, byte lengths, and
SHA-256 values are recorded in the [4.1.5 port log](docs/port/SPT-4.1.5-PORT-LOG.md).
All 42 required reference files are present and the five critical pins match.
The official 4.1.4 and 4.1.5 modules tags identify the same source commit, so
the pinned compile-only `hollowed.dll` remains unchanged.
UnityToolkit keeps the reviewed SPT 4.1 rebuild bundled in v1.3.9. The [Toolkit build guide](tools/dependencies/unitytoolkit/README.md)
records its source patch, provenance, and package preparation. Players do not
need a separate Toolkit or overlay download. Historical pins remain in the
[4.1.2](docs/port/SPT-4.1-PORT-LOG.md) and
[4.1.4](docs/port/SPT-4.1.4-PORT-LOG.md) port logs. The v1.3.9 validation record is historical;
the 4.1.5 checks and their remaining limits are recorded in
`docs/release-notes-v1.3.10.md`. The maintainer's local feedback does not
establish individual service or Fika multiplayer compatibility.

TSC references WTT Common Lib from the local SPT dependency install. Do not copy WTT Common Lib source or binaries into the TSC source tree or release archive.

## Reference Paths

Create a local `Shared.User.props` or pass MSBuild properties:

- `SptDir`: path to a local SPT 4.1.5 root used for reference lookup and
  optional post-build output. The server runtime is below `SPT_Runtime/`.
- `SptSharedAssembliesDir`: folder containing the versioned SPT reference
  assemblies. For `SPT-4.1 Release`, `410x/hollowed.dll` is the compile-only
  `Assembly-CSharp` reference produced by the matching SPT.Modules
  `Shared/Hollowed` project. It must match the exact EFT build and must never be
  shipped in the mod. SPT 4.1.4 changes serialized fields despite retaining
  EFT build 40743, so the old 4.1.2 reference cannot certify this update.
  Record the exact 4.1.5 assembly identities, provenance, and SHA-256 values in
  the candidate build evidence. The 4.1.4 and earlier reference records remain
  historical; do not assume reference equivalence from the EFT build number.

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
JavaScript syntax and dashboard interaction tests, verifies release/version metadata, checks tracked-file
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

The v1.3.10 release retains the themed SIC dashboard and separate native
editor runtime/disk actions introduced in v1.3.9. Build, regression, native
API, package, and installation results must identify the new target and
source revision. See `docs/release-notes-v1.3.10.md` for scope, maintainer
feedback, and remaining checks; previous 4.1.4 results are not new 4.1.5 test results.

### SPT 4.1 client and server contracts

`InputManagerUtil` resolves only the public four-parameter
`InputManager.Create(KeyGroup[], AxisGroup[], float, bool)` overload. A missing
overload fails patch initialization, and `FireSupportSpotter.Load` times out
after five seconds if the manager is not captured.

The pre-raid entry resolves `PreloaderUI.Instance.MenuTaskBar` and clones
Character's complete wrapper into the native `Tabs` horizontal layout.
Its cloned toggle group and listeners remain separate from native navigation.
Center-menu transforms are not rewritten. Keep the Seasonal client handoff
and active-menu/raid guards when changing this entry.

`FireSupportServerConfigService` and `FireSupportAuthorizationLedger` must
remain explicit DI singletons: their paths, revision, and transaction state
are shared by load callbacks, HTTP handlers, and purchase consumers. The native
SPT config editor shares validation and atomic disk writes with the TSC
dashboard. Apply changes runtime only; Save changes disk only. Each editor
registration owns its snapshot, and revisions protect against stale writes.

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
top-level ZIP folders, while `installRoots` declare the exact TSC and bundled
UnityToolkit destinations
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
reviewed mirror inventory, four built TSC DLLs, fourteen pinned UnityToolkit
DLLs, and eight named asset bundles. The complete package has 186 files,
including the fifteen dependency files and two copies of their license notice.
It verifies bundled dependency lengths and SHA-256 values directly from both
staged files and ZIP entry streams. It rejects extra files, unpinned
dependencies, profiles, storage, logs, build artifacts, archives, and
`.gitkeep` files.

The v1.3.10 package contract retains the verified public v1.0.8 asset layout:

- Extract the ZIP directly into the SPT installation root.
- The archive contains exactly `BepInEx/` and `SPT_Runtime/` at top level.
- TSC files install only into
  `BepInEx/plugins/Tylevo.TacticalServicesControl/` and
  `SPT_Runtime/user/mods/Tylevo.TacticalServicesControl/`.
- Bundled UnityToolkit files install only into
  `BepInEx/plugins/UnityToolkit/` and `BepInEx/patchers/UnityToolkit/`, using
  their established paths. Do not retain another Toolkit plugin or prepatcher
  under a renamed folder; duplicate copies can conflict during BepInEx startup.
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
  -EvidencePath "C:\External\TSC\v1.3.10-build-evidence.json"
```

`-EvidencePath` must not already exist and must be outside the repository. Once
that command succeeds, create the package in a separate new or empty external
directory:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\New-ReleasePackage.ps1 `
  -BaselineAssetArchive "C:\Path\To\Tylevo.TacticalServicesControl-v1.0.8-SPT4.0.13.zip" `
  -OutputDirectory "C:\External\TSC\v1.3.10-candidate" `
  -BuildEvidencePath "C:\External\TSC\v1.3.10-build-evidence.json" `
  -UnityToolkitDirectory "C:\External\Dependencies\UnityToolkit"
```

`-UnityToolkitDirectory` is an explicit external input containing exactly the
fifteen paths in the manifest's `bundledDependencies` contract. Prepare it
from the official `UnityToolkit-v2.0.1.7z`, retaining the thirteen unchanged
companion files and replacing `UnityToolkit.dll` and
`UnityToolkit-Prepatcher.dll` with the reviewed SPT 4.1 compatibility overlay.
The directory must include the upstream `Assemblies.jsonc` and its exact
`ZLinq.Unity.UnityCollectoins.dll` spelling. Extra files or directories,
missing files, changed bytes, and symlinks/junctions are rejected. A live SPT
installation or an overlay containing only two DLLs is not a valid input.

The manifest pins every original and shipped length/hash, upstream archive
identity, source commit, and compatibility patch. The shipped plugin pin also
has to match the critical UnityToolkit compile-reference pin. Toolkit binaries
remain outside Git, and CI's tracked-binary ban is unchanged. The complete
notice at `tools/dependencies/unitytoolkit/THIRD_PARTY_NOTICES.txt` is read from
the release commit and copied into both Toolkit directories. The reviewed
source patch and preparation notes are in `tools/dependencies/unitytoolkit/`.
The original overlay compiler was not retained; the pins identify the reviewed
binary inputs and do not claim a byte-identical rebuild from source.

CI exercises the dependency inventory, hashes, staged-directory and ZIP checks
with synthetic files. It verifies rejection of modified or missing dependencies,
extra files, traversal, duplicates, and EFT/SPT/WTT/Fika files. It does not need
to download or load any bundled binaries.

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
exact entries and stages them at the current `SPT_Runtime/user/mods/...`
destinations. The Uplink container uses the separately pinned override, while
the loot bundle stays byte-identical. The baseline archive and its recorded
history are never rewritten.

`tools/Repair-UplinkBundle.py` reproduces the override from the exact baseline
ZIP with UnityPy 1.25.0. It repairs the two-layer controller's default states
and the nonzero `OutUse` animation-event hash. It rejects source/output drift,
overwrites, changes outside the two approved Unity objects, or changed streamed
resource bytes. The 4.1.4 serialized-field audit and equip/stow acceptance are
separate checks recorded in `docs/port/SPT-4.1.4-PORT-LOG.md`.

The four TSC DLLs come only from the fixed project build-output paths recorded in
the manifest. All four must have the reviewed assembly name,
`AssemblyVersion`/`FileVersion` `1.3.10.0`, and
`AssemblyInformationalVersion` `1.3.10+<current-clean-HEAD>`. This rejects old,
mixed, or locally modified build outputs. The packager requires the external
build evidence and matches its HEAD/tree, SDK, configuration, output paths,
sizes, SHA-256 values, and assembly metadata against those four DLLs. Run the
full local verification after committing packaging/source changes so the
binaries and evidence identify that exact candidate revision.

The output directory must be outside the entire repository and must be new or
completely empty. The command creates a fresh `stage/`, validates it, creates a
new archive without updating any prior ZIP, validates the ZIP directly,
extracts it into a fresh `verify-extracted/`, and independently hashes all 186
files to require exact path/size/SHA-256 equality with the stage.
The repository root, manifest, checker, and build-output mappings are fixed by
the tracked packaging script; release inputs must be tracked, clean, and equal
to `HEAD`, with no external manifest or repository override. Mirrored and
copied source content is read from exact `HEAD` blobs rather than mutable
working-tree bytes, and the clean HEAD/tree identity is checked again before
success.

For this port the generated archive name is exactly
`Tylevo.TacticalServicesControl-v1.3.10-SPT4.1.5-TESTER.zip`. The `TESTER`
suffix must remain until the 4.1.5 runtime acceptance gates are complete.

The command also writes a new external `*.content-evidence.json` sidecar with
the source HEAD/tree, manifest identity, verified baseline archive, complete
186-file content inventory, TSC DLL identities and versions, bundled dependency
provenance and pins, bundle pins, archive identity, and separate built/bundled
DLL counts. The sidecar is not included in the
installer ZIP and is never overwritten.

ZIP entries are sorted ordinally and receive one fixed timestamp, so identical
inputs on the same toolchain produce the same SHA-256. To audit
reproducibility, run the command twice with two different empty external output
directories and compare the reported hashes.

Verify the release identity independently:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-ReleaseMetadata.ps1
```

Never include EFT/SPT/Fika/WTT assemblies, unpinned UnityToolkit files, local
profiles, logs, build caches, source-only prompt files, or local machine paths
in a release. Only the fifteen reviewed Toolkit files are exempted from the
dependency filename restrictions, at their exact pinned paths and bytes.
