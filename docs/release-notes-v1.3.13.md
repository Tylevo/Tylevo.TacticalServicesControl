# Tylevo's Tactical Services Control v1.3.13 Public Beta

**Development candidate · SPT 4.1.5 / EFT 0.16.9.5.40743**

Each service can now have its own payment currency. For example, configure
UAV Recon to cost **10,000 RUB**, A-10 Strafe to cost **1 GP coin**, and
UH-60 Extraction to cost **1 physical Bitcoin**.

## Configuration

Open **SIC > Mod pages > Tactical Services Control > Service Pricing**.
Each service card pairs its currency selector with its price. The same
settings appear under `serviceCurrencies` and `prices` in SIC's config editor.

- **Use global / Inherit** keeps the global payment currency. All existing
  services default to this, preserving their prices and current cash currency.
- Choose **RUB, USD, EUR, GP, or BTC** for a service override.
- GP and BTC purchases always use the authenticated PMC stash, including
  eligible items inside stash containers. They do not consume carried coins.
- Prices count units of the selected currency. Selecting GP or BTC keeps the
  existing number; set it to the desired item count before saving. There is no
  exchange-rate conversion or found-in-raid requirement.
- Cash payments retain the configured payment source. Pilot's Services
  purchases continue to use the stash.

The phone, Pilot's Services prices, balances, confirmations, and purchase
history use the appropriate currency. Retries retain the original purchase
price and currency. Failed dispatch still restores an authorization credit.

Cargo Transfer's service authorization can use a different currency. The
separate native fee for handling the extracted items remains in RUB.

## Updating a test installation

Close the game and SPT server and back up profiles and TSC configuration/storage.
Install all four TSC DLLs and dashboard assets from the same candidate.
Dependencies remain UnityToolkit 2.0.2 and WTT CommonLib 3.0.6, installed separately.

The optional Pilot Questline still requires a matching 1.3.13 add-on manifest.
Its quests, objectives, rewards, and progression have not changed. The existing
add-on compatibility rules remain in place.

All Fika participants must update together. Mixed payment protocols reject
manual service requests to prevent a coin price from being treated as cash.

## Validation

This is an unreleased candidate. Automated checks cover mixed currencies,
stash debits and recovery, config migration, dashboard editing, and snapshot
coverage. Native integration and gameplay results are recorded with the
candidate artifacts; automated checks do not establish in-game or multiplayer
acceptance. The existing [known issues](known-issues.md) still apply.
