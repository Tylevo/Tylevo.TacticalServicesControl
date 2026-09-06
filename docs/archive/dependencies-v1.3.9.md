# Historical SPT 4.1.4 dependencies

> **Historical instructions — permission correction.** The old explicit
> bundling-permission claim was a maintainer/assistant misunderstanding and
> is withdrawn. TSC v1.3.11 requires standalone Toolkit 2.0.2; both new packages
> are unpublished. See [current installation guidance](../dependencies.md)
> and the [corrected permission record](../../PERMISSIONS.md).

This guide preserves the v1.3.8 and v1.3.9 installation differences for older
packages. Their installation steps are retained as history and do not describe
the prepared **TSC v1.3.11 / SPT 4.1.5** package.

The v1.3.8 and v1.3.9 GitHub releases are retained as archived drafts. Their
old release pages and downloads are no longer public. The instructions below
are a record for previously downloaded archives, not steps required for the
current release.

## v1.3.9: bundled UnityToolkit

The v1.3.9 full ZIP for SPT 4.1.4 already includes UnityToolkit 2.0.1 with its
plugin and prepatcher rebuilt against SPT 4.1, companion libraries, and license
notices. WTT CommonLib 3.0.6 is separately required, including its client,
server, and serialization prepatcher components. Fika is optional and was not
validated in multiplayer on that target.

With the game, launcher, and server closed, extraction merges the package's
`BepInEx` and `SPT_Runtime` folders into the SPT root. Existing Toolkit files
are replaced in their standard folders; duplicate installations should not
be kept elsewhere.

- `BepInEx/plugins/UnityToolkit/`: plugin, companion libraries, and `Assemblies.jsonc`.
- `BepInEx/patchers/UnityToolkit/`: prepatcher and its companion library.
- Both folders include `THIRD_PARTY_NOTICES.txt` with dependency licenses.

Arys remains the author of UnityToolkit. The maintainer confirmed permission
to bundle the rebuilt dependency on September 5, 2026. It retains version
2.0.1 and its original plugin identity. UnityToolkit remains under MIT and
its companion libraries retain their respective licenses. See
[permissions](../../PERMISSIONS.md) and
[third-party notices](../../THIRD_PARTY_NOTICES.md).

See the [v1.3.9 notes](../release-notes-v1.3.9.md) and
[validation record](../validation/v1.3.9.md) for that release's scope.

## v1.3.8: official Toolkit plus overlay

The v1.3.8 TSC ZIP does not include UnityToolkit. Its separate compatibility
archive was named `UnityToolkit-v2.0.1-SPT4.1-compat-overlay.zip`.

1. Close the game, launcher, and server.
2. Extract the official
   [UnityToolkit v2.0.1 release](https://github.com/ArysWasTaken/UnityToolkit/releases/tag/v2.0.1)
   into the SPT root so its `BepInEx` folder merges with the existing one.
3. Extract the previously downloaded compatibility overlay into the same SPT
   root and replace the two UnityToolkit DLLs when prompted.
4. Keep the remaining official files, including `Assemblies.jsonc`, the
   UniTask/VContainer/ZLinq/ZString/Unity.Collections libraries, and the
   prepatcher's `System.Runtime.CompilerServices.Unsafe.dll`.

The overlay is an unofficial TSC compatibility build of Arys's MIT-licensed
UnityToolkit. It retains version 2.0.1 and supplies the SPT 4.1 build
configuration, updated references, deployment guards, and a string-based
lookup for the existing player-loop patch target. It is not a complete
UnityToolkit installation. Installing the official archive afterward would
overwrite the compatibility build.

The two overlay DLLs are the compatibility binaries used by the TSC SPT 4.1.4
tester. All 13 remaining files match the official archive byte for byte.
The overlay adds no other libraries, proprietary references, or game files.
Its `LICENSE` preserves Arys's MIT notice; the source patch and build-input
manifest were included alongside the installation files.

| File | Bytes | SHA-256 |
| --- | ---: | --- |
| Official `UnityToolkit-v2.0.1.7z` | 500753 | `81FF11B228B73863F5CF1F54B9D823C344D23A6E900EC8FC3C33578569906FA1` |
| `BepInEx/plugins/UnityToolkit/UnityToolkit.dll` | 8704 | `DBA886D4C8B118C389795B1196EC13742DA42771C50D190209C370F69C416E75` |
| `BepInEx/patchers/UnityToolkit/UnityToolkit-Prepatcher.dll` | 5120 | `730156D8360A0BCA9024CF20F3886FBBD9509A7D793760FDD75C3BE186DFBDDE` |
| `UnityToolkit-v2.0.1-SPT4.1-compat.patch` | See archive | `1AD825EF63012A2EC9F2B6658A86E3F713AEDC1FE2C2E6DCD43701D28EE8283D` |

SPT 4.1.4's prepatch validator rejects the original plugin's SPT 4.0.1 assembly
reference before Toolkit or TSC initializes. The rebuild references SPT 4.1
and passes that startup version check. This does not establish a Toolkit
runtime defect. The later SPT 4.1.5 asset-bundle validation fix affects a
separate server check.

## Rebuilding and packaging

The [Toolkit source and packaging guide](../../tools/dependencies/unitytoolkit/README.md)
contains the pinned upstream commit, compatibility patch, required local
references, and package-input contract. Compiled dependencies are provided
separately to the packager and checked against the reviewed pins.
