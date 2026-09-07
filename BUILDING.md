# Building

The current source prepares **TSC v1.3.11 for SPT 4.1.5**, with
**UnityToolkit 2.0.2 installed separately**. Neither new package is published.
The earlier candidate passed build, package, and isolated server checks and
was later installed for local TSC testing. The corrected Toolkit prepatcher
still needs its own in-game check. See the
[validation record](docs/validation/v1.3.11.md). Current Fika multiplayer
remains untested.

This repository does not include proprietary EFT or SPT assemblies. Provide local references from your own SPT install.

## Requirements

- .NET SDK 10.0.201, pinned by `global.json` and the CI workflow with SDK
  roll-forward disabled.
- SPT 4.1.5 reference assemblies from a clean installation.
- WTT Client CommonLib and WTT Server CommonLib `3.0.6`, installed separately
  from the official `v3.0.6` release, including its serialization prepatcher.
- The prepared standalone UnityToolkit `2.0.2` update, built against SPT 4.1.5.
  See the [Toolkit build guide](tools/dependencies/unitytoolkit/README.md) for
  its upstream source and build evidence. The original `2.0.1` binary is not
  a substitute: SPT 4.1's prepatch validator rejects its older SPT assembly
  reference at startup.
  The current 2.0.2 candidate also fixes the prepatcher's companion lookup:
  it resolves `System.Runtime.CompilerServices.Unsafe.dll` beside the
  prepatcher DLL. This changes Toolkit initialization for dependent mods
  without changing its public API.
- Project Fika client `2.4.2` from the official `v2.4.2` release when building
  the optional Fika interop. Multiplayer validation also requires its
  compatible server component.

The official SPT 4.1.5 archive and game-reference provenance are recorded in
the [4.1.5 port log](docs/port/SPT-4.1.5-PORT-LOG.md). The official 4.1.4 and
4.1.5 modules tags identify the same source commit, so the compile-only
`hollowed.dll` remains unchanged. Use the current Toolkit package evidence:
the corrected prepatcher has a different hash from the earlier unpublished
2.0.2 candidate, while the plugin code is unchanged. Earlier TSC validation
does not establish acceptance of the corrected prepatcher.

TSC references UnityToolkit and WTT from local dependency installations.
Dependency binaries belong outside the source repository and are excluded
from the TSC release archive. The standalone Toolkit package is maintained
separately on Arys's existing project page.

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

Build, regression, native API, package, and installation results must identify
TSC v1.3.11, its source revision, and the standalone Toolkit 2.0.2 references.
See the [release notes](docs/release-notes-v1.3.11.md) and
[validation record](docs/validation/v1.3.11.md). Earlier TSC 1.3.10 local
feedback does not establish runtime acceptance of this new pair.

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

The checker normalizes and de-duplicates paths, requires the reviewed mirror
inventory, four built TSC DLLs, and eight named asset bundles. UnityToolkit
files are excluded from this package. It rejects extra files, dependency
binaries, profiles, storage, logs, build artifacts, archives, and `.gitkeep`
files. The reviewed main TSC package has 173 files, four TSC DLLs, and eight bundles;
the generated evidence records their exact identities and hashes.

The optional Pilot Questline is a separate data package. Its exact eight-file
allowlist lives in `Get-TscPilotQuestlinePackageContract` in
`tools/PackageContract.ps1`. Source assets live under `addons/pilot-questline/`
and install below
`SPT_Runtime/user/mods/Tylevo.TacticalServicesControl/addons/pilot-questline/`.
They are excluded from the main server `CopyToOutput` tree and main archive.
The addon contains its manifest, installation README, repeater assortment, and
five quest/locale/quest-assort files. It adds no DLL, bundle, client files,
service configuration, or player state. Both versions are checked against
`Directory.Build.props`; the addon requires its matching main TSC release.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-PilotQuestlinePackage.ps1 -ValidateSourceInputs
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-PilotQuestlinePackage.ps1 -Path "C:\Path\To\PilotQuestline.zip"
```

CI checks both inventories, addon JSON/version metadata, and directory/ZIP
fixtures that reject missing quests, mixed packages, runtime configs, DLLs,
duplicate paths, and traversal entries. The main package contract also rejects
quest/addon assets even if they are accidentally added to its allowlist.

The v1.3.11 package contract retains the verified public v1.0.8 asset layout:

- Extract the ZIP directly into the SPT installation root.
- The archive contains exactly `BepInEx/` and `SPT_Runtime/` at top level.
- TSC files install only into
  `BepInEx/plugins/Tylevo.TacticalServicesControl/` and
  `SPT_Runtime/user/mods/Tylevo.TacticalServicesControl/`.
- UnityToolkit is installed from its standalone package. Its plugin and
  prepatcher folders must not appear in the TSC ZIP.
- The mutable `config/tsc-config.json` is intentionally not shipped. A new
  installation creates schema-3 defaults on first server start; an upgrade
  therefore preserves and migrates the administrator's existing file.
- Root-level README, changelog, license, and release-note files are not part of
  the installer.

Pilot Services ships four artwork PNGs in `assets/content/ui/pilot-services/`:
the existing `pilot-portrait.png`, the restored airfield `pilot-banner.png`,
and `a10-detail.png` and `uh60-detail.png`. The two aircraft detail images are
renders of the models already shipped in TSC's aircraft bundles. The UAV view
draws its radar rings and contacts in code and needs no separate PNG. Both
Pilot portraits use a shared close-up crop in the client; the original portrait
files are unchanged. The six service icons use transparent vector silhouettes,
with editable sources and a generator in `tools/artwork/service-icons/`.
The current local build uses Core `1.3.11-pilot-services.6` for the portraits
and icons, with Server `1.3.11-pilot-services.5` for balance synchronization.
These local build identifiers do not mark a release.

Native trader balance synchronization runs only in menus. After pending native
inventory operations finish, the client requests an authenticated absolute cash
snapshot and applies it through native inventory events. This should update
both the TSC balance and the native trader header without another charge.
In-game acceptance of the synchronization remains pending; use the
[Pilot Services checklist](docs/pilot-services-testing.md).

## Clean Release Staging

Build and verify from the exact clean commit that will identify the candidate.
For a release candidate, write the build evidence to a new file outside the
repository:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\verify-local.ps1 `
  -SptDir "C:\Path\To\SPT" `
  -SptSharedAssembliesDir "C:\Path\To\SPT Assemblies" `
  -EvidencePath "C:\External\TSC\v1.3.11-build-evidence.json"
```

`-EvidencePath` must not already exist and must be outside the repository. Once
that command succeeds, create the package in a separate new or empty external
directory:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\New-ReleasePackage.ps1 `
  -BaselineAssetArchive "C:\Path\To\Tylevo.TacticalServicesControl-v1.0.8-SPT4.0.13.zip" `
  -OutputDirectory "C:\External\TSC\v1.3.11-candidate" `
  -BuildEvidencePath "C:\External\TSC\v1.3.11-build-evidence.json"
```

Add `-IncludePilotQuestline` to this same command to also produce the separate
`Tylevo.TacticalServicesControl-PilotQuestline-v1.3.11-SPT4.1.5-TESTER.zip`.
The main archive remains unchanged. The addon is staged in
`stage-pilot-questline/`, independently checked as a directory and ZIP, then
extracted into `verify-extracted-pilot-questline/` for exact content/hash
comparison. Its own `*.content-evidence.json` sidecar identifies the same clean
HEAD/tree and build evidence, its fixed allowlist, all eight file hashes, and
the required main archive. The existing clean-worktree, exact-HEAD, fresh
output, and build-attestation guards apply to both archives. The addon can be
installed on the server after the main mod; Fika clients use the ordinary
matching main download. See the [addon instructions](addons/pilot-questline/README.md).

The TSC packager no longer accepts a Toolkit package directory. UnityToolkit
is a compile-time/runtime dependency supplied separately, not release content.
The [Toolkit guide](tools/dependencies/unitytoolkit/README.md) records the
standalone update's provenance, binary identities, and license handling.

CI checks the TSC inventory and verifies that extra dependency directories
and DLLs cannot enter the archive. It does not download proprietary references
or package the locally installed Toolkit, WTT, or Fika binaries.

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
`AssemblyVersion`/`FileVersion` `1.3.11.0`, and
`AssemblyInformationalVersion` `1.3.11+<current-clean-HEAD>`. This rejects old,
mixed, or locally modified build outputs. The packager requires the external
build evidence and matches its HEAD/tree, SDK, configuration, output paths,
sizes, SHA-256 values, and assembly metadata against those four DLLs. Run the
full local verification after committing packaging/source changes so the
binaries and evidence identify that exact candidate revision.

The output directory must be outside the entire repository and must be new or
completely empty. The command creates a fresh `stage/`, validates it, creates a
new archive without updating any prior ZIP, validates the ZIP directly,
extracts it into a fresh `verify-extracted/`, and independently hashes every
file to require exact path/size/SHA-256 equality with the stage.
The repository root, manifest, checker, and build-output mappings are fixed by
the tracked packaging script; release inputs must be tracked, clean, and equal
to `HEAD`, with no external manifest or repository override. Mirrored and
copied source content is read from exact `HEAD` blobs rather than mutable
working-tree bytes, and the clean HEAD/tree identity is checked again before
success.

For this port the generated archive name is exactly
`Tylevo.TacticalServicesControl-v1.3.11-SPT4.1.5-TESTER.zip`. The `TESTER`
suffix must remain until the 4.1.5 runtime acceptance gates are complete.

The command also writes a new external `*.content-evidence.json` sidecar with
the source HEAD/tree, manifest identity, verified baseline archive, complete
content inventory, TSC DLL identities and versions, bundle pins, archive
identity, and built DLL count. The sidecar is not included in the
installer ZIP and is never overwritten.

ZIP entries are sorted ordinally and receive one fixed timestamp, so identical
inputs on the same toolchain produce the same SHA-256. To audit
reproducibility, run the command twice with two different empty external output
directories and compare the reported hashes.

Verify the release identity independently:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-ReleaseMetadata.ps1
```

Never include EFT/SPT/Fika/WTT/UnityToolkit assemblies, local
profiles, logs, build caches, source-only prompt files, or local machine paths
in a TSC release. Toolkit's reviewed companion files belong only in its
separate standalone package, at their established paths.
