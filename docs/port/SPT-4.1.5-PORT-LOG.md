# SPT 4.1.5 Compatibility Log

This records the archive, compile references, and static startup audit for the
unreleased TSC `1.3.10` candidate targeting SPT `4.1.5` and EFT
`0.16.9.5.40743`, assessed on 2026-09-05. Candidate execution results belong in
the [v1.3.10 release notes](../release-notes-v1.3.10.md). This static audit alone
does not establish runtime compatibility. The maintainer later reported
successful local use; individual service checks were not documented and Fika
multiplayer remains untested.

## Official archive and source

The [official SPT 4.1.5 release](https://github.com/SP-Tushonka/build/releases/tag/4.1.5)
requires .NET and ASP.NET runtimes `10.0.9`. Its listed fix concerns server
validation of older Unity bundles. The release supports 4.1.x mods and
profiles, with the documented compatibility exceptions; it excludes 4.0.x mods.

Verified archive: `SPT-4.1.5-40743-7d7add5.7z`, **154,686,006 bytes**.
Its MD5 matches the release's published base64 digest `YSgw+K4K7aA9aug/GE8RtA==`:

- MD5: `612830F8AE0AEDA03D6AE83F184F11B4`.
- Independently calculated SHA-256: `5CC04274C88115730FE982FD12C7525D57E5FC64B6B7271AB3929383E3AC4432`.

The official 4.1.4 and 4.1.5 archives each contain 1,165 files, with no added or
removed file paths. Their inventory comparison identifies 21 changed files.
The [SPT.Modules 4.1.4-to-4.1.5 comparison](https://github.com/SP-Tushonka/modules/compare/4.1.4...4.1.5)
is identical: both tags resolve to commit
`d52d9c99836b6d7dc5ad93852cd8032158df0f9c`. This establishes source equivalence
for the existing compile-only hollowed game assembly. The SPT DLLs still carry
new `4.1.5.0` assembly versions and were refreshed from the new archive.

## Compile references and dependencies

The isolated reference preparation verified all 42 required project reference
files and all five critical reference pins. Six required DLLs changed from the
4.1.4 build: `spt-common`, `spt-reflection`, `SPTarkov.Common`, `SPTarkov.DI`,
`SPTarkov.Server.Core`, and `SPTarkov.Server.Web`. Remaining required game and
dependency references retain their previous bytes. `hollowed.dll` remains a
compile-only input and must never be packaged.

The SPT client/prepatch DLLs below identify product version
`4.1.5+d52d9c99836b6d7dc5ad93852cd8032158df0f9c`. The server DLLs identify
`4.1.5-RELEASE+7d7add5.20260905.7d7add556a6f781e9a531fa3b0cf4cc925986e03`.

| File | Bytes | SHA-256 |
| --- | ---: | --- |
| `hollowed.dll` | 8705024 | `8E29BF643BA75530C82BD749D2814F3A45487257D4E9C544754C46E3A12D532D` |
| `spt-prepatch.dll` | 30208 | `313325BEE42BE526274B8F40B232B0147071CE909E460E51AF2515B3252CD991` |
| `spt-common.dll` | 26624 | `4D825507D2172857E208BB8B991EE1EF73EAEB0D642509EC1EBD59E85C24E618` |
| `spt-reflection.dll` | 22016 | `8F8B5D8FA51A79C858ED9D40D630E6899DB4255DEF5CA83BF65F52915F26548C` |
| `SPT.Server.dll` | 229376 | `D9DF44F271558D8FA459759526529B9B1414CE97ACB17416FCEAFCF5284E852C` |
| `SPTarkov.Common.dll` | 48128 | `66554B9A7515362F0AFC8CDE58CD630E6BB0471921B3AA16DBAF191428C635D2` |
| `SPTarkov.DI.dll` | 16896 | `4AA9D16678D1CBE59F80294115BE4570E579B93B1F3AADEA6C460232E517904D` |
| `SPTarkov.Server.Core.dll` | 5660160 | `C502D59B03C625E918EFB4CEA5F836D26FC6D99D0A5BE1DF38C90F0A8098EC88` |
| `SPTarkov.Server.Web.dll` | 634368 | `9D3C393470D957091EBFFDD3F94E2AB114F1DCD720FCBEDBD442610F32A3D743` |
| `UnityToolkit.dll` (SPT 4.1 rebuild) | 8704 | `DBA886D4C8B118C389795B1196EC13742DA42771C50D190209C370F69C416E75` |
| `Fika.Core.dll` | 1968128 | `7EBC9A97EF51719075CB2B888C54934E0AE47B1908CDAEB0A3656E6F415BD015` |
| `WTT-ClientCommonLib.dll` | 156160 | `40B345CDC5D509028023989EFA8D96DD7DD4753257338D1F9D37A024FE9A1A3C` |
| `WTT-ServerCommonLib.dll` | 304640 | `DC765A67977C97315CE776D1042980894520FA02DDE35C67DBE80DE8CF4B15E8` |

Dependency versions remain WTT CommonLib `3.0.6`, Fika client `2.4.2`, and the
reviewed SPT 4.1 rebuild of UnityToolkit `2.0.1`. Their original provenance is
recorded in the [4.1.4 port log](SPT-4.1.4-PORT-LOG.md). UnityToolkit's exact
15-file inventory, including its two rebuilt DLLs, is pinned by the
[package contract](../../tools/package-layout.allowlist.json); its
[build guide and notices](../../tools/dependencies/unitytoolkit/README.md)
document the bundled dependency. SPT, EFT, WTT, and Fika binaries remain
external and are excluded from the TSC archive.

## Client startup gate versus server bundle validation

Static inspection of the official `spt-prepatch.dll` above confirms that
`PluginValidator` scans plugin DLLs for SPT assembly references and requires
the detected reference's major and minor versions to match the running SPT
version. Patch versions are not compared. A mismatch exits during game
prepatching, before ordinary plugin startup; this is separate from the server's
Unity bundle validation fix.

The unmodified upstream UnityToolkit `2.0.1` plugin has SHA-256
`A2EC73858992A6E573A1C53E2C1F7AF142E339ADBDFBC087FAEEE6A8EA1B6575`
and references `spt-reflection` `4.0.1.0`. It fails the observed 4.1.5 rule.
The pinned rebuild above references `4.1.2.0`, which satisfies that same
major/minor rule. This is a static compatibility finding, not a game launch or
gameplay test. The server bundle fix therefore does not remove the need for
the reviewed Toolkit rebuild in this installation.
