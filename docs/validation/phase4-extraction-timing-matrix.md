# Phase 4 Extraction Timing Validation Matrix

Status:

- Historical Phase 4 implementation checkpoint `ae02516` was reviewed, built,
  and installed during development. It is retained as provenance only and is
  not the current Phase 7 candidate identity.
- v1.1.0 Phase 7 candidate record: **OPEN - final commit and package manifest
  not yet recorded**.
- Phase 4 live acceptance: **OPEN - not yet run**.
- Every test row below starts `OPEN`; this document records expected evidence,
  not a test result.
- Phase 2 Fika transaction validation: **still OPEN**. Extraction timing
  evidence does not close acceptance, consumption, commit, refund, duplicate,
  or disconnect-settlement rows.
- Phase 3 physical-phone UAV live acceptance: **still OPEN**. Phase 4 evidence
  does not close any UAV requester, loiter, or teardown row.

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

This matrix validates the server-authoritative timing contract for standard
Extraction and Priority Exfil. It covers dispatch delay, helicopter animation
speed, on-site wait window, extraction-zone countdown, configuration revision
consistency, trigger reset, and teardown. A clean build or a successful solo
run is not a Phase 4 live pass.

## Test-Set Safety

Before each topology:

1. Stop the SPT server, game, launcher, every Fika client, and any dedicated
   headless process before installing DLLs.
2. Record the candidate commit, package manifest, and Core, Server, Fika
   Interop, and Fika bootstrap SHA-256 values on every participant. Use one
   matched v1.1.0 four-DLL candidate everywhere. Mixed packet-contract builds
   are not a valid test set.
3. Back up the complete TSC storage and configuration directories while the
   server is stopped. The current contract is config schema 3 and authorization
   ledger schema 5; keep each backup with its matching Server DLL. Use
   disposable profiles for death, disconnect, forced cancellation, and abrupt
   raid-end rows.
4. Record `configSchemaVersion`, `pendingUseTimeoutSeconds`, the starting
   server config revision, the Fika timing revision, and the effective values
   for both services before entering the raid.
5. Synchronize participant clocks closely enough to compare host, client, and
   server logs. Record video at a visible timer or include an external
   stopwatch in the capture.
6. Grant or purchase enough authorizations to repeat a failed row without
   editing the ledger during the raid. Preserve pre-run and post-run
   authorization counts, but assess their transaction correctness against the
   still-open Phase 2 matrix.

Do not edit multiple fields between repetitions unless a row explicitly calls
for a revision race. Do not infer a timing pass from the service label alone.

## Deliberately Distinct Test Profiles

Use these valid profiles unless a row specifies packaged defaults or an invalid
combination. Record the actual saved values and revision in the evidence; the
table is a test recipe, not proof that the server accepted them.

| Field | Revision A: Extraction | Revision A: Priority Exfil | Revision B: Extraction | Revision B: Priority Exfil |
| --- | ---: | ---: | ---: | ---: |
| `dispatchDelaySeconds` | 9 | 2 | 1 | 11 |
| `waitTimeSeconds` | 45 | 20 | 24 | 40 |
| `extractTimeSeconds` | 14 | 5 | 6 | 13 |
| `speedMultiplier` | 0.8 | 1.8 | 1.5 | 0.8 |

Both profiles satisfy
`waitTimeSeconds >= ceil(extractTimeSeconds + 1.0)`. Values are intentionally
far apart so a stale standard value, stale priority value, local default,
partial revision, or host-sync failure is visible.

Measure these boundaries separately:

- **solo dispatch delay:** target confirmation/payment completion -> local
  runtime dispatch;
- **human-host Fika dispatch delay:** host canonical-snapshot/admission log ->
  local runtime start and accepted broadcast;
- **headless Fika dispatch delay:** host canonical-snapshot/admission log ->
  headless authority-accepted log, accepted broadcast, and requester visual
  start; headless intentionally has no local runtime/presentation;
- **arrival animation:** authoritative dispatch -> landed/zone-ready event;
- **wait window:** landed/zone-ready event -> helicopter departure;
- **zone countdown:** local player enter -> extraction completion while the
  player remains continuously inside.

Use frame/network tolerance when comparing a displayed countdown with logs, but
do not accept a value that matches the other service or revision. For speed,
record dispatch-to-zone-ready duration and verify that the higher multiplier is
materially faster on the same map and target geometry. Visual impression alone
is insufficient.

## Required Evidence

For every row, capture:

- row ID, tester/date, evidence location, candidate commit, package-manifest
  identity, topology, participant role, map, target location, service, profile
  ID, `configSchemaVersion`, server config revision, Fika timing revision,
  `pendingUseTimeoutSeconds`, and all four DLL hashes;
- both services' effective dispatch, wait, countdown, and speed values;
- authorization count before/after and support request identity where logged;
- timestamps for confirmation, acceptance, dispatch, zone-ready, zone
  enter/exit/re-enter, extraction, departure, cancellation, death, and raid
  teardown as applicable;
- requester video showing the helicopter and extraction countdown; observer
  video when host/client consistency or isolation matters;
- requester, human-host or headless-host, and SPT server log excerpts from
  request through teardown, including the canonical timing log and any exact
  rejection reason;
- the profile that received the functional `HeliExfiltrationPoint`; every
  other peer may render one local helicopter visual but must have no functional
  extraction trigger or countdown;
- the next raid's initial state for cleanup and repeat-raid rows.

Status values are `OPEN`, `PASS`, `FAIL`, or `BLOCKED`. Attach the evidence
location before changing a row from `OPEN`. Preserve the first failing run
before retrying with different values.

## Configuration and Migration Matrix

| ID | Scenario | Action | Required result | Status |
| --- | --- | --- | --- | --- |
| P4-C01 | Exact wait/countdown boundary | For both services, submit extract `10`, wait `10` and extract `10`, wait `11` through the dashboard. Submit fractional extract `10.5`, wait `11` and extract `10.5`, wait `12` through the direct API/config reload path because the dashboard uses whole-second steps. | `10/10` and `10.5/11` are rejected. `10/11` and `10.5/12` are accepted because the rule is `wait >= ceil(extract + 1)`. A rejection preserves the previous revision and the other service unchanged. | OPEN |
| P4-C02 | Unsafe startup repair | With the server stopped and a rollback copy saved, place a schema-2 timing with extract `10.5`, wait `11`, and otherwise valid distinctive dispatch/speed values in the config, then start the server once. | Startup migrates and saves config schema 3, logs an actionable timing repair, and saves wait `12`. Valid dispatch, speed, and numeric price values are preserved; the pre-currency input defaults `paymentCurrency` to `RUB`. No profile, authorization count, or ledger entry changes. | OPEN |
| P4-C03 | Unsafe update/reload rejection | Submit the same unsafe relationship through the dashboard/API and supported reload path while a valid revision is active. | The update/reload is rejected rather than repaired. The last valid revision remains authoritative and no partial field from either service is applied. | OPEN |
| P4-C04 | Local fallback safety | Disable shared server-URL timing, explicitly clear server/synced tuning, set both standard and priority local BepInEx wait `10` and extract `30`, and request each service in solo. | Each service warns and snapshots an effective wait of `31`, proving the local values were captured and preventing departure before extraction completes. The BepInEx file is not silently rewritten. | OPEN |
| P4-C05 | Pending authorization timeout | With persistence enabled, try timeout `154`, then `155`; separately run a `120`-second dispatch with the normal `180`-second timeout. Optionally repeat `154` with persistence disabled. | Enabled persistence rejects `154` and accepts `155`; the fixed minimum is `ceil(120 + 35) = 155`, independent of current dashboard dispatch values. The 120-second request cannot expire before its single commit. The constraint is skipped only while persistence is disabled. | OPEN |
| P4-C06 | Schema/default migration | Test the exact published schema-less v1.0.8 config with standard dispatch `0`, a schema-less custom nonzero standard dispatch, a schema-less missing/null Extraction section, a fresh packaged config, and a schema-2 explicit standard dispatch `0`. | Every legacy input saves as schema 3. Schema-less standard values retain the historical effective `8`-second delay and missing/null sections receive safe defaults. Pre-currency inputs default to `RUB` without converting numeric prices. A fresh config is schema 3 with standard `8` and priority `3`. A schema-2 explicit `0` survives migration/restart and dispatches immediately. | OPEN |

## Solo Matrix

| ID | Scenario | Action | Required result | Status |
| --- | --- | --- | --- | --- |
| P4-01 | Standard timing contract | Save Revision A, start a new solo raid, request standard Extraction, remain outside the zone until it is ready, then enter and remain inside through extraction. | Dispatch occurs after the configured 9 seconds, the standard 0.8 speed is used, the helicopter remains available for its configured 45-second window, and the zone starts at 14 seconds. No Priority Exfil value is used. | OPEN |
| P4-02 | Priority timing contract | On the same map and comparable target geometry, request Priority Exfil under Revision A and remain inside through extraction. | Dispatch occurs after 2 seconds, the 1.8 speed is materially faster than P4-01, the configured 20-second wait window is used, and the zone starts at 5 seconds. No standard Extraction value is used. | OPEN |
| P4-03 | Zone leave/re-enter reset | For each service under Revision A, enter the ready zone, allow more than half of its countdown to elapse, leave completely, wait, then re-enter twice. Include one rapid boundary crossing. | Exit immediately closes the countdown without extracting. Every re-entry starts from that service's full accepted value (14 standard or 5 priority), never from the remaining value or the other service. Only one active countdown/coroutine exists and one completed hold extracts once. | OPEN |
| P4-04 | Revision A -> B between requests | Complete or cancel a Revision A request, save Revision B, confirm its new revision, and issue each service again in a new valid request. | All four timing values for each new request come from Revision B. No A value survives in the later request, and the standard/priority mapping remains distinct. | OPEN |
| P4-05 | Active-request revision race | Confirm a solo request under Revision A, then save Revision B separately during dispatch, arrival, and an active zone countdown in three repetitions; after teardown issue a fresh request. Repeat for both services. | The request captures one internally consistent A snapshot before dispatch. Saving B cannot partially mutate, reset, shorten, or extend that request. The next request uses all B values. | OPEN |
| P4-06 | Pre-accept cancellation | Cancel or close the targeting flow before confirmation, then cancel after payment/begin but before authoritative dispatch where test controls permit. Repeat for both services. | No helicopter, zone, countdown, delayed dispatch, or completion callback survives cancellation. Authorization settlement occurs once according to the configured persistence policy; any settlement defect also remains a Phase 2 failure. A later request works normally. | OPEN |
| P4-07 | Death and post-accept teardown | Die once during dispatch and once while a zone countdown is active for each service. | Timer UI closes; helicopter, trigger, coroutine, and request completion state reach deterministic teardown; death cannot later produce extraction or a delayed duplicate dispatch. The next raid has no inherited zone or timer. Record, but do not infer, Phase 2 settlement status. | OPEN |
| P4-08 | Raid-end teardown | End/extract/abort the raid during dispatch, arrival, wait, and zone countdown across separate repetitions. | Scene transition removes the helicopter, extraction trigger, countdown UI, callbacks, and cached accepted timing. No log exception or completion from the old raid appears in the menu or next raid. | OPEN |
| P4-09 | Repeat raids and alternating services | Run at least three raids, alternating standard -> priority -> standard. In each raid perform one leave/re-enter before the successful hold. | Each raid starts without an old helicopter, trigger, countdown, support-type identity, revision, or completion estimate. Every new request uses its accepted service/revision exactly once and remains usable after prior teardown. | OPEN |
| P4-10 | Invalid standard relationship | Repeat the rejected P4-C01 boundary pairs with only standard Extraction invalid and Priority Exfil valid. Test both dashboard and direct supported config/API path. | The revision is rejected before a paid request can use it, with an actionable standard-Extraction error stating the one-second margin. The last valid config remains authoritative; no partial standard or priority update is applied. | OPEN |
| P4-11 | Invalid priority relationship | Repeat P4-10 with only Priority Exfil invalid. | The revision is rejected before use with a Priority Exfil-specific one-second-margin error. Standard values are not mutated as a side effect, and the last valid revision remains active. | OPEN |
| P4-12 | Packaged/migrated runtime compatibility | After P4-C06, run one request per service with a fresh schema-3 config, a published legacy config migrated to schema 3, and a schema-2 explicit standard delay of zero migrated to schema 3. | Every exposed timing field affects gameplay as labeled. Fresh and migrated defaults preserve the historical effective standard eight-second delay; the migrated explicit zero produces intentional immediate dispatch; priority retains its separate three-second default. All three saved configs remain schema 3 with a valid payment currency. | OPEN |

## Fika Matrix

| ID | Topology / scenario | Action | Required result | Status |
| --- | --- | --- | --- | --- |
| P4-13 | Human host requester | Under Revision A, have the human host request standard Extraction and Priority Exfil while one client observes. Measure all four timing boundaries for both services, then have the observer stand in the landing zone longer than the configured countdown. | Each peer renders one local helicopter visual using the canonical host snapshot. Only the requesting host receives a functional point/countdown; the observer gets no UI or extraction. Standard and priority retain distinct contracts without duplicate local visuals. | OPEN |
| P4-14 | Client -> human host | Have a client request each service from a human host under Revision A. The client enters, leaves, re-enters, and completes the zone while the host also stands in its local landing zone longer than the countdown. | The client's zone resets to 14 seconds for standard and 5 seconds for priority. Host settings govern all four timings. The host renders one visual but receives no functional point/UI/extraction. Client extraction follows Fika without ending or stranding the host session. | OPEN |
| P4-15 | Two clients / service isolation | Client A requests standard Extraction and Client B later requests Priority Exfil where admission rules permit; otherwise run sequentially without restarting. Have A and B each cross both local visual landing zones. Record both clients and host. | Each service retains its own host snapshot. A can use only A's functional point and B only B's; crossing the other visual zone cannot start, reset, close, complete, or relabel a timer. Each peer renders at most one local visual per accepted request, with no duplicate extraction, charge, or completion. | OPEN |
| P4-16 | Human-host revision races | Use a longer diagnostic dispatch. First send an A request only after the host has applied B but before the requester receives B; then retry with current settings. Separately run three post-snapshot repetitions: after the host `host-authoritative extraction timing` A log, prove B became effective on the host during dispatch, arrival, and countdown respectively, then issue the opposite service after teardown. | The pre-snapshot stale request rejects as `ExtractionTimingContractChanged`, creates no effect, and refunds once; retry uses B. Each post-snapshot active request remains wholly A and the later request wholly B. Record server and Fika timing revisions; identical values under a newer revision may canonicalize and accept. | OPEN |
| P4-17 | Client -> dedicated headless | With a dedicated headless authority, have a client request and complete each service under Revision A, including one leave/re-enter cycle. | Headless validates and owns dispatch without creating audio, a helicopter presentation, UI, or functional point locally. Each non-headless peer renders one visual, only the requester receives the functional point/countdown, the requester extracts through Fika, and no headless camera/audio dependency blocks acceptance. | OPEN |
| P4-18 | Headless revision races | Repeat P4-16 against dedicated headless authority: one pre-snapshot stale-A request after B is active, then three post-snapshot repetitions proving B became effective during dispatch, requester arrival, and requester countdown after the canonical A log. Perform each next valid request without restarting the client. | Pre-snapshot stale timing rejects with no effect and one refund. Every post-snapshot request remains one complete A snapshot; the active requester countdown is not retuned and each next request receives B through the matched settings contract. | OPEN |
| P4-19 | Client death/disconnect/cancellation | Against both a human host and a dedicated headless host, cancel before dispatch, die during arrival/countdown, and disconnect during an active wait window in separate runs. | Authority removes or deterministically completes request state; the requester-owned trigger/countdown is destroyed and no remaining player inherits it or extracts. Other peers may let their already-accepted local visual finish naturally, but it has no functional point. Reconnect or the next raid starts cleanly. Transaction settlement remains subject to Phase 2. | OPEN |
| P4-20 | Fika raid teardown and repeats | End/restart human-host and headless raids with each service active at dispatch, wait, and countdown stages. Include one abrupt client process exit, then run a clean request in the next raid. | No helicopter, trigger, timer, accepted timing snapshot, support-type identity, requester mapping, or callback leaks across the raid boundary. The next request is single, correctly timed, and governed by the current host revision. | OPEN |
| P4-21 | Matched-protocol preflight | Do not enter a deliberately mixed raid. Before startup, compare every participant's Core, Server, Fika Interop, and Fika bootstrap SHA-256 values with one reviewed four-DLL build manifest. | PASS requires one exact matched set and documentation that mixed builds are unsupported. Any mismatch is corrected before startup or marked `BLOCKED`; there is no packet-version handshake that makes a mixed Phase 3/4 run safe. | OPEN |

## Failure Triage

On failure:

1. Preserve every participant log, video, config file, revision, DLL hash, and
   pre/post ledger snapshot from the first run.
2. Classify the first divergence as config validation, snapshot/revision,
   Fika serialization, dispatch, animation speed, wait window, zone trigger,
   extraction flow, transaction settlement, or teardown.
3. Compare the observed value with Revision A, Revision B, the opposite
   service, the packaged default, and any old hardcoded value. This usually
   identifies the stale authority source.
4. Reproduce the smallest row while changing only one field or lifecycle
   boundary.
5. Keep Phase 2 open for failures involving request identity, acceptance,
   duplicate execution, authorization commit/refund, disconnect settlement, or
   cross-profile state.
6. Keep Phase 3 open regardless of Phase 4 results; extraction timing does not
   exercise UAV requester presentation or teardown.

## Exit Record

Phase 4 is live-complete only when every applicable row has attached evidence
and:

- standard Extraction and Priority Exfil each use their labeled dispatch,
  speed, wait, and countdown values;
- one accepted request uses one immutable, internally consistent authority
  revision in solo, human-host, client, and dedicated-headless topologies;
- zone exit/re-entry, cancellation, death, disconnect, raid teardown, and
  repeat raids cannot preserve or duplicate a countdown, trigger, helicopter,
  or completion callback;
- invalid `wait < ceil(extract + 1)` relationships cannot become an accepted
  paid request;
- the default/migration contract and matched-client requirement are documented.

Do not mark Phase 4 complete from solo success alone. Phase 2 and Phase 3 remain
separately open until their own live matrices are completed with evidence.
