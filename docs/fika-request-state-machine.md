# Fika Support Request State Machine

This document defines the Phase 2 request and authorization lifecycle for the
six networked TSC services:

- A-10 Strafe
- A-10 Double Pass
- UH-60 Extraction
- UH-60 Priority Exfil
- UAV Recon
- UAV Focused Sweep

## Pre-Phase 2 Failure

Before Phase 2, a Fika client treated a successful `SendData` call as gameplay
acceptance. The requesting service could therefore commit its pending
authorization before the host or dedicated headless executor had accepted the
request. A disabled service, unavailable executor, cancellation, or executor
startup failure had no response path back to the requester.

The server ledger also inferred terminal state from its capped transaction
history. A refunded request could later be committed through the legacy
`Consume` fallback, and an expired pending use was committed rather than
refunded.

## Request Lifecycle

`ConsumeOn` currently supports one normalized policy:
`AuthorizationAccepted`. Under that policy the lifecycle is:

1. The requester reserves one authorization in the server ledger. The ledger
   records the use as `Pending`.
2. The requester sends a Fika support request with a stable
   `SupportRequestId`. Transport delivery is not an acceptance.
3. The host validates the request, prepares the selected executor, and crosses
   one guarded execution-start boundary.
4. If executor startup succeeds, the authority records `Accepted`, returns a
   self-contained accepted result, and broadcasts the same canonical request.
   Dedicated-headless A-10 performs a side-effect-free preflight, publishes
   acceptance, and only then starts damage and releases buffered tracers.
5. The requester starts requester-owned visuals and commits the pending ledger
   use exactly once.
6. If validation or executor startup fails, the authority returns a stable
   rejection without broadcasting an accepted event. The requester refunds the
   pending use exactly once.
7. An authority-confirmed cancellation or timeout before acceptance follows
   the same refund path. If neither accepted path nor cancel settlement is
   observable within the client's 30-second wait plus five-second settlement
   window, the current requester also refunds `AuthorityCancelUnsettled`. That
   outcome is locally final but not authority-confirmed; an accepted execution
   may already exist and is a known distributed failure window.

Solo play has no Fika handler. Its request result is `NotHandled`, so the
original local runtime path executes and the same commit/refund decision is
made from that local result.

## Network Results

| Result | Meaning | Requester action |
| --- | --- | --- |
| `NotHandled` | No network authority owns the request | Run the original solo path |
| `Accepted` | The selected authority executor accepted/started the request | Start requester-owned effects and commit |
| `Rejected` | Authority validation or executor startup failed | Do not start requester effects; refund |
| `TimedOut` | Authority admission timed out, or the requester exhausted its wait and cancel-settlement window | Confirmed authority timeout refunds. `AuthorityCancelUnsettled` currently also refunds without proving that acceptance lost. |
| `Cancelled` | Authority-confirmed cancellation, or caller cancellation whose authority settlement was not observed | Confirmed cancellation refunds. `AuthorityCancelUnsettled` has the same accepted-plus-refund residual as an unsettled timeout. |

## Authority Idempotency

Authority state is keyed by `SupportRequestId` and an immutable request
fingerprint. A duplicate with the same ID and payload must not execute again;
the cached terminal result is replayed. Reusing an ID with a different type,
pass, requester, or request geometry is rejected as a conflict.

The direct result contains the canonical accepted payload, so it can start the
requester presentation if the broadcast is lost. Requester-side accepted
results and broadcasts share one fingerprinted dedupe set, so either arrival
order settles once and cannot replay a helicopter, A-10 pass, or recon
presentation. Accepted authority outcomes and accepted client fingerprints are
retained for the raid. Rejected outcomes are retained for the raid as well, so
a late same-ID replay cannot become fresh work after its client refunded.

## Ledger Terminality

Authorization uses have durable state independent of the capped audit
transaction list:

| Current state | Consume replay | Commit | Refund |
| --- | --- | --- | --- |
| `Pending` | Return the existing reservation | Transition once to `Committed` | Transition once to `Refunded` |
| `Committed` | Reject as already committed | Idempotent success | Reject |
| `Refunded` | Reject as already refunded | Reject | Idempotent success |
| `ExpiredRefunded` | Reject as expired/refunded | Reject | Idempotent success |

The request ID and service are one identity. Reusing an ID for another service
is always a conflict. Expiring `Pending` restores the credit that was already
owned and records an audit refund. If an administrator lowered the storage
limit while the use was pending, the restored count may temporarily exceed the
new limit; subsequent grants remain blocked until the count is reduced.

## Double Pass

One Double Pass authorization owns two child dispatch IDs, one per pass. The
first accepted pass is the point at which a delivered service can no longer be
refunded safely. The authorization commits once at that transition; the second
pass remains deduplicated and best-effort. Cancellation or rejection before
the first accepted pass refunds normally.

## Teardown

Requester pending waits are bounded and resolved during cancellation,
disconnect, Fika manager replacement, plugin shutdown, and raid end. A client
waits up to 30 seconds for a terminal response, sends an explicit cancel, and
allows a five-second authority-settlement window so an acceptance that already
won is not incorrectly refunded. Authority admission has a 20-second pre-start
timeout. The client admits at most 8 pending requests, the authority at most
128 in-flight requests, and the per-raid authority table admits at most 512
unique request IDs. Once full, unknown IDs are rejected; admitted terminal IDs
remain replayable until raid reset.

Authority in-flight and terminal replay caches, plus client accepted-event
caches, are cleared between network sessions so repeated raids do not inherit
stale IDs. Cancellation cannot turn an executor that already crossed the
execution-start boundary into a rejection at the authority. The requester can
still refund if both accepted paths and cancel settlement remain unavailable
through its bounded wait; that separate residual is documented below. Accepted
dedicated-headless A-10 work also observes teardown cancellation before
publishing tracers and before or during direct-fallback damage.

Server pending expiry remains the durable recovery path when a client
disconnects before it can send a commit or refund mutation.

## Server Finalization and Identity

The requester's backend session key and authenticated PMC profile are captured
when the authorization is reserved. Consume, commit, and refund calls must
still match that identity before send and after response. The echoed request ID
and parsed support type must also match before a response can change local
state.

At Fika authority admission, every remote request's `RequesterProfileId` must
match the profile currently bound to its originating `NetPeer.Player`. A
mismatch or an unbound peer fails closed and the rejection is retained with the
request ID, so that ID cannot execute later after a client-side refund. The
local human-host path is explicitly peerless and does not use this remote
identity check.

Commit and refund retry the same mutation ID for transient HTTP, invalid
response, ledger-save, and session-transition failures. The ledger's schema-4
authorization-use records make terminal state independent of the capped audit
history. Restoring an older Server DLL requires restoring the ledger copy that
predates schema 4; a DLL-only downgrade is unsupported.

## Deliberate Boundaries

- Client finalization retries are volatile. A process crash, permanent logout,
  or backend outage that outlasts pending expiry can transition an already
  accepted use to `ExpiredRefunded`, making that delivered service free. A
  durable client outbox or authority-to-ledger commit protocol is still needed
  to close that distributed failure window.
- If both the direct accepted result and accepted broadcast are unavailable
  through the 30-second request wait and five-second cancel-settlement window,
  `AuthorityCancelUnsettled` currently causes a refund even when the authority
  already accepted/executed. A late accepted replay can then register after the
  original waiter was removed.
- `StartUavLoiterPacket` remains a presentation side channel without the parent
  request identity. The paid UAV request and requester overlay are
  identity-bound; extending identity to the aircraft-loiter packet is deferred.
- Dedicated-headless A-10 preflights before acceptance and executes immediately
  after accepted delivery. A live test must still prove that no world-state
  transition between those points suppresses an accepted pass and that the
  ballistic path plus experimental direct fallback do not double-apply damage.
- The remote identity check uses Fika's operational `NetPeer.Player` binding;
  it is a fail-closed server-side consistency check, not cryptographic
  attestation against a deliberately modified client.

## Validation Status

Solo SPT was reported healthy before Phase 2. The new transaction protocol
still requires matched-version live validation as a human Fika host, Fika
client, and dedicated headless host. In particular, duplicate/replayed
requests, disabled headless A-10, cancellation, disconnect, and repeated-raid
teardown remain live test gates rather than assumed passes. Use
[`validation/phase2-fika-transaction-matrix.md`](validation/phase2-fika-transaction-matrix.md)
for the exact evidence checklist.
