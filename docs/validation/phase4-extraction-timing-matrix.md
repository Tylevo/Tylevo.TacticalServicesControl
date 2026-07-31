# Phase 4 Standard Extraction Timing Validation Matrix

This matrix applies only to standard **UH-60 Extraction**
(`ESupportType.Extract`). Wire service value `10` now represents
**UH-60 Cargo Transfer** and is not an extraction service. Cargo has no
extraction countdown, cannot complete extraction, and must not route through
the Fika extraction flow.

Cargo dispatch, wait-window, speed, transfer, and “never extract” checks live
in
[`helicopter-item-transfer-matrix.md`](helicopter-item-transfer-matrix.md).
The released `PriorityExfil` enum value, configuration key, authorization
credit, and artwork names remain compatibility identifiers for Cargo. The
persisted `priorityExfil.extractTimeSeconds` value is legacy data only: it must
remain readable and round-trippable but cannot affect runtime behavior.

Status:

- Historical Phase 4 implementation checkpoint `ae02516` was reviewed, built,
  and installed during development. It is retained as provenance only and is
  not the current Phase 7 candidate identity.
- v1.1.0 Phase 7 candidate record: **OPEN - final commit and package manifest
  not yet recorded**.
- Standard Extraction live acceptance: **OPEN - not yet run**.
- Every test row below starts `OPEN`; this document records expected evidence,
  not a test result.
- Phase 2 Fika transaction validation remains **OPEN**. Timing evidence does
  not close acceptance, consumption, commit, refund, duplicate, or
  disconnect-settlement rows.
- Phase 3 physical-phone UAV live acceptance remains **OPEN**.

## Phase 7 Candidate Record

Complete this record before changing any live row from `OPEN`. The commit,
archive, and manifest must describe the exact four-DLL set installed on every
participant.

| Field | Value |
| --- | --- |
| Candidate version | `v1.1.0` |
| Candidate status | `OPEN - final Phase 7 package not yet recorded` |
| Candidate commit | `TO RECORD` |
| Release archive filename / SHA-256 | `TO RECORD` |
| Package manifest SHA-256 | `TO RECORD` |
| Build evidence SHA-256 | `TO RECORD` |
| Content evidence SHA-256 | `TO RECORD` |
| Core DLL SHA-256 | `TO RECORD` |
| Server DLL SHA-256 | `TO RECORD` |
| Fika Interop DLL SHA-256 | `TO RECORD` |
| Fika bootstrap DLL SHA-256 | `TO RECORD` |
| Config schema / authorization-ledger schema | `3 / 5` |
| Evidence root | `TO RECORD` |

## Contract Under Test

Standard Extraction owns four active timing values:

- `extraction.dispatchDelaySeconds`;
- `extraction.waitTimeSeconds`;
- `extraction.extractTimeSeconds`;
- `extraction.speedMultiplier`.

The server, client, Fika authority, accepted request, and local extraction
point must use one immutable snapshot of those values. The supported safety
relationship is:

`waitTimeSeconds >= ceil(extractTimeSeconds + 1.0)`

Only the requester receives a functional extraction point and countdown.
Observers may render one helicopter visual but cannot start a timer or extract.
A dedicated headless authority owns admission and dispatch without creating
client presentation objects locally.

## Test-Set Safety

Before each topology:

1. Stop SPT, the game, launcher, every Fika client, and any dedicated headless
   process before installing DLLs.
2. Record the candidate commit, package manifest, and Core, Server, Fika
   Interop, and Fika bootstrap SHA-256 values on every participant. Use one
   matched v1.1.0 four-DLL candidate everywhere.
3. Back up the complete TSC storage and configuration directories while the
   server is stopped. Use disposable profiles for death, disconnect,
   cancellation, and abrupt raid-end rows.
4. Record `configSchemaVersion`, `pendingUseTimeoutSeconds`, the starting
   server config revision, Fika timing revision, and all four effective
   standard-Extraction timing values.
5. Synchronize participant clocks closely enough to compare host, client, and
   server logs. Record video with a visible timer or external stopwatch.
6. Preserve pre-run and post-run authorization counts, but assess transaction
   correctness against the still-open Phase 2 matrix.

Do not edit multiple fields between repetitions unless a row explicitly calls
for a revision race.

## Deliberately Distinct Test Profiles

| Field | Revision A | Revision B |
| --- | ---: | ---: |
| `dispatchDelaySeconds` | 9 | 1 |
| `waitTimeSeconds` | 45 | 24 |
| `extractTimeSeconds` | 14 | 6 |
| `speedMultiplier` | 0.8 | 1.5 |

Both revisions satisfy the safety relationship. The values are intentionally
far apart so a stale revision, local default, partial snapshot, or host-sync
failure is visible.

Measure these boundaries separately:

- target confirmation or authority admission to runtime dispatch;
- dispatch to landed/zone-ready;
- landed/zone-ready to helicopter departure;
- requester zone entry to extraction completion while continuously inside.

For speed, compare dispatch-to-ready duration on the same map and target
geometry. Visual impression alone is insufficient.

## Required Evidence

For every row, capture:

- row ID, tester/date, evidence location, candidate commit, package identity,
  topology, participant role, map, target location, profile ID, config schema,
  server revision, Fika revision, pending timeout, and all four DLL hashes;
- the effective dispatch, wait, extraction-countdown, and speed values;
- authorization count before/after and support request identity where logged;
- timestamps for confirmation, acceptance, dispatch, zone-ready, zone
  enter/exit/re-entry, extraction, departure, cancellation, death, and teardown
  as applicable;
- requester video showing the helicopter and countdown, plus observer video
  where requester isolation matters;
- requester, authority, and SPT server logs from request through teardown;
- confirmation that no non-requester received a functional extraction trigger;
- the next raid’s initial state for cleanup and repeat-raid rows.

Status values are `OPEN`, `PASS`, `FAIL`, or `BLOCKED`. Attach the evidence
location before changing a row from `OPEN`.

## Configuration And Migration Matrix

| ID | Scenario | Action | Required result | Status |
| --- | --- | --- | --- | --- |
| P4-C01 | Exact wait/countdown boundary | Submit extract `10`, wait `10`, then extract `10`, wait `11` through the dashboard. Submit fractional extract `10.5`, wait `11`, then extract `10.5`, wait `12` through the supported direct config/reload path. | `10/10` and `10.5/11` reject. `10/11` and `10.5/12` accept. Rejection preserves the previous revision and all unrelated service settings. | OPEN |
| P4-C02 | Unsafe startup repair | With SPT stopped and a backup saved, place schema-2 standard timing with extract `10.5`, wait `11`, and distinctive valid dispatch/speed values in the config, then start once. | Startup migrates to schema 3, repairs wait to `12`, and preserves valid dispatch, speed, prices, profiles, and ledger state. A pre-currency input defaults to RUB. | OPEN |
| P4-C03 | Unsafe update/reload rejection | Submit the same unsafe standard relationship through the dashboard/API and supported reload path while a valid revision is active. | The update rejects instead of partially applying. The last valid revision remains authoritative. | OPEN |
| P4-C04 | Local fallback safety | Disable shared server-URL timing, clear server/synced tuning, set local standard wait `10` and extract `30`, then request standard Extraction in solo. | Runtime warns and snapshots an effective wait of `31`, preventing departure before extraction completes. The BepInEx file is not silently rewritten. | OPEN |
| P4-C05 | Pending authorization timeout | With persistence enabled, try timeout `154`, then `155`; separately run the supported `120`-second dispatch with the normal `180`-second timeout. | Enabled persistence rejects `154` and accepts `155`; the request cannot expire before its single commit. The constraint is skipped only while persistence is disabled. | OPEN |
| P4-C06 | Standard schema/default migration | Test the published schema-less v1.0.8 config, a schema-less custom standard dispatch, a missing/null Extraction section, a clean install, and a schema-2 explicit standard dispatch `0`. | Inputs save as schema 3. Schema-less standard dispatch retains the historical effective `8` seconds; missing/null receives safe defaults; schema-2 explicit `0` remains immediate. Legacy Cargo/`PriorityExfil` fields remain compatible but do not participate in this extraction test. | OPEN |

## Solo Matrix

| ID | Scenario | Action | Required result | Status |
| --- | --- | --- | --- | --- |
| P4-01 | Standard timing contract | Save Revision A, request standard Extraction, remain outside until ready, then remain inside through extraction. | Dispatch occurs after 9 seconds, speed `0.8` is used, the wait window is 45 seconds, and the requester countdown starts at 14 seconds. | OPEN |
| P4-02 | Zone leave/re-entry reset | Enter, allow more than half the countdown to elapse, leave completely, wait, and re-enter twice, including one rapid boundary crossing. | Exit closes the countdown without extracting. Every re-entry starts at 14 seconds. One countdown/coroutine exists and one completed hold extracts once. | OPEN |
| P4-03 | Revision A to B | Complete or cancel Revision A, save Revision B, confirm the revision, and issue a new request. | All four values in the new request come from Revision B; no Revision A value survives. | OPEN |
| P4-04 | Active-request revision race | Save Revision B separately during Revision A dispatch, arrival, and active countdown in three repetitions, then issue a fresh request. | Each active request remains one immutable Revision A snapshot. The next request uses all Revision B values. | OPEN |
| P4-05 | Pre-accept cancellation | Cancel before confirmation, then cancel after reservation but before authoritative dispatch where test controls permit. | No helicopter, trigger, countdown, delayed dispatch, or callback survives. Settlement occurs once under the Phase 2 contract. | OPEN |
| P4-06 | Death after acceptance | Die once during dispatch and once during an active countdown. | Timer, trigger, coroutine, and request state tear down deterministically; death cannot later extract or dispatch a duplicate. | OPEN |
| P4-07 | Raid-end teardown | End the raid during dispatch, arrival, wait, and countdown in separate repetitions. | No helicopter, trigger, countdown, callback, or accepted timing leaks into the menu or next raid. | OPEN |
| P4-08 | Repeat raids | Run at least three raids and perform one leave/re-entry before each successful extraction. | Every raid starts clean and each request uses its accepted revision exactly once. | OPEN |
| P4-09 | Packaged/migrated runtime | Run fresh schema-3, migrated published-v1.0.8, and migrated schema-2-zero configurations. | Every exposed standard timing field affects gameplay as labeled; historical eight-second and explicit-zero migration behavior match P4-C06. | OPEN |

## Fika Matrix

| ID | Topology / scenario | Action | Required result | Status |
| --- | --- | --- | --- | --- |
| P4-10 | Human host requester | Have the human host request standard Extraction while a client observes and stands in the rendered landing zone. | Every peer renders at most one visual. Only the host receives the functional point/countdown and can extract. | OPEN |
| P4-11 | Client requester / human host | Have a client request standard Extraction, leave/re-enter, and complete it while the host stands in its rendered zone. | Host timing governs all four values. Only the requester’s countdown resets to 14 seconds and only that client extracts through Fika; the hosted session remains alive. | OPEN |
| P4-12 | Two-client isolation | Client A requests standard Extraction while Client B crosses the rendered landing zone and observes the full lifecycle. | Client B cannot start, reset, close, complete, or relabel A’s timer. The accepted request executes and settles once. | OPEN |
| P4-13 | Human-host revision race | Send a stale Revision A request after the host applies B, then retry. Separately apply B during accepted A dispatch, arrival, and countdown. | The stale request rejects as `ExtractionTimingContractChanged`; retry uses B. Each already accepted request remains wholly A. | OPEN |
| P4-14 | Client requester / dedicated headless | Have a client complete standard Extraction through a dedicated headless authority, including leave/re-entry. | Headless owns validation and dispatch without local presentation. Non-headless peers render once; only the requester gets a functional countdown and extracts through Fika. | OPEN |
| P4-15 | Headless revision race | Repeat P4-13 against dedicated headless authority. | Pre-snapshot stale timing rejects; accepted A remains immutable and the next request receives B. | OPEN |
| P4-16 | Death, disconnect, and cancellation | Against human and headless authorities, cancel before dispatch, die during countdown, and disconnect during the wait window in separate runs. | Authority state settles deterministically; no other player inherits the trigger or extracts, and reconnect/next raid starts cleanly. | OPEN |
| P4-17 | Fika teardown and repeats | End/restart human-host and headless raids during dispatch, wait, and countdown, including one abrupt client exit. | No helicopter, trigger, timer, snapshot, requester mapping, or callback leaks across the raid boundary. | OPEN |
| P4-18 | Matched-protocol preflight | Before startup, compare all four DLL hashes on every participant with one reviewed build manifest. | One exact matched set is installed. Any mismatch is corrected before testing or the row is `BLOCKED`. | OPEN |

## Failure Triage

On failure:

1. Preserve every participant log, video, config, revision, DLL hash, and
   pre/post ledger snapshot from the first run.
2. Classify the first divergence as config validation, snapshot/revision,
   Fika serialization, dispatch, speed, wait window, countdown, extraction
   flow, settlement, or teardown.
3. Compare the observed value with Revision A, Revision B, packaged defaults,
   and historical hardcoded values.
4. Reproduce the smallest row while changing only one field or lifecycle
   boundary.
5. Keep Phase 2 open for request identity, acceptance, duplicate execution,
   commit/refund, disconnect settlement, or cross-profile failures.

## Exit Record

Phase 4 is live-complete only when every applicable row has evidence and:

- standard Extraction uses its labeled dispatch, speed, wait, and countdown;
- one accepted request uses one immutable authority revision in every supported
  topology;
- zone re-entry, cancellation, death, disconnect, raid teardown, and repeat
  raids cannot preserve or duplicate a countdown, trigger, helicopter, or
  completion callback;
- invalid `wait < ceil(extract + 1)` relationships cannot become an accepted
  paid standard-Extraction request;
- no Cargo request enters the standard extraction timing/countdown/Fika-extract
  path; and
- default migration and matched-client requirements are documented.

Do not mark Phase 4 complete from solo success alone. Phase 2, Phase 3, and the
Cargo/item-transfer matrix remain separately open until their own live evidence
is complete.
