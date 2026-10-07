# Optional Pilot Questline validation

Scope: TSC 1.3.11 / SPT 4.1.5 with WTT CommonLib 3.0.6, extending the existing
Pilot Services work. The main download opens Pilot and sells the ₽50,000
phone immediately. The optional server content add-on enables the existing
three-quest progression. Both use the same four runtime DLLs and keep phone
loot injections disabled.

## Automated checks

- **295 regression tests passed, 0 failed**, including base access, native
  quest contracts, strict add-on completion gates, profile-bound permits,
  missing/malformed/mismatched add-on handling, immutable startup selection,
  purchases, refunds, commits, and interrupted transaction recovery.
- **7 dashboard interaction tests passed.**
- Full `tools/verify-local.ps1` passed using the pinned SPT 4.1.5 references.
  All four runtime DLLs built with `SkipTscDeploy=true`: 5 existing warnings,
  0 errors. CI source checks, release metadata, JSON, hygiene, and both package
  fixture suites passed. The ledger singleton source guard now accepts its
  existing `sealed partial` declaration while retaining the singleton check.
- Current main package layout passed: **173 files, 4 DLLs, 8 bundles**.
  Optional add-on layout passed: **8 content/documentation files, no DLLs or
  bundles**. The main archive rejects quest/add-on data; the add-on rejects
  extra binaries, mutable configuration, invalid paths, and version mismatch.
  Carried-forward bundles were checked against their pinned hashes and sizes.

Evidence is under the workspace's `work/tsc-pilot-addon/`:
`full-validation.log`, `package-input-hashes.json`, `base-layout/`, and
`addon-layout/`. Validation used a temporary Git index to include the new
source files without staging or changing the user's existing index. These
are local working-tree checks, not a clean release attestation or release ZIP.

The tested server build has SHA-256
`DFE2CAD9385722E2250038CD9779AD0CA5D373A644E6E927D2886FEBA8A65D10`.

## Isolated native server checks

**115 checks passed, 0 failed** against a disposable SPT 4.1.5 server with WTT
CommonLib 3.0.6: 47 lifecycle checks and 68 native quest checks. The server DLL
matched the build hash above. The isolated process is stopped and loopback
port 6994 is closed.

- Base fresh profiles start with Pilot unlocked, no introduction quests or
  repeater stock, and a working phone purchasable for exactly ₽50,000.
  Authenticated profiles receive permits; mismatched requesters are rejected.
- Installing the add-on gates an existing base profile's service and phone
  purchases while retaining already visible Pilot access. Fresh add-on
  profiles follow the native level-5 introduction, partial non-FIR handovers,
  native item consumption, repeaters, reward mail, and purchase unlocks.
- Final quest turn-in grants the working phone once and permits the completed
  requester independently of an unfinished profile. Native `QuestComplete`
  was called directly to isolate server rewards; it does not validate actual
  radio placement or extraction.
- Removing the add-on restores base phone and service access. Tokens issued
  before restart are invalid. Malformed and incomplete add-ons prevent usable
  startup instead of silently granting base access.
- A completed fixture was saved through native `/client/game/logout`, then
  verified on disk with all three Success quests, 29,500 XP, and its purchased
  phone before restart. After removal and normal `/client/game/start`, SPT
  removed the orphaned quest records while retaining the phone. Saving and
  reinstalling confirmed that the introduction must be completed again unless
  a compatible backup is restored. Installation/removal docs reflect this.

Native evidence is in `work/tsc-pilot-addon/server-smoke/`:
`native-summary.json`, `lifecycle-validation.json`, `quest-validation.json`,
and `native-retention-finding.json`. `harness-save-correction.json` records an
earlier discarded retention expectation from stopping before native save.
The corrected checks verify saved state explicitly. The isolated checkout
fixture selected the existing `StashRoubles` payment mode to exercise native
server checkout; shipping payment defaults were not changed. Real permission
tokens were held only in test-process memory, not logged or saved.

## Gameplay acceptance

Actual EFT placement at the weather-station antenna, interrupted placement,
death followed by a later successful extraction, coexistence with vanilla
Signal, phone/Services UI, and real solo/Fika dispatch remain untested on this
candidate. Follow the [questline checklist](../pilot-questline.md#validation)
and [Pilot Services checklist](../pilot-services-testing.md) before publication.
Native server quest completion checks do not establish field interaction or
client counter behavior.

No live installation or profile was changed. No release was published.
