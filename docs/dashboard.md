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

## Data Access

The dashboard reads and writes TSC server config. Stash payment routes can debit stash roubles through server-side SPT profile APIs. The server calculates prices from authoritative config and validates support type before granting authorization.

## Safe Defaults

- Dashboard enabled for localhost.
- Remote dashboard disabled.
- No telemetry.
- No external network calls.
- No automatic downloads.

Trusted LAN/VPN only. Do not port-forward this dashboard.

## Disabling

Set the dashboard enabled option to false in the config or dashboard UI, then restart/reload the server config.
