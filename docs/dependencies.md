# SPT 4.1.4 dependencies

Install dependencies separately before installing TSC. The TSC archive contains
only TSC files; it does not include UnityToolkit, WTT, Fika, or game assemblies.

## UnityToolkit 2.0.1 compatibility overlay

1. Close the game.
2. Download and extract the official
   [UnityToolkit v2.0.1 release](https://github.com/ArysWasTaken/UnityToolkit/releases/tag/v2.0.1)
   into your SPT root. Its `BepInEx` folder must merge with your existing one.
3. Download `UnityToolkit-v2.0.1-SPT4.1-compat-overlay.zip` from the
   [TSC v1.3.8 release](https://github.com/Tylevo/Tylevo.TacticalServicesControl/releases/tag/v1.3.8).
   Copy its `BepInEx` folder into the same SPT root and replace the two
   UnityToolkit DLLs when prompted.
4. Keep the other files from the official archive. In particular, retain
   `Assemblies.jsonc`, the UniTask/VContainer/ZLinq/ZString/Unity.Collections
   libraries, and the prepatcher's `System.Runtime.CompilerServices.Unsafe.dll`.

The overlay is an unofficial TSC compatibility build of Arys's MIT-licensed
UnityToolkit. It retains version 2.0.1 and supplies the SPT 4.1 build configuration,
updated references, deployment guards, and player-loop patch target adaptation.
It is not a complete UnityToolkit installation. Install the official archive
first; installing it again afterward would overwrite the compatibility build.

The two overlay DLLs are the exact compatibility binaries used by the TSC
SPT 4.1.4 tester. All 13 remaining files match the official archive byte for byte.
No additional libraries, proprietary references, or game files are redistributed
in the overlay. Its `LICENSE` preserves Arys's MIT notice; the complete source
patch and build-input manifest are included alongside the installation files.

| File | Bytes | SHA-256 |
| --- | ---: | --- |
| Official `UnityToolkit-v2.0.1.7z` | 500753 | `81FF11B228B73863F5CF1F54B9D823C344D23A6E900EC8FC3C33578569906FA1` |
| `BepInEx/plugins/UnityToolkit/UnityToolkit.dll` | 8704 | `DBA886D4C8B118C389795B1196EC13742DA42771C50D190209C370F69C416E75` |
| `BepInEx/patchers/UnityToolkit/UnityToolkit-Prepatcher.dll` | 5120 | `730156D8360A0BCA9024CF20F3886FBBD9509A7D793760FDD75C3BE186DFBDDE` |
| `UnityToolkit-v2.0.1-SPT4.1-compat.patch` | See archive | `1AD825EF63012A2EC9F2B6658A86E3F713AEDC1FE2C2E6DCD43701D28EE8283D` |

### Building the compatibility source

The patch applies to upstream commit
`3c27a9798dc4396ca0b3dc765448a4221ff3007b` (`v2.0.1`). It changes five source/build
files and includes no compiled references. Clone the upstream repository,
check out that commit, and apply the patch from the overlay:

```powershell
git clone https://github.com/ArysWasTaken/UnityToolkit.git
Set-Location UnityToolkit
git checkout 3c27a9798dc4396ca0b3dc765448a4221ff3007b
git apply --check ../UnityToolkit-v2.0.1-SPT4.1-compat.patch
git apply ../UnityToolkit-v2.0.1-SPT4.1-compat.patch
```

Use Windows with the .NET SDK and .NET Framework 4.8 targeting pack. The
clean-source checks used SDK 9.0.314. Copy the 11 library DLLs from the official
archive's `BepInEx/plugins/UnityToolkit` folder, excluding `UnityToolkit.dll`,
into `project/UnityToolkit/References`.

Provide your own SPT 4.1.4 references under a local `410x` directory. The required
names are `Assembly-CSharp.dll`, `0Harmony.dll`, `BepInEx.dll`,
`Newtonsoft.Json.dll`, `spt-reflection.dll`, `System.Memory.dll`, `UnityEngine.dll`,
`UnityEngine.CoreModule.dll`, `Mono.Cecil.dll`, `Mono.Cecil.Mdb.dll`,
`Mono.Cecil.Pdb.dll`, `Mono.Cecil.Rocks.dll`, `MonoMod.RuntimeDetour.dll`, and
`MonoMod.Utils.dll`. Use the matching SPT publicized/hollowed assembly as the
compile-only `Assembly-CSharp.dll`, following [BUILDING.md](../BUILDING.md).

Set `$toolkitReferences` to the parent of your `410x` folder, retaining a trailing
slash. From the patched source root, run:

```powershell
$toolkitReferences = 'C:/SPT-Refs/'
$toolkitBuild = @('--configuration', 'SPT-4.1 Release', '-p:Platform=AnyCPU', '-p:SptVersion=410x', '-p:SkipDeploy=true', "-p:SptSharedAssembliesDir=$toolkitReferences")
dotnet build project/UnityToolkit/UnityToolkit.csproj @toolkitBuild
dotnet build project/UnityToolkit.Prepatcher/UnityToolkit.Prepatcher.csproj @toolkitBuild
```

These commands only build; the `SkipDeploy` guard suppresses the upstream live
copy and archive targets. The outputs are
`project/UnityToolkit/Build/SPT-4.1/netstandard2.1/UnityToolkit.dll` and
`project/UnityToolkit.Prepatcher/Build/SPT-4.1/UnityToolkit-Prepatcher.dll`.
Both projects compiled from a clean patched source tree against SPT 4.1.4
references with zero warnings or errors. This is a source/build check; compiler,
reference, or source-path differences may change binary hashes. The shipped
overlay preserves the existing installed build, originally compiled for SPT
4.1.2; its original reference hashes are recorded in `build-inputs.json`.

## WTT and multiplayer

Install WTT Client CommonLib and WTT Server CommonLib 3.0.6 from the
[official WTT v3.0.6 release](https://github.com/WelcomeToThursday/WTT-CommonLib/releases/tag/v3.0.6),
including the serialization prepatcher supplied by that release.

Solo play does not require Fika. For multiplayer, the current reference target is
Project Fika client 2.4.2 plus its compatible server component. Follow the
[official Project Fika v2.4.2 release](https://github.com/project-fika/Fika-Plugin/releases/tag/v2.4.2)
and the [TSC known issues](known-issues.md) for remaining multiplayer tester limits.
