# TSC consolidation validation - 2026-10-07

This is a source integration, not a release or an installation. Core remains
1.3.13 targeting SPT 4.1.5. The optional companion is 0.10.1. Existing in-game
files were not used as build destinations. No new helmet work is included.

## Integration and preservation

The integration starts at `release/1.3.13`. Original PR #14 supplies the
visual-only HH-60 implementation; its commits are retained. PR #15 supplies
stash-only payments, special-slot support, phone handoff and Pilot stock.
The subsequent companion work is reviewed separately from the original PR.

The three active and three older dirty checkouts have verified all-ref Git
bundles and complete working-file snapshots, including untracked content.
Historical evidence stays unchanged in private archives, with original and
destination paths recorded externally. Later A-10 audio/round-planner work is
preserved on `development/a10-preserved-20261007` and excluded from this integration.

The canonical checkout is independent of old task-folder paths. Machine paths
belong in ignored `Shared.User.props`; see [BUILDING.md](../../BUILDING.md).
Dependencies, local exports, packages, validation evidence, and private
integrations belong outside Git. The private rope provenance record does not
establish redistribution permission, and its code/assets are not published.

## Review fixes

- Failed nonpersistent cash dispatches preserve their base-request provenance
  through refunds and retries. Genuine prepaid credits retain their budget bypass.
- Escape/RMB after payment commit cancels optional deployment while the payment
  and phone restoration finish. A successful authorization remains available.
- Extraction gun flashes use the selected visual marker, including fresh UH-60
  loading and switching back from a cached HH-60.
- Companion builds take an explicit approved Core hash, audit native contracts
  before compilation, then check the compiled pin against that exact binary.
  Runtime identity guards remain fail-closed. A Git-stamped Core rebuilt at a
  different commit must be audited and paired again; see the companion build guide.

## Automated validation

Executed locally against copied SPT 4.1.5 references with .NET SDK 10.0.201:

- 387 Core regression cases and 44 JavaScript/dashboard tests passed.
- CI package/layout guards and non-deploying build configuration checks passed.
- All 12 synthetic HH-60 suites passed, plus six incremental Core-identity and
  invalid-hash/rejection scenarios. The identity scenarios use synthetic fixtures.
- All 28 Python exporter/overlay tests passed using pinned dependencies.
- All four runtime DLLs built with zero errors; six existing nullable/obsolete-API
  warnings remain in the solution build.
- Local donor gun-visual checks passed (97); optional native input checks passed
  (38). The donor payload remains private and is not needed by synthetic CI.

The final committed build, exact Core/companion hashes, native metadata report,
package inventory and installed-file comparison are recorded in the external
validation evidence. These checks do not run a raid or verify rendering, AI,
physics, audio, extraction or network behavior.

## Required in-raid checks

Use a solo SPT 4.1.5 raid and enable experimental options individually. The
companion still refuses firing in Fika/multiplayer or when another human is
present; do not remove those restrictions to test this integration.

1. Select UH-60, then HH-60, including first load, switching during load, and
   reuse of an already pooled aircraft. Check correct body, crew, rotors, windows,
   original interaction areas and muzzle flashes on the selected gun.
2. Summon each escort mode in an open outdoor area. Check approach/hold/patrol,
   target acquisition, gun arcs, hull and rotor clearance, and protected actors.
3. On an inbound extraction, hold the configured trigger while moving. Verify
   visible gun alignment, accepted rounds, sound and flashes before arrival.
4. Keep the trigger held through arrival: gun pose and cadence should continue
   without reset. Release it and confirm firing stops immediately. Check pause,
   menus, focus loss and permission toggles also suppress firing.
5. Cancel during inbound/arrival, summon again, and reuse pooled aircraft. Check
   cleanup, no stale sounds/flashes, no residual trigger ownership and no duplicate guns.
6. Board and extract with each appearance. Check pickup continuity and departure.
   Current cover fire deliberately stops on departure; sustained departure fire
   is a remaining limitation, not a verified or implemented promise.
7. Transfer cargo with each appearance, stay in raid, and verify Pilot mail
   delivery and one charge only. Check failed/cancelled request payment recovery.
8. Cancel phone deployment after payment commit, retry a refunded Hybrid cash
   request, and compare remaining requests with a genuine prepaid authorization.

Original contributor-reported visual-only tests predate these changes. They do
not establish runtime verification of the new integrations or multiplayer support.
