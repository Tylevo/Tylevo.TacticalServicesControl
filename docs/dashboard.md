# TSC dashboard and SIC

The TSC dashboard keeps its TerraGroup styling. Starting with v1.3.9, you can
open it from SPT's SIC through the launcher.

## Open from the launcher

Start the SPT server and open SIC from the launcher. On the SIC home page,
choose **Tactical Services Control** under **Mod pages**. This opens the same
themed dashboard, including its service cards, pricing controls, and diagnostics.
The dashboard sidebar has **SPT SIC** and **Config editor** links to return to
SPT's pages. The in-game store's **Dashboard** button still opens this page.

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

## Routes

- Public health: `/tsc/health`
- Dashboard UI: `/tsc/admin`
- Admin health/diagnostics: `/tsc/admin/health`
- Legacy `/raidops/firesupport` routes are accepted only for compatibility.

## Files

- Config: `config/tsc-config.json`
- Token: `config/tsc-admin-token.txt`
- Ledger: server-side TSC storage.

Existing installs that still have the legacy `config/raidops-firesupport.json`
filename are migrated automatically when `tsc-config.json` is absent. New
packages do not ship either mutable config file: the server creates canonical
schema-3 defaults on a clean install and preserves an existing file during an
upgrade.

## Data Access

The dashboard reads and writes TSC server config. Payment currency is server-authoritative and can be set to roubles (RUB), US dollars (USD), or euros (EUR). Stash payment routes debit the selected currency through server-side SPT profile APIs. The server calculates prices from authoritative config and validates support type before granting authorization.

Changing currency does not convert saved service prices. Review all prices in the same dashboard session before saving a different currency.

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

The main-menu pre-raid store includes a **Dashboard** button. Its address is
derived from the active SPT backend connection, so a Fika client does not open
an unrelated local server.

## Safe Defaults

- Dashboard enabled for localhost.
- Remote dashboard disabled.
- No telemetry.
- No external network calls.
- No automatic downloads.

Trusted LAN/VPN only. Do not port-forward this dashboard.

## Disabling

Set the dashboard enabled option to false in the config or dashboard UI, then restart/reload the server config.
