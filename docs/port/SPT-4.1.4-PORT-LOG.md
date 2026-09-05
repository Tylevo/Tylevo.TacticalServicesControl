# SPT 4.1.4 Compatibility Log

> This log preserves the initial v1.3.0 port audit and dependency provenance.
> Current release behavior and validation are recorded in
> [v1.3.8 release notes](../release-notes-v1.3.8.md) and
> [v1.3.8 validation](../validation/v1.3.8.md).

## Candidate and scope

- TSC version: `1.3.0` (unchanged).
- Target: SPT `4.1.4`, EFT `0.16.9.5.40743`.
- Candidate archive: `Tylevo.TacticalServicesControl-v1.3.0-SPT4.1.4-TESTER.zip`.
- Assessment date: 2026-09-04.
- This candidate preserves the v1.3.0 Seasonal Modifiers/Danger Close work and
  the later SPT 4.1 input, menu, config-editor, DI, and bundle repairs.

Historical SPT 4.1.2 dependency pins, compilation, server/dashboard smoke, and
partial client findings remain in [SPT-4.1-PORT-LOG.md](SPT-4.1-PORT-LOG.md).
They do not establish 4.1.4 runtime compatibility. No live installation or
profile changes are required to build this candidate; use `SkipTscDeploy=true`.

## Official release and source audit

The [SPT 4.1.4 release](https://github.com/SP-Tushonka/build/releases/tag/4.1.4)
retains EFT build 40743, updates the game assembly, and identifies an exception
to general 4.1.x mod compatibility for renamed serialized fields. The release
requires .NET and ASP.NET runtime 10.0.9. Those runtimes are installed in the
local validation environment; the project's SDK remains pinned by `global.json`.

The [official 4.1.4 modding notes](https://wiki.sp-tushonka.com/en/modding/SPT_41_Modding/414_Changes)
give the following exact field map. A direct reference requires renaming;
string reflection can compile and still fail at runtime. A bundle containing
one of these components with the old field names needs rebuilding against the
matching assembly to prevent silently lost serialized values.

| Type | Through 4.1.3 | 4.1.4 |
| --- | --- | --- |
| `EFT.Hideout.MultiObjectAmbiance+AmbianceAffectedComponent` | `String` | `MethodName` |
| `EFT.Quests.ConditionArenaPreset` | `String` | `classIds` |
| `WsRequestJson` | `String` | `Method` |
| `WsResponseJson` | `String` | `Method` |
| `EFT.Skill` | `ESkillClass` | `Class` |
| `EFT.Settings.Sound.SoundSettingsGroup` | `_gameSetting` | `InterfaceVolume` |
| `DebugBotProfilesStructContainer` | `DebugBotProfilesStruct` | `Struct` |
| `RuntimeInspector.Debugger` | `Texture2D` | `ClassTex` |
| `RuntimeInspector.Debugger` | `Texture2D_1` | `MethodTex` |
| `RuntimeInspector.Debugger` | `Texture2D_2` | `classTex` |
| `RuntimeInspector.Debugger` | `Texture2D_3` | `methodTex` |
| `RuntimeInspector.Debugger` | `GUIStyle` | `ClassTypeStyle` |
| `RuntimeInspector.Debugger` | `GUIStyle_1` | `ClassTypeStyle2` |
| `RuntimeInspector.Debugger` | `GUIStyle_2` | `ClassTypeStyleSelected` |
| `EFT.HealthSystem.EffectsSettings+ZombieInfectionSettings` | `СumulativeTime` | `float_0` |

The first character of `СumulativeTime` is Cyrillic `С` (U+0421).

The TSC source search found no direct or string-based references to the listed
types and old fields. This is a source finding only; inspect the eight packaged
Unity bundles separately, including the repaired Uplink container. TSC's own
serialized script types are not automatically affected by this field map.

Additional reflection checks cover `GesturesMenu._gesturesBindPanel`,
`InputNode._children`, the exact `InputManager.Create` overload, and named
impact effects. Assembly inspection identified `Systems.Effects.Effects._names`
in both 4.1.2 and 4.1.4; the older TSC `dictionary_1` lookup had relied on its
`EffectsArray` fallback. Confirm the candidate uses the observed field and
retains the fallback. Server startup must also validate TSC's expectation of
exactly two stock `BtrDeliveryCallbacks` service descriptors.

## Reference and dependency provenance

The official SPT archive is
`SPT-4.1.4-40743-072d534.7z`, 154,677,284 bytes. Its SHA-256 matched the GitHub
release asset digest:
`BFC392E53ECF4CE2FF77C8C119FA5AF4552EA63A6FF4AE8B5DB663837ADC6B5B`.

The exact 4.1.4 compile-only `hollowed.dll` comes from SPT.Modules commit
`d52d9c99836b6d7dc5ad93852cd8032158df0f9c`: 8,705,024 bytes, SHA-256
`8E29BF643BA75530C82BD749D2814F3A45487257D4E9C544754C46E3A12D532D`.
It replaces the older 4.1.2 compile reference and must never be shipped.

Dependency inputs are [WTT CommonLib 3.0.6](https://github.com/WelcomeToThursday/WTT-CommonLib/releases/tag/v3.0.6),
[Fika client 2.4.2](https://github.com/project-fika/Fika-Plugin/releases/tag/v2.4.2),
and the existing SPT 4.1 rebuild of [UnityToolkit 2.0.1](https://github.com/ArysWasTaken/UnityToolkit/releases/tag/v2.0.1).
UnityToolkit's upstream latest tag is still 2.0.1; its unmodified pre-4.1 binary
is not the pinned rebuild below. WTT 3.0.6 includes the image-manager fix for
SPT 4.1.3. Fika 2.4.2 targets EFT 40743. These release facts do not substitute
for compilation and a matched client/server multiplayer test.

| Assembly | Informational/file version | Bytes | SHA-256 |
| --- | --- | ---: | --- |
| `SPT.Server.dll` | `4.1.4-RELEASE+072d534.20260904.072d5340dc91218d010778972d85bbbbc7b3d6a9` | 229376 | `2A07D3D64B9DCDA1697F4562BCB321AA2A2DF340EDCC7F809A374104000FD532` |
| `SPTarkov.Server.Core.dll` | `4.1.4-RELEASE+072d534.20260904.072d5340dc91218d010778972d85bbbbc7b3d6a9` | 5660160 | `8056B5BFE68C038767B4FC761BDE797C9056E2E6F710267C3F4363D84036501E` |
| `spt-common.dll` | `4.1.4+d52d9c99836b6d7dc5ad93852cd8032158df0f9c` | 26624 | `713343429010F81F630A652A6F51CA65F80A42CDD8FA489207810EA68AEF79A6` |
| `spt-reflection.dll` | `4.1.4+d52d9c99836b6d7dc5ad93852cd8032158df0f9c` | 22016 | `312823E4017202D714E35279C0D620C66731F0F8564B5ABB4FBDCB778A0D362B` |
| `UnityToolkit.dll` | `2.0.1+3c27a9798dc4396ca0b3dc765448a4221ff3007b` | 8704 | `DBA886D4C8B118C389795B1196EC13742DA42771C50D190209C370F69C416E75` |
| `Fika.Core.dll` | `2.4.2.0` | 1968128 | `7EBC9A97EF51719075CB2B888C54934E0AE47B1908CDAEB0A3656E6F415BD015` |
| `WTT-ClientCommonLib.dll` | `3.0.6+9e12f952d01556742befe666c10b51312e877887` | 156160 | `40B345CDC5D509028023989EFA8D96DD7DD4753257338D1F9D37A024FE9A1A3C` |
| `WTT-ServerCommonLib.dll` | `3.0.6+9e12f952d01556742befe666c10b51312e877887` | 304640 | `DC765A67977C97315CE776D1042980894520FA02DDE35C67DBE80DE8CF4B15E8` |

SPT, UnityToolkit, WTT, and Fika binaries remain external reference/dependency
inputs. The TSC archive must not redistribute them.

## Validation record

| Gate | Status | Evidence |
| --- | --- | --- |
| Official 4.1.4 field-map source audit | PASS | No matching TSC source references; details above. |
| Exact SPT archive/reference and dependency identity | PASS | Recorded sizes, versions, and hashes above. |
| Release metadata consistency | PASS | `tools/Test-ReleaseMetadata.ps1`: v1.3.0, SPT 4.1.4, SDK 10.0.201, expected tester filename. |
| Regression suite | PASS | Final candidate: 168/168 tests (160 feature tests plus eight port-hardening tests). |
| Complete CI-safe checks | PASS | Metadata, source/package inventory, hygiene, JSON/JavaScript, whitespace, and regressions. |
| Five-project 4.1.4 build, deployment disabled | PASS | Clean revision `ad49410`: 0 errors, four existing warnings, four runtime output hashes recorded. |
| Serialized bundle audit and package checks | PASS (static) | All eight baseline bundles and repaired Uplink parsed; serialized game fields compatible. Final ZIP: 168 files, four DLLs, eight bundles. |
| Disposable 4.1.4 server bootstrap | PASS | Packaged TSC and WTT 3.0.6 loaded; DI, config, UH-60 messenger/journal, and HTTP routes initialized. |
| Dashboard and native config editor | PARTIAL | Health/config/schema/dashboard assets return HTTP 200; editing, native editor interaction, authentication/revision/persistence exercise remain pending. |
| Client menu, phone equip/stow, and targeting | PENDING | Use the final matched package. |
| Solo A-10, UAV, UH-60 transfer/extraction, and repeat raids | PENDING | Record live outcomes and logs. |
| Fika human-host/client and dedicated-headless matrix | PENDING | Match every peer and dependency; retain experimental limitations. |

The exact artifact revision, checksum, and final evidence summary are in
[SPT-4.1.4-VALIDATION.md](SPT-4.1.4-VALIDATION.md).

Update this record only with evidence from the actual 4.1.4 candidate. Package
generation and a successful compile do not close the live gameplay gates.
