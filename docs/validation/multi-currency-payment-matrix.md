# Multi-Currency Payment Validation Matrix

Status:

- v1.1.0 implementation/build checks: complete.
- Phase 7 candidate identity: **VERIFIED - installed on the local server;
  matched peer installation remains open**.
- Live acceptance: **OPEN - not yet run**.
- Every live row below starts `OPEN`; no build or static result closes a row.

This matrix validates server-authoritative RUB, USD, and EUR selection across
configuration, display, carried and stash wallets, persistent purchases, and
Fika synchronization.

## Phase 7 Candidate Record

Complete this record before changing any live row from `OPEN`.

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

## Test-Set Safety

Before testing:

1. Stop the SPT server, game, launcher, every Fika client, and any dedicated
   headless process before installing DLLs.
2. Install the exact candidate recorded above on the server and every
   participant. Mixed builds are valid only for the explicit fail-closed
   compatibility row.
3. Back up the test profile, `config/tsc-config.json`, and the complete TSC
   storage directory while the server is stopped. Keep schema-5 storage with
   its matching v1.1.0 Server DLL.
4. Use a disposable profile and low, distinctive temporary service prices.
   Place visibly different RUB, USD, and EUR amounts in carried inventory,
   secure container, and nested stash containers so the debit source is clear.
5. Record the config revision, payment mode, selected currency, service price,
   owned authorization count, and every applicable currency stack before and
   after each action.
6. Rows requiring a lost response or prepared-purchase interruption need a
   local test-only fault point, proxy, or debugger. Do not publish an
   instrumented test build.

Changing the selected currency changes the unit applied to the saved numeric
price; it is not a foreign-exchange conversion.

## Evidence And Status

For every row, record:

- row ID, `OPEN`/`PASS`/`FAIL`/`BLOCKED`, tester, date, topology, and evidence
  location;
- candidate commit, package-manifest identity, all four DLL hashes,
  `configSchemaVersion`, authorization-ledger schema, and server config
  revision;
- profile ID, service, payment mode, selected currency, currency item template,
  authoritative price, purchase request ID, and support request ID where
  applicable;
- authorization count plus every RUB/USD/EUR stack and wallet total before and
  after the action;
- dashboard, main-menu store, confirmation screen, and phone captures when
  display or synchronization is under test;
- client, host/headless, and SPT server logs through the next restart or raid
  when persistence or cleanup is under test.

`TO RECORD` is a placeholder, not evidence. Attach the evidence location before
changing a row from `OPEN`, and preserve the first failing run before retrying.

Informal solo observation on 2026-07-28 confirmed that a service-price save
during an active raid affected a subsequent purchase without restarting the
raid. MC-F06 keeps the cross-peer, headless, and in-flight quote behavior open
until it has matched-candidate evidence.

## Configuration And Display Matrix

| ID | Scenario | Action | Required result | Status | Evidence |
| --- | --- | --- | --- | --- | --- |
| MC-C01 | Authoritative currency selection | Select and save `RUB`, `USD`, and `EUR` one at a time in the dashboard. Refresh the dashboard and open the main-menu store, purchase-confirmation screen, and in-raid phone after each save. | Each surface shows the saved authoritative currency and matching symbol/code, with the same config revision and prices. No stale prior currency appears after refresh/reconnect. | OPEN | TO RECORD |
| MC-C02 | No implicit price conversion | Save distinctive numeric prices, then change only `paymentCurrency` through RUB -> USD -> EUR -> RUB. | Every numeric service price remains unchanged. Only its currency unit changes; no conversion, rounding, or cross-service mutation occurs. | OPEN | TO RECORD |
| MC-C03 | Legacy migration | With rollback copies saved, start once from a representative schema-2 pre-currency config containing distinctive prices and timing values. | The file migrates and saves as config schema 3, defaults `paymentCurrency` to `RUB`, and preserves numeric prices and unrelated valid settings. No wallet, profile, authorization, or ledger entry changes during migration. | OPEN | TO RECORD |
| MC-C04 | Invalid current-schema currency | Put an invalid nonempty `paymentCurrency` in a schema-3 config and exercise startup/reload plus a purchase attempt. | The value is rejected/fails closed with an actionable error. No fallback debit, authorization grant, config revision advance, or mutation of any currency stack occurs. | OPEN | TO RECORD |

## Wallet Source Matrix

Repeat every row with RUB, USD, and EUR. Use exact-funds and insufficient-funds
boundaries as well as a normal success case.

| ID | Scenario | Action | Required result | Status | Evidence |
| --- | --- | --- | --- | --- | --- |
| MC-W01 | `CarriedRoubles` | Put the selected currency in carried inventory/secure container and different currencies in the stash. Buy each service needed to cover normal, exact-funds, and insufficient-funds cases. | Only carried stacks of the selected currency are counted/debited. Success grants one authorization and exact funds leave zero. Insufficient funds change no stack and grant nothing. | OPEN | TO RECORD |
| MC-W02 | `StashRoubles` | Put the selected currency in root and nested stash containers and different currencies in carried inventory. Repeat the normal, exact, and insufficient boundaries. | Only stash stacks of the selected currency are counted/debited, including eligible nested stacks. One successful debit grants one authorization; a denial mutates no money or ledger state. | OPEN | TO RECORD |
| MC-W03 | `PreferCarriedThenStash` | Give the carried wallet enough for one run, then make carried insufficient while stash is sufficient, then make the combined available source insufficient. | Carried pays first when sufficient; otherwise the intended stash fallback pays once. No successful request charges both wallets, and an insufficient request changes neither wallet nor authorization count. | OPEN | TO RECORD |
| MC-W04 | `PreferStashThenCarried` | Give the stash enough for one run, then make stash insufficient while carried is sufficient, then make the combined available source insufficient. | Stash pays first when sufficient; otherwise the intended carried fallback pays once. No successful request charges both wallets, and an insufficient request changes neither wallet nor authorization count. | OPEN | TO RECORD |
| MC-W05 | Currency isolation and stack boundaries | For each payment mode, surround the selected-currency stacks with high-value stacks of both non-selected currencies. Split the selected amount across multiple eligible stacks and container depths. | Non-selected currencies never satisfy or fund the purchase. Eligible selected stacks total correctly, deductions equal the authoritative price exactly, depleted stacks are handled cleanly, and unrelated items/currencies remain unchanged. | OPEN | TO RECORD |

## Purchase Persistence Matrix

| ID | Scenario | Action | Required result | Status | Evidence |
| --- | --- | --- | --- | --- | --- |
| MC-P01 | Main-menu purchase hydration | Buy from the main-menu store in each currency, close the client, restart, and enter a raid. | One debit grants one persistent authorization, and the same authorization hydrates after restart without another debit or a stale currency/price. | OPEN | TO RECORD |
| MC-P02 | Lost response / same-ID retry | Interrupt a purchase response after the server receives the request, restore connectivity, and retry the identical purchase request ID. Repeat once per currency. | The durable journal converges on the original result. The selected wallet is debited at most once and exactly one authorization is granted. | OPEN | TO RECORD |
| MC-P03 | Prepared purchase across currency change | Pause a purchase after it is durably prepared, change the dashboard currency and price, then retry the same request ID. | Recovery remains pinned to the prepared request's original currency and price and cannot charge twice. A new request uses the current currency/price only. | OPEN | TO RECORD |
| MC-P04 | Accepted replay across currency change | Complete a purchase, change the dashboard currency and price, then replay the accepted request ID and restart the client. | The original accepted ledger result is returned without a second debit/grant. Current configuration and current-currency balances/prices are not overwritten by the historical result. | OPEN | TO RECORD |

## Fika Matrix

Except for the explicit compatibility row, install the exact same candidate on
the human host, every client, and any dedicated headless host.

| ID | Scenario | Action | Required result | Status | Evidence |
| --- | --- | --- | --- | --- | --- |
| MC-F01 | Host-selected settings sync | In human-host and dedicated-headless topologies, save each currency and distinctive prices on the server/host, join with clients, and inspect all payment surfaces. | Every current client shows the host-selected currency and prices at the same revision. No client-local fallback currency leaks into a connected session. | OPEN | TO RECORD |
| MC-F02 | Correct requester debit | With two profiles holding different balances, have the host and each client separately buy through carried, stash, and hybrid modes in all three currencies. | Only the authenticated requester's selected-currency wallet is debited once and only that profile receives one authorization. No host, observer, or other-profile balance changes. | OPEN | TO RECORD |
| MC-F03 | Older-client fail-closed compatibility | In an isolated compatibility run, attempt a USD/EUR server purchase with an older client that lacks the current currency contract. Do not use this mixed set for any other acceptance row. | The request fails closed before debit/grant with an actionable compatibility or contract error. It cannot be reinterpreted as RUB, use a client-local price, or mutate the ledger. | OPEN | TO RECORD |
| MC-F04 | Headless duplicate resistance | In a dedicated-headless raid, retry/duplicate one purchase and one paid support request while observing client and server logs. | Headless authority creates no extra debit or authorization. The purchase request and support request each retain one canonical identity and execute/grant at most once. | OPEN | TO RECORD |
| MC-F05 | Disconnect override cleanup | Connect to a host using a currency different from the client's fallback, disconnect/end the raid, then inspect the supported offline/server fallback and reconnect. | Disconnect clears the stale host override. Offline/fallback state returns to its configured source, and reconnect applies the current host value once without carrying the prior session's revision. | OPEN | TO RECORD |
| MC-F06 | Live in-raid price/currency update | In human-host and dedicated-headless topologies, keep one prepared purchase paused, save a distinctive new price during the active raid, and make a fresh request from the host and each client. Repeat once while changing both currency and price, then resume/retry the original request ID. | Fresh requests use the latest server revision, price, and currency without a raid restart. The prepared request remains bound to its original quote and currency. Only the authenticated requester is debited, no peer-local stale price is accepted, and retries/replays cannot debit or grant twice. | OPEN | TO RECORD |

## Exit Record

Multi-currency validation is live-complete only when every applicable row has
attached evidence and:

- RUB, USD, and EUR remain server-authoritative and display consistently;
- only the selected currency and intended wallet source can fund a request;
- success debits once and grants once, while denial mutates nothing;
- prepared and accepted request IDs retain their original price/currency across
  retry, restart, and later configuration changes;
- profile isolation, host synchronization, headless behavior, and disconnect
  cleanup pass with one matched candidate.

Do not mark this matrix complete from solo success or automated regression
tests alone.
