# Multi-Currency Payment Validation

Use a low temporary service price and back up the test profile before running this matrix.

## Configuration

- Select `RUB`, `USD`, and `EUR` in the dashboard and save each value.
- Confirm the dashboard, main-menu store, confirmation screen, and phone show the matching symbol/code.
- Confirm changing currency does not change the numeric service prices.
- Confirm a legacy schema-2 config migrates to `RUB`.
- Confirm an invalid schema-3 currency is rejected and cannot debit a wallet.

## Wallet Sources

For each currency, test `CarriedRoubles`, `StashRoubles`, `PreferCarriedThenStash`, and `PreferStashThenCarried`.

- Only the selected EFT currency is counted and debited.
- Exact funds succeed and leave zero.
- Insufficient funds fail without changing any currency stack or authorization.
- Hybrid fallback charges the intended wallet once.
- Money inside nested stash containers and the secure container is counted in the applicable wallet.

## Purchase Persistence

- Buy from the main-menu store, restart the client, and confirm the authorization remains.
- Lose or interrupt a purchase response, then retry the same request ID and confirm it is not charged twice.
- Prepare a purchase, change the dashboard currency, then retry and confirm recovery uses the original price and currency.
- Replay an already accepted request after a currency change and confirm its ledger is restored without replacing the current currency balance or prices.

## Fika

Install the same TSC build on the host, every client, and any headless host.

- Human host and clients show the host-selected currency and prices.
- Carried and stash purchases debit once and grant one authorization.
- Joining with an older client fails closed for USD/EUR server purchases.
- Headless sessions do not create duplicate charges or authorizations.
- Disconnecting clears the host override and restores the local/server fallback.
