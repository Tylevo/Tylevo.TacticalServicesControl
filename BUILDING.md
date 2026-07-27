# Building

This repository does not include proprietary EFT or SPT assemblies. Provide local references from your own SPT install.

## Requirements

- .NET SDK 9.0.314, pinned by `global.json` and the CI workflow with SDK
  roll-forward disabled.
- SPT 4.0.13 reference assemblies.
- UnityToolkit v2.0.1.
- WTT Client Common Lib and WTT Server Common Lib, installed separately as dependencies.
- Project Fika references if building the Fika plugin.

TSC references WTT Common Lib from the local SPT dependency install. Do not copy WTT Common Lib source or binaries into the TSC source tree or release archive.

## Reference Paths

Create a local `Shared.User.props` or pass MSBuild properties:

- `SptDir`: path to a local SPT-style folder used for post-build output.
- `SptSharedAssembliesDir`: folder containing the versioned SPT reference assemblies, such as `400x/Assembly-CSharp.dll`.

Use forward slashes or quote paths carefully when paths contain spaces.

## Verification Layers

TSC has two intentionally separate verification layers.

### CI-safe verification

The CI-safe layer requires .NET 9, Node.js, Git, and PowerShell, but no EFT,
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
`SPT-4.0 Release` configuration. The solution includes Core, Server, Fika
Interop, the Fika bootstrap, and the regression runner. The build always passes
`SkipTscDeploy=true`; it reads local references but does not deploy to or alter
the supplied SPT installation. Use `-Configuration` only when intentionally
checking another configured target.

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

The checker normalizes and de-duplicates paths, requires the exact 155-file
reviewed mirror inventory, exactly four TSC DLL mappings, and exactly eight
named asset bundles. It asserts the exact archive-root set and rejects
proprietary dependencies, profiles, storage, logs, build artifacts, archives,
and `.gitkeep` files from the package.

The v1.1.0 package contract follows the verified public v1.0.8 artifact:

- Extract the ZIP directly into the SPT installation root.
- The archive contains exactly `BepInEx/` and `SPT/` at top level.
- TSC files install only into
  `BepInEx/plugins/Tylevo.TacticalServicesControl/` and
  `SPT/user/mods/Tylevo.TacticalServicesControl/`.
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

The baseline archive is an explicit input because the eight Unity bundles are
not tracked source files. The packager requires the verified public v1.0.8 ZIP
SHA-256 recorded in the manifest, then verifies each allowlisted bundle's byte
length and SHA-256 and extracts only those eight entries. It never accepts or
reads a live SPT directory as package content.

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
extracts it into a fresh `verify-extracted/`, and independently hashes all 169
files to require exact path/size/SHA-256 equality with the stage.
The repository root, manifest, checker, and build-output mappings are fixed by
the tracked packaging script; release inputs must be tracked, clean, and equal
to `HEAD`, with no external manifest or repository override. Mirrored and
copied source content is read from exact `HEAD` blobs rather than mutable
working-tree bytes, and the clean HEAD/tree identity is checked again before
success.

The command also writes a new external `*.content-evidence.json` sidecar with
the source HEAD/tree, manifest identity, verified baseline archive, complete
169-file content inventory, DLL identities and versions, bundle pins, archive
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
