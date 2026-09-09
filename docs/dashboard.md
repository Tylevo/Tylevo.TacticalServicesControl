# TSC dashboard and SIC

Open the themed TerraGroup TSC dashboard from SPT's SIC through the launcher.
The preset library, new default prices, and GP/BTC payment options described
here belong to the unpublished **1.3.13 candidate**; 1.3.12 is the current release.

## Open from the launcher

Start the SPT server and open SIC from the launcher. On the SIC home page,
choose **Tactical Services Control** under **Mod pages**. This opens the same
themed dashboard, including its service cards, pricing controls, and diagnostics.
The dashboard sidebar has **SPT SIC** and **Config editor** links to return to
SPT's pages. The Pilot's Services tab is for buying support authorizations.

SIC's **Config Editor > Mods > Tactical Services Control** entry is also
available. That editor uses SPT's standard appearance. It exposes the routine
gameplay settings; dashboard administration and player records are kept out of
the editable view. Personal phone controls and presentation settings stay in F12.

## Apply, save, and reload

The themed dashboard's **Save Config** updates the active settings and saves
them to `config/tsc-config.json`. **Reload Config** fetches the current server
settings. **Reload From Disk** loads and applies the saved file.

SIC has separate actions: **Apply to Runtime** changes the running server,
while **Save to Disk** persists the edited values for the next start. Use Apply
and then Save when you want both. **Load Disk** reads the file into the editor
without applying it. After a disk save, refresh the editor before making
another change so it has the latest revision. Applying the exact draft you
just saved is also supported, unless another edit has happened since.

Both editors reject stale edits. If a save reports that the configuration has
changed, keep a note of your intended changes, reload the latest settings,
and edit again. The dashboard keeps your unsaved values when a save fails;
reloading asks before discarding them. Saves replace the file atomically,
and a failed write or invalid disk reload does not change the active settings.

## Gameplay presets (candidate 1.3.13)

Open **Gameplay presets** in the themed dashboard. **Balanced, Casual,
Hardcore, Barter, and Classic 1.3.12** are built in. The preset controls use
43 routine gameplay fields that are also editable through SIC's native config
editor: payment, service prices and currencies, availability, recon, helicopter
timing and cargo size, cooldown, and authorization limits. The library and
sharing controls are in the themed dashboard.

Fresh installs use Balanced. Existing installs retain their saved configuration
when updated; choose Balanced to preview the new prices. All amounts in this
table are RUB:

| Service | Balanced | Casual | Hardcore | Classic 1.3.12 |
| --- | ---: | ---: | ---: | ---: |
| Focused Sweep | 25,000 | 15,000 | 100,000 | 90,000 |
| UAV Recon | 50,000 | 25,000 | 200,000 | 125,000 |
| A-10 Strafe | 150,000 | 75,000 | 500,000 | 250,000 |
| A-10 Double Pass | 250,000 | 125,000 | 800,000 | 450,000 |
| UH-60 Extraction | 125,000 | 75,000 | 400,000 | 300,000 |
| UH-60 Cargo Transfer | 75,000 | 40,000 | 250,000 | 450,000 |

Balanced keeps standard service timing and limits. Casual also strengthens
recon, shortens the cooldown to 60 seconds, and permits five stored credits per
service. Hardcore reduces recon, uses a 600-second cooldown, and permits one
stored credit per service. Classic restores the original gameplay defaults.
The preview shows the timing changes as well as prices.

Barter uses the stash wallet: Focused Sweep and A-10 Strafe cost **1 GP** each;
Double Pass and Cargo Transfer cost **2 GP** each; Extraction costs **1 BTC**;
UAV costs **10,000 RUB**. The Uplink stays at **50,000 RUB**, the optional
questline repeater stays at **20,000 RUB**, and EFT still calculates a separate
RUB handling fee for sent cargo.

### Preview and apply

1. Choose a built-in or saved preset and an **Apply to** scope.
2. Select **Preview preset** and review each current/proposed value.
3. Choose **Apply to draft**. You can adjust the resulting draft further.
4. Select **Save Config** to update the running server and saved config.

**All gameplay settings** applies every portable field in the preset.
**Prices and payment** applies global currency and wallet plus service prices
and currency overrides; it leaves payment mode, timing, and availability alone.
**Recon services**, **UH-60 services**, and **Fire support** apply that category's
prices, overrides, availability, and timing. Global currency and wallet stay
as configured for these category scopes.

A category's **Inherit** currency is resolved to the preset's global currency
and written as an explicit service override. For example, applying a USD recon
preset to a server with a RUB global setting keeps its recon prices in USD.
A partial imported preset without a global currency uses your current global
currency for inherited fields. Review currency changes alongside each price.

### Save and share your own preset

Under **Save or share your settings**, enter a name and optional description.
**Save current as preset** stores a copy of the current dashboard draft in the
SPT host's preset library. It does not apply the draft to the running server.
The files live in the mod's `config/presets/` directory and remain available
when you change browsers or use another computer connected to the same host.
Library access follows the dashboard's administrator permissions.

The host assigns each saved preset a file ID; your name and description are
editable labels. Saving the same name asks before replacing that saved copy.
**Remove saved preset** deletes the selected library copy. Built-in presets
remain available.

**Export current JSON** downloads the current draft as one preset file.
**Get share code** copies a complete `TSC1.` code, or puts it in the text box
for manual copying when the clipboard is unavailable. Both export the current
draft, including unsaved edits. To share a selected library preset, preview
and apply it to your draft first.

Use **Open JSON file** or paste JSON/a share code and choose **Preview pasted
preset** to import. Review, apply to draft, then Save Config when ready. You
can also save the resulting draft as a named preset on your host. A code
contains the settings themselves; it is not a link to an online service.
No database or online account is needed. JSON imports are limited to 32 KB;
share-code input is limited to 48 KB.

Exports contain gameplay settings only. Dashboard access controls, tokens,
player balances, purchase records, progression, revisions, and dormant
compatibility fields are excluded. Invalid values or unknown fields are
rejected before the draft changes.

## UH-60 cargo grid size

In SPT 4.1.5 SIC, open **Config Editor > Mods > Tactical Services Control >
priorityExfil** and edit **GridWidth** and **GridHeight**. In the themed
dashboard, open **UH-60 Services** and edit **Cargo Grid Columns** and **Cargo
Grid Rows**. Both editors update the same `priorityExfil.gridWidth` and
`priorityExfil.gridHeight` settings.

Columns accept whole numbers from **0 to 10**; rows accept **0 to 30**.
**0** is the default and preserves EFT's native size for that dimension. Set
both to 0 to use the full native grid, or override either dimension separately.
For example, 5 columns and 5 rows provide 25 inventory cells; 10 columns and
10 rows provide 100 cells. Larger items occupy multiple cells, so this is
not a fixed item-count limit.

Use the Apply/Save actions above to make the change active and persistent.
Active settings take effect the next time the cargo transfer screen opens;
an already open screen keeps its current size. Resizing preserves previously
submitted cargo. These controls belong to **UH-60 Cargo Transfer**; normal
player extraction has no cargo grid.

## Routes

- Public health: `/tsc/health`
- Dashboard UI: `/tsc/admin`
- Admin health/diagnostics: `/tsc/admin/health`
- Built-in preset catalog: `/tsc/presets`
- Saved preset library: `/tsc/presets/saved` (dashboard administrator access)
- Legacy `/raidops/firesupport` routes are accepted only for compatibility.

## Files

- Config: `config/tsc-config.json`
- Token: `config/tsc-admin-token.txt`
- Custom preset library: `config/presets/` on the SPT host, within the TSC mod folder.
- Ledger: server-side TSC storage.

Existing installs that still have the legacy `config/raidops-firesupport.json`
filename are migrated automatically when `tsc-config.json` is absent. New
packages do not ship either mutable config file: the server creates canonical
schema-4 defaults on a clean 1.3.13 install and preserves an existing file
during an upgrade. Back up the preset library along with your config and storage.

## Data Access

The dashboard reads and writes TSC server config. Each Service Pricing card
pairs its price with **RUB, USD, EUR, GP, BTC, or Use global / Inherit**. The
server's global currency supplies inherited values. SIC's native editor exposes
the same `prices` and `serviceCurrencies` dictionaries.

GP coins and physical Bitcoin always come from the authenticated PMC stash,
including eligible items inside stash containers. Cash uses the configured
wallet; pre-raid Pilot Services purchases use the stash. The server calculates
prices and debits the selected asset before granting an authorization.

Changing currency does not convert saved prices. Set GP/BTC prices to whole
item counts and review all affected values before saving. The phone and Pilot
Services display the selected service's currency and balance.

The dashboard constrains normal inputs to supported UI ranges. Standard
Extraction is validated server-side: dispatch delay must be 0-120 seconds,
wait time 5-300 seconds, extraction countdown 1-60 seconds, speed multiplier
0.5-3, and the wait window must be at least the countdown plus one second.
Cargo Transfer separately validates dispatch delay, landed wait time, and speed
multiplier; it has no extraction-countdown setting or wait/countdown
relationship.
Double Pass delay is 6-45 seconds. Persistent-use timeout must cover the
maximum dispatch delay plus settlement margin.

`purchasePersistence.mode` and `consumeOn` are fixed protocol values in the
current release. `refundFailedDispatch` remains an advanced configuration
value in the generated JSON config rather than a dashboard control.

Open the dashboard through SIC on the server you want to configure.

## Safe Defaults

- Dashboard enabled for localhost.
- Remote dashboard disabled.
- No telemetry.
- No external network calls.
- No automatic downloads.

Trusted LAN/VPN only. Do not port-forward this dashboard.

## Disabling

Set the dashboard enabled option to false in the config or dashboard UI, then restart/reload the server config.
