# Building

This repository does not include proprietary EFT or SPT assemblies. Provide local references from your own SPT install.

## Requirements

- .NET SDK compatible with the project.
- SPT 4.0.13 reference assemblies.
- UnityToolkit v2.0.1.
- WTT Client Common Lib and WTT Server Common Lib, installed separately as dependencies.
- Project Fika references if building the Fika plugin.

TSC references WTT Common Lib from the local SPT dependency install. Do not copy WTT Common Lib source or binaries into the TSC source tree or release archive.

## Reference Paths

Create a local `Shared.User.props` or pass MSBuild properties:

- `SptDir`: path to a local SPT-style folder used for post-build output.
- `SptSharedAssembliesDir`: folder containing the versioned SPT reference assemblies, such as `400x/Assembly-CSharp.dll`.
- `SevenZipPath`: optional path to `7z.exe` when creating release archives. Defaults to `C:\Program Files\7-Zip\7z.exe`.

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
JavaScript syntax, checks tracked-file hygiene, and validates the declarative
package inputs. GitHub Actions runs this same command. CI must never download,
cache, upload, or redistribute proprietary reference assemblies.

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

The Core and Fika bootstrap retain their historical internal assembly
identities. Their build outputs are
`SamSWAT.FireSupport.ArysReloaded.Core.dll` and
`SamSWAT.FireSupport.ArysReloaded.Fika.dll`; clean release staging must rename
the files to `Tylevo.TacticalServicesControl.Core.dll` and
`Tylevo.TacticalServicesControl.Fika.dll` without changing their internal
assembly names.

## Package Layout Verification

`tools/package-layout.allowlist.json` is the exact release-content allowlist.
Validate the source mappings without building:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-PackageLayout.ps1 -ValidateSourceInputs
```

Validate either a clean staging directory or a ZIP:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-PackageLayout.ps1 -Path "C:\Path\To\package-stage"
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-PackageLayout.ps1 -Path "C:\Path\To\TSC.zip"
```

The checker normalizes and de-duplicates paths, rejects every file not present
in the two mirrored `CopyToOutput` trees or explicit generated-file list,
requires exactly four TSC DLLs and eight named asset bundles, and rejects
proprietary dependencies, profiles, storage, logs, build artifacts, archives,
and `.gitkeep` files.

For Phase 5 the checker follows the verified public package shape and permits
only these extraction roots:

- `BepInEx/plugins/Tylevo.TacticalServicesControl/`
- `SPT/user/mods/Tylevo.TacticalServicesControl/`

The documentation/package-root contradiction is intentionally deferred to
Phase 6. Until that decision is made, root-level release documents are not
allowlisted. The checker validates a package but does not stage or create one;
clean allowlisted staging replaces the legacy live-tree/update-in-place ZIP
path in Phase 7.

Never include EFT/SPT/Fika/WTT/UnityToolkit assemblies, local profiles, logs,
build caches, source-only prompt files, or local machine paths in a release.
