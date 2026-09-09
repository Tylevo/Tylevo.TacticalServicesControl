# Tylevo's Tactical Services Control v1.3.13 Public Beta

**Draft description for SPT 4.1.5. This candidate has not been published.**

Give each support service its own price and payment currency. Charge roubles
for UAV Recon, GP coins for an A-10 strike, or physical Bitcoin for helicopter
extraction. Choose RUB, USD, EUR, GP, or BTC in the dashboard or SIC config.

GP coins and Bitcoin are taken from the PMC stash, including eligible items
inside stash containers. Prices are whole item counts. Existing configurations
inherit the global currency until you select an override, and changing currency
does not convert the amount automatically.

Phone and Pilot Services screens show the matching prices and balances.
The 1.3.12 phone movement, sprint zoom, and configurable cargo grid remain included.
Cargo's separate native item-handling fee remains in RUB.

Install UnityToolkit 2.0.2 and WTT CommonLib 3.0.6 separately. Update all TSC
components and Fika participants together. If you use the optional Pilot
Questline, its manifest must match the main version; quest content is unchanged.

See the [candidate notes](release-notes-v1.3.13.md) for setup and validation limits.
