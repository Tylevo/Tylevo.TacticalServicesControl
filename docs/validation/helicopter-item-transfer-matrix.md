# UH-60 Mid-Raid Item Transfer Validation Matrix

Status:

- 1.3.13 prepaid cargo handling candidate: **OPEN - live acceptance not yet run**.
- All in-game acceptance rows remain `OPEN`; a build pass or solo smoke test
  does not close human-host Fika acceptance. Non-host Fika transfer remains
  unsupported and gated. Its rows verify that gate, not working client transfer.
- Use disposable profiles and preserve the first failing run before retrying.

## Contract Under Test

The released `PriorityExfil` service identity is retained as the compatibility
slot for **UH-60 Cargo Transfer**. While that helicopter is landed and its
loading zone is active, the requester can use the normal EFT interaction
**SEND ITEMS VIA UH-60** to open EFT's native in-raid item-transfer screen.
The cargo helicopter never starts an extraction countdown and never ends the
raid. Standard **UH-60 Extraction** helicopters never expose cargo transfer.

This feature deliberately reuses EFT's canonical transfer infrastructure:

1. Prefer the raid's canonical **Transit** transfer controller.
2. Fall back to the canonical **BTR** transfer controller when Transit is not
   available.
3. On first use in a fresh raid, initialize the requester's grid once through
   that canonical controller's native `InitPlayerStash` path, then revalidate
   it. An existing grid must never be reinitialized because EFT clears it.
4. Fail closed when no canonical controller, initialized requester grid, or
   native service data is available. It must not create a standalone transfer
   controller or an untracked temporary grid.

The Cargo dispatch authorization prepays item handling. Once the helicopter
has been requested, sending items requires **no additional fee and no carried
or stash cash**. The selected TSC currency and price still apply to the upfront
service purchase, and dispatch still uses its authorization normally.

The resulting transaction keeps EFT's native Transit/BTR transfer grid,
simulation, submission, success callback, and delivery accounting:

- the native quote is zero only for the active UH-60 controller and the
  requester's bound temporary stash;
- the native purchase receives a copied service descriptor with an **empty
  cost dictionary**, not a zero-valued RUB requirement that still expects a
  carried money item; every other service property is preserved;
- shared/native service data and unrelated BTR or Transit prices are unchanged;
- opening, cancelling, or submitting cargo spends no additional TSC
  authorization and debits no payment asset;
- new submissions create no handling-fee prepare, commit, or refund record;
  legacy fee-source settings cannot reactivate billing;
- submitted items leave the raid inventory but do **not** appear immediately
  in the stash; native post-raid mail completes delivery.

Recovery for journals created by an earlier fee-paying build remains available.
An existing transaction retains its original ID, profile, amount, and terminal
intent: a confirmed submission settles once, and a failed submission's prepared
debit is refunded once. Reconnects and retries must never create a fresh debit,
refund a completed submission, or apply an old record to another profile.
A missing legacy fee endpoint cannot turn a new prepaid transfer into a paid
transaction or trigger carried-cash fallback. Unresolved old records are kept
for recovery without being confused with a new cargo submission.

After the native controller accepts a submission, TSC marks only item IDs that
are confirmed in that controller's persistent transfer grid. The authenticated
server binds those markers to the current PMC/session and stores them durably
until the delayed delivery callback runs. At delivery time, connected item
trees are partitioned by marker:

- TSC-marked cargo is delivered by the isolated **UH-60 Pilot** messenger;
- unmarked native BTR cargo remains delivered by the stock **BTR Driver**;
- a missing/rejected marker, marker-store failure, or TSC routing failure
  falls back to the stock BTR delivery path rather than losing the cargo.

TSC must never rename or replace the native BTR trader. A package containing
both marked and unmarked roots must keep every attachment with its parent and
must deliver each root through exactly one sender. Marker persistence must
survive an SPT restart between submission and delayed delivery.

The transfer interaction and screen are requester-local. Other human peers
may render the helicopter, but must not receive the requester's screen, grid,
cargo, or delivery. Solo and a requesting human Fika host are the supported
candidate paths. Non-host Fika requesters remain gated because native transfer
authority is not supported for that path. A zero quote does not establish
host ownership of the requester-local native purchase or make client transfer
supported. The open and purchase paths must retain their authority checks
before grid or service mutation. A dedicated headless host creates no UI and
does not enable a non-host client's cargo purchase.

## Test-Set and Evidence Requirements

Before each topology:

1. Stop SPT, the game, all Fika clients, and any dedicated headless process.
   Install one matched candidate on every participant and record the candidate
   commit plus all installed DLL SHA-256 values.
2. Back up the affected profiles and mail/storage state. Use distinguishable,
   disposable test items and record each template, stack count, durability,
   attachment tree, and found-in-raid status.
3. Record the upfront Cargo price/currency and authorization use separately.
   Immediately before opening the landed helicopter's screen, record the
   requester's carried/stash RUB, USD, EUR, GP, BTC, authorization counts,
   in-raid inventory, stash, and pending mail. Use non-currency test cargo so
   payment balances can be compared directly. Record the zero native quote
   and, where a diagnostic fixture permits, the empty native cost dictionary.
   Identify pre-existing handling-fee journal records before testing recovery.
4. Capture requester video and requester, host/headless, and SPT server logs
   from helicopter arrival through raid teardown and delivery collection.
   Record the sender shown for each mail: **UH-60 Pilot** for valid TSC-marked
   cargo and **BTR Driver** for unmarked or safely-fallen-back cargo.
5. After submission, compare four checkpoints: immediately in raid, the first
   post-raid menu, native delivery mail arrival, and after collecting the mail
   followed by a restart/reload.

For each submitted item, the final accounting equation is:

`starting copies - copies lost or consumed for another recorded reason =`
`copies still held + copies submitted and delivered`

Status values are `OPEN`, `PASS`, `FAIL`, or `BLOCKED`. A row is `PASS` only
when its evidence location and exact before/after inventory and currency
counts are attached.

## Cargo Timing And Extraction-Isolation Matrix

Cargo uses only the dispatch delay, landed wait window, and helicopter speed
stored under the released `priorityExfil` configuration path. Its legacy
`extractTimeSeconds` member is retained for configuration compatibility but is
not an active Cargo setting and must not enter runtime or Fika extraction
logic.

Use deliberately distinct Cargo timing revisions so stale or partially mixed
snapshots are visible:

| Field | Revision A | Revision B |
| --- | ---: | ---: |
| `priorityExfil.dispatchDelaySeconds` | 2 | 11 |
| `priorityExfil.waitTimeSeconds` | 20 | 40 |
| `priorityExfil.speedMultiplier` | 1.8 | 0.8 |
| `priorityExfil.extractTimeSeconds` (legacy/inert) | 5 | 47 |

| ID | Scenario | Action | Required result | Status |
| --- | --- | --- | --- | --- |
| HIT-T01 | Active Cargo timing | In solo, save Revision A and request Cargo Transfer. Measure target confirmation to dispatch, dispatch to ready, and ready to departure without opening the transfer screen. Repeat under Revision B on comparable geometry. | Dispatch, wait, and speed follow the selected revision. The legacy extract value changes no measured boundary, creates no countdown, and cannot end the raid. | OPEN |
| HIT-T02 | Legacy extraction value is inert | Change only `priorityExfil.extractTimeSeconds` through the supported config path, reload, and run another Cargo request. Repeat with values that would violate the standard Extraction wait/countdown relationship. | The legacy value remains readable/round-trippable but is neither validated as a Cargo countdown nor copied into active Cargo runtime timing. Dispatch, wait, speed, interaction, departure, and raid state remain unchanged. | OPEN |
| HIT-T03 | Cargo validation is independent | Submit invalid Cargo dispatch, wait, and speed values one at a time, then submit valid active values with an arbitrary legacy extract value. | Each invalid active field rejects with a Cargo-specific error and preserves the last valid revision. A valid dispatch/wait/speed combination is accepted without applying the standard `wait >= ceil(extract + 1)` relationship. | OPEN |
| HIT-T04 | Immutable Cargo revision | Accept a Revision A Cargo request, save Revision B separately during dispatch, arrival, an open transfer screen, and the post-close wait window, then issue a fresh request. | The active helicopter remains one complete Revision A snapshot; it is not retuned or converted into an extraction. The next request uses all Revision B active values. | OPEN |
| HIT-T05 | Standard Extraction isolation | Set conspicuously different standard-Extraction and Cargo timings. Alternate Extraction, Cargo, and Extraction across clean raids while capturing countdown, interaction, voiceover, and Fika/session behavior. | Standard Extraction alone owns extraction time, countdown UI, completion, and Fika extraction routing. Cargo alone owns the transfer interaction; it never starts or resumes a countdown, completes extraction, or terminates the raid. | OPEN |
| HIT-T06 | Human-host authority | On a human Fika host, repeat HIT-T01 and HIT-T04 while a client observes both landing zones. | Host-authoritative Cargo dispatch/wait/speed remain immutable and every non-requester is visual-only. Neither host nor observer receives extraction behavior from the Cargo request. | OPEN |
| HIT-T07 | Successful-transfer wait override | Use a long Cargo wait. Submit distinguishable prepaid cargo early in one run; cancel without submitting in another. | The verified native item move short-circuits only the first run's remaining landed wait and plays the successful-pickup departure. Cancellation charges nothing, publishes no completion, and resumes the second run's original remaining wait through normal no-pickup departure. | OPEN |

## Prepaid Handling Matrix

Run supported rows in solo SPT and with the requesting player as the human
Fika host unless a row names a narrower fixture. The starting point is after
Cargo authorization purchase and dispatch. Record carried and stash balances
for all five payment assets separately; new item submissions must change none
of them. HIT-P09 separately accounts for an old fee journal's recovery.

| ID | Scenario | Action | Required result | Status |
| --- | --- | --- | --- | --- |
| HIT-P01 | Zero quote and native purchase descriptor | Stage different non-currency item sets, stacks, and attachment trees in the active UH-60 screen. Observe quote recalculation and capture the service descriptor supplied to native purchase in a controlled fixture. | Every UH-60 handling quote is zero. The purchase uses a per-operation copy with an empty cost dictionary, preserving the original service's other properties and native simulation, transaction, success callback, and item movement. No shared service cost is cleared or overwritten. | OPEN |
| HIT-P02 | No cash or money item | Acquire and dispatch Cargo, then test with no carried RUB item and no stash RUB. Repeat with no carried or stash cash in any currency, retaining only the already-used service authorization and non-currency cargo. Submit once. | Zero-cost handling succeeds without searching for a carried money item or reporting insufficient money. The exact cargo moves and is delivered once. No balance, remaining authorization, or new fee journal changes. A zero-valued RUB requirement must not substitute for the empty cost dictionary. | OPEN |
| HIT-P03 | Rich carried and nested stash balances | Fund carried wallets and nested stash stacks generously in RUB, USD, EUR, GP, and BTC. Submit distinguishable non-currency cargo. Repeat with old `Carried` and `Stash` fee-source values retained in an upgraded local config. | No payment stack is decremented, removed, or relocated for handling; both wallets remain unchanged in every asset. Legacy fee-source values cannot restore billing. Cargo movement, immediate departure, and delayed delivery occur once, with no new fee transaction. | OPEN |
| HIT-P04 | Empty grid | Open the active cargo screen with no staged items. Attempt submission where the UI permits, then close and reopen. | A zero quote cannot fabricate a successful pickup. No item movement, delivery, balance change, extra authorization use, fee journal, or successful-pickup departure occurs. Native empty-grid validation and the remaining helicopter retry window are preserved. | OPEN |
| HIT-P05 | Cancellation and native rejection | Stage cargo and cancel. Reopen, then use a controlled fixture to reject native simulation or transaction validation before acceptance. Finally perform one valid submission. | Cancellation/rejection returns or preserves every staged item under native rules, makes no charge or delivery, and does not publish pickup completion. The remaining wait window permits retry. The final accepted native move alone triggers one delivery and departure. | OPEN |
| HIT-P06 | Duplicate submit and deferred success | Double-click submit and replay the completion callback where the fixture permits. Delay the native completion while the same bound requester, controller, and screen remain valid. | At most one native purchase accepts the cargo and one success is published. Deferred execution retains its own copied empty-cost descriptor; it does not depend on a temporary edit to shared service data. No second item copy, delivery, departure, payment debit, or authorization use occurs. | OPEN |
| HIT-P07 | Stock BTR and Transit isolation | Perform ordinary BTR and Transit transfers before and after UH-60 cargo, including after cancellation and on the same canonical controller where possible. Compare their service data and quotes to an unmodified native baseline. | Only the active UH-60 operation gets prepaid handling. Ordinary BTR/Transit quotes, payment requirements, canonical grids, and delivery behavior remain native. Unrelated BTR cargo receives no UH-60 marker and still arrives through **BTR Driver**. A stale UH-60 override cannot make another transfer free. | OPEN |
| HIT-P08 | Stale deferred operation | In a controlled fixture, pause a native purchase before execution, then separately close/replace the screen, replace the controller or temporary grid, switch requester/profile, and tear down the raid. Resume the old operation and test a fresh interaction. | Stale work fails closed before applying a zero-cost override or moving unaccepted cargo in the changed context. No quote, grid, service descriptor, balance, authorization, callback, or success signal leaks into the new operation/profile/raid. Already accepted native cargo retains its original delivery accounting. | OPEN |
| HIT-P09 | Legacy fee journal recovery | Seed real recorded pending commit and refund intents from an earlier fee-paying build in separate disposable runs. Reconnect the same PMC and replay status/terminal recovery, including an SPT restart. Also reconnect another profile and temporarily make the legacy endpoint unavailable. | Only the original matching transaction may settle: an accepted old submission remains charged once, or a failed old submission's prepared debit is refunded once. Replays make no new debit/refund/item move; another profile remains untouched. Unavailable recovery preserves the old record without new billing or cash fallback. New prepaid submissions create no journal and cannot be mistaken for old recovery. | OPEN |
| HIT-P10 | Upfront authorization ownership | Purchase/request Cargo using each supported service currency and configured authorization mode in separate runs. Record purchase and dispatch accounting, then open, cancel, reopen, and submit cargo without additional funds. | The configured upfront purchase/deployment accounts once. Handling adds no charge, grant, consume, or refund to that service authorization. A missing or unavailable Cargo authorization still blocks dispatch normally; prepaid handling does not bypass service payment or availability gates. | OPEN |

## Solo Matrix

| ID | Scenario | Action | Required result | Status |
| --- | --- | --- | --- | --- |
| HIT-S01 | Service isolation | Call standard UH-60 Extraction and verify it has no cargo action. In a separate raid, call UH-60 Cargo Transfer, wait for the aircraft to land, and inspect interactions outside and inside its active zone. | Standard Extraction retains its normal countdown/extract behavior and never shows **SEND ITEMS VIA UH-60**. Cargo Transfer shows that action only to the local requester while its point is active and the F12 transfer toggle is enabled. Cargo never shows a countdown or extracts the PMC. | OPEN |
| HIT-S02 | Open and cancel | Enter the landed Cargo Transfer zone, open transfer, place one test item in the temporary grid, then cancel/close without submitting. | No extraction countdown appears before, during, or after the screen. Cancel returns every staged item unchanged, charges no native fee, creates no delivery, and changes no TSC authorization/currency state. A second open starts with an empty grid. | OPEN |
| HIT-S03 | Successful native delivery | Open transfer, submit several distinguishable items including a stack and an attachment tree, then end the raid normally. | Each submitted item leaves the raid inventory exactly once. The native handling quote is zero and no carried/stash RUB, USD, EUR, GP, BTC, TSC price, or remaining authorization count changes during submission. As soon as EFT verifies the item move, the screen closes and the helicopter immediately begins its successful-pickup departure. Nothing is inserted directly into the stash. One **UH-60 Pilot** post-raid delivery contains the exact submitted item set, while the native BTR contact remains unchanged. | OPEN |
| HIT-S04 | Delivery collection and persistence | Collect the HIT-S03 mail, reload the profile/menu, restart SPT, and inspect stash and mail again. | Every submitted item is collectible once, preserves its recorded structure/state as supported by EFT, and persists in the stash. No duplicate mail, second charge, disappearing item, restored in-raid copy, or sender change appears after reload/restart. | OPEN |
| HIT-S05 | Cancel/failure retry | Open and cancel once, reopen, then force one native simulation/transaction rejection and reopen again before finally completing one valid transfer. | Cancel and native rejection preserve the canonical player grid and remaining helicopter window without charging, moving items, or starting departure. The final verified submission moves and delivers the items once without a handling charge, blocks another open, and starts exactly one immediate departure. No old item appears in a later temporary grid. | OPEN |
| HIT-S06 | Zone exit and re-entry | Close transfer normally, leave the Cargo Transfer zone, wait, and re-enter. Where test controls permit, also force the player outside while the screen is open. | The interaction disappears outside, no countdown appears, and re-entry exposes a clean transfer interaction. Forced exit cannot leave movement, cursor, interaction, or transfer state stuck. | OPEN |
| HIT-S07 | Wait window while open | Keep the transfer screen open longer than the configured helicopter wait window, then cancel/close it. | The helicopter and point remain present while the screen is open, so normal departure never invokes EFT's destructive forced-close fallback. The helicopter wait clock resumes only after a voluntary close, unsubmitted items return normally, and departure occurs after the remaining active window. | OPEN |
| HIT-S08 | Raid teardown while open | End the raid separately by extraction, death, and abort while the transfer screen is open; then start another raid. | Teardown force-closes UI and releases input, callbacks, temporary state, and service-availability overrides. Unsubmitted items follow EFT's normal raid outcome and are not delivered. The next raid has no stale screen/grid/interaction. Previously confirmed submissions are neither lost nor duplicated and retain their intended delivery sender. | OPEN |
| HIT-S09 | First-use grid, canonical selection, and fail-closed behavior | On a fresh raid where the requester has not used Transit or BTR, open UH-60 cargo and confirm its canonical grid is initialized. Reopen without submitting to confirm the existing grid is not recreated. In a controlled diagnostic build or fixture, make Transit unavailable to exercise BTR fallback, then make both controllers or native grid initialization unavailable. | First use initializes exactly one requester grid through the chosen raid-owned controller; reopening preserves it. Transit is selected first and existing canonical BTR only as fallback. With no valid canonical controller/grid/service data, opening fails with an actionable warning, moves no items, changes no currency, and creates no delivery. No standalone controller or untracked grid is constructed. | OPEN |
| HIT-S10 | Cargo sizing and occupied-cell preservation | Open cargo with native dimensions (0/0), then custom columns/rows through SIC and the dashboard, including large multi-cell items. Change dimensions while a screen is open and reopen later; include an already populated canonical persistent grid before requesting a smaller size. | An open screen keeps its original dimensions; a new screen uses the configured valid size while preserving occupied cells and previously submitted cargo. Item footprints and native placement validation remain intact. Zero handling cost cannot bypass capacity checks, clear an existing grid, resize ordinary BTR/Transit storage, or expose another requester's cargo. | OPEN |

## Delivery Routing And Failure Matrix

These cases may use disposable profiles plus a controlled fixture or diagnostic
build where inducing a server/storage failure is unsafe in a normal profile.
Every row still requires exact before/after item-tree and currency accounting.

| ID | Scenario | Action | Required result | Status |
| --- | --- | --- | --- | --- |
| HIT-R01 | Sender isolation | Complete one UH-60 cargo submission and one unrelated native BTR submission with distinguishable items. Wait for both deliveries and open both message threads. | UH-60 cargo arrives from **UH-60 Pilot**. Native BTR cargo still arrives from **BTR Driver**, whose name, portrait, thread, and unrelated messages are unchanged. Each sender contains only its intended cargo. | OPEN |
| HIT-R02 | Mixed marked/unmarked package | Arrange for one delayed delivery package to contain a TSC-marked root with attachments and an unmarked native BTR root with attachments. Trigger delivery. | The marked root and its complete attachment tree arrive once from **UH-60 Pilot**. The unmarked root and its complete tree arrive once from **BTR Driver**. No attachment is split across senders, omitted, or duplicated. | OPEN |
| HIT-R03 | Marker restart persistence | Submit TSC cargo, confirm it has left the raid, then stop and restart SPT before its delayed delivery callback. Start the same profile and wait for delivery. | The durable marker is recovered and the exact cargo still arrives once from **UH-60 Pilot**. Restart creates no BTR duplicate, lost item, second debit, or cross-profile marker. | OPEN |
| HIT-R04 | Authentication and profile binding | With disposable profiles A and B, send unauthenticated, malformed, wrong-profile, and cross-profile marker requests, including item IDs owned by the other profile. Then allow the native delivery to complete. | Every invalid marker request is rejected without writing a marker or exposing profile data. No item is rerouted to another profile. Because marker failure must not lose accepted native cargo, the unmarked delivery safely remains on **BTR Driver** exactly once. | OPEN |
| HIT-R05 | Marker/storage/routing failure fallback | In separate controlled runs, make the marker request unavailable, force marker persistence to fail, and force the custom UH-60 send step to fail before completion. | Accepted native cargo is never dropped. Each affected package follows the stock **BTR Driver** fallback exactly once, with the original item tree and no UH-60 duplicate. The failure is logged and does not alter the stock BTR trader. | OPEN |
| HIT-R06 | Replay and duplicate suppression | Repeat an identical marker request, replay a delivery callback where the fixture permits, reconnect, collect once, and restart/reload. | Repeated marker/callback activity cannot create a second sender delivery, a second item copy, or a second handling debit. Marker cleanup cannot erase another pending package. Final accounting contains every accepted item exactly once and no unaccepted item. | OPEN |

## Fika Matrix

| ID | Topology / scenario | Action | Required result | Status |
| --- | --- | --- | --- | --- |
| HIT-F01 | Human host is requester | On a human-hosted raid with at least one client observer, have the host call UH-60 Cargo Transfer, open transfer, cancel once, then submit a distinct item set with empty wallets and again with funded wallets in separate runs. Have the client stand in its rendered landing zone and attempt to interact. | Only the host receives the interaction, screen, temporary grid, zero-cost native purchase, and delivery. Neither wallet is charged for handling. Neither player sees an extraction countdown and the host remains in raid. The observer can render the helicopter but cannot open or affect transfer. Cancellation leaves every local helicopter for the remaining window; verified submission makes the host and every observer copy begin one immediate departure. The host's submitted items serialize once and arrive once after raid from **UH-60 Pilot**. | OPEN |
| HIT-F02 | Client requester / human host gate | Have a client call the UH-60 and inspect interactions while the host observes. In a diagnostic build, directly invoke the guarded open path once. | **SEND ITEMS VIA UH-60** is absent for the client. The direct invocation also fails before grid/service mutation and no native screen opens. No grid, item, RUB, TSC authorization, or service-availability mutation occurs on either machine. | OPEN |
| HIT-F03 | Two-client gate and isolation | Have Client A and Client B separately request a UH-60 and inspect the cargo interaction. | The cargo action is absent for both non-host requesters. Neither can create or affect the other profile's native grid, currency, delivery, or helicopter state. | OPEN |
| HIT-F04 | Client requester / dedicated headless gate | Against a dedicated headless host, have a client call the UH-60 while another client observes and inspect both players' interactions. | The cargo action is absent. Headless and observer create no UI or transfer state, and no profile is charged or mutated. | OPEN |
| HIT-F05 | Human-host disconnect/teardown | While the human host is the requester, cancel once, submit once, and then end the hosted raid in separate disposable runs. | Supported host-local transfers settle once. Teardown clears the screen, grid references, interaction, helicopter timing, and callbacks; the next hosted raid opens cleanly. | OPEN |
| HIT-F06 | Zero-cost handling cannot bypass the non-host gate | On client/human-host and client/headless topologies, attempt Cargo with empty and funded wallets. In a diagnostic fixture, attempt to invoke the prepaid native purchase path directly or reuse a host-local descriptor. | Non-host transfer remains unsupported and gated even with a zero quote. No requester-local or copied descriptor bypasses authority checks, opens native UI, changes a grid, moves cargo, or creates a delivery. Concurrent requesters cannot overwrite host service data or another profile's operation. | OPEN |

## Final Accounting Gate

For every successful topology, reconcile all of the following before acceptance:

- each new UH-60 handling quote is zero and its native purchase cost dictionary
  is empty; no carried money item is required;
- carried/stash RUB, USD, EUR, GP, and BTC are unchanged by new item handling,
  and no new fee journal, debit, or refund is created;
- the upfront configured Cargo purchase and dispatch authorization account
  normally, with no extra grant, consume, or refund from opening/cancelling/
  submitting the transfer screen;
- duplicate input, deferred callbacks, reconnect, replay, and restart create no
  second accepted item move, delivery, departure, or currency mutation;
- any recovery currency change belongs to an explicitly recorded legacy fee
  transaction for the same profile; it settles its original amount once and
  does not charge or reverse a new prepaid submission;
- no submitted item remains usable in the raid or appears immediately in the
  stash;
- valid TSC-marked cargo appears under **UH-60 Pilot**, while unmarked native
  BTR cargo remains under **BTR Driver**;
- marker, storage, or custom-routing failure falls back to **BTR Driver**
  without losing an accepted item;
- mixed packages keep connected item trees intact and route each root through
  exactly one sender;
- restart between submission and delivery preserves valid marker routing;
- unauthenticated, wrong-profile, malformed, and cross-profile marker requests
  cannot reroute or expose another PMC's cargo;
- the post-raid mail contains every accepted item exactly once;
- cancelled/unsubmitted items are never mailed or charged;
- stale controller, requester, profile, grid, screen, or raid bindings cannot
  apply prepaid handling to a new or unrelated native operation;
- ordinary BTR/Transit pricing and purchase requirements remain native; their
  cargo never acquires a zero-cost UH-60 override or an unrelated Pilot marker;
- legacy recovery failure preserves its journal without starting a new debit
  or falling back to carried cash;
- collecting mail once and restarting produces neither item loss nor a second
  copy;
- non-requesters and dedicated headless receive no requester cargo, charge,
  delivery, UI, or functional Cargo interaction.

Any unexplained duplicate, loss, cross-profile delivery, new handling charge,
carried-money requirement, BTR/Transit price change, immediate-stash insertion,
extra TSC authorization mutation, non-host gate bypass, or headless UI dependency
is a release-blocking failure. All rows remain OPEN until their in-game evidence
has been recorded and reviewed.
