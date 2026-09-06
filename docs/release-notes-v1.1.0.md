# Tylevo's Tactical Services Control v1.1.0 Public Beta

> **Historical documentation.** These instructions and results describe this
> earlier version. For the prepared TSC v1.3.11 / SPT 4.1.5 update, use the
> [release notes](release-notes-v1.3.11.md) and [installation guide](dependencies.md).
> TSC v1.3.11 requires standalone UnityToolkit 2.0.2; both new packages are
> unpublished. See the [archive index](archive/README.md) for older availability.

> Separate tester prerelease published as GitHub tag `v1.1.0-beta.1`. This does not consume the final `v1.1.0` tag.

Target: **SPT 4.1.2 / EFT 0.16.9.5.40743 tester**. This port follows the
published v1.0.8/4.0.13 build, whose archive remains the immutable asset
baseline. The expected artifact is
`Tylevo.TacticalServicesControl-v1.1.0-SPT4.1.2-TESTER.zip`.

This target declaration is not a compatibility claim. SPT 4.1.2 build,
server-boot, menu, raid, and Fika acceptance must be recorded separately in
`docs/port/SPT-4.1-PORT-LOG.md`.

## Highlights

- Added an authenticated **TSC UPLINK** store to the main menu. It appears below **Records** when that entry is available and otherwise below **Character**.
- Added an explicit pre-raid purchase confirmation showing the service, authoritative price, current stash balance, and projected balance. The store also includes a link to the active server's TSC Dashboard.
- Added configurable RUB, USD, and EUR payments across the dashboard, in-raid phone, pre-raid store, carried wallet, stash debit, persistent purchase journal, and Fika settings sync.
- Added a configurable UAV presentation choice in F12. `Phone` keeps the held-`J` physical Uplink; `HUD` shows only the square live scanner in a selected screen corner.
- Added an F12 Cargo Transfer handling-fee source. `Carried` remains the default native behavior; `Stash` pays the same EFT-calculated RUB fee from the authenticated PMC stash.
- Hardened persistent authorization hydration and the Fika request lifecycle so payment, authority acceptance, execution, commit, refund, and duplicate delivery have explicit states.
- Replaced the released Priority Exfil slot with **UH-60 Cargo Transfer** while preserving its saved key, credits, artwork, dispatch delay, wait window, and speed tuning for upgrade compatibility.

## Pre-Raid Authorization Store

- The store displays all six services with server-authoritative availability, price, stash balance, owned count, and storage limit.
- Pre-raid buying requires persistent authorizations and a server-backed stash payment source. It never falls back to carried cash or a local-only credit.
- Purchases are bound to the authenticated PMC session. A stable request ID prevents a retry from charging or granting twice.
- Price and currency changes fail closed. The player must review and confirm the updated quote.
- Prepared purchases can be recovered and retried after an interrupted response without guessing whether another debit is required.
- Opening a raid rehydrates the authenticated ledger, so a pre-purchased authorization is available from the deployment phone without buying again.

## Authorizations And Payments

- Authoritative empty ledgers now clear stale client counts, while omitted ledger data cannot erase valid state.
- A player who already owns every service at the storage limit can still hydrate the ledger and deploy an owned authorization.
- Purchase, limit denial, consume, commit, refund, reconnect, and two-profile isolation use the server-backed ledger.
- Ledger writes use atomic replacement, backup recovery, corrupt-file preservation, serialized mutation, and rollback on save failure.
- Persistent purchase and authorization-use records remain idempotent across repeated requests.
- RUB, USD, and EUR use their matching EFT currency items in carried and stash wallets. Changing the selected currency does not convert numeric service prices.
- A prepared or accepted purchase remains pinned to its original currency and price when it is retried.

## Fika Request Reliability

- Sending a packet is transport delivery, not gameplay acceptance. A requester waits for an authority result before starting requester-owned effects or finalizing payment.
- Stable support request IDs and immutable request fingerprints deduplicate admission, accepted results, broadcasts, A-10 passes, helicopter presentation, and recon activation.
- Rejection, pre-start cancellation, timeout, and executor-start failure follow one idempotent refund path when an authorization has already been reserved.
- A-10 Double Pass commits its parent authorization when the first pass is accepted; the second pass is deduplicated and cannot refund an already delivered first pass.
- Solo SPT and human Fika hosts preserve the original Arys-style A-10 runtime and ballistic path. Fika clients remain visual-only.
- The dedicated-headless A-10 damage executor remains separately gated and **experimental**.

## UAV And Phone Presentation

- Standard UAV and Focused Sweep carry one authority-provided duration, range, and scan cadence through acceptance, requester presentation, and loiter lifetime.
- Radar UI is requester-local. A human host does not receive a client's private feed, and a dedicated headless process creates no phone, scanner, camera, or HUD object.
- The physical-phone view uses `J` by default. Holding the key raises the Uplink; releasing it restores the previous weapon while the recon contract continues.
- The `K` deployment phone and held-`J` radar now reveal directly in the upright presentation instead of briefly showing a landscape or ghost phone.
- HUD mode shows only the square scanner, sweep, orientation labels, player marker, and contacts. Phone header, status, telemetry, footer, and surrounding chrome remain exclusive to the held-phone view.
- HUD position can be set to any screen corner in F12.
- Async equip, rapid release, death, disconnect, raid teardown, render textures, phone renderers, loiter objects, and repeated-raid state have guarded cleanup paths.

## UH-60 Services

- Standard Extraction retains its dispatch delay, wait window, extraction-zone countdown, and speed multiplier.
- UH-60 Cargo Transfer is cargo-only: it opens EFT's native mid-raid transfer screen for the requester, never starts an extraction countdown, and never ends the raid.
- Cargo Transfer reuses the legacy `PriorityExfil` configuration and authorization slot one-for-one. Its dispatch delay, wait window, and speed multiplier remain configurable; the old Priority extraction-time field is retained only as an ignored compatibility value.
- After EFT confirms at least one paid item reached its persistent delivery grid, Cargo Transfer immediately triggers the successful-pickup departure. Cancellation, insufficient funds, and rejected native purchases retain the remaining landed window, and Fika observers receive one reliable request-bound departure event.
- The Cargo dispatch authorization and EFT's native per-item handling charge are separate costs. The handling fee remains RUB-only and never follows the configurable RUB, USD, or EUR authorization currency.
- The F12 **Transfer fee source** defaults to `Carried`, preserving EFT's native carried-RUB purchase. `Stash` uses an authenticated TSC server debit while leaving the native transfer grid, item removal, and delivery flow unchanged.
- Stash-fee prepare, commit, refund, and replay are keyed by one stable transaction ID in an idempotent write-ahead journal. A retry cannot charge twice, a rejected native purchase refunds once, and cancelling before submission charges nothing.
- Unfinished client-side commit or refund intents survive a restart and retry when the same PMC reconnects. New stash-funded cargo submissions remain blocked until that recovery reaches a confirmed terminal state.
- `Stash` mode fails closed when the matching server endpoint is unavailable, including a client paired with an older TSC server. It does not fall back to carried cash or submit cargo after an unconfirmed debit.
- Valid TSC-marked cargo returns through post-raid mail from an isolated **UH-60 Pilot** messenger. This does not rename or replace the stock **BTR Driver**; unmarked native cargo stays with BTR, and a marker or custom-routing failure falls back to stock BTR delivery rather than dropping accepted items.
- Solo requests honor the configured dispatch delay. In Fika, the raid authority validates the requested timing snapshot and owns dispatch.
- Only the requester receives the functional local service point: an extraction point for standard Extraction or a loading interaction for Cargo. Other non-headless peers render the accepted helicopter visual; a dedicated headless authority creates no client presentation.
- Cargo Transfer is available in solo SPT and to a human Fika host. Non-host Fika clients fail closed before purchase, authorization consumption, or dispatch until native transfer pricing can be synchronized safely.
- Leaving standard Extraction resets its extraction countdown. Leaving Cargo closes its loading interaction; Cargo never starts, resumes, or completes a countdown.
- Standard Extraction rejects timing unless `waitTimeSeconds >= ceil(extractTimeSeconds + 1)`. Cargo independently validates only dispatch delay, landed wait time, and speed; its legacy `priorityExfil.extractTimeSeconds` value is never an active runtime setting.
- Existing schema-less standard dispatch settings migrate to the historical effective eight-second delay; an explicit current-schema zero remains immediate dispatch.

## Verification

- The current 101-test zero-dependency regression suite covers authorization
  presence and persistence, published-v1.0.8 migration, request races and
  deduplication, Fika serialization and service semantics, multi-currency
  payment, extraction/cargo isolation, native UH-60 item transfer and delivery,
  stash-fee recovery, and ownership-safe headless A-10 damage routing.
- Added CI-safe repository, JSON, JavaScript, solution, deployment-guard, and package-layout checks that require no proprietary game assemblies.
- Added one deploy-disabled local verification path for Core, Server, Fika Interop, Fika bootstrap, and the regression runner.
- The pre-port SPT 4.0.13 local release build completed with 0 errors and 17
  existing nullable/obsolete-API warnings. That result does not validate the
  SPT 4.1.2 port.

## Updating

1. Back up player profiles, `config/tsc-config.json`, and the complete TSC `storage` directory.
2. Close the SPT server, launcher, game, every Fika client, and any headless process.
3. Extract the release ZIP into the SPT root.
4. Install the exact same TSC build on the server, human host, every client, and any dedicated headless host.

After extraction, the client files belong under
`BepInEx/plugins/Tylevo.TacticalServicesControl/` and the server files belong
under `SPT_Runtime/user/mods/Tylevo.TacticalServicesControl/`.

The ZIP intentionally does not contain `config/tsc-config.json`, so extracting
an upgrade cannot replace custom settings with release defaults. A clean
installation creates schema-3 defaults on first server start. An existing
configuration migrates to schema 3 and defaults an older currency-less config
to RUB. The authorization ledger migrates to schema 5. Downgrading only the
Server DLL after the ledger upgrade is unsupported; restore the matching
pre-upgrade storage backup with the older DLL set.

Required dependencies are not bundled: use a verified SPT 4.1-compatible
UnityToolkit build and matching WTT Client/Server CommonLib 3.x builds.
Project Fika is optional and required only for multiplayer; use one matching
SPT 4.1 client/server pair. Exact commits, versions, and hashes must be pinned
in the port log before distributing a tester. Do not reuse 4.0 dependency
binaries merely because they load.

## Validation Status And Known Limitations

- Prior-version solo SPT has been reported healthy, but no such report is an
  SPT 4.1.2 acceptance result. The complete 4.1.2 persistent-purchase, recon,
  standard-Extraction, and Cargo-transfer matrices remain open.
- The new human-host, Fika-client, two-client, and dedicated-headless transaction, recon, standard-Extraction, and Cargo-transfer matrices remain open. Do not treat automated tests or a successful build as live multiplayer acceptance.
- No current headless tester has yet verified that the experimental A-10 executor applies damage once and settles the matching authorization correctly in a real raid. The previous direct health-controller fallback has been removed: headless-owned bots now use Fika's player damage lifecycle and remote humans use Fika's explosive damage-packet wrapper, but the lethal corpse/downed matrix remains open until tested live.
- If both accepted-result paths and cancellation settlement are lost beyond the bounded wait, an authority-executed service can still be refunded. A late acceptance remains deduplicated, but the service may become free.
- Client commit/refund retries are in memory. A client crash, permanent logout, or backend outage lasting beyond pending expiry can refund an already delivered service.
- Dedicated-headless A-10 is not claimed to match the human-host ballistic path on every map or mod combination.
- Remote third-person phone animation sync is not included.
- Phone inventory-inspect presentation may still need polish.
- Mortar and artillery support are not included.
