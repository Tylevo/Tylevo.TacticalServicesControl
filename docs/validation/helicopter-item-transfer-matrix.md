# UH-60 Mid-Raid Item Transfer Validation Matrix

Status:

- Implementation candidate: **OPEN - live acceptance not yet run**.
- Supported-path rows start `OPEN`; a build pass or solo smoke test does not
  close human-host Fika acceptance. Non-host Fika transfer rows are explicitly
  `BLOCKED` by the current fail-closed authority gate.
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

The resulting transaction keeps EFT's native Transit/BTR transfer grid,
submission, and delivery accounting. Its handling fee remains the native
service's **RUB** quote, but the local F12 **Transfer fee source** controls
which wallet pays:

- `Carried` is the default and preserves EFT's native carried-RUB purchase;
- `Stash` debits the authenticated PMC's nested stash RUB through the matching
  TSC server, then lets the native transaction own item submission and
  delivery;
- neither source spends an additional TSC UH-60 authorization or uses TSC's
  configurable RUB, USD, or EUR dispatch pricing;
- cancelling before submission charges neither source;
- submitted items leave the raid inventory but do **not** appear immediately
  in the stash;
- delivery is completed through the native post-raid mail/delivery flow.

Stash-fee prepare, commit, refund, and status replay use one stable transaction
ID and an idempotent write-ahead server journal. A repeated prepare cannot
debit twice, a failed native purchase refunds once, a committed native
submission remains charged exactly once, and a repeated terminal request
cannot reverse or duplicate it. If the endpoint is missing or does not confirm
the authenticated debit, including when a newer client is paired with an
older server, Cargo fails closed without submitting items or falling back to
carried cash.

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
charge, cargo, or delivery. Solo and a requesting human Fika host are the
supported candidate paths. A non-host Fika requester receives a fail-closed
gate with no cargo interaction because EFT's item-dependent transfer cost is
not included in Fika's native purchase descriptor; the host could otherwise
validate or charge a different amount. The open path repeats the authority
check defensively before any grid or service mutation. A dedicated headless
host creates no UI and does not enable this unsafe client purchase path.

## Test-Set and Evidence Requirements

Before each topology:

1. Stop SPT, the game, all Fika clients, and any dedicated headless process.
   Install one matched candidate on every participant and record the candidate
   commit plus all installed DLL SHA-256 values.
2. Back up the affected profiles and mail/storage state. Use distinguishable,
   disposable test items and record each template, stack count, durability,
   attachment tree, and found-in-raid status.
3. Record the requester's starting carried/stash RUB, USD, EUR, TSC
   authorization counts, in-raid inventory, stash, and pending mail. Record
   the native transfer fee displayed by the EFT screen.
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
| HIT-T07 | Successful-transfer wait override | Use a long Cargo wait. Submit and pay for distinguishable cargo early in one run; cancel without submitting in another. | The verified paid item move short-circuits only the first run's remaining landed wait and plays the successful-pickup departure. Cancellation charges nothing, publishes no completion, and resumes the second run's original remaining wait through normal no-pickup departure. | OPEN |

## Handling-Fee Source Matrix

Run the supported rows in both solo SPT and with the requesting player as the
human Fika host unless a row names a narrower fixture. Record carried and stash
RUB separately; USD, EUR, TSC authorization price, and authorization count must
remain unchanged in every row.

| ID | Scenario | Action | Required result | Status |
| --- | --- | --- | --- | --- |
| HIT-P01 | Default carried fee | Remove the local Cargo fee-source entry or set it to `Carried`, stage cargo with a known native fee, and submit with sufficient carried and stash RUB. | `Carried` is the default. Carried RUB decreases by exactly the displayed native fee once; stash RUB is unchanged. Item submission and later **UH-60 Pilot** delivery complete once. No TSC stash-fee journal entry is created. | OPEN |
| HIT-P02 | Stash exact balance | Set the source to `Stash`, leave exactly the displayed fee in the authenticated PMC stash, keep enough carried RUB to expose fallback, and submit once. | The exact stash RUB balance is debited once and reaches zero. Carried RUB is unchanged, no fallback occurs, and the native submission/delivery completes once. | OPEN |
| HIT-P03 | Stash insufficient balance | Set `Stash`, leave the stash one RUB below the displayed fee, keep sufficient carried RUB, and attempt submission. | Submission fails closed before item removal. Neither wallet changes, no authorization changes, no delivery is created, carried RUB is never used as fallback, and the helicopter remains for the active retry window. | OPEN |
| HIT-P04 | Nested stash RUB | Set `Stash` and distribute sufficient RUB across multiple nested containers and stacks in the authenticated stash. Submit cargo whose fee requires more than one recorded stack. | Only the signed-in PMC's eligible nested stash RUB is consumed, totaling the exact fee once. Carried cash and every other profile are unchanged; item submission and delivery complete once. | OPEN |
| HIT-P05 | Cancel without charge | With sufficient carried and stash RUB, open, stage, and cancel once under `Carried`, then repeat under `Stash`. | Every staged item returns unchanged. Neither wallet is charged, no stash-fee prepare is recorded for cancellation, no cargo is submitted, no delivery is created, and the helicopter retains its remaining retry window. | OPEN |
| HIT-P06 | Double-click, replay, restart, and refund | Under `Stash`, double-click submit, then use a controlled fixture to repeat the same transaction's prepare/commit/status messages. Restart the client with a pending commit or refund intent and reconnect the same PMC. In a separate run, force the native purchase to reject after a prepared debit and replay refund/status. | One UI submission creates at most one native purchase and one successful departure. Prepare debits once, committed replays remain charged once, and a rejected native purchase restores the exact debit once without triggering departure. Pending recovery survives restart, resolves when the same PMC reconnects, and blocks another stash-funded submission until terminal confirmation. Repeated commit, refund, or status calls create no second debit, refund, item move, delivery, or departure. | OPEN |
| HIT-P07 | Legacy/missing fee endpoint | Pair the candidate client with a controlled older server that lacks `/tsc/uh60-transfer/fee`, select `Stash`, and attempt submission with both wallets funded. | Cargo fails closed with an actionable error. No item leaves the raid, neither wallet changes, no authorization changes, no delivery is created, and the client does not fall back to carried payment. | OPEN |
| HIT-P08 | Stock BTR isolation | Keep Cargo's F12 fee source set to `Stash`, then perform a normal stock BTR item transfer with distinguishable cargo and sufficient carried RUB. | Stock BTR retains its native carried-payment, grid, messenger, and delivery behavior. TSC does not prepare a stash fee, debit stash RUB, mark the cargo for **UH-60 Pilot**, or alter the **BTR Driver** contact. | OPEN |

## Solo Matrix

| ID | Scenario | Action | Required result | Status |
| --- | --- | --- | --- | --- |
| HIT-S01 | Service isolation | Call standard UH-60 Extraction and verify it has no cargo action. In a separate raid, call UH-60 Cargo Transfer, wait for the aircraft to land, and inspect interactions outside and inside its active zone. | Standard Extraction retains its normal countdown/extract behavior and never shows **SEND ITEMS VIA UH-60**. Cargo Transfer shows that action only to the local requester while its point is active and the F12 transfer toggle is enabled. Cargo never shows a countdown or extracts the PMC. | OPEN |
| HIT-S02 | Open and cancel | Enter the landed Cargo Transfer zone, open transfer, place one test item in the temporary grid, then cancel/close without submitting. | No extraction countdown appears before, during, or after the screen. Cancel returns every staged item unchanged, charges no native fee, creates no delivery, and changes no TSC authorization/currency state. A second open starts with an empty grid. | OPEN |
| HIT-S03 | Successful native delivery | Open transfer, submit several distinguishable items including a stack and an attachment tree, then end the raid normally. | Each submitted item leaves the raid inventory exactly once. The native RUB fee shown by EFT is charged exactly once from the selected fee source; the other RUB wallet, USD, EUR, TSC prices, and TSC authorization counts are unchanged. As soon as EFT verifies the item move, the screen closes and the helicopter immediately begins its successful-pickup departure. Nothing is inserted directly into the stash. One **UH-60 Pilot** post-raid delivery contains the exact submitted item set, while the native BTR contact remains unchanged. | OPEN |
| HIT-S04 | Delivery collection and persistence | Collect the HIT-S03 mail, reload the profile/menu, restart SPT, and inspect stash and mail again. | Every submitted item is collectible once, preserves its recorded structure/state as supported by EFT, and persists in the stash. No duplicate mail, second charge, disappearing item, restored in-raid copy, or sender change appears after reload/restart. | OPEN |
| HIT-S05 | Cancel/failure retry | Open and cancel once, reopen, then force one payment rejection and reopen again before finally completing one valid transfer. | Cancel and payment rejection preserve the canonical player grid and remaining helicopter window without charging, moving items, or starting departure. The final verified submission charges and delivers once, blocks another open, and starts exactly one immediate departure. No old item appears in a later temporary grid. | OPEN |
| HIT-S06 | Zone exit and re-entry | Close transfer normally, leave the Cargo Transfer zone, wait, and re-enter. Where test controls permit, also force the player outside while the screen is open. | The interaction disappears outside, no countdown appears, and re-entry exposes a clean transfer interaction. Forced exit cannot leave movement, cursor, interaction, or transfer state stuck. | OPEN |
| HIT-S07 | Wait window while open | Keep the transfer screen open longer than the configured helicopter wait window, then cancel/close it. | The helicopter and point remain present while the screen is open, so normal departure never invokes EFT's destructive forced-close fallback. The helicopter wait clock resumes only after a voluntary close, unsubmitted items return normally, and departure occurs after the remaining active window. | OPEN |
| HIT-S08 | Raid teardown while open | End the raid separately by extraction, death, and abort while the transfer screen is open; then start another raid. | Teardown force-closes UI and releases input, callbacks, temporary state, and service-availability overrides. Unsubmitted items follow EFT's normal raid outcome and are not delivered. The next raid has no stale screen/grid/interaction. Previously confirmed submissions are neither lost nor duplicated and retain their intended delivery sender. | OPEN |
| HIT-S09 | First-use grid, canonical selection, and fail-closed behavior | On a fresh raid where the requester has not used Transit or BTR, open UH-60 cargo and confirm its canonical grid is initialized. Reopen without submitting to confirm the existing grid is not recreated. In a controlled diagnostic build or fixture, make Transit unavailable to exercise BTR fallback, then make both controllers or native grid initialization unavailable. | First use initializes exactly one requester grid through the chosen raid-owned controller; reopening preserves it. Transit is selected first and existing canonical BTR only as fallback. With no valid canonical controller/grid/service data, opening fails with an actionable warning, moves no items, changes no currency, and creates no delivery. No standalone controller or untracked grid is constructed. | OPEN |

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
| HIT-F01 | Human host is requester | On a human-hosted raid with at least one client observer, have the host call UH-60 Cargo Transfer, open transfer, cancel once, then submit a distinct item set under both `Carried` and `Stash` in separate runs. Have the client stand in its rendered landing zone and attempt to interact. | Only the host receives the interaction, screen, temporary grid, selected-source fee, and delivery. Neither player sees an extraction countdown and the host remains in raid. The observer can render the helicopter but cannot open or affect transfer. Cancellation leaves every local helicopter for the remaining window; verified submission makes the host and every observer copy begin one immediate departure. The host's submitted items serialize once and arrive once after raid from **UH-60 Pilot**. | OPEN |
| HIT-F02 | Client requester / human host gate | Have a client call the UH-60 and inspect interactions while the host observes. In a diagnostic build, directly invoke the guarded open path once. | **SEND ITEMS VIA UH-60** is absent for the client. The direct invocation also fails before grid/service mutation and no native screen opens. No grid, item, RUB, TSC authorization, or service-availability mutation occurs on either machine. | BLOCKED - native price is not host-synchronized |
| HIT-F03 | Two-client gate and isolation | Have Client A and Client B separately request a UH-60 and inspect the cargo interaction. | The cargo action is absent for both non-host requesters. Neither can create or affect the other profile's native grid, currency, delivery, or helicopter state. | BLOCKED - native price is not host-synchronized |
| HIT-F04 | Client requester / dedicated headless gate | Against a dedicated headless host, have a client call the UH-60 while another client observes and inspect both players' interactions. | The cargo action is absent. Headless and observer create no UI or transfer state, and no profile is charged or mutated. | BLOCKED - native price is not host-synchronized |
| HIT-F05 | Human-host disconnect/teardown | While the human host is the requester, cancel once, submit once, and then end the hosted raid in separate disposable runs. | Supported host-local transfers settle once. Teardown clears the screen, grid references, interaction, helicopter timing, and callbacks; the next hosted raid opens cleanly. | OPEN |
| HIT-F06 | Future client authority contract | Extend the Fika/native purchase contract so the raid authority derives or validates the exact canonical-grid item price instead of accepting a requester-local quote, then rerun client/human-host and client/headless delivery accounting under both fee sources. | The authority proves the exact cost before any debit, one client purchase settles once, and concurrent requesters cannot overwrite global service cost or another profile's fee transaction. | BLOCKED - authoritative item-price synchronization required |

## Final Accounting Gate

For every successful topology, reconcile all of the following before acceptance:

- under `Carried`, carried RUB decreased by exactly the recorded native fee per
  accepted submission and stash RUB remained unchanged;
- under `Stash`, the authenticated PMC's stash RUB decreased by exactly the
  recorded native fee per accepted submission and carried RUB remained
  unchanged;
- no second debit or refund occurred after double-click, reconnect, replay, or
  restart; a native rejection after prepared stash debit restored exactly that
  debit once;
- USD, EUR, configured TSC service prices, and all TSC authorization counts are
  unchanged by item transfer;
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
- a missing stash-fee endpoint fails closed without carried-payment fallback;
- ordinary BTR transfer never enters TSC's stash-fee or UH-60 marker paths;
- collecting mail once and restarting produces neither item loss nor a second
  copy;
- non-requesters and dedicated headless receive no requester cargo, charge,
  delivery, UI, or functional Cargo interaction.

Any unexplained duplicate, loss, cross-profile delivery, double charge,
immediate-stash insertion, TSC authorization mutation, or headless UI
dependency is a release-blocking failure.
