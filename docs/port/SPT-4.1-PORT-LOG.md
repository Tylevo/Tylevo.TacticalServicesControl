# TSC SPT 4.1 Port Log

Updated: 2026-08-12

Status: **active SPT 4.1.2 tester port; not ready for distribution**.

This log separates static inspection, compilation, server boot, client load,
raid behavior, and multiplayer behavior. A pass in one column is not evidence
for another. The tracked documents intentionally use `<SPT_ROOT>` instead of a
developer's absolute machine path.

## Source And Immutable Baseline

- Port branch: `port/spt-4.1`.
- Authoritative starting commit:
  `78415317f7aca480a8d2b3408764070ae55cdac6`.
- TSC version remains `1.1.0` during the port.
- Published asset baseline:
  `Tylevo.TacticalServicesControl-v1.0.8-SPT4.0.13.zip`.
- Baseline byte length: `41,236,560`.
- Baseline SHA-256:
  `C3C0390CC6641E5F82C99C3E57D8AEDA358FD0278E667E6616E53963EB2FCB8D`.

The baseline archive, hash, per-bundle hashes, and historical 4.0.13 paths are
immutable evidence. The SPT 4.1 packager reads only the eight pinned bundle
entries. For the two historical server bundles, it verifies their original
`SPT/user/mods/...` archive entries and stages the same bytes at
`SPT_Runtime/user/mods/...`; it does not edit or relabel the source archive.

## Exact Target Evidence

Target: **SPT 4.1.2 / EFT 0.16.9.5.40743**.

The clean reference installation was inventoried before installing any TSC,
WTT, UnityToolkit, or Fika files. Its server runtime declares `net10.0`,
`Microsoft.NETCore.App` 10.0.0, and `Microsoft.AspNetCore.App` 10.0.0.

| File | File/product version | SHA-256 |
| --- | --- | --- |
| `SPT_Runtime/SPT.Server.dll` | `4.1.2`; `4.1.2-RELEASE+cf04a11.20260806.cf04a1120c0bc1626bfdb1bce8154a9d3607f303` | `D85C0C5E220628F3DA6660DCF6EE24FD9F4169BF4BA3B06A1CFAEE6801D8B1D2` |
| `SPT_Runtime/SPTarkov.Server.Core.dll` | `4.1.2`; `4.1.2-RELEASE+cf04a11.20260806.cf04a1120c0bc1626bfdb1bce8154a9d3607f303` | `57F5B3600E6FDCB7CDF31832A32EF5B88B51FD0E98B1F20BECF726B4B5F7CFD3` |
| `EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll` | EFT `0.16.9.5.40743` target; assembly file metadata is `0.0.0.0` | `43A539F5AD00FCCD87EE54A084D8DBE1C5F63D12F8D855C8A392D68B3A1DEAF9` |
| compile-only `410x/hollowed.dll` | Exact-build SPT.Modules `Shared/Hollowed` output; `Assembly-CSharp, Version=0.0.0.0`; 8,696,320 bytes; never packaged | `E40F6E470CD3C09E827900EFE98BB490920E97CAE962880DCA23DDF2A78E501C` |
| `BepInEx/plugins/spt/spt-common.dll` | `4.1.2+8dea32ab780eb32eecb444e5f6d318309651b241` | `67B1F4CA27720E69526955877FB9D8BA515A78498D949A1A630B4B2DA4C02C60` |
| `BepInEx/plugins/spt/spt-reflection.dll` | `4.1.2+8dea32ab780eb32eecb444e5f6d318309651b241` | `5EBC6E889510FF560EB486AC9224FCD72EC09E347C8523B2CE6578821C904B08` |
| `BepInEx/core/BepInEx.dll` | `5.4.23.5+57f1fb859bd4d0264cd2a59074d0e96c6a492a33` | `8255B28902886085C578B9E427D3073C97002DB85176D2090CDEDA90EF14CE70` |

Repository toolchain target: .NET SDK `10.0.201`, with SDK roll-forward
disabled. The SPT server and server-side regression project target `net10.0`;
client projects remain `netstandard2.1` unless a verified dependency requires
otherwise.

The `hollowed.dll` above was obtained byte-identically from the tracked
`References/hollowed.dll` in Project Fika checkout
`c89e28e41700093eb874589c440d3d8c77a25add` (Git blob
`1cb3df593f3079a976b65d97f3da7557f6207d39`). Fika documents that file as
output from SPT.Modules `project/Shared/Hollowed`. Its raw exact-build input is
the `Assembly-CSharp.dll` hash recorded above. No SPT.Modules commit or
generator command was independently verified, so this log does not invent one.

## Dependency Pins

The exact files used by the passing 4.1.2 build are pinned below. They remain
external dependencies and are never copied into the TSC archive.

| Dependency | Source pin and version | Exact binary evidence | Status |
| --- | --- | --- | --- |
| WTT Client CommonLib | `v3.0.3`, commit `d3f588d611774ab15f2b358760ac76ab3cb06efd`; assembly/file `3.0.3.0` | `WTT-ClientCommonLib.dll`; 154,112 bytes; SHA-256 `6C5B99E752D1AA614DA6E14B5FE56BBC1BCC0772C388DA57458F382DE3C34453` | Build pin verified; runtime pending |
| WTT Fika bridge | Same WTT `v3.0.3` source pin; assembly/file `3.0.3.0` | `WTT-ClientCommonLibFika.dll`; 8,704 bytes; SHA-256 `4CF38DECE5D5936A6616264B9FFEA111F284C32D67F20C5BA79615D863CEB610` | Build pin verified; Fika runtime pending |
| WTT Server CommonLib | Same WTT `v3.0.3` source pin; assembly/file `3.0.3.0`; TSC metadata accepts `~3.0.0` | `WTT-ServerCommonLib.dll`; 300,032 bytes; SHA-256 `30164AE02D6F39B9E02CBC569115C494954E4D187406D89E7A4E998AEDF5D754` | Server build, disposable bootstrap, and packaged-tree boot verified |
| WTT serialization prepatcher | Tracked by the same WTT `v3.0.3` source pin; assembly `1.0.0.0` | `FixPluginTypesSerialization.dll`; 140,800 bytes; SHA-256 `BD6B988E1D2EE0EC070E69A2711C79F72F4BB1930D6778CF0900C446DC70325C` | Required for custom phone item typing; startup pending |
| UnityToolkit plugin | Base tag/commit `v2.0.1` / `3c27a9798dc4396ca0b3dc765448a4221ff3007b`, rebuilt with the SPT 4.1 configuration, exact reference paths, deploy guards, and updated player-loop target; assembly `2.0.1.0` | `UnityToolkit.dll`; 8,704 bytes; SHA-256 `DBA886D4C8B118C389795B1196EC13742DA42771C50D190209C370F69C416E75` | Rebuild compiles; player-loop runtime pending |
| UnityToolkit prepatcher | Same rebuilt UnityToolkit source; assembly `0.0.0.0` | `UnityToolkit-Prepatcher.dll`; 5,120 bytes; SHA-256 `730156D8360A0BCA9024CF20F3886FBBD9509A7D793760FDD75C3BE186DFBDDE` | Rebuild compiles; startup pending |
| Project Fika client | Client `2.4.1`; checkout `c89e28e41700093eb874589c440d3d8c77a25add`, with the `v2.4.1` code tag at `fbd3814a`; assembly `2.4.1.0` | `Fika.Core.dll`; 1,967,104 bytes; SHA-256 `EB754C63F061B65B2F167109F9704F93E61C14F96D86A5AC192CFF21C83D5A7E` | TSC Fika projects compile; multiplayer runtime pending |
| Project Fika server | Compatible server checkout `2547995894e269f058b967da6b838f1506377f27`; server metadata `2.4.0`; the 2.4.1 client requires server `>=2.4.0` | Source/runtime pin for the later multiplayer matrix; no server binary is consumed by the TSC build | Runtime package and live version handshake pending |

The UnityToolkit pin is a deliberate 4.1.2 rebuild of version 2.0.1, not the
old unmodified 4.0-targeted binary. WTT must be installed as the complete
3.0.3 client/server dependency, including the serialization prepatcher, so
`UavDeviceItem` can be materialized as its registered custom runtime type.

## Port Contract

- Active target metadata: SPT `4.1.2`.
- Release configuration: `SPT-4.1 Release`, shared-reference selector `410x`.
- Expected tester archive:
  `Tylevo.TacticalServicesControl-v1.1.0-SPT4.1.2-TESTER.zip`.
- Archive roots: exactly `BepInEx/` and `SPT_Runtime/`.
- Client install root:
  `BepInEx/plugins/Tylevo.TacticalServicesControl/`.
- Server install root:
  `SPT_Runtime/user/mods/Tylevo.TacticalServicesControl/`.
- External WTT, UnityToolkit, Fika, SPT, EFT, and BepInEx DLLs are not bundled.
- Mutable config, storage, profiles, admin tokens, logs, PDBs, build caches,
  local reference packs, and absolute local paths are not bundled.

## Build And Validation Record

| Gate | Status | Evidence or next action |
| --- | --- | --- |
| Baseline commit and public asset pins | Pass | Commit, archive name, size, and SHA-256 recorded above |
| Clean 4.1.2 target inventory | Pass (static) | Exact assembly versions and hashes recorded above |
| Package-layout source inventory | Pass | `Test-PackageLayout.ps1 -ValidateSourceInputs`: 154 reviewed tracked files, four DLL mappings, eight pinned bundles |
| Release metadata verification | Pass | v1.1.0, SPT 4.1.2, SDK 10.0.201, and exact `-TESTER.zip` identity agree |
| CI-safe verification | Pass | Metadata, solution/deploy guards, JSON/JavaScript, package sources, hygiene, whitespace, and regression checks pass |
| Full local verification | Pass | Exact staged 4.1.2 references/dependencies; CI checks plus the five-project `SPT-4.1 Release` solution; four fresh runtime outputs; deployment suppressed |
| Server compilation | Pass | Exact 4.1.2 server references plus WTT Server CommonLib 3.0.3; 0 warnings, 0 errors |
| Core client compilation | Pass | Exact `hollowed.dll`, WTT 3.0.3, rebuilt UnityToolkit 2.0.1, and Fika 2.4.1 references; 0 errors. A focused standalone build reports 28 non-blocking warnings, including two obsolete inventory API calls and Unity serialized-field warnings |
| Fika bootstrap/interop compilation | Pass | Both projects build as part of the exact five-project solution; runtime loading remains untested |
| Full five-project solution | Pass | `SPT-4.1 Release`: 0 errors and four non-blocking warnings in the final clean build: two obsolete Core inventory-API calls and two regression-harness nullability warnings |
| Zero-dependency regression suite | Pass | 101 passed, 0 failed against the integrated 4.1 source |
| Disposable SPT server bootstrap | Pass (smoke) | Exact SPT 4.1.2 loaded TSC 1.1.0 and WTT Server CommonLib 3.0.3, completed database/startup callbacks and flea generation, initialized TSC config/dashboard/UH-60 messenger/fee journal, bound HTTPS/WSS on loopback port 6969, emitted `Server has started`, and accepted a TCP connection; the source install was untouched and the process stopped automatically |
| Evidence-backed packaged-tree server boot | Pass (rehearsal) | An earlier validated package stage booted with WTT Server CommonLib 3.0.3, reached `Server has started`, returned HTTP 200 from `/tsc/health`, and emitted no missing-bundle or fatal startup errors; this validates the package/runtime path but does not identify the final rebuilt archive |
| Dashboard public health route | Pass (smoke) | `GET https://127.0.0.1:6969/tsc/health` through the migrated `IHttpListener` returned HTTP 200 and valid JSON with `ok: true`, revision 1, and dashboard state |
| Dashboard admin/schema/config routes | Not run | Authentication, admin health, schema, config read/write, rejection paths, persistence, and migration still require explicit exercise |
| Client main-menu load without Fika | Not run | Use the pinned rebuilt UnityToolkit and complete WTT 3.0.3 install |
| Solo raid and second consecutive raid | Not run | Manual GUI gate |
| Fika human host/client | Not run | Must follow solo acceptance |
| Fika dedicated headless | Not run | Final optional multiplayer gate; A-10 executor remains experimental |
| Final tester ZIP creation and SHA-256 | Pending final rebuild | Freeze the exact release-branch candidate, rerun verification with a fresh external build-evidence file, and take the archive identity from the new external content-evidence sidecar |

The 101/101 result above is a current SPT 4.1 source result. Historical 4.0.13
build/package evidence remains useful baseline history but is not used to
claim 4.1.2 runtime compatibility.

The exact Core build uses the pinned `hollowed.dll`,
`ItemComponent.Types.dll`, `ItemTemplate.Types.dll`, rebuilt UnityToolkit, and
WTT/Fika dependencies. Its reproducible command shape is:

```powershell
dotnet build .\project\SamSWAT.FireSupport\SamSWAT.FireSupport.Core.csproj `
  --configuration "SPT-4.1 Release" `
  --property:SptDir="<PINNED_DEPENDENCY_ROOT>/" `
  --property:SptSharedAssembliesDir="<SPT_4_1_REFERENCE_ROOT>/" `
  --property:SkipTscDeploy=true `
  --no-restore --nologo
```

The server lifecycle migration also compiles against SPT 4.1.2's ordered DI
callbacks. TSC registers at `OnLoadOrder.GameCallbacks + 1` (`200001`): after
SPT runs `PerformPostDbLoadActions`, before trader/handbook/preset/ragfair
loaders, and between WTT 3.0.3 preload setup and deferred postload processing.
That is the exact 4.1 replacement for the old post-database lifecycle intent.
The disposable bootstrap and the earlier evidence-backed packaged-tree boot
confirmed that TSC and WTT reach SPT's completed startup. Client and in-raid
feature behavior remain separate gates.

## Commands

Run the following only with legally obtained local references and separately
installed, pinned 4.1-compatible dependencies:

```powershell
dotnet --version

powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\verify-ci.ps1

powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\verify-local.ps1 `
  -SptDir "<SPT_ROOT>" `
  -SptSharedAssembliesDir "<SPT_4_1_REFERENCE_ROOT>" `
  -Configuration "SPT-4.1 Release" `
  -EvidencePath "<EXTERNAL_OUTPUT>\v1.1.0-spt4.1.2-build-evidence.json"

powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\New-ReleasePackage.ps1 `
  -BaselineAssetArchive "<BASELINE_DIR>\Tylevo.TacticalServicesControl-v1.0.8-SPT4.0.13.zip" `
  -OutputDirectory "<EXTERNAL_OUTPUT>\v1.1.0-spt4.1.2-tester" `
  -BuildEvidencePath "<EXTERNAL_OUTPUT>\v1.1.0-spt4.1.2-build-evidence.json"
```

The evidence and output paths must be outside the repository, new, and free of
prior candidate contents. Every tracked candidate change invalidates earlier
archive identity evidence. For the final rebuild, create a new external
build-evidence file and an empty external output directory, then retain the
generated content-evidence sidecar beside the ZIP. Record final source and
archive identities only from those fresh external evidence files.

## Runtime Log Collection

For every manual test, preserve the full server console output and collect:

- `<SPT_ROOT>/SPT_Runtime/user/logs/spt/`
- `<SPT_ROOT>/SPT_Runtime/user/logs/requests/` when request logging is enabled
- `<SPT_ROOT>/BepInEx/LogOutput.log`
- `<SPT_ROOT>/Logs/` for the matching game session

Include the TSC DLL hashes, dependency hashes, profile role (solo, host,
client, or headless), map, service, payment source, and whether it was the
first or second consecutive raid. Redact account/profile identifiers and do
not commit logs to the repository.

## Open Runtime And Release Gates

There are no known dependency-pin, client-symbol, server-build, client-build,
Fika-build, or automated-regression blockers. Compilation does not close these
remaining gates:

1. Exercise dashboard admin health, schema, config read/write, authentication,
   rejection, migration, and clean-stop behavior. The packaged-tree bootstrap,
   bundle resolution, and public health route have passed.
2. Reach the main menu without Fika using the pinned UnityToolkit/WTT install.
   Confirm every Harmony target resolves exactly once, the custom Uplink item
   type loads through `FixPluginTypesSerialization.dll`, and no type-load or
   patch-registration errors occur.
3. Complete solo first-raid and second-consecutive-raid matrices, including
   phone hands, every service, cancel/failure paths, payment sources, cargo
   interaction/delivery, teardown, and profile persistence.
4. Repeat the applicable matrix with Fika 2.4.1 for a human host/client, then
   run the dedicated-headless gates. Keep the documented cargo requester and
   experimental A-10 restrictions fail-closed until their live results pass.
5. Before publication, commit the exact candidate on the release branch and
   rerun the evidence/package workflow from that branch revision into fresh
   external evidence and output paths. Earlier packaging rehearsal evidence
   does not identify the current candidate. Do not remove the `TESTER` suffix
   until all required manual gates pass.
