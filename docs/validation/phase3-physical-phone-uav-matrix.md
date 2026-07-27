# Phase 3 Physical-Phone UAV Validation Matrix

Status:

- Existing physical-phone implementation: build-verified only.
- Phase 3 static lifecycle, timing, and Fika-isolation audit: complete; no
  remaining P0-P2 blocker found.
- Phase 3 live acceptance: **OPEN - not yet run**.
- Phase 2 multiplayer/headless transaction matrix: **still OPEN**. Phase 3
  evidence does not close any Phase 2 row.

This matrix validates the requester-owned physical-phone presentation for UAV
Recon and UAV Focused Sweep. A successful build or static review is not a live
pass.

## Test-Set Safety

Before each topology:

1. Stop the SPT server, game, launcher, every Fika client, and any dedicated
   headless process before installing DLLs.
2. Record the Core, Server, Fika Interop, and Fika bootstrap SHA-256 values on
   every participant. Do not mix source checkpoints between machines.
3. Back up the complete TSC storage directory while the server is stopped and
   use disposable profiles for forced death, disconnect, or process-exit rows.
4. Record the TSC config revision and the effective UAV duration, range, and
   cadence before entering the raid.
5. If Phase 3 changes any Fika packet, serialized field, or request/result
   contract, rebuild and install a matched four-DLL set everywhere before live
   testing. A Core-only test build must not be combined with an older Interop
   or bootstrap if their packet contract changed.

Use visibly different Standard and Focused settings so stale/local values are
obvious. Record the actual values rather than assuming packaged defaults.

## Required Evidence

For every row, capture:

- topology, player/profile role, map, service, config revision, and all four DLL
  hashes;
- request/activation identity where logged, authorization count before/after,
  activation time, phone-open/close times, and expiry time;
- requester video showing the phone, countdown, movement/aiming, stow, and
  restoration; observer video where isolation or loiter deduplication matters;
- requester, human-host or headless-host, and SPT server log excerpts from
  request through teardown;
- measured scan intervals and known-distance contacts for cadence/range rows;
- the next raid's initial state for teardown rows.

For UI isolation, absence from a log alone is insufficient: record the
requester and every non-requester view over the same time window. For cleanup,
record FOV, prior weapon/hands, input, camera, phone screen/renderers, overlay,
and loiter state before activation and after teardown. Note any Unity exception
or duplicate-object warning even if the visible result looks correct.

Status values are `OPEN`, `PASS`, `FAIL`, or `BLOCKED`. Every row starts
`OPEN`; attach evidence before changing it.

## Solo Matrix

| ID | Scenario | Action | Required result | Status |
| --- | --- | --- | --- | --- |
| P3-01 | Standard lifecycle | Purchase/deploy UAV Recon, activate it, open and stow the phone, wait for expiry, then use a newly authorized Recon again. | One credit is consumed per accepted activation. One requester feed and one loiter presentation start per activation; the feed and active orbit reach expiry at the configured duration. A configured aircraft flyoff may finish afterward. The second use starts cleanly. | OPEN |
| P3-02 | Focused lifecycle | Repeat P3-01 with Focused Sweep and settings visibly different from Standard. | Focused range, cadence, countdown, and lifetime match its authoritative settings; no Standard value leaks into the session. | OPEN |
| P3-03 | Held-input behavior | Hold the configured radar chord while stationary, walking, sprinting, aiming, turning, and changing direction; release it in each state. | Unrelated movement/aim keys do not count as release. The phone remains readable and correctly oriented while held; releasing the configured chord restores the prior weapon once. | OPEN |
| P3-03A | Upright K/J presentation | From a firearm, melee item, and empty hands, open the deploy phone with K and the held radar phone with J. Capture the first-person presentation frame-by-frame through equip and repeat with a short J tap. | The concealed EFT hand transaction emits no visible landscape-phone or empty-hands mime. The first visible phone frame is portrait, the real phone and hands rise together vertically without a ghost/double phone, and early release restores every saved renderer, animator, framing, and weapon state. | OPEN |
| P3-04 | Async equip release/death window | Release immediately after the hold begins, repeatedly varying timing across the async phone equip. Separately die during that window. | A late equip callback cannot strand or reveal a phone after release/death. No weapon is equipped after death; no duplicate restore, input lock, exception, or stale equip-in-progress state survives. | OPEN |
| P3-05 | Rapid open/close | Perform at least 20 rapid press/release cycles, including repeated presses during equip and release during close. | At most one phone controller, screen camera, render texture, and radar view exist. The final release restores one prior weapon and leaves no delayed reopen. | OPEN |
| P3-06 | Stow contract | Activate recon, note remaining time, stow the phone for a measured interval, then reopen it. Repeat several times. | The recon clock and aircraft keep running while stowed. Reopening shows the original remaining time; stow never pauses, restarts, or extends the contract. | OPEN |
| P3-07 | Radar geometry and pooling | Place or observe contacts at known bearings and distances inside, on, and outside the configured range. Rotate and move the player through several scans. Let contacts enter, leave, die, and re-enter. | Blips remain player-relative, range clipping is correct, scan intervals match cadence, stale blips clear, and pooled contacts do not duplicate or retain an old identity. | OPEN |
| P3-08 | Phone/loiter lifetime parity | Record the accepted activation time, phone countdown/expiry, aircraft transition from orbit to flyoff, and final aircraft destruction for both services. | The phone link and aircraft's active orbit use the same accepted deadline within normal frame/network tolerance. The configured flyoff/despawn is recorded separately. Neither contract drifts because of a stale local default or cross-process clock. | OPEN |
| P3-09 | Cleanup boundaries | While the phone is equipping, open, and stowed, separately trigger inventory, death, extraction, raid end, and scene transition. | Each applicable boundary closes requester UI and removes overlay, phone camera/render texture, phone renderer state, input capture, and aircraft. Surviving-player paths restore original FOV, weapon, hands, right-arm/animator, and camera state. | OPEN |
| P3-10 | Repeat raids | Complete P3-09, enter a second and third raid, and activate/open/stow recon again. | No prior overlay, controller, cancellation token, camera, renderer, held-key state, loiter identity, or async-equip flag affects the next raid. | OPEN |

## Fika Matrix

| ID | Topology / scenario | Action | Required result | Status |
| --- | --- | --- | --- | --- |
| P3-11 | Human host requester | In a human-host raid, have only the host activate each UAV service while a client observes. | The host receives one phone feed; the observing client receives no radar UI or requester phone screen. World loiter presentation occurs once and uses the host-authoritative settings. | OPEN |
| P3-12 | Client -> human host | Have one client request each UAV service while the human host observes; repeat after stow/reopen and after expiry. | Only the requesting client receives the feed. The host validates/executes once but never receives requester UI. Consumption, phone link, and loiter each occur once. | OPEN |
| P3-13 | Client -> dedicated headless | Request each UAV service from a client connected to a dedicated headless host. Inspect the headless process and a separate observer when available. | The requester alone receives the feed. The headless authority creates no phone, camera, overlay, radar UI, or renderer and does not require a local player to accept the request. | OPEN |
| P3-14 | Two-client isolation | Client A activates Standard; while it is active, Client B activates Focused at a different time. Record A, B, and host views continuously. | A and B retain independent requester identity, settings, countdown, contacts, and teardown. Neither sees or mutates the other's feed; the host sees neither feed. | OPEN |
| P3-15 | Authority revision race | Start with revision A and distinct duration/range/cadence. During request/equip and then during an active link, save revision B with clearly different values. After expiry, activate again. | One accepted activation uses one internally consistent authority snapshot; a live revision cannot partially mix A/B or extend/mutate that link. The next accepted activation uses revision B. Phone and aircraft remain paired in both runs. | OPEN |
| P3-16 | Duplicate/overlap admission | Duplicate one activation/request packet where test tooling permits, spam activation locally, and attempt the other UAV service while a link is active. | The active link is not restarted, extended, replaced, or charged twice. No second requester overlay, phone controller, or loiter aircraft is created from the same accepted activation. A later valid activation after expiry works. | OPEN |
| P3-17 | Loiter identity/dedupe/lifetime | With host plus two clients observing, activate at staggered times, replay any presentation side channel where test tooling permits, then let each link expire. | Exactly one aircraft represents each accepted activation, ownership/timing cannot cross between clients, and each active orbit expires with its matching link before its configured flyoff. Record any duplicate caused by the presentation-only loiter side channel as a failure; do not infer identity from appearance alone. | OPEN |
| P3-18 | Requester disconnect/death | Disconnect or kill the requester during async equip, while viewing, and while stowed. Repeat once with a human host and once with a dedicated headless host. | Requester-owned UI and local presentation are removed, no observer inherits the feed, and authority/loiter state reaches a deterministic teardown. Reconnect or the next raid begins cleanly. | OPEN |
| P3-19 | Fika raid boundary | End/restart raids with active links in the human-host and dedicated-headless topologies, including one abrupt client process exit. | No pending equip, overlay, remote phone visual, loiter object, timer, accepted-event replay, or requester mapping leaks into the next raid. Subsequent activation remains single and correctly isolated. | OPEN |

## Failure Triage

On failure, preserve the first failing run before retrying:

1. Copy all participant and server logs and note synchronized timestamps.
2. Preserve the TSC config/revision and storage snapshot.
3. Record whether the defect is input/equip, requester routing, authoritative
   timing, phone rendering, loiter presentation, or teardown.
4. Reproduce the smallest matching row without changing multiple variables.
5. Keep Phase 2 open if the failure involves acceptance, duplicate execution,
   authorization commit/refund, disconnect settlement, or packet identity.

## Exit Record

Phase 3 is live-complete only when all applicable rows have attached evidence
and:

- the phone survives the full input, async-equip, repeat-use, and teardown
  matrix without stranded Unity or player state;
- requester isolation holds for human-host, client/human-host,
  client/headless, and two-client sessions;
- each accepted activation has one immutable authority timing/geometry
  snapshot, one requester feed, and one matching loiter lifetime;
- duplicate, overlap, death, disconnect, and raid-boundary paths cannot extend,
  transfer, replay, or leak recon state.

Do not mark Phase 3 complete from solo success alone. Phase 2 remains separately
open until its transaction matrix is run and its documented residual exit
conditions are resolved.
