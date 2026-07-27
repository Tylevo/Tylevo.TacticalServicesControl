# TSC Release Stabilization Execution Plan

Last updated: 2026-07-27
Target: the next public beta after the currently published 1.0.8 release
Status: Phase 3 implementation candidate build-verified; Phase 1, 1A, 2, and 3 runtime validation pending

This plan converts the current TSC audit and community bug reports into an ordered implementation and validation sequence. Priority describes risk; phase order also accounts for dependencies. Do not advance to the next phase until the current phase's exit gate is satisfied.

## Sources of Truth

Use these authorities in this order:

1. [GitHub Releases](https://github.com/Tylevo/Tylevo.TacticalServicesControl/releases) is the source of truth for what is publicly shipped. Verify the latest release through GitHub release metadata, its tag, and its attached artifact rather than relying only on a cached rendered page.
2. The release tag and attached ZIP are the source of truth for the exact code/artifact represented by a published version.
3. The GitHub default branch is the source of truth for committed unreleased development after it has been fetched and compared locally.
4. `C:\Users\tylev\Desktop\RaidOps\build_source\Tylevo.TacticalServicesControl-github-1.0.7` is the active local working checkout. Its uncommitted files are development candidates, not evidence of what has shipped.

At the time this plan was written, GitHub's release API reported:

- Release: **Tylevo's Tactical Services Control v1.0.8 Public Beta**
- Tag: `v0.9.8`
- Published: 2026-07-13 03:08:41 UTC
- Artifact: `Tylevo.TacticalServicesControl-v1.0.8-SPT4.0.13.zip`
- Artifact size: 41,236,560 bytes
- Release URL: `https://github.com/Tylevo/Tylevo.TacticalServicesControl/releases/tag/v0.9.8`

Re-query GitHub at the start of Phase 0 and Phase 7. If local documentation or the local checkout disagrees with GitHub about what was published, GitHub release metadata, the release tag, and the attached artifact win. Investigate and document the discrepancy before continuing.

## Operating Rules

- Use the local checkout above as the only working directory for this plan, while keeping its role distinct from the published and committed sources of truth.
- Preserve the existing uncommitted physical-phone UAV work. Do not discard, overwrite, or mix it with unrelated fixes.
- Keep each phase in its own reviewable commit or clearly labeled checkpoint.
- Treat Fika host-global settings and per-profile authorization state as different authority domains.
- Preserve `SupportRequestId`, `RequesterProfileId`, duplicate protection, and the original solo/human-host A-10 execution path.
- Never give a Fika client authoritative raid damage.
- Run deploy-suppressed builds while developing. Do not modify `D:\SPT`, create a release, tag, upload, or publish without explicit approval.
- Record live-test evidence for every supported topology: solo, human Fika host, Fika client, and dedicated headless where applicable.

## Execution Order

| Phase | Priority | Outcome |
| --- | --- | --- |
| 0 | Release safety | Preserve the current UAV work and establish a reproducible baseline |
| 1 | P1 | Fix authorization hydration and server-ledger consumption |
| 1A | P1 | Add a session-authenticated pre-raid authorization store |
| 2 | P1 | Make Fika request acceptance, consumption, commit, and refund transactional |
| 3 | P1 | Complete the physical-phone UAV live acceptance matrix |
| 4 | P2 | Correct Extraction and Priority Exfil timing configuration |
| 5 | P2 | Add regression coverage and complete build integration |
| 6 | Release safety | Reconcile versions, documentation, configuration, and the working tree |
| 7 | Release gate | Produce and validate a clean release candidate |

## Phase 0 - Preserve Work and Establish the Baseline

### Goal

Protect the current physical-phone UAV changes and prove that the starting point is understood before changing authorization, Fika, or extraction behavior.

### Tasks

- [x] Query GitHub Releases and record the latest release name, tag, publication state, timestamp, target commit, and asset list.
- [x] Fetch the release tag and default branch, then compare them with the local committed base.
- [x] Download or inspect the published artifact and record its SHA-256, size, roots, entry count, and included component versions.
- [x] Capture the complete working-tree status and diff.
- [x] Separate the existing UAV work from later bug fixes with a branch, checkpoint commit, or other recoverable snapshot.
- [x] Confirm the committed base, product display version, assembly version, and target SPT version.
- [x] Confirm the intended local reference paths without copying proprietary EFT, SPT, Fika, or WTT assemblies into the repository.
- [x] Run `git diff --check`.
- [x] Run all four deploy-suppressed builds: Core, Server, Fika Interop, and Fika bootstrap.
- [x] Record build results and any pre-existing warnings.
- [x] Identify the authoritative package entry points and resolve whether release archives intentionally contain only `BepInEx` and `SPT` roots or also root documentation.

### Exit Gate

- The published baseline is tied to verified GitHub release metadata, its tag, and its attached artifact.
- The current UAV work is recoverable.
- The working changes are attributable to a known checkpoint.
- All four projects build, or every baseline failure is documented before new code is written.
- No live installation or release artifact was modified.

### Completion Evidence - 2026-07-24

- GitHub release `v0.9.8`, local tag `v0.9.8`, `origin/main`, and the original local `main` all resolve to commit `b7835ea7995fef08ab7c95cfe8c9bf8af2be6c0c`.
- The published v1.0.8 asset is 41,236,560 bytes with SHA-256 `C3C0390CC6641E5F82C99C3E57D8AEDA358FD0278E667E6616E53963EB2FCB8D`.
- The published ZIP contains 207 entries: 172 files and 35 directory entries. Its only top-level roots are `BepInEx/` and `SPT/`.
- The package contains Core, Server, Fika Interop, and Fika bootstrap DLLs at assembly version `0.9.8.0`, plus eight Unity asset bundles.
- The local development work is preserved on branch `codex/tsc-stabilization-20260724` in five separate checkpoint commits:
  - `89f1fdc` - physical-phone UAV implementation and authority timing;
  - `5fcc718` - deploy-suppressed build guards;
  - `24a5846` - physical-phone UAV workflow documentation;
  - `5dc254e` - development handoff and stabilization plan;
  - `03aa54b` - isolated, unvalidated Forge-description draft.
- `git diff --check v0.9.8..HEAD` passes, and the checkpoint branch is clean.
- Core build: succeeded with 28 warnings and 0 errors.
- Server build: succeeded with 9 warnings and 0 errors.
- Fika Interop build: succeeded with 0 warnings and 0 errors.
- Fika bootstrap build: succeeded with 0 warnings and 0 errors.
- The live `D:\SPT` DLL timestamps remain dated 2026-07-13, confirming that the verification builds did not deploy.
- The published artifact and current ZIP target establish `BepInEx/` plus `SPT/` as the package-root contract. The root-document requirement in `BUILDING.md` is stale and must be corrected in Phase 6.
- Packaging still reads from live `SptDir` folders and updates an existing archive in place. Phase 7 must replace that with a clean, allowlisted stage to prevent live-state or stale-entry leakage.

## Phase 1 - Authorization Hydration and Ledger Consumption

### Goal

Eliminate the empty-tablet and all-services-maxed soft-lock while ensuring that a deployed persistent authorization is consumed exactly once from the correct player's server ledger.

### Implementation Tasks

- [x] Split host-global configuration authority from per-profile state synchronization.
- [x] Keep authenticated per-profile refresh active for Fika clients, either through `/tsc/config` or a dedicated player-state endpoint.
- [x] Synchronize at least:
  - authorization counts;
  - purchase-persistence state;
  - stash balance when required by the selected payment mode.
- [x] Keep Fika host values authoritative for shared prices, service availability, timing, and other raid-global settings.
- [x] Decouple player-state hydration from the hidden legacy `Use server config URL` toggle, or retire the obsolete toggle.
- [x] Define whether response authorization data is present and authoritative. Prefer a nullable value or explicit `AuthorizationsIncluded` flag so "omitted" and "authoritative empty" are distinguishable.
- [x] Apply included ledger state before returning from `AuthorizationLimitReached` and other responses that intentionally carry it.
- [x] Do not place per-player authorization counts in the broadcast Fika host-settings packet.
- [x] Refresh the deploy menu if hydration completes while the tablet is already open.
- [x] Ensure persistent deployment uses the server-backed begin/commit/refund path rather than decrementing only the client mirror.

### Implementation Evidence - 2026-07-24

- `/tsc/config` now refreshes authenticated per-profile state during every raid, including when the legacy URL toggle is false and when a Fika host owns the raid-global settings.
- `PlayerStateIncluded` distinguishes a resolved profile snapshot from omitted profile state. The client also recognizes the v1.0.8 nullable-stash contract so a matched upgrade is not required merely to hydrate an authoritative empty legacy ledger.
- `AuthorizationsIncluded` distinguishes an omitted purchase/mutation ledger from an authoritative empty ledger. Limit denials and valid ledger mutations return and apply the authoritative state before success or failure handling.
- Fika host-synchronized prices, payment settings, availability, tuning, and UAV settings remain authoritative; per-player counts are never added to the broadcast host-settings packet.
- Server-backed purchase, consume, commit, and refund responses reconcile the client mirror. Refund-disabled failures no longer create a local phantom credit, and Double Strafe commits only after both requested passes succeed.
- Server ledger mutations remain serialized through response reconciliation, so an older refund or commit snapshot cannot overwrite a newer purchase or consume result.
- A mutation epoch prevents a delayed pre-mutation config GET from overwriting a newer purchase, consume, commit, or refund response; canceled refreshes are also discarded after the backend call so an old raid cannot overwrite the next raid.
- The deploy-phone controller refreshes its owned-entry sequence while open, preserves the selected service when possible, and rebuilds only when membership or order changes.
- Runtime-only player fields are scrubbed from shared server configuration before it is saved or returned without a resolved profile.
- `git diff --check` passes.
- Deploy-suppressed clean rebuilds pass: Core with 28 baseline warnings, Server with 9 baseline warnings, Fika Interop with 0 warnings, and Fika bootstrap with 0 warnings; all have 0 errors.
- After explicit approval, the four Phase 1 DLLs built from commit `c98bb33` were installed into the live `D:\SPT` component paths. Every installed file was verified against its build-output SHA-256.
- The replaced v1.0.8 DLLs were backed up to `C:\Users\tylev\Desktop\RaidOps\backups\TSC-live-pre-phase1-c98bb33-20260724-102545`; the backup manifest records original and installed hashes plus rollback instructions.
- No player profile, configuration, authorization ledger, asset, release artifact, or published GitHub release was modified. The SPT server and game were not started as part of installation.
- The Phase 1 exit gate remains open until the solo and Fika runtime validation matrix below is recorded. Phase 2 implementation is recorded below, but it does not close this earlier runtime gate.

### Validation Matrix

- [x] Solo player starts a new raid with stored credits and sees them without purchasing.
- [ ] Human Fika host starts with stored credits and sees them without purchasing.
- [ ] Fika client joins with stored credits and sees them without purchasing.
- [ ] Two Fika clients receive only their own ledger counts.
- [ ] A player with every service at the storage limit can immediately deploy an owned service.
- [ ] A limit-denied purchase hydrates the tablet rather than leaving it locked.
- [ ] An authoritative empty ledger clears stale client state without creating phantom credits.
- [ ] Purchase -> deploy -> consume -> commit decrements the server ledger once.
- [ ] Cancellation or failed execution refunds once when required by the configured persistence policy.
- [ ] Reconnect and next-raid hydration preserve the decremented count.
- [ ] A legacy saved `Use server config URL = false` value cannot suppress player-state hydration.
- [ ] Opening the deploy tablet during initial synchronization either waits safely or updates without requiring it to be reopened.

Solo smoke evidence recorded 2026-07-24: the authenticated snapshot loaded,
the deployment tablet opened with the existing ledger, and Extraction could be
selected without making a new purchase. The run ended before target confirmation,
so consume/commit behavior remains unverified.

### Exit Gate

- No topology requires a purchase to reveal previously owned authorizations.
- A maxed ledger cannot soft-lock deployment.
- Client display, server ledger, and persistence mode agree after purchase, deployment, cancellation, reconnect, and raid transition.

## Phase 1A - Authenticated Pre-Raid Authorization Store

### Goal

Let a signed-in player buy persistent authorizations from the main menu before entering a raid, using the same server-owned prices, stash debit, profile save, and authorization ledger as the in-raid Uplink.

Implementation began early on the explicitly authorized stabilization test branch
while the Phase 1 runtime matrix remains open. This does not close either phase's
runtime gate. The current in-raid physical-phone purchase and deployment paths
remain unchanged.

### Server Hardening

- [x] Require a resolvable authenticated HTTP session for player-state snapshots and purchase mutations. Treat request/query profile identifiers only as consistency hints and never as fallback authentication.
- [x] Derive the authorization-ledger key canonically from the resolved player profile so request hints cannot split one player's credits across multiple keys.
- [x] Mark pre-raid purchase intent explicitly and reject it with `PurchasePersistenceDisabled` before any debit when persistent authorizations are disabled.
- [x] Require a unique purchase `RequestId` and make repeated delivery of the same authenticated purchase return the original result without a second debit or grant.
- [x] Preserve the existing serialized debit, profile-save, ledger-grant, and rollback transaction.
- [x] Return an authoritative purchase catalog containing server prices, service availability, stash balance, stored counts, persistence state, and maximum stored count.
- [x] Accept an optional player-confirmed quote and reject a changed price before creating a persistent journal record or debiting the stash; preserve accepted and prepared request replay at the original journaled price.

### Client UI and State

- [x] Postfix the stable SPT 4.0 `MenuScreen.Show(Profile, MatchmakerPlayerControllerClass, ESessionMode)` boundary and inject one idempotent **TSC UPLINK** main-menu button.
- [x] Place **TSC UPLINK** directly below **Records** when available, otherwise directly below **Character**; shift later menu entries by one slot only when needed, and restore their exact positions when TSC is disabled or removed.
- [x] Open a standalone 2D menu page; do not reuse `UavDeviceController`, player hands, the carried Uplink item, or raid camera/FOV state.
- [x] Perform one authenticated, bounded state fetch when the page opens and after a mutation. Do not enable background menu polling.
- [x] Enable **Buy** only after an authoritative profile snapshot arrives and confirms persistent authorizations plus a server-backed stash payment source.
- [x] Display server-authoritative service prices, enabled/disabled state, stash balance, owned counts, and maximum stored count.
- [x] Disable duplicate clicks while a purchase is pending, submit a stable `RequestId`, and apply the authoritative response before redrawing.
- [x] Require an explicit modal confirmation showing the selected service, authoritative price, and projected stash balance; cancellation creates no request ID and sends no mutation.
- [x] Provide a dashboard link that derives `/tsc/admin` from the active SPT launcher-selected HTTP(S) host instead of the legacy hard-coded TSC config URL.
- [x] Clear menu state when the backend profile/session changes or signs out.
- [x] Preserve raid-start reset followed by authenticated rehydration so a pre-raid purchase appears in the deployment tablet without another purchase.

### Implementation Evidence - 2026-07-24

- `MenuScreen.Show(Profile, MatchmakerPlayerControllerClass, ESessionMode)` now attaches one sibling-scoped **TSC UPLINK** button and a standalone main-menu overlay. It creates no `GameWorld`, hands controller, physical item, or raid camera state.
- The page renders all six exact service mappings with authoritative enabled state, price, stash balance, owned count, and configured maximum. It performs one authenticated GET on open and one verification GET after a correlated successful mutation, with no idle network polling.
- Pre-raid POSTs bind the profile and backend session captured at click time, use `BuyPersistentAuthorization`, submit one stable `RequestId`, and require an exact echoed ID before applying a response.
- The server resolves the HTTP session first, treats body/query IDs only as consistency checks, and derives the ledger key from the authenticated PMC profile. Profile fields and prepared-purchase recovery IDs are scrubbed from global/admin configuration.
- The schema-3 ledger writes a durable `Prepared` purchase before debit and permanently retains accepted request IDs outside the capped audit list. Retries return the original price/result without a second grant.
- Prepared records store deterministic pre-debit and expected-post-debit rouble-stack fingerprints. Recovery debits only on an exact pre-state, finalizes without debit only on the exact expected post-state, and otherwise remains `PersistentPurchasePending` for same-ID retry instead of guessing.
- Authenticated snapshots expose the oldest prepared request ID per service. After a client or server restart, the menu adopts that original ID and exposes **RETRY**; storage limits and refunds account for prepared reservations.
- Omitted response cost and balance use `-1`, preserving valid zero-cost services without displaying an early denial as an authoritative zero price.
- Independent transaction, API-compatibility, and final whole-diff reviews found no remaining P0-P2 issue. `git diff --check` passes.
- Deploy-suppressed builds pass with 0 errors: Core has 28 baseline warnings, Server has 9 baseline warnings, and both Fika assemblies have 0 warnings.
- The implementation is recorded in commit `dcf1894` (`feat: add authenticated pre-raid authorization store`).
- The initial dashboard-shell experiment is recorded in commit `0f3663c` (`style: align pre-raid store with dashboard`). Live acceptance showed that its sidebar/card hierarchy exceeded the intended scope and its dynamic OS-font path rendered soft in EFT, so it is superseded.
- Commit `3f83aaa` (`fix: simplify pre-raid store styling`) restores the original centered six-row storefront, retains only the dashboard palette, thin borders, darker compact controls, and corrected top-button spacing, and restores Unity's built-in Arial path for crisp text. An independent final review found no P0/P1 issue; the deploy-suppressed Core build passes with 0 errors and the same 28 baseline warnings.
- Commit `8948b10` (`fix: align pre-raid store header groups`) aligns the title/subtitle block with the status and service-row left edge and aligns the stash/control block with the service-row right edge. It changes five horizontal offsets only.
- Commit `285cde3` (`fix: separate stash from header controls`) moves the stash label upward by 12 layout units, eliminating its text-rectangle overlap with the Refresh/Close row.
- Commit `1434898` (`feat: place TSC Uplink below Records`) inserts the menu entry immediately after **Records** and moves **Trading**, **Hideout**, **Exit**, and other known later entries down one slot. The reflow is idempotent, restores the original stack during fallback/disable/teardown, and reconciles external menu relayouts. Two independent reviews found no remaining P0-P2 issue; the deploy-suppressed Core build passes with 0 errors and the same 28 baseline warnings.
- Commit `444e089` (`fix: fall back TSC Uplink to Character`) keeps **Records** as the preferred anchor and uses **Character** whenever the Records mod/button is unavailable. It fills an inactive Records vacancy without double-shifting, handles late Records activation, restores cached positions on disable/teardown, and delegates reflow to an active Unity layout group. Two independent reviews found no remaining P0-P2 issue; the deploy-suppressed Core build passes with 0 errors and the same 28 baseline warnings.
- Commit `fae8281` (`fix: align TSC menu entry with Character`) keeps **Records** as the vertical/order anchor but uses **Character** for horizontal RectTransform geometry, preventing Career Log/Menu Overhaul relayouts from pushing the Character-derived clone off-stack. A one-second drift guard repairs only real late relayouts and tracks manual/LayoutGroup transitions without leaving gaps. Two independent reviews found no remaining P0-P2 issue; the deploy-suppressed Core build passes with 0 errors and the same 28 baseline warnings.
- Commit `1de0ce7` (`fix: reserve a unique TSC menu slot`) replaces the transient-vacancy heuristic with a deterministic stack invariant derived from the native signed Play-to-Character spacing. The final order is **Character -> Records -> TSC -> Trading -> Hideout -> Exit**, or **Character -> TSC -> Trading -> Hideout -> Exit** when Records is unavailable. It repairs repeated Career Log/Menu Overhaul rebuilds, selects the preferred duplicate Records row, restores Squad/Pit placement across late Records activation, closes and reopens the slot without accumulating offsets, and retires duplicate/stale TSC controllers and buttons safely. Independent layout, timing, and lifecycle reviews found no remaining P0/P1 issue in the installed Menu Overhaul path; the deploy-suppressed Core build passes with 0 errors and the same 28 baseline warnings.
- Commit `38166e1` (`feat: confirm pre-raid purchases`) adds the footer **DASHBOARD** link and a raycast-blocking confirmation modal with separate cancel/confirm paths. Confirm revalidates the authenticated profile/session and local snapshot, while the matched server rejects a changed `ExpectedCost` before journal preparation or debit. Accepted and prepared retries bypass the quote check and retain their original idempotent request and journaled price.
- Independent UI, transaction-boundary, replay-compatibility, and final whole-diff reviews found no remaining P0-P2 issue. Deploy-suppressed Core and Server builds pass with 0 errors and their existing 28 and 9 baseline warnings; `git diff --check` passes.
- After explicit approval, all four reviewed DLLs were installed into the live `D:\SPT` component paths and verified byte-for-byte by SHA-256 against the build outputs.
- The replaced DLLs plus exact pre-first-start ledger/config copies are backed up at `C:\Users\tylev\Desktop\RaidOps\backups\TSC-live-pre-phase1a-dcf1894-20260724-114230`; `INSTALL-MANIFEST.md` records hashes and rollback instructions.
- The superseded dashboard-shell Core and its rollback instructions remain backed up at `C:\Users\tylev\Desktop\RaidOps\backups\TSC-live-pre-dashboard-style-0f3663c-20260724-120948`.
- The current live matched pair includes the purchase-confirmation contract: Core SHA-256 `F99E49CBDD5D66CEA9AD020C8E8F13A31BB360BF0EA4A853508E2E46AA1D8E56` and Server SHA-256 `CC422A533BC6500EAEF15AB180F735161BB0E96D8BD6EF374C3FFB27700B155A`. Fika and Fika Interop remain byte-identical to the Phase 1A install.
- The replaced Core (`23FF2879559379CDBD70B420181CEF8C8BAC339C1665A123EE51A83C721A1D4D`), Server (`A53604DB2BC0A1F95CE2962B0A3A618FDEAAC3540423F015687FC34692D21D0E`), and rollback instructions are backed up at `C:\Users\tylev\Desktop\RaidOps\backups\TSC-live-pre-purchase-confirm-38166e1-20260727-101740`; preceding rollback backups remain available.
- No player profile, TSC configuration, authorization ledger, release artifact, or published GitHub release was modified during installation. The SPT server and game were not started.
- The Phase 1A exit gate remains open until the runtime matrix below is recorded. Phase 2 implementation is recorded below, but it does not close this earlier runtime gate.

### Validation Matrix

- [ ] An authenticated solo player buys one authorization in the main menu; the stash is debited once, the profile is saved, and the ledger increments once.
- [ ] Closing and reopening the page preserves the authoritative balance and counts.
- [ ] **CANCEL** closes the confirmation without allocating a request ID, debiting the stash, or changing the ledger.
- [ ] **CONFIRM BUY** charges exactly the displayed quote; if the dashboard price changes first, the server returns `PurchaseQuoteChanged` without a journal record or debit and the page shows the updated price.
- [ ] **DASHBOARD** opens `/tsc/admin` on the active SPT launcher-selected HTTP(S) host.
- [ ] Entering a raid rehydrates the pre-purchased authorization before deployment and does not require another purchase.
- [ ] A player at the per-service limit is denied without a debit and sees the authoritative owned count.
- [ ] Insufficient funds, a disabled service, persistence disabled, or an unavailable profile cannot debit the stash.
- [ ] Mismatched profile hints and unauthenticated requests are rejected without disclosing another player's balance or ledger.
- [ ] Repeating the same `RequestId` after a timeout returns the original outcome without a second debit or grant.
- [ ] Concurrent purchases at the balance or storage boundary cannot overspend or exceed the configured limit.
- [ ] Profile-save or ledger-save failure restores the stash and returns a deterministic denial.
- [ ] Two Fika users can pre-purchase only for their own authenticated profiles and later receive only their own raid ledger.
- [ ] A dedicated headless server requires no client UI and accepts the same authenticated per-player requests.
- [ ] Repeated `MenuScreen.Show` calls create one button, and opening/closing the page does not leave an overlay or consume menu text input.
- [ ] No periodic `/tsc/config` traffic or log spam occurs while the store is closed.

### Exit Gate

- Pre-raid purchase never requires a `GameWorld`, live player, carried Uplink, or raid hands controller.
- No unauthenticated or cross-profile request can read or mutate player state.
- Every accepted purchase has one stash debit and one persistent ledger grant, including timeout/retry cases.
- A purchased authorization survives menu-to-raid transition and is immediately deployable after hydration.

## Phase 2 - Transactional Fika Request Acceptance and Refunds

### Goal

Make transport delivery, authority acceptance, execution start, authorization consumption, commit, and refund explicit and idempotent.

Implementation proceeded as a reviewable checkpoint while the Phase 1 and 1A
runtime gates remain open. This does not advance Phase 3 or claim headless/Fika
live acceptance.

### Implementation Tasks

- [x] Document the current state machine for every networked support request.
- [x] Treat packet send success only as transport delivery, not gameplay acceptance.
- [x] Add or confirm an authority response keyed by `SupportRequestId` that communicates accepted or rejected state to the requester.
- [x] Start requester-side visuals and advance the payment lifecycle only after the authoritative transition required by the configured `ConsumeOn` policy.
- [x] Do not broadcast an accepted support event until the selected host/headless executor has accepted the request.
- [x] Return a deterministic rejection when no valid executor exists or the relevant headless feature is disabled.
- [x] Refund on rejection, timeout, cancellation, or executor-start failure when the authorization has already entered a pending/consumed state.
- [x] Make acceptance, commit, refund, replay, and duplicate packets idempotent.
- [ ] Bound pending requests and verify clearing across death, disconnect, raid end, and repeated raids. Disconnect, raid-end, manager-reset, and plugin teardown paths are implemented; death-specific cleanup remains live-unverified.
- [x] Preserve the original solo and human-host A-10 ballistic path.

### Implementation Evidence - 2026-07-27

- Fika request send now reports transport only. A client waits for an explicit
  `FireSupportAuthorityResultPacket` or the matching canonical accepted
  broadcast before advancing the service/payment lifecycle.
- Result packets carry the full accepted request payload. Direct results and
  broadcasts use one fingerprinted client dedupe path, while authority replay
  is keyed by the immutable request ID/type/pass/requester/geometry payload.
- An explicit cancellation packet arbitrates with a guarded execution-start
  transition. At the authority, cancellation can win before execution starts
  but cannot overwrite an accepted executor. If both accepted paths and cancel
  settlement are unavailable for more than 35 seconds, however, the requester
  currently returns `AuthorityCancelUnsettled` and refunds without proving that
  acceptance lost.
- The client wait is bounded at 30 seconds plus a five-second cancel-settlement
  window; authority admission is bounded at 20 seconds. Client pending requests
  are capped at 8, authority in-flight requests at 128, and the authority table
  at 512 unique IDs per raid. Every admitted accepted or rejected outcome
  remains replayable until raid reset; unknown IDs are rejected once full.
- Dedicated-headless A-10 now performs side-effect-free preflight, publishes
  the terminal accepted result/broadcast, and only then begins damage. Tracer
  bursts are held until accepted delivery and dropped on rejection.
- The server ledger is schema 4 with durable `Pending`, `Committed`,
  `Refunded`, and `ExpiredRefunded` authorization-use records. Terminal state
  no longer depends on the capped audit history; same-ID commit/refund replays
  are idempotent and conflicting replays are rejected.
- Refunds restore the credit that was already reserved even if the storage
  maximum was lowered while pending. New grants remain capped until an
  over-limit restored balance is reduced.
- Consume, commit, and refund bind to the backend session/profile captured at
  reservation time and require exact request-ID and support-type correlation.
  Transient finalization failures retry the same mutation ID.
- A-10 Double Pass uses one parent authorization and deterministic child pass
  IDs. Pass 0 acceptance commits the authorization once; pass 1 is deduplicated
  best-effort and cannot refund an already delivered first pass.
- Post-acceptance extraction presentation/audio failure cannot convert a live
  helicopter into a rejection/refund.
- Accepted dedicated-headless A-10 work observes teardown cancellation before
  tracer publication, after its shot loop, and throughout direct-fallback
  damage application, preventing an abandoned raid entry from emitting new
  damage into teardown or the next session.
- `git diff --check` passes. All four deploy-suppressed builds pass with 0
  errors: Core has 28 existing warnings, Server has 9 existing warnings, and
  Fika Interop plus Fika bootstrap have 0 warnings on the final rebuilds.
- Commit `440140b` was installed to the closed live `D:\SPT` instance as one
  matched four-DLL set. Installed SHA-256 values are Core
  `2F0C6E40ABBB61B7BE1AFD0B41EE29516D3F41A45A12AF7D590A49C67420314F`,
  Fika bootstrap
  `D7675DA112232912C7A32A3382F5B548D9753181B912E9A1675FFE6483E87800`,
  Fika Interop
  `9E0F3CD19771D021533D48272A6C175890329B7029C0DF5FF8CA51995D92EE45`,
  and Server
  `035E6B30709A63A5C894033A8DE45EAEB34C56EDBAA4CF4A6131CCE921E2F544`.
- The replaced DLLs, complete pre-start `storage` directory, current
  `tsc-config.json`, hashes, and rollback procedure are backed up at
  `C:\Users\tylev\Desktop\RaidOps\backups\TSC-live-pre-phase2-440140b-20260727-114338`.
  The server and game were not started, and live profile/config/ledger contents
  were unchanged during installation. The next server initialization upgrades
  the ledger to schema 4; DLL-only downgrade after that point is unsafe.
- Known distributed limit: client commit/refund retries are in-memory. A crash,
  permanent logout, or backend outage beyond pending expiry can make an already
  accepted service `ExpiredRefunded` and therefore free. Closing that window
  requires a durable client outbox or authority-to-ledger commit protocol.
- Known result-path limit: losing both accepted paths and the cancel-settlement
  replay beyond the client's 35-second bound can make an authority-executed
  service refund immediately; a late accepted replay can then arrive after the
  original waiter was removed.
- Fresh remote Fika requests must match `NetPeer.Player.ProfileId`; missing
  peer/player identity fails closed. Identity rejections are cached as terminal
  outcomes for the request ID, so a later binding change cannot turn a refunded
  retry into fresh execution. Local human-host requests retain their explicit
  peerless path.
- The executable runtime checklist is
  [`validation/phase2-fika-transaction-matrix.md`](validation/phase2-fika-transaction-matrix.md).
  All live rows remain open.

### Validation Matrix

- [ ] Solo support requests execute and consume once.
- [ ] Human-host requests execute and consume once.
- [ ] Fika-client requests wait for host/headless acceptance.
- [ ] Duplicate request packets neither execute nor consume twice.
- [ ] Lost or duplicate acceptance packets converge on one final state.
- [ ] Unavailable or disabled dedicated-headless execution rejects and refunds cleanly.
- [ ] Requester disconnect, death, or raid teardown cannot strand a pending authorization.
- [ ] A-10 single pass, A-10 double pass, both extraction services, UAV Recon, and Focused Sweep follow the intended lifecycle.

### Exit Gate

- No authorization is lost merely because a packet was sent.
- No accepted request can execute or consume twice.
- Every rejection and timeout reaches a deterministic, observable final state.

## Phase 3 - Physical-Phone UAV Live Acceptance

### Goal

Validate the preserved physical-phone UAV work in live raids and correct only
observed failures.

The implementation and static-audit checkpoint advanced at the user's
direction while the earlier multiplayer/headless gates remain open. This does
not mark any live Phase 2 or Phase 3 row complete.

### Implementation and Static-Audit Tasks

- [x] Carry one host-authoritative UAV duration, scan cadence, and range
  snapshot through request acceptance and requester presentation.
- [x] Make loiter presentation authority-originated, request-bound,
  request-ID-deduplicated, and locally timed on each process.
- [x] Reject overlapping fresh UAV links from one requester while allowing
  independent links for different requesters.
- [x] Keep requester radar UI off non-requesters and dedicated headless hosts.
- [x] Make async phone equip, release, failure, death/raid invalidation, and
  weapon restoration generation- and ownership-safe.
- [x] Reset recon, loiter, phone input, render texture, screen renderer, and
  Fika request state at their lifecycle boundaries.
- [x] Publish an executable solo/Fika/headless validation matrix without
  claiming live acceptance.

### Implementation Evidence - 2026-07-27

- Accepted UAV request/result packets now carry duration, scan interval, and
  range as one canonical authority snapshot. Client-local defaults cannot
  partially replace an accepted host contract.
- Fika clients can no longer originate a loiter command. The authority
  publishes one accepted-request-bound loiter event, and every receiver
  validates it against the canonical accepted event before request-ID
  deduplication.
- Loiter controllers are keyed by request ID rather than one global instance.
  Different requesters can have staggered world presentations, while one
  requester's fresh overlap is authority-rejected until its accepted link
  expires or is torn down.
- Human hosts render one world aircraft, dedicated headless hosts render none,
  and only the requesting player creates the private recon feed. Disconnect,
  manager reset, plugin teardown, and raid boundaries clear requester UI,
  accepted-event replay state, reservations, and loiter objects.
- Phone equip/restore operations now carry a boundary generation and explicit
  ownership. Cancellation and EFT's post-drop callback make one atomic
  deferred-restore decision, synchronous failures receive the actual operation
  handle, and one claim gate prevents duplicate weapon restoration.
- Fire-support controller, spotter, and UI async creation is generation-checked.
  A stale controller tears down only its captured UI/spotter and cannot dispose
  the replacement's shared runtime. Phone-screen shutdown is idempotent and
  restores captured renderer state only after successful capture.
- Three independent reviews found no remaining P0-P2 actionable blocker in the
  lifecycle, packet serialization/authority, or final whole diff.
- `git diff --check` passes. Deploy-suppressed rebuilds pass with 0 errors:
  Core has 28 existing warnings, Server has 9 existing warnings, and Fika
  Interop plus Fika bootstrap have 0 warnings.
- The reviewable implementation and matrix are recorded in commit `a37d5c2`
  (`feat: harden physical UAV lifecycle and authority`).
- Build-output SHA-256 values are Core
  `572CDBC7EF85AD91787B281A5C8B6EFC12E04A2EC908DBF6043A69DA7352DE87`,
  Fika bootstrap
  `51BBEF5D4D7F31B693491902EF13F1A439916DD538978889C0A36F9E600F95CF`,
  Fika Interop
  `5C215B0FB1A58B3011C26D6C629186C977DF94DC286AAEFA194FA13C725B3629`,
  and Server
  `E732405482C7F258295CA3B4355913A3DD07A90890690C6E5D78AC93B943DC33`.
- The packet layout changed, so Core, Server, Fika Interop, and Fika bootstrap
  must be installed as one matched set on the server and every participant.
  This candidate has not yet been installed live.
- The executable checklist is
  [`validation/phase3-physical-phone-uav-matrix.md`](validation/phase3-physical-phone-uav-matrix.md).
  Every live row remains open, and Phase 2 remains separately open.

### Solo Tests

- [ ] Standard UAV and Focused Sweep purchase, deployment, activation, expiry, and repeat use.
- [ ] Hold the configured radar key while stationary, walking, sprinting, aiming, and changing direction.
- [ ] Release during the asynchronous phone equip and verify the previous weapon restores.
- [ ] Rapid press/release and repeated open/close cycles do not duplicate or strand the phone.
- [ ] Raid FOV, weapon state, hand state, camera state, and phone renderers restore correctly.
- [ ] Inventory, death, extraction, raid end, and scene teardown leave no overlay, camera, renderer, or input state behind.
- [ ] Radar orientation, scan cadence, range, contact pooling, and countdown remain correct.
- [ ] Phone-link duration and loiter-aircraft duration expire together.
- [ ] Stowing the phone does not pause or extend the recon contract.

### Fika Tests

- [ ] Human-host requester sees only the host's feed.
- [ ] Fika-client requester sees the feed while the host and other clients do not.
- [ ] Two clients requesting at different times remain isolated by requester identity.
- [ ] A dedicated headless host creates no phone presentation or radar UI.
- [ ] Disconnect, death, and raid teardown remove requester-owned recon state.
- [ ] Duplicate or overlapping activation cannot extend or mutate the active link.
- [ ] Host-synchronized duration, range, and scan cadence match the dashboard.

### Exit Gate

- The physical phone is stable through the full input and teardown matrix.
- Requester isolation holds for human-host, client, and dedicated-headless sessions.
- Recon timing is server/host-authoritative and does not drift between the phone and aircraft.

## Phase 4 - Extraction Timing Configuration

### Goal

Make the dashboard contract match runtime behavior for standard Extraction and Priority Exfil.

### Product Decision

The current dashboard promises separate extraction-zone countdowns. Unless the product intentionally wants one shared hold time, implement separate values. If a shared value is intentional, remove the dead priority field and label the remaining setting as common.

### Implementation Tasks

- [ ] Make extraction-zone countdown tuning support-type-aware.
- [ ] Pass both `extraction.extractTimeSeconds` and `priorityExfil.extractTimeSeconds` through the server-config client.
- [ ] Carry both countdowns through the Fika settings packet and its serialization.
- [ ] Account for packet compatibility or require a matched client set for the new protocol.
- [ ] Initialize `HeliExfiltrationPoint` with `Extract` or `PriorityExfil`.
- [ ] Use the matching countdown when the player enters or re-enters the zone.
- [ ] Wire `extraction.dispatchDelaySeconds`, currently represented in the dashboard but hardcoded to eight seconds at runtime, or remove the misleading field.
- [ ] Preserve the already-correct per-service helicopter wait windows and animation-speed multipliers.
- [ ] Validate `waitTimeSeconds >= extractTimeSeconds` and return an actionable dashboard/config error for invalid combinations.
- [ ] Replace fixed completion estimates with lifecycle/event-driven completion, or make the estimate account for configured animation speed.

### Validation Matrix

- [ ] Standard and priority countdowns can be configured to visibly different values in solo.
- [ ] Standard and priority countdowns remain distinct for a Fika client receiving host settings.
- [ ] Leaving and re-entering the zone resets the correct service-specific value.
- [ ] Standard and priority dispatch delays match their configured values.
- [ ] Invalid wait/countdown combinations are rejected before a paid service becomes impossible.
- [ ] Default behavior remains unchanged unless an intentional migration is documented.

### Exit Gate

- Every exposed extraction timing field either affects gameplay as labeled or has been removed.
- Priority Exfil cannot silently inherit the standard extraction countdown.
- A valid configuration cannot make the helicopter leave before the extraction countdown can complete.

## Phase 5 - Regression Coverage and Build Integration

### Goal

Turn the repaired behavior into repeatable checks so later phone, Fika, payment, and extraction work cannot silently reintroduce it.

### Tasks

- [ ] Add focused tests for authorization-response presence semantics.
- [ ] Add ledger lifecycle tests covering purchase, limit denial, begin, commit, refund, reconnect, and two-profile isolation.
- [ ] Add request-state-machine tests for acceptance, rejection, timeout, duplicate packets, and teardown.
- [ ] Add Fika settings-packet round-trip tests, including both extraction timers and dispatch values.
- [ ] Add tuning-precedence tests for local, server, and host-synchronized values.
- [ ] Add extraction-trigger timer initialization/reset tests.
- [ ] Add config validation tests for wait/countdown combinations.
- [ ] Add package-layout allowlist checks.
- [ ] Add Fika Interop and the Fika bootstrap project to the normal solution/build verification path.
- [ ] Provide one documented local verification command or script that builds all required projects with deployment disabled.
- [ ] Add CI checks that do not require redistributing proprietary assemblies. Document any full-build checks that must remain local.
- [ ] Run `git diff --check` as part of the verification path.

### Exit Gate

- The confirmed authorization and extraction regressions fail under test when their fixes are removed.
- All four projects participate in the documented verification path.
- Package-layout and repository hygiene checks are repeatable.

## Phase 6 - Version, Documentation, and Repository Reconciliation

### Goal

Convert the accumulated work into an intentional, reviewable next-release state.

### Tasks

- [ ] Review every modified and untracked file and assign it to a phase/commit.
- [ ] Remove generated, obsolete, or accidental files without discarding intentional user work.
- [ ] Choose the next public display version, internal assembly version, and release/tag version.
- [ ] Update version metadata consistently across build properties, plugin metadata, server metadata, config templates, and release filenames.
- [ ] Update `CHANGELOG.md`.
- [ ] Update `README.md`, `docs/fika.md`, `docs/dashboard.md`, `docs/known-issues.md`, and `docs/roadmap.md`.
- [ ] Update the handoff with the final authority model, validation evidence, and remaining limitations.
- [ ] Create release notes and a Forge description for the chosen version.
- [ ] Reconcile the package-root contradiction between current build documentation and the verified 1.0.8 archive.
- [ ] Confirm all shipped configuration templates match server defaults and dashboard ranges.
- [ ] Confirm dedicated-headless A-10 remains clearly labeled experimental.
- [ ] Confirm no secrets, local paths, profiles, logs, proprietary binaries, or temporary artifacts are tracked.

### Exit Gate

- The working tree contains only intentional release changes.
- Version numbers and release naming are internally consistent.
- Documentation describes tested behavior rather than planned behavior.
- Known limitations are explicit.

## Phase 7 - Clean Release Candidate and Final Acceptance

### Goal

Produce a reproducible release candidate from a clean source state and prove that it is safe to hand to testers.

### Build and Package Tasks

- [ ] Build from a clean worktree or clean clone at the intended release commit.
- [ ] Run all four deploy-suppressed release builds.
- [ ] Run the complete automated verification suite.
- [ ] Stage the package from an explicit allowlist.
- [ ] Verify the final archive roots against the resolved package contract.
- [ ] Verify required Core, Server, Fika Interop, Fika bootstrap, assets, config, and metadata files are present and version-matched.
- [ ] Verify WTT Common Lib, EFT/SPT/Fika proprietary assemblies, profiles, logs, caches, source prompts, and local paths are absent.
- [ ] Extract the archive into an empty temporary directory and validate its layout.
- [ ] Calculate and record the archive SHA-256, size, entry count, and asset-bundle count.
- [ ] Perform a clean-install smoke test only after explicit approval to modify a test SPT installation.

### Final Live Acceptance

- [ ] Repeat the Phase 1 persistent-authorization matrix.
- [ ] Repeat the Phase 2 authority/commit/refund matrix.
- [ ] Repeat the critical Phase 3 UAV requester and teardown matrix.
- [ ] Repeat the Phase 4 standard/priority extraction timing matrix.
- [ ] Verify clean server and client logs for startup, purchase, deployment, extraction, teardown, and repeated raids.
- [ ] Confirm upgrade behavior from the published 1.0.8 configuration and ledger format.

### Release Gate

- The release candidate is reproducible from the recorded commit.
- All P1 issues are closed with evidence.
- P2 issues are closed or explicitly accepted and documented.
- The archive passes content, clean-install, and live-smoke validation.
- Publishing, tagging, uploading, replacing assets, or changing Forge remains a separate user-approved action.

## Standard Development Verification

Use the repository's configured reference paths. Keep deployment disabled:

```powershell
dotnet build .\project\SamSWAT.FireSupport\SamSWAT.FireSupport.Core.csproj -c "SPT-4.0 Release" "-p:SptDir=D:/SPT/" "-p:SptSharedAssembliesDir=C:/Users/tylev/Desktop/RaidOps/SPT Assemblies/" -p:SkipTscDeploy=true
dotnet build .\project\SamSWAT.FireSupport.Server\SamSWAT.FireSupport.Server.csproj -c "SPT-4.0 Release" "-p:SptDir=D:/SPT/" "-p:SptSharedAssembliesDir=C:/Users/tylev/Desktop/RaidOps/SPT Assemblies/" -p:SkipTscDeploy=true
dotnet build .\project\SamSWAT.FireSupport.Fika.Interop\SamSWAT.FireSupport.Fika.Interop.csproj -c "SPT-4.0 Release" "-p:SptDir=D:/SPT/" "-p:SptSharedAssembliesDir=C:/Users/tylev/Desktop/RaidOps/SPT Assemblies/" -p:SkipTscDeploy=true -p:BuildProjectReferences=false
dotnet build .\project\SamSWAT.FireSupport.Fika\SamSWAT.FireSupport.Fika.csproj -c "SPT-4.0 Release" "-p:SptDir=D:/SPT/" "-p:SptSharedAssembliesDir=C:/Users/tylev/Desktop/RaidOps/SPT Assemblies/" -p:SkipTscDeploy=true -p:BuildProjectReferences=false
git diff --check
```

These commands may read locally installed references but must not deploy into or modify the live SPT installation.
