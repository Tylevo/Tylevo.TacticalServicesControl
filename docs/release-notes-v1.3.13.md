# Tylevo's Tactical Services Control v1.3.13 Public Beta

**Unpublished development candidate - SPT 4.1.5 / EFT 0.16.9.5.40743**

Support has lower default rouble prices, five built-in gameplay presets, and a
shareable preset library stored on your SPT host. Each service can also use
its own currency: RUB, USD, EUR, GP coins, or physical Bitcoin.

## Rebalanced default prices

Fresh installs use the Balanced prices below. Existing configurations keep
their saved prices and gameplay settings during an update. Choose **Balanced**
in the dashboard to review and adopt the new settings, or use **Classic 1.3.12**
to restore the original values.

| Service | Previous default (RUB) | Balanced default (RUB) |
| --- | ---: | ---: |
| Focused Sweep | 90,000 | 25,000 |
| UAV Recon | 125,000 | 50,000 |
| A-10 Strafe | 250,000 | 150,000 |
| A-10 Double Pass | 450,000 | 250,000 |
| UH-60 Extraction | 300,000 | 125,000 |
| UH-60 Cargo Transfer | 450,000 | 75,000 |

The Uplink still costs **50,000 RUB**, and the optional questline's replacement
repeater costs **20,000 RUB**. Cargo's separate native item-handling fee remains
in RUB and is calculated by EFT for the items you send.

## Gameplay presets

Open **SIC > Mod pages > Tactical Services Control > Gameplay presets**.

- **Balanced:** the new prices, with standard service timing and limits.
- **Casual:** cheaper support, stronger recon, a 60-second cooldown, and up to
  five stored authorizations per service.
- **Hardcore:** higher prices, reduced recon, a 600-second cooldown, and one
  stored authorization per service.
- **Barter:** Focused Sweep and A-10 Strafe each cost 1 GP coin; Double Pass and
  Cargo Transfer each cost 2 GP coins; Extraction costs 1 Bitcoin; UAV costs
  10,000 RUB. This preset uses the stash wallet.
- **Classic 1.3.12:** the original prices, timing, and gameplay limits.

Choose a preset and an **Apply to** scope, then select **Preview preset**.
Review the current and proposed values, choose **Apply to draft**, and use
**Save Config** to activate and persist your changes. You can apply all gameplay
settings, prices and payment, recon, UH-60 services, or fire support.

Category scopes keep your global currency and wallet. An inherited service
currency is made explicit using the preset's default currency, so a USD price
keeps its meaning when applied to a server using RUB or GP globally. Prices and
payment applies the global currency, wallet, service prices, and overrides;
payment mode, timings, and service availability belong to the wider scopes.

Use **Save current as preset** to store a named copy of your current draft as a
JSON file in the mod's `config/presets/` folder on the SPT host. The library is
available from other browsers and computers connected to that host, subject to
the dashboard's administrator permissions. Saving a preset does not activate
its settings; **Save Config** remains the action that updates the running mod.

**Export current JSON** and **Get share code** share the current draft. Import a
file or paste a JSON document or `TSC1.` code to preview it before applying.
Codes contain the settings themselves. No database or online account is needed.
Presets cover 43 editable gameplay fields; dashboard administration, tokens,
profile records, revisions, and dormant compatibility fields are excluded.
See the [dashboard guide](dashboard.md) for the full workflow.

## Per-service payment currencies

Each **Service Pricing** card pairs its currency selector with its price. The
same settings appear under `serviceCurrencies` and `prices` in SIC's native
config editor.

- **Use global / Inherit** keeps the global currency. Existing services inherit
  it until an override is selected.
- Choose **RUB, USD, EUR, GP, or BTC** for a service override.
- GP and BTC always come from the authenticated PMC stash, including eligible
  items inside stash containers. Carried coins are not used.
- Prices count units of the selected currency. Set GP/BTC prices to the desired
  whole item count before saving; changing currency does not convert amounts.
  There is no found-in-raid requirement.
- Cash retains the configured wallet. Pilot's pre-raid Services purchases use
  the stash.

The phone, Pilot's Services prices, balances, confirmations, and purchase
history use the matching currency. Purchase recovery retains original price
and currency terms. Auto-purchase uses the credit from that purchase's source,
and late responses cannot replace another profile's balance or authorizations.

## Updating a test installation

Close the game and SPT server and back up profiles and TSC configuration/storage,
including any saved preset library. Install all four TSC DLLs and dashboard
assets from the same candidate. Dependencies remain **UnityToolkit 2.0.2** and
**WTT CommonLib 3.0.6**, installed separately.

The optional Pilot Questline still requires the matching **1.3.13** add-on.
Its quests, objectives, rewards, and progression have not changed. All Fika
participants must update together; mixed payment protocols reject manual
service requests rather than interpreting a coin price as cash.

The phone movement, sprint zoom, and configurable cargo grid from 1.3.12 are
included.

## Validation

This candidate has not been published. Automated checks cover preset transport,
scope selection, currency inheritance, privacy filtering, and draft preview
behavior, alongside the payment and config regression checks. New preset
library native integration and gameplay acceptance remain pending. Automated
checks do not establish in-game or multiplayer compatibility. The existing
[known issues](known-issues.md) still apply.
