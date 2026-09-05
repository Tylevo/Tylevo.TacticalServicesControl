# Bundled UnityToolkit

TSC v1.3.9 includes UnityToolkit 2.0.1 with its plugin and prepatcher rebuilt
for SPT 4.1. Arys remains the author. The maintainer confirmed permission to
update and bundle it on September 5, 2026; see [PERMISSIONS.md](../../../PERMISSIONS.md).

The package uses the official release's complete 15-file installation layout.
Thirteen files are unchanged. Only `UnityToolkit.dll` and
`UnityToolkit-Prepatcher.dll` use the previously tested compatibility builds.
The upstream plugin identity and version remain `com.arys.unitytoolkit` and
`2.0.1`; this is a TSC-distributed rebuild, not a new official Arys release.
Do not install another copy of the plugin under a different folder.

## Source and binary provenance

- Upstream: <https://github.com/ArysWasTaken/UnityToolkit>
- Source tag: `v2.0.1`
- Source commit: `3c27a9798dc4396ca0b3dc765448a4221ff3007b`
- Official archive: `UnityToolkit-v2.0.1.7z` (500753 bytes)
- Archive SHA-256: `81FF11B228B73863F5CF1F54B9D823C344D23A6E900EC8FC3C33578569906FA1`
- Patch: [UnityToolkit-v2.0.1-SPT4.1-compat.patch](UnityToolkit-v2.0.1-SPT4.1-compat.patch)
- Patch SHA-256: `1AD825EF63012A2EC9F2B6658A86E3F713AEDC1FE2C2E6DCD43701D28EE8283D`

The package allowlist pins the exact path, size, and SHA-256 of all 15 files.
The two rebuilt DLLs originally used SPT 4.1.2 references and have been used
on SPT 4.1.4. The original compiler version was not retained; no byte-for-byte
rebuild claim is made. A later clean-source build against SPT 4.1.4 references
passed with .NET SDK 9.0.314. SPT 4.1.5 has not been tested.

SPT 4.1.4's prepatch validator rejects the official plugin because its
`spt-reflection` reference is `4.0.1.0`. The validator requires matching major
and minor versions; the rebuilt plugin's `4.1.2.0` reference passes. This check
runs before UnityToolkit or TSC initializes. The inspected startup methods
have identical IL in both builds, so this failure does not establish a
Toolkit runtime defect.

## Rebuilding the source

Check out the upstream commit and apply the adjacent patch. Use Windows with
the .NET SDK and the .NET Framework 4.8 targeting pack. Copy the 11 companion
DLLs from the official archive's `BepInEx/plugins/UnityToolkit` directory,
excluding `UnityToolkit.dll`, to `project/UnityToolkit/References`.

Provide your own SPT 4.1 references in a `410x` directory: `Assembly-CSharp.dll`,
`0Harmony.dll`, `BepInEx.dll`, `Newtonsoft.Json.dll`, `spt-reflection.dll`,
`System.Memory.dll`, `UnityEngine.dll`, `UnityEngine.CoreModule.dll`,
`Mono.Cecil.dll`, `Mono.Cecil.Mdb.dll`, `Mono.Cecil.Pdb.dll`,
`Mono.Cecil.Rocks.dll`, `MonoMod.RuntimeDetour.dll`, and `MonoMod.Utils.dll`.
Use the matching SPT publicized/hollowed game assembly for compilation, as
described in [BUILDING.md](../../../BUILDING.md). Never distribute those
game or SPT reference assemblies.

From the patched source directory, with `$toolkitReferences` set to the parent
of `410x` and ending in a slash:

```powershell
$toolkitBuild = @('--configuration', 'SPT-4.1 Release', '-p:Platform=AnyCPU', '-p:SptVersion=410x', '-p:SkipDeploy=true', "-p:SptSharedAssembliesDir=$toolkitReferences")
dotnet build project/UnityToolkit/UnityToolkit.csproj @toolkitBuild
dotnet build project/UnityToolkit.Prepatcher/UnityToolkit.Prepatcher.csproj @toolkitBuild
```

`SkipDeploy=true` disables upstream installation and archive targets. The
patch also uses the string `"Injection"` for reflection lookup, adds SPT 4.1
build settings, and retargets the prepatcher to .NET Framework 4.8.

## Preparing the package input

Extract the official archive to a new directory outside this repository and
replace only the two Toolkit DLLs with the pinned compatibility binaries.
Pass that directory to `New-ReleasePackage.ps1` using
`-UnityToolkitDirectory`. It must contain exactly the 15 dependency files,
with `BepInEx` at its root. Licenses are added separately from the tracked
[THIRD_PARTY_NOTICES.txt](THIRD_PARTY_NOTICES.txt).

The packager rejects missing, extra, or changed dependency files. A new
rebuild with different bytes needs reviewed pins before it can be packaged.
Compiled dependencies and private permission conversations stay outside the
source repository. Package evidence records which dependency bytes shipped.
