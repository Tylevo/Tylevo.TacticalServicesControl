# Building

This repository does not include proprietary EFT or SPT assemblies. Provide local references from your own SPT install.

## Requirements

- .NET SDK 10.0.201, pinned by `global.json` and the CI workflow with SDK
  roll-forward disabled.
- SPT 4.1.2 reference assemblies from a clean installation.
- WTT Client CommonLib and WTT Server CommonLib `3.0.3`, installed separately
  as dependencies. The verified binaries come from tag `v3.0.3`, commit
  `d3f588d611774ab15f2b358760ac76ab3cb06efd`.
- UnityToolkit `2.0.1` rebuilt for SPT 4.1.2 from tag/commit
  `3c27a9798dc4396ca0b3dc765448a4221ff3007b` with the documented SPT 4.1
  configuration, reference, deploy-guard, and player-loop target adaptations.
  The unmodified pre-4.1 binary is not a substitute.
- Project Fika client `2.4.1` when building the optional Fika interop. The
  verified client checkout is
  `c89e28e41700093eb874589c440d3d8c77a25add` (the `v2.4.1` code tag is
  `fbd3814a`); its compatible server line reports `2.4.0` and is pinned at
  `2547995894e269f058b967da6b838f1506377f27`.

Exact dependency assembly identities, byte lengths, and SHA-256 values are in
`docs/port/SPT-4.1-PORT-LOG.md`. Those pins produced a passing full local
verification; changing any dependency requires new compile and runtime
evidence.

TSC references WTT Common Lib from the local SPT dependency install. Do not copy WTT Common Lib source or binaries into the TSC source tree or release archive.

## Reference Paths

Create a local `Shared.User.props` or pass MSBuild properties:

- `SptDir`: path to a local SPT 4.1.2 root used for reference lookup and
  optional post-build output. The server runtime is below `SPT_Runtime/`.
- `SptSharedAssembliesDir`: folder containing the versioned SPT reference
  assemblies. For `SPT-4.1 Release`, `410x/hollowed.dll` is the compile-only
  `Assembly-CSharp` reference produced by the matching SPT.Modules
  `Shared/Hollowed` project. It must match the exact EFT build and must never be
  shipped in the mod. The pinned file is 8,696,320 bytes with SHA-256
  `E40F6E470CD3C09E827900EFE98BB490920E97CAE962880DCA23DDF2A78E501C`.
  It was obtained byte-identically from the tracked
  `References/hollowed.dll` in the Fika checkout above (Git blob
  `1cb3df593f3079a976b65d97f3da7557f6207d39`). That checkout documents the
  file as SPT.Modules `project/Shared/Hollowed` output. The corresponding raw
  EFT `Assembly-CSharp.dll` SHA-256 is
  `43A539F5AD00FCCD87EE54A084D8DBE1C5F63D12F8D855C8A392D68B3A1DEAF9`.
  No unverified SPT.Modules commit is asserted.

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

The SPT 4.1.2 port has passed this command end to end against the exact pinned
reference/dependency root: CI-safe checks, release metadata, package-source
inventory, the regression suite, the five-project solution, and all four fresh
runtime outputs. After the latest client hardening, `verify-ci.ps1` passes
109/109 tests and the exact five-project solution builds with 0 errors and two
obsolete Core inventory-API warnings. The earlier end-to-end local evidence
must still be refreshed from the final clean candidate before packaging. This
is compile and static-verification evidence only; the exact server rebuild also
passes with 0 warnings and 0 errors. A live installed smoke after the SPT 4.1
singleton-lifetime correction returned HTTP 200 for the TSC public-health
route, admin shell, and both dashboard assets and preserved the predeployment
config hash. Dashboard schema/config/authentication exercise, final-package
boot cleanliness, corrected client relaunch, solo raids, and Fika sessions
remain separate gates in `docs/port/SPT-4.1-PORT-LOG.md`.

### SPT 4.1 client hardening contract

`InputManagerUtil` must resolve only the public four-parameter
`InputManager.Create(KeyGroup[], AxisGroup[], float, bool)` overload. A missing
exact overload throws `MissingMethodException` during patch initialization,
and `FireSupportSpotter.Load` fails after five seconds if the postfix never
captures a ready manager. This prevents an ambiguous overload or missing patch
from leaving targeting initialization waiting indefinitely.

Main-menu vertical placement uses `MainMenuSlotStepPolicy`: measured
Trade-to-Hideout and Hideout-to-Exit adjacent rows take precedence, followed by
a cached native row interval. Those trusted measurements accept magnitudes up
to 160 pixels. The potentially multi-row Play-to-Character gap is considered
only afterward and only up to 90 pixels; otherwise the native fallback is used.
The policy is linked into the zero-dependency regression project.

The final hardening Core DLL was installed at
`<SPT_ROOT>/BepInEx/plugins/Tylevo.TacticalServicesControl/Tylevo.TacticalServicesControl.Core.dll`:
645,120 bytes, SHA-256
`D7A9124C3D29A252ED235BB3BE2B24EEC9D314A39E814B472482E92BA8C8A2CE`.
The immediately prior build, SHA-256
`B65388AB7DCAF48A532DCD074B5F7037DFF6C121CA5A7E31E69A552C2BCC5986`,
was copied to the external/local
`<WORKSPACE>/work/live-backups/tsc-client-final-hardening-20260812-112848/`
rollback directory before replacement; that backup is not a release input. The
game and server were stopped after deployment. This installed DLL has not yet
been manually relaunched, so it is deployment evidence, not client acceptance.

The superseded client run also emitted
`LayersDefaultStates.Length 3 != _animator.layerCount 2` for
`uav_uplink_container.bundle` and separate handler-hash-0 animation warnings.
The deterministic 4.1.2 override repairs the exact serialized mismatch and the
Uplink controller's named `OutUse` zero hash. The packager pins its output and
the repair tool proves that no other Unity object or streamed resource changed.
A corrected equip/stow runtime replay remains required.

`FireSupportServerConfigService` and `FireSupportAuthorizationLedger` must
remain explicit DI singletons. Their initialized paths, config revision, and
transaction state are shared by the server load callback, HTTP listener, and
purchase/authorization consumers; transient instances break that contract.

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

The v1.1.0 package contract follows the verified public v1.0.8 artifact:

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
  -EvidencePath "C:\External\TSC\v1.1.0-build-evidence.json"
```

`-EvidencePath` must not already exist and must be outside the repository. Once
that command succeeds, create the package in a separate new or empty external
directory:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\New-ReleasePackage.ps1 `
  -BaselineAssetArchive "C:\Path\To\Tylevo.TacticalServicesControl-v1.0.8-SPT4.0.13.zip" `
  -OutputDirectory "C:\External\TSC\v1.1.0-candidate" `
  -BuildEvidencePath "C:\External\TSC\v1.1.0-build-evidence.json"
```

The baseline archive is an explicit input for the eight historical Unity
bundles. The packager requires the verified public v1.0.8 ZIP SHA-256 recorded
in the manifest, then verifies every baseline entry. Seven bytesets are staged
unchanged. The Uplink container is deliberately replaced by the tracked,
reviewed SPT 4.1.2 override at
`tools/assets/spt-4.1.2/uav_uplink_container.bundle`; its exact length and
SHA-256 are independently pinned in the same manifest. It never accepts or
reads a live SPT directory as package content.

The baseline ZIP remains immutable: its required name is
`Tylevo.TacticalServicesControl-v1.0.8-SPT4.0.13.zip`, its byte length is
41,236,560, and its SHA-256 is
`C3C0390CC6641E5F82C99C3E57D8AEDA358FD0278E667E6616E53963EB2FCB8D`.
Its two server-side bundle entries retain their historical
`SPT/user/mods/...` source paths in the allowlist. The packager verifies those
exact entries and stages them at the 4.1.2 `SPT_Runtime/user/mods/...`
destinations. The Uplink container then uses the separately pinned override;
the loot bundle remains byte-identical. The baseline archive and its recorded
history are never rewritten.

`tools/Repair-UplinkBundle.py` reproduces the override only from the exact
baseline ZIP with UnityPy 1.25.0. It refuses source drift, output drift,
overwrites, changes outside the two approved serialized objects, or any change
to streamed resource bytes.

The four DLLs come only from the fixed project build-output paths recorded in
the manifest. All four must have the reviewed assembly name,
`AssemblyVersion`/`FileVersion` `1.1.0.0`, and
`AssemblyInformationalVersion` `1.1.0+<current-clean-HEAD>`. This rejects old,
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
`Tylevo.TacticalServicesControl-v1.1.0-SPT4.1.2-TESTER.zip`. The `TESTER`
suffix must remain until the 4.1.2 runtime acceptance gates are complete.

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
