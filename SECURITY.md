# Security

The TSC Dashboard is intended for local host configuration, not public internet exposure.

## Dashboard Defaults

- Localhost-only by default.
- Remote access disabled by default.
- Admin token required for remote writes when remote access is enabled.
- Public health output is minimal.
- Admin diagnostics are behind the admin route.

Trusted LAN/VPN only. Do not port-forward this dashboard.

## Admin Token

The server creates `config/tsc-admin-token.txt` if no token exists. The new environment variable is `TSC_ADMIN_TOKEN`; the legacy environment variable is accepted only for compatibility.

Dashboard write routes require the token when remote access requires authentication. POST config save/reset/reload routes must not be exposed without this protection.

## Payment Safety

- Stash payment is server-authoritative.
- The server calculates price from current config.
- Client-sent prices are not trusted.
- The selected support type is validated server-side.
- Authorization is granted only after payment succeeds.
- Failed payment must not grant authorization.

## Reporting Issues

Report security issues privately to the maintainer before posting public exploit details.
