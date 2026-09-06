# Standalone UnityToolkit 2.0.2

UnityToolkit 2.0.2 is prepared as a separate SPT 4.1.5 update on Arys's existing
project page. Arys remains the author and has added Tylevo as a coauthor for
maintenance. It has not been published yet. TSC v1.3.11 requires this package
separately and does not bundle Toolkit binaries or companion libraries.

Earlier TSC documentation incorrectly described explicit permission to bundle
Toolkit. That was a maintainer/assistant misunderstanding; see the
[corrected permission record](../../../PERMISSIONS.md). The MIT license and
companion-library licenses are unchanged.

## Source and build provenance

- Upstream: [ArysWasTaken/UnityToolkit](https://github.com/ArysWasTaken/UnityToolkit)
- Base source tag: `v2.0.1`
- Base source commit: `3c27a9798dc4396ca0b3dc765448a4221ff3007b`
- Update patch: [UnityToolkit-v2.0.2-SPT4.1.5.patch](UnityToolkit-v2.0.2-SPT4.1.5.patch)
- Patch SHA-256: `4B39EAB920B84A8119C67CF9DD6999913CCFD6E15BD9ECB6C55186675A548911`
- Build SDK: .NET 9.0.314, with the .NET Framework 4.8 targeting pack.
- Build target: `SPT-4.1 Release`, with local SPT 4.1.5 references.
- Assembly and file versions: `2.0.2.0` for both plugin and prepatcher.
- Plugin identity remains `com.arys.unitytoolkit`; its plugin version is `2.0.2`.

The update adds SPT 4.1 build settings and deployment guards, uses a
string-based lookup for the existing player-loop target, retargets the
prepatcher to .NET Framework 4.8, and gives both assemblies the distinct
2.0.2 version metadata. The rebuilt plugin references `spt-reflection 4.1.5.0`.

The original plugin's `spt-reflection 4.0.1.0` reference fails SPT 4.1's
startup version check before Toolkit or TSC initializes. SPT 4.1.5's fix for
older Unity asset bundles affects a separate server check.

## Verified standalone package

The prepared archive has **17 files**: the complete 15-file upstream
installation layout with the two rebuilt DLLs, plus two copies of the license
notices. The remaining 13 upstream files are unchanged.

| Item | Bytes | SHA-256 |
| --- | ---: | --- |
| Standalone Toolkit 2.0.2 ZIP | 758925 | `18A32E842966F0D8B71F1C5FE07CFF40726BC854B4CECE320A7E7BF7068375A7` |
| `UnityToolkit.dll` | 8704 | `F047AED2C3A1AC118DB2BC9C86BD36CF89D675C7522E36E28129487CCFCF1EDC` |
| `UnityToolkit-Prepatcher.dll` | 6144 | `BB151BBB6F859141BE6D773173864A3D543531729EF4EC07C63FFECE1D3CC357` |

Both projects compiled with zero warnings and errors. All 15 plugin method
bodies and six prepatcher method bodies matched the prior compatibility build
under IL comparison; the larger prepatcher carries the new assembly metadata.
These static checks do not establish game startup or raid compatibility.
The TSC 1.3.11/Toolkit 2.0.2 pair has not been tested in the live game.

## Rebuild

Check out the base source commit and apply the 2.0.2 patch above. Select
.NET SDK 9.0.314 in that separate checkout; TSC itself uses the SDK pinned in
its own `global.json`.

Copy the 11 companion DLLs from the official 2.0.1 archive's
`BepInEx/plugins/UnityToolkit` directory, excluding `UnityToolkit.dll`, into
`project/UnityToolkit/References`.

Provide your own SPT 4.1.5 compile references in a `410x` directory:
`Assembly-CSharp.dll`, `0Harmony.dll`, `BepInEx.dll`,
`Newtonsoft.Json.dll`, `spt-reflection.dll`, `System.Memory.dll`,
`UnityEngine.dll`, `UnityEngine.CoreModule.dll`, `Mono.Cecil.dll`,
`Mono.Cecil.Mdb.dll`, `Mono.Cecil.Pdb.dll`, `Mono.Cecil.Rocks.dll`,
`MonoMod.RuntimeDetour.dll`, and `MonoMod.Utils.dll`. Use the matching
publicized/hollowed game assembly for compilation, as described in
[BUILDING.md](../../../BUILDING.md). Never distribute those game or SPT
references.

With `$toolkitReferences` set to the parent of `410x` and ending in a slash:

```powershell
$toolkitBuild = @('--configuration', 'SPT-4.1 Release', '-p:Platform=AnyCPU', '-p:SptVersion=410x', '-p:SkipDeploy=true', "-p:SptSharedAssembliesDir=$toolkitReferences")
dotnet build project/UnityToolkit/UnityToolkit.csproj @toolkitBuild
dotnet build project/UnityToolkit.Prepatcher/UnityToolkit.Prepatcher.csproj @toolkitBuild
```

`SkipDeploy=true` disables installation and archive targets. Build in the
separate Toolkit checkout and keep binaries outside the TSC source tree.

## Package separately from TSC

Stage the complete upstream installation layout in an empty external folder,
replace the two Toolkit DLLs with the verified 2.0.2 builds, and include the
[full license notices](THIRD_PARTY_NOTICES.txt) in both Toolkit directories.
Preserve `Assemblies.jsonc`, every companion library, and their established
paths.

The upstream companion source is `UnityToolkit-v2.0.1.7z` (500753 bytes),
SHA-256 `81FF11B228B73863F5CF1F54B9D823C344D23A6E900EC8FC3C33578569906FA1`.
Check each unchanged file against that archive. Validate the standalone ZIP
and its extracted contents before publication on the existing Toolkit page.

Do not pass this folder to TSC's `New-ReleasePackage.ps1`: TSC's archive
contains only TSC's files. Preserve the separate source patch, binary hashes,
and package evidence for the Toolkit upload. No proprietary references, local
profiles, or private permission conversations belong in either package.

The [older 2.0.1 compatibility patch](UnityToolkit-v2.0.1-SPT4.1-compat.patch)
remains historical provenance; use the 2.0.2 patch for this update.
