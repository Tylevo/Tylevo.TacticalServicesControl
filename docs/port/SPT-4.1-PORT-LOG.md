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
immutable evidence. The SPT 4.1 packager verifies all eight pinned baseline
entries. For the two historical server bundles, it verifies their original
`SPT/user/mods/...` archive entries and stages them at
`SPT_Runtime/user/mods/...`. The loot bundle remains identical; the container
is replaced by the separately pinned 4.1.2 repair. The source archive is never
edited or relabeled.

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
| WTT Client CommonLib | `v3.0.3`, commit `d3f588d611774ab15f2b358760ac76ab3cb06efd`; assembly/file `3.0.3.0` | `WTT-ClientCommonLib.dll`; 154,112 bytes; SHA-256 `6C5B99E752D1AA614DA6E14B5FE56BBC1BCC0772C388DA57458F382DE3C34453` | Loaded in the superseded partial client run; final-candidate relaunch pending |
| WTT Fika bridge | Same WTT `v3.0.3` source pin; assembly/file `3.0.3.0` | `WTT-ClientCommonLibFika.dll`; 8,704 bytes; SHA-256 `4CF38DECE5D5936A6616264B9FFEA111F284C32D67F20C5BA79615D863CEB610` | Build pin verified; Fika runtime pending |
| WTT Server CommonLib | Same WTT `v3.0.3` source pin; assembly/file `3.0.3.0`; TSC metadata accepts `~3.0.0` | `WTT-ServerCommonLib.dll`; 300,032 bytes; SHA-256 `30164AE02D6F39B9E02CBC569115C494954E4D187406D89E7A4E998AEDF5D754` | Server build, disposable bootstrap, and packaged-tree boot verified |
| WTT serialization prepatcher | Tracked by the same WTT `v3.0.3` source pin; assembly `1.0.0.0` | `FixPluginTypesSerialization.dll`; 140,800 bytes; SHA-256 `BD6B988E1D2EE0EC070E69A2711C79F72F4BB1930D6778CF0900C446DC70325C` | Loaded in the superseded partial client run; final-candidate relaunch pending |
| UnityToolkit plugin | Base tag/commit `v2.0.1` / `3c27a9798dc4396ca0b3dc765448a4221ff3007b`, rebuilt with the SPT 4.1 configuration, exact reference paths, deploy guards, and updated player-loop target; assembly `2.0.1.0` | `UnityToolkit.dll`; 8,704 bytes; SHA-256 `DBA886D4C8B118C389795B1196EC13742DA42771C50D190209C370F69C416E75` | Loaded in the superseded partial client run; corrected-client relaunch pending |
| UnityToolkit prepatcher | Same rebuilt UnityToolkit source; assembly `0.0.0.0` | `UnityToolkit-Prepatcher.dll`; 5,120 bytes; SHA-256 `730156D8360A0BCA9024CF20F3886FBBD9509A7D793760FDD75C3BE186DFBDDE` | Loaded in the superseded partial client run; corrected-client relaunch pending |
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
| Package-layout source inventory | Refresh after commit | The manifest preserves all eight baseline pins and adds one tracked, exact SPT 4.1.2 Uplink override; tracked-source validation is expected to pass after the new repair script, override, provider, and tests are committed |
| Release metadata verification | Pass | v1.1.0, SPT 4.1.2, SDK 10.0.201, and exact `-TESTER.zip` identity agree |
| CI-safe verification | Partial | The expanded regression suite passes 109/109 and targeted builds/parsers are green; the tracked-source gate correctly awaits committing the new provider, repair script, asset override, and tests |
| Full local verification | Refresh required for final candidate | The earlier exact staged run passed end to end. The current hardening separately passes `verify-ci.ps1` and the exact five-project solution; create fresh external build evidence before packaging |
| Server compilation | Pass | Exact 4.1.2 server references plus WTT Server CommonLib 3.0.3; the singleton-lifetime correction rebuilds with 0 warnings, 0 errors |
| Core client compilation | Pass | Exact `hollowed.dll`, WTT 3.0.3, rebuilt UnityToolkit 2.0.1, and Fika 2.4.1 references; 0 errors. The exact solution reports only the two obsolete Core inventory-API warnings |
| Fika bootstrap/interop compilation | Pass | Both projects build as part of the exact five-project solution; runtime loading remains untested |
| Full five-project solution | Pass | Exact `SPT-4.1 Release`: 0 errors and two non-blocking warnings, both obsolete Core inventory-API calls |
| Zero-dependency regression suite | Pass | 109 passed, 0 failed against the integrated 4.1 source |
| Server DI lifetime contract | Pass | `FireSupportServerConfigService` and `FireSupportAuthorizationLedger` explicitly use `Injectable(InjectionType.Singleton)`, so load callbacks, HTTP listeners, and transaction consumers resolve the same initialized state |
| Disposable SPT server bootstrap | Pass (smoke) | Exact SPT 4.1.2 loaded TSC 1.1.0 and WTT Server CommonLib 3.0.3, completed database/startup callbacks and flea generation, initialized TSC config/dashboard/UH-60 messenger/fee journal, bound HTTPS/WSS on loopback port 6969, emitted `Server has started`, and accepted a TCP connection; the source install was untouched and the process stopped automatically |
| Evidence-backed packaged-tree server boot | Pass (rehearsal) | An earlier validated package stage booted with WTT Server CommonLib 3.0.3, reached `Server has started`, returned HTTP 200 from `/tsc/health`, and emitted no missing-bundle or fatal startup errors; this validates the package/runtime path but does not identify the final rebuilt archive |
| Live installed dashboard shell | Pass (smoke) | The exact installed 4.1.2 target returned HTTP 200 for `/tsc/health` (432 B), `/tsc/admin` (3,047 B), `/tsc/admin/app.mjs` (20,289 B), and `/tsc/admin/styles.css` (14,538 B) after the lifetime correction |
| Live-install preservation and teardown | Pass (smoke) | The TSC config SHA-256 matched its predeployment backup after the route exercise; the SPT server, launcher, and game were all left stopped |
| Native SPT config editor | Pass (static/build/shell smoke) | Curated `IConfigEditorConfigProvider` compiles against exact 4.1.2 Web APIs and routes load/apply/save through TSC normalization, validation, revision-conflict detection, and atomic persistence. An isolated exact server boot returned HTTP 200 for `/configs` without a provider exception; interactive Blazor selection/save/reload remains open |
| Missing UH-60 artwork fallback | Pass (isolated server smoke) | With the custom Pilot PNG deliberately absent, startup inherited the exact native BTR Driver avatar and registered the isolated Pilot identity without requesting the retired hard-coded avatar route; client-side portrait rendering remains a visual gate |
| Dashboard schema/config/authentication routes | Not run | Admin health, schema, config read/write, authentication, rejection paths, persistence, and migration still require explicit exercise |
| Exact InputManager target and readiness | Pass (static/build) | Targets only `InputManager.Create(KeyGroup[], AxisGroup[], float, bool)`, throws if absent, and fails spotter readiness after five seconds; installed build needs manual relaunch |
| Adjacent-row-first menu spacing | Pass (static/build) | Trade-to-Hideout, Hideout-to-Exit, then cached native spacing; trusted cap 160 px, ambiguous Play-to-Character cap 90 px; installed build needs visual confirmation |
| Client main-menu load without Fika | Partial/failing (superseded build) | Prior session loaded TSC/WTT/UnityToolkit and reached the menu, but the old Core logged ambiguous `InputManager.Create` resolution and exhibited the wide-row spacing defect; not a pass |
| Solo raid and second consecutive raid | Partial/failing (superseded build) | Prior session entered one solo raid, but targeting services were not registered after the input patch failure; corrected first raid and second consecutive raid remain unrun |
| Corrected isolated solo visual smoke | Partial pass (user-observed) | Menu spacing and targeting/support flow were reported working; A-10 flyover, impact effects, and tracers were visible, and passenger UH-60 extraction completed. A-10 damage, second-raid persistence, the repaired Uplink bundle, cargo mail, and every Fika path remain unverified |
| Final live Core deployment | Pending manual relaunch | 645,120-byte Core SHA-256 `D7A9124C3D29A252ED235BB3BE2B24EEC9D314A39E814B472482E92BA8C8A2CE` installed at the standard client path; prior `B65388AB...` build backed up; game/server stopped |
| Uplink animator/hash repair | Pass (binary/static) | Deterministic override length 1,167,863; SHA-256 `8C9F8D8878076D4FFCB2687D62609F606552B3E9F3529FBE584DF79E43365861`; only UsableHandsPrefab layer defaults and AnimatorControllerStaticData `OutUse` hash changed. Corrected equip/stow runtime replay remains open |
| Fika human host/client | Not run | Must follow solo acceptance |
| Fika dedicated headless | Not run | Final optional multiplayer gate; A-10 executor remains experimental |
| Final tester ZIP creation and SHA-256 | Pending final rebuild | Freeze the exact release-branch candidate, rerun verification with a fresh external build-evidence file, and take the archive identity from the new external content-evidence sidecar |

The corrected live Core is installed at
`<SPT_ROOT>/BepInEx/plugins/Tylevo.TacticalServicesControl/Tylevo.TacticalServicesControl.Core.dll`.
It is 645,120 bytes with SHA-256
`D7A9124C3D29A252ED235BB3BE2B24EEC9D314A39E814B472482E92BA8C8A2CE`.
Before replacement, the immediately prior 645,120-byte build, SHA-256
`B65388AB7DCAF48A532DCD074B5F7037DFF6C121CA5A7E31E69A552C2BCC5986`,
was copied to
`<WORKSPACE>/work/live-backups/tsc-client-final-hardening-20260812-112848/`.
That rollback copy is external evidence, not a release input. The game and
server were stopped after deployment. No manual client relaunch has exercised
the `D7A912...` build, so neither the exact input capture nor visual menu
spacing is recorded as a runtime pass.

The 109/109 result above is a current SPT 4.1 source result. Historical 4.0.13
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
SPT 4.1 DI also requires the mutable `FireSupportServerConfigService` and
`FireSupportAuthorizationLedger` to be registered explicitly as singletons.
That prevents server load and HTTP/transaction consumers from receiving
different config paths, revisions, or ledger state. The exact server rebuild
passes with 0 warnings and 0 errors, and the expanded regression suite passes 109/109
regressions, and the live installed route smoke above confirms that the
initialized dashboard state reaches the HTTP listener without modifying the
existing config. Client and in-raid feature behavior remain separate gates.

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
   bundle resolution, live installed public health/admin shell/static assets,
   config-preservation check, and stopped-process teardown have passed.
2. Relaunch the installed `D7A912...` Core without Fika using the pinned
   UnityToolkit/WTT install. Confirm every Harmony target resolves exactly once,
   the exact four-parameter input patch captures a ready manager, the custom
   Uplink item type loads through `FixPluginTypesSerialization.dll`, and the
   adjacent-row menu policy produces one visually correct native interval.
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
