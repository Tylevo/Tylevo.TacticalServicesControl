# TSC v1.3.0 / SPT 4.1.4 tester validation

> Historical initial-port evidence. The current release is TSC v1.3.10 for
> SPT 4.1.5; see its [release notes](../release-notes-v1.3.10.md) and
> [validation summary](../validation/v1.3.10.md) for current results and limits.
> Pending statements below apply to the initial v1.3.0 candidate only.

Validated on 2026-09-04. Candidate source revision:
`ad49410bf5b809fc6aaf265d2dade40880210795`.

Artifact: `Tylevo.TacticalServicesControl-v1.3.0-SPT4.1.4-TESTER.zip`

SHA-256: `158384906280F876767409614ACD60C0ABB37A1A67FEBD3CD4657E15DB597AF5`

## Changes

- Targets SPT 4.1.4 while retaining TSC v1.3.0 functionality.
- Preserves the later SPT 4.1 menu, native config editor, input, server, and repaired Uplink bundle fixes alongside Danger Close and Seasonal Modifiers integration.
- Uses the verified `_names` field for the A-10 named-effects lookup.
- Pins the actual SPT 4.1.4 compile reference, WTT 3.0.6, and Fika 2.4.2; retains the prior SPT 4.1 rebuild of UnityToolkit 2.0.1.

## Results

| Check | Result |
| --- | --- |
| Full local verification | PASS: all five projects build, four runtime DLLs verified, deployment suppressed. |
| Regression suite | PASS: 168 passed, 0 failed. |
| CI checks | PASS: metadata, JSON, JavaScript syntax, source/package inventory, hygiene, whitespace, and deploy guards. |
| Build warnings | Four existing warnings: two obsolete inventory API calls and two regression nullability warnings. No errors. |
| Exact references | PASS: official SPT modules 4.1.4 reference and release DLLs verified by hash; critical dependency pins checked. |
| Serialized assets | PASS (static): all eight baseline bundles plus the repaired Uplink bundle parsed; nine game script types and their inherited serialized fields remain compatible. No additional bundle repair needed. |
| Final ZIP | PASS: 168 files, four TSC DLLs, eight bundles, only `BepInEx/` and `SPT_Runtime/` roots. Extracted package verified against content evidence. |
| SPT 4.1.4 startup | PASS: packaged TSC 1.3.0 and WTT 3.0.6 loaded; TSC configuration, UH-60 messenger, transfer journal, and HTTP listener initialized. |
| HTTP smoke | PASS: health, config, schema, dashboard HTML/JS/CSS, and legacy health route returned HTTP 200. |

The initial sandbox run could not load its Windows HTTPS certificate. Running
the same isolated test outside the sandbox succeeded without changing the mod
or server binaries. The test server was stopped afterward. No live profiles or
game installation were changed.

Client startup, menu interaction, phone equip/stow, solo raids, native config
editor interaction, and multiplayer behavior have not been tested on 4.1.4.
Fika client 2.4.2 compilation passed; human-host/client and dedicated-headless
gameplay remain pending. This is a tester package, not a gameplay-certified release.

## Installation

Extract the tester ZIP into an SPT 4.1.4 installation root. It installs TSC into
`BepInEx/plugins/Tylevo.TacticalServicesControl/` and
`SPT_Runtime/user/mods/Tylevo.TacticalServicesControl/`.
The archive excludes mutable configuration, storage, admin tokens, profiles,
and external dependencies.

Install WTT CommonLib 3.0.6 (client, server, and serialization prepatcher) and
the SPT 4.1 rebuild of UnityToolkit 2.0.1 separately. For multiplayer, use
Fika client 2.4.2 and its compatible server component on all peers. Exact pins
and upstream links are recorded in [SPT-4.1.4-PORT-LOG.md](SPT-4.1.4-PORT-LOG.md).

Historical SPT 4.1.2 test results remain separate from these results.
