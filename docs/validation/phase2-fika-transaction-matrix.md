# Phase 2 Fika Transaction Validation Matrix

Status:

- v1.1.0 implementation/build checks: complete.
- Live acceptance: **OPEN - not yet run**.

## Phase 7 Candidate Record

Complete this record before changing any live row from `OPEN`. The archive,
evidence chain, and four DLL hashes must identify the exact matched candidate
installed on every participant.

| Field | Value |
| --- | --- |
| Candidate version | `v1.1.0` |
| Candidate status | `VERIFIED - installed on the local server; matched peer installation remains OPEN` |
| Candidate commit | `a958630633e2f792bc52558aba2b6d0a67fa485a` |
| Release archive filename / SHA-256 | `Tylevo.TacticalServicesControl-v1.1.0-SPT4.0.13.zip` / `A871D44AE86C28779C3471338F61A896530580AF97D6AAF1F1C3C4D31547B3CE` |
| Package manifest SHA-256 | `057924363255172AAD1B9102A75F61CDF7A7A94BFE1DDF6E5EEF7294B56DB654` |
| Build evidence SHA-256 | `3ACDB3A59FA4D415F3F6886FA1044E3F47B9A633A31A5329CD3B81FF996093D6` |
| Content evidence SHA-256 | `E9A1DCFA77351B12AB6284515F0B00DC2ED4E61D1DB8BAF33FB3149638824345` |
| Core DLL SHA-256 | `D2F4476F1006ADED9B3701F332BA45AE5E6178C4D93CD8DFB43C4D91EFB1E032` |
| Server DLL SHA-256 | `2BDC2E25CBFF42C538FF9AECC3055492B8A685E396109FA421736AF9F16B3159` |
| Fika Interop DLL SHA-256 | `9E28B8BE3A09CDFF9789A68B98BC9846BDAE5025564E774744BABE6E3ACAD401` |
| Fika bootstrap DLL SHA-256 | `37164B44E16C65809017BC2E54E0D7307F51163C43AC14B07B3BB953F2265ECE` |
| Config schema / authorization-ledger schema | `3 / 5` |
| Evidence root | External `release_candidate/v1.1.0-a958630` directory |

This matrix validates that a TSC authorization is finalized from an explicit
authority outcome rather than from packet transport. It applies to A-10
Strafe, A-10 Double Pass, UH-60 Extraction, UH-60 Cargo Transfer (legacy
`PriorityExfil` wire identity), UAV Recon,
and UAV Focused Sweep.

## Test-Set Safety

Before starting the SPT server:

1. Stop the SPT server, game, launcher, Fika headless process, and all test
   clients.
2. Install the exact candidate recorded above on every participant. Confirm
   its package manifest, build evidence, content evidence, and Core, Server,
   Fika Interop, and Fika bootstrap hashes before startup.
3. Archive the complete
   `SPT/user/mods/Tylevo.TacticalServicesControl/storage` directory while the
   server is stopped. This preserves the primary ledger, `.bak`, and any
   recovery state as one generation; no live `.tmp` file should exist.
4. Record the SHA-256 of every installed DLL and the pre-test storage archive.
5. Keep the schema-5 storage directory with the matching v1.1.0 candidate
   Server DLL. A DLL-only downgrade is not a safe rollback; restore the whole
   pre-upgrade storage snapshot with the older DLL set. That restoration
   intentionally discards disposable-profile ledger changes made after the
   snapshot.

Use disposable test profiles where a disconnect, forced process exit, or
network fault is required.

## Pinned Test Configuration

Record the server revision and full TSC config for every run. Unless a row says
otherwise, pin:

- persistent authorizations enabled;
- `paymentMode=PhoneAuthorizations`, or Hybrid with
  `purchasePersistence.spendCreditsBeforeCash=true`;
- `purchasePersistence.consumeOn=AuthorizationAccepted`;
- `purchasePersistence.refundFailedDispatch=true`;
- `purchasePersistence.pendingUseTimeoutSeconds` longer than the injected
  fault window (at least 300 seconds for this matrix);
- the tested service enabled on the requester and authority;
- the exact dedicated-headless A-10 mode.

Pay-per-use/carried-cash requests have no server pending authorization, and
`refundFailedDispatch=false` intentionally commits a failed server-backed
dispatch. Those are separate product-mode tests and must not be evaluated
against the refund expectations below.

Rows requiring packet loss, duplication, payload mutation, or a timing race
need a local test-only Fika packet shim, network proxy, or debugger-controlled
fault point. Do not publish an instrumented test build.

## Evidence Per Case

Record:

- topology and participant/profile role;
- service, parent `SupportRequestId`, child pass ID when applicable, and the
  loiter packet's parent `SupportRequestId` and `RequesterProfileId` for UAV
  rows;
- ledger count and authorization-use state before and after;
- effective authority executions and requester presentations;
- requester, authority, and SPT server log excerpts;
- final result: `Accepted`, `Rejected`, `TimedOut`, or `Cancelled`;
- whether the next raid hydrates the same final ledger state.

Transport log lines that say a packet was sent are not acceptance evidence.
For a remote requester, require an accepted authority result or canonical
accepted broadcast plus the final ledger state. Solo and local human-host
cases may instead use the local-runtime/in-process authority acceptance log
plus the final ledger state.

Informal solo observation on 2026-07-28 confirmed that saving a changed service
price during an active raid affected a subsequent purchase without restarting
the raid. This is useful preliminary evidence for P2-17, but it does not close
the matched Fika or dedicated-headless cases.

## Matrix

| ID | Topology / fault | Action | Required result |
| --- | --- | --- | --- |
| P2-01 | Solo SPT | Deploy each of the six services once. | The local path executes once and the authorization commits once. No Fika handler is required. |
| P2-02 | Human Fika host | Request each service as the host. | The original human-host gameplay path executes once and each authorization commits once. |
| P2-03 | Fika client -> human host | Request each service as a client. | The client waits for authority acceptance; the host executes once; shared A-10/UH-60 world presentation appears once on applicable peers; private UAV feed/UI appears only for the requester; the requester's ledger commits once. |
| P2-04 | Fika client -> dedicated headless | Request each supported service, including A-10 with headless A-10 enabled. Run separate nonlethal and lethal strikes against a headless-owned bot, then a remote human. Capture health, damage, death/downed, AI-brain, corpse, and observer events before and after each pass. | The headless authority accepts once. A-10 damage starts only after accepted delivery is published, buffered tracers play once, and target-health evidence shows one intended damage sequence rather than ballistic-plus-fallback double damage. A lethal bot enters one native Fika death, stops AI, and becomes one lootable corpse on every peer with no upright half-dead actor. A remote human applies damage only on its owning client and publishes exactly one native corpse sync, or exactly one native downed state when Fika revival is enabled. |
| P2-05 | Disabled/unavailable executor (injected) | Reserve while enabled, then change authority state before validation, or inject a valid packet after disabling the authority service. Separately disable dedicated-headless A-10. | The authority returns a stable rejection, no accepted event or gameplay effect occurs, and an existing pending authorization refunds once. The normal UI's pre-send service check is not this test. |
| P2-06 | Duplicate request | Deliver the same request ID and identical payload at least twice before and after completion. | One authority execution occurs. Replays return the cached outcome and do not consume, commit, refund, or present twice. |
| P2-07 | Conflicting replay | Reuse one request ID with a changed service, pass, requester, or geometry. | The authority rejects the conflict and never executes the changed payload. The original outcome remains authoritative. |
| P2-08 | Lost/duplicate result paths (injected) | Independently drop the initial request, a rejection, the direct accepted result, and the accepted broadcast. Duplicate each terminal path independently. | Initial reliable retry reaches one authority admission; a lost rejection is replayed; either accepted path settles once; duplicates do not replay presentation or ledger finalization. |
| P2-09 | Pre-start cancel arbitration (injected) | Hold the authority before `TryBeginExecution`, cancel deployment, and separately end the raid. Do not merely delay past 20 seconds, because authority admission self-times out first. | A cancellation/rejection refunds once; an acceptance that already won commits once. |
| P2-10 | Disconnect/death/teardown | Disconnect the requester and test death/raid end before execution start; repeat across two raids. | No pre-start request executes after teardown, no pending client wait survives, and the ledger reaches one observable terminal state. The next raid contains no stale replay state. |
| P2-11 | Double Pass | Complete both passes, duplicate pass 0, and cancel or fail pass 1 after pass 0 accepts. | Pass 0 acceptance commits the parent authorization once. Each child pass executes at most once; pass 1 is best-effort and never refunds delivered pass 0. |
| P2-12 | Two clients / identity spoof (injected) | Submit overlapping requests, replay one ID from the other peer, inject a fresh request whose client-supplied `RequesterProfileId` names the other profile, and inject a request before `NetPeer.Player` is available. | An admitted ID remains bound to its originating `NetPeer`. A fresh remote request must match `NetPeer.Player.ProfileId`; a mismatch rejects as `RequesterProfilePeerMismatch`, and an unbound peer rejects as `RequesterPeerProfileUnavailable`. Both outcomes are cached for that request ID, cause no gameplay effect or cross-profile UAV UI, and refund the initiator once. |
| P2-13 | Storage limit lowered while pending | Reserve the last authorization, lower the configured storage maximum, then force a valid refund. | The already-owned credit is restored even if that temporarily exceeds the new maximum. New purchases remain blocked until the count is within the limit. |
| P2-14 | Commit/refund transport interruption | Interrupt the SPT HTTP finalization call, then restore it before the pending-use timeout while the client remains active. | Same-ID retries converge on one terminal ledger state. An uncorrelated response or changed backend session/profile is never applied. |
| P2-15 | Backend outage beyond pending expiry | Keep the SPT ledger endpoint unavailable beyond the pending-use timeout after an accepted service. | Record the known limitation: expiry can restore the credit before the same-ID commit arrives, leaving the delivered service free. No second execution may occur. |
| P2-16 | Both accepted paths and cancel settlement lost (injected) | Let the authority accept, but drop the direct result, accepted broadcast, and cancel-settlement replay for more than 35 seconds while SPT HTTP remains available; then deliver a late accepted replay. | Record the known limitation: `AuthorityCancelUnsettled` refunds before authority state is known, so the accepted effect can be free and the late replay can register after the waiter is gone. It must still never execute twice. |
| P2-17 | Live in-raid price change | In solo, human-host, remote-client, and dedicated-headless topologies, begin with a distinctive service price A. Keep one prepared/in-flight purchase at a controlled fault point, save price B through the dashboard during the active raid, then make a fresh purchase and resume/retry the original request ID. | The fresh request uses authoritative price B without a raid restart. The prepared/in-flight request remains pinned to price A and its original currency across retry or replay. Only the authenticated requester is debited, each purchase grants at most once, and no host, observer, or headless profile is charged. Record the config revision and quote identity for both requests. |

## Known Residuals And Contract Checks

- Client finalization retries are in-memory. A process crash, permanent logout,
  or backend outage lasting beyond the server pending-use timeout can transition
  an accepted use to `ExpiredRefunded`, making the delivered service free. A
  durable client outbox or authority-to-ledger commit protocol is required to
  remove this distributed failure window.
- A result-path partition longer than 35 seconds can produce
  `AuthorityCancelUnsettled`, an immediate refund, and a later accepted replay
  even while SPT HTTP remains healthy.
- The UAV aircraft-loiter presentation packet now carries the accepted parent
  `SupportRequestId` and `RequesterProfileId`. Match both fields to the
  canonical accepted request in live evidence. A missing/mismatched identity,
  cross-profile presentation, or replay that creates a second aircraft is a
  failure; live proof of this request-bound deduplication remains `OPEN`.
- Dedicated-headless A-10 performs a side-effect-free preflight before
  acceptance, then starts damage in the accepted background phase. Record any
  world-state change between those two points that prevents the accepted pass
  from firing.

## Exit Record

Phase 2 is not live-complete until every applicable row has attached evidence
and the following invariants hold:

- no request executes or consumes twice;
- no rejection begins requester presentation;
- every pre-start rejection, cancellation, and timeout reaches one observable
  refund state;
- repeated raids start without stale pending requests or accepted-event
  replays.

Phase 2 cannot pass this exit record until P2-16's unobserved-acceptance refund
window is fixed. P2-12's strict peer/profile binding and fail-closed unbound
peer path still require live validation. Until P2-16 is closed,
"lost or duplicate acceptance packets converge on one final state" remains an
unmet target rather than an implemented invariant.
