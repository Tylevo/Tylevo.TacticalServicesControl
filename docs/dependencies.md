# SPT 4.1.4 dependencies

## v1.3.9 unreleased candidate

The v1.3.9 TSC ZIP includes UnityToolkit 2.0.1 with its plugin and prepatcher
rebuilt against SPT 4.1, companion libraries, and license notices. No separate
Toolkit or compatibility-overlay download is needed. WTT CommonLib remains
required separately; Fika is optional and also installed separately.

1. Close the game, launcher, and SPT server. Back up profiles and TSC's
   configuration and complete storage directory before updating.
2. Install WTT Client CommonLib and WTT Server CommonLib 3.0.6 from the
   [official WTT v3.0.6 release](https://github.com/WelcomeToThursday/WTT-CommonLib/releases/tag/v3.0.6),
   including its serialization prepatcher.
3. Extract the complete v1.3.9 candidate ZIP into the SPT 4.1.4 root. Merge
   its `BepInEx` and `SPT_Runtime` folders with the existing folders.
4. If Toolkit is already installed, replace its files in the standard folders
   when prompted. Keep one installation; additional copies of the same plugin
   in other folders can conflict.

The bundled dependency uses these standard locations:

- `BepInEx/plugins/UnityToolkit/`: plugin, companion libraries, and `Assemblies.jsonc`.
- `BepInEx/patchers/UnityToolkit/`: prepatcher and its companion library.
- Both folders include `THIRD_PARTY_NOTICES.txt` with the dependency licenses.

Arys remains the author of UnityToolkit. On September 5, 2026, the maintainer
confirmed Arys's explicit permission to bundle the rebuilt dependency with
TSC. It retains version 2.0.1 and the original plugin identity. This is a
TSC-distributed rebuild, not a new official Arys release. UnityToolkit remains
under MIT, and its companion libraries keep their respective licenses. See
[permissions](../PERMISSIONS.md) and [third-party notices](../THIRD_PARTY_NOTICES.md).

SPT 4.1.4's prepatch validator rejects the original plugin's SPT 4.0.1 assembly
reference before Toolkit or TSC initializes. The bundled rebuild references
SPT 4.1 and passes that version check. This identifies a startup compatibility
check; it does not establish a Toolkit runtime defect. The
[source and binary provenance](../tools/dependencies/unitytoolkit/README.md)
records the rebuild inputs, source patch, and limits of the checks performed.

## Optional multiplayer

Solo play does not require Fika. The current build reference is Project Fika
client 2.4.2 plus its compatible server component. For experimental testing,
follow the [official Project Fika v2.4.2 release](https://github.com/project-fika/Fika-Plugin/releases/tag/v2.4.2)
and the [TSC known issues](known-issues.md).

**Multiplayer on the current SPT/Fika versions has not been tested. SPT 4.1.5
has not been tested either.** A successful build does not establish live
compatibility on another version.

## Historical v1.3.8 installation: official Toolkit plus overlay

The published v1.3.8 ZIP is unchanged and does not include UnityToolkit.
The steps and hashes below apply to that release. For the v1.3.9 candidate,
use the bundled installation above.

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
updated references, deployment guards, and a string-based lookup for the existing player-loop patch target.
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

## Rebuilding and packaging the Toolkit dependency

The current [Toolkit source and packaging guide](../tools/dependencies/unitytoolkit/README.md)
contains the pinned upstream commit, compatibility patch, required local build
references, deployment-suppressed build commands, and package-input contract.
The source repository contains the patch and notices; compiled dependencies
are supplied separately to the packager and checked against the reviewed pins.
