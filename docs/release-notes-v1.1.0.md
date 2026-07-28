# Tylevo's Tactical Services Control v1.1.0 Public Beta

> Release-candidate draft. This build has not been published.

Target: SPT 4.0.13. This update follows the published v1.0.8 build.

## Highlights

- Added an authenticated **TSC UPLINK** store to the main menu. It appears below **Records** when that entry is available and otherwise below **Character**.
- Added an explicit pre-raid purchase confirmation showing the service, authoritative price, current stash balance, and projected balance. The store also includes a link to the active server's TSC Dashboard.
- Added configurable RUB, USD, and EUR payments across the dashboard, in-raid phone, pre-raid store, carried wallet, stash debit, persistent purchase journal, and Fika settings sync.
- Added a configurable UAV presentation choice in F12. `Phone` keeps the held-`J` physical Uplink; `HUD` shows only the square live scanner in a selected screen corner.
- Hardened persistent authorization hydration and the Fika request lifecycle so payment, authority acceptance, execution, commit, refund, and duplicate delivery have explicit states.
- Made standard Extraction and Priority Exfil use separate configurable dispatch, wait, zone-countdown, and helicopter-speed contracts.

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

## Extraction Timing

- Standard Extraction and Priority Exfil retain distinct dispatch delay, wait window, extraction-zone countdown, and speed multiplier values.
- Solo requests honor the configured dispatch delay. In Fika, the raid authority validates the requested timing snapshot and owns dispatch.
- Only the requester receives a functional extraction point and countdown. Other non-headless peers render the accepted helicopter visual; a dedicated headless authority creates no client presentation.
- Leaving the zone resets the correct service-specific countdown. Multi-collider boundary jitter cannot restart or complete it twice.
- Invalid timing is rejected unless `waitTimeSeconds >= ceil(extractTimeSeconds + 1)`.
- Existing schema-less standard dispatch settings migrate to the historical effective eight-second delay; an explicit current-schema zero remains immediate dispatch.

## Verification

- Added 39 zero-dependency regression tests covering authorization presence,
  ledger persistence, published-v1.0.8 config and ledger migration, request
  races and deduplication, Fika settings serialization, tuning precedence,
  extraction timing, and countdown reset.
- The preceding 35-test baseline passed 20 consecutive runs: 700 test
  executions without a failure or timeout. The four migration cases pass in
  the current suite.
- Added CI-safe repository, JSON, JavaScript, solution, deployment-guard, and package-layout checks that require no proprietary game assemblies.
- Added one deploy-disabled local verification path for Core, Server, Fika Interop, Fika bootstrap, and the regression runner.
- The full local release build completed with 0 errors. Its 13 warnings are pre-existing source/API warnings.

## Updating

1. Back up player profiles, `config/tsc-config.json`, and the complete TSC `storage` directory.
2. Close the SPT server, launcher, game, every Fika client, and any headless process.
3. Extract the release ZIP into the SPT root.
4. Install the exact same TSC build on the server, human host, every client, and any dedicated headless host.

The ZIP intentionally does not contain `config/tsc-config.json`, so extracting
an upgrade cannot replace custom settings with release defaults. A clean
installation creates schema-3 defaults on first server start. An existing
configuration migrates to schema 3 and defaults an older currency-less config
to RUB. The authorization ledger migrates to schema 5. Downgrading only the
Server DLL after the ledger upgrade is unsupported; restore the matching
pre-upgrade storage backup with the older DLL set.

Required dependencies are not bundled: UnityToolkit v2.0.1, WTT Client Common Lib, and WTT Server Common Lib. Project Fika is optional and required only for multiplayer.

## Validation Status And Known Limitations

- Solo SPT has been reported healthy, but the complete persistent-purchase, recon, and extraction acceptance matrices are not yet recorded.
- The new human-host, Fika-client, two-client, and dedicated-headless transaction/recon/extraction matrices remain open. Do not treat automated tests or a successful build as live multiplayer acceptance.
- No current headless tester has yet verified that the experimental A-10 executor applies damage once and settles the matching authorization correctly in a real raid.
- If both accepted-result paths and cancellation settlement are lost beyond the bounded wait, an authority-executed service can still be refunded. A late acceptance remains deduplicated, but the service may become free.
- Client commit/refund retries are in memory. A client crash, permanent logout, or backend outage lasting beyond pending expiry can refund an already delivered service.
- Dedicated-headless A-10 is not claimed to match the human-host ballistic path on every map or mod combination.
- Remote third-person phone animation sync is not included.
- Phone inventory-inspect presentation may still need polish.
- Mortar and artillery support are not included.
