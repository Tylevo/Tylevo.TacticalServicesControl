# Tylevo's Tactical Services Control v1.3.13 Public Beta

**Draft description for SPT 4.1.5. This candidate has not been published.**

Choose how support fits your raids with lower default prices, five gameplay
presets, and separate payment currencies for each service.

Fresh installs use **Balanced** prices: Focused Sweep **25,000 RUB**, UAV
**50,000 RUB**, A-10 Strafe **150,000 RUB**, Double Pass **250,000 RUB**,
Extraction **125,000 RUB**, and Cargo Transfer **75,000 RUB**. Existing installs
keep their saved settings until you choose a preset or edit them.
Fresh installs and Reset Defaults use **stash roubles**; existing wallet
choices are preserved. Classic 1.3.12 restores the original carried wallet.

The dashboard includes **Balanced, Casual, Hardcore, Barter, and Classic 1.3.12**.
Preview each change, apply the preset to your draft, then Save Config. Choose
all gameplay settings or apply only prices and payment, recon, UH-60 services,
or fire support.

Save your own named presets as JSON files on the SPT host. The library survives
browser changes, and you can share the current draft as a JSON file or a complete
share code. Imported settings receive the same preview. No database or online
account is needed. Presets exclude dashboard access settings, tokens, and player
records.

Give each service its own **RUB, USD, EUR, GP, or BTC** price in SIC or the
dashboard. GP coins and Bitcoin come from the PMC stash, including eligible
items inside stash containers. Prices are whole units; changing currency does
not convert the amount automatically. Phone and Pilot Services screens show
the matching prices and balances.

The Uplink remains **50,000 RUB**, the optional quest repeater remains
**20,000 RUB**, and cargo's separate native item-handling fee remains in RUB.
The 1.3.12 phone movement, sprint zoom, and configurable cargo grid are included.

The dashboard has a dedicated gameplay presets page, aligned selection and
sharing panels, wider dropdowns, and a darker in-game color theme with green
switches. The phone's payment swipe flows straight into stowing, with a larger
upward arrow that follows the hand animation.

Install UnityToolkit 2.0.2 and WTT CommonLib 3.0.6 separately. Update all TSC
components and Fika participants together. **Keep the existing Pilot Questline
1.3.12 add-on for SPT 4.1.5.** Main TSC 1.3.13 accepts it, so there is no new
add-on download. Quests, objectives, rewards, and progression are unchanged.

Native integration checks cover presets and add-on startup. In-game acceptance
of the latest changes remains pending. Current Fika
multiplayer remains untested. See the [candidate notes](release-notes-v1.3.13.md)
for setup and validation limits.
