# TSC Dashboard

The TSC Dashboard is a local server configuration UI.

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
packages ship only the canonical template.

## Data Access

The dashboard reads and writes TSC server config. Payment currency is server-authoritative and can be set to roubles (RUB), US dollars (USD), or euros (EUR). Stash payment routes debit the selected currency through server-side SPT profile APIs. The server calculates prices from authoritative config and validates support type before granting authorization.

Changing currency does not convert saved service prices. Review all prices in the same dashboard session before saving a different currency.

The dashboard constrains normal inputs to supported UI ranges. Extraction
settings are also validated server-side: dispatch delay must be 0-120 seconds,
wait time 5-300 seconds, extraction countdown 1-60 seconds, speed multiplier
0.5-3, and the wait window must be at least the countdown plus one second.
Double Pass delay is 6-45 seconds. Persistent-use timeout must cover the
maximum dispatch delay plus settlement margin.

`purchasePersistence.mode` and `consumeOn` are fixed protocol values in the
current release. `refundFailedDispatch` remains an advanced configuration
value in the JSON template rather than a dashboard control.

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
