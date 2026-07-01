# Privacy

Tylevo's Tactical Services Control does not include telemetry and does not make external network calls by default.

## Local Data Read

The server mod reads:

- TSC config from `config/tsc-config.json`.
- Admin token from `config/tsc-admin-token.txt`.
- Player profile/stash data through SPT APIs when stash payment or authorization status is requested.
- Authorization ledger data from server-side mod storage.

Legacy filenames may be copied forward for compatibility, but new installs use TSC filenames.

## Local Data Written

The server mod may write:

- TSC config updates.
- Admin token file, if one does not exist.
- Authorization ledger state.
- Player profile changes when stash roubles are debited through server-authoritative payment.

## Dashboard

The dashboard is localhost-only by default and should not be exposed publicly. If remote access is enabled, use a trusted LAN/VPN and require the admin token for writes.

## No Telemetry

No analytics, telemetry, automatic downloads, or hard-coded external service calls are part of the release defaults.
