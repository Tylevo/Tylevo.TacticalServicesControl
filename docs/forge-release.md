# Forge Release Draft

## Title

Tylevo's Tactical Services Control

## Version

0.9.0 Public Beta

## Short Description

TerraGroup-style Fire Support rework with phone-based support authorization, Fika support, stash payments, UAV recon, A-10, UH-60 extraction, Priority Exfil, Focused Sweep, and dashboard configuration.

## Suggested Category

Gameplay or Other, depending on Forge options.

## SPT Version

4.0.13.

## Dependencies

- UnityToolkit v2.0.1.
- Project Fika for multiplayer/Fika features.
- WTT Client Common Lib and WTT Server Common Lib, matching the tested SPT setup.

## License

Use a license option compatible with Creative Commons BY-NC 4.0 and upstream permissions. Include `THIRD_PARTY_NOTICES.md` and `PERMISSIONS.md`.

## Description

Tylevo's Tactical Services Control adds the TerraGroup TSC Uplink, a phone-based authorization flow for Fire Support services, stash and carried rouble payment support, A-10 Strafe, A-10 Double Pass, UH-60 Extraction, Priority Exfil, UAV Recon, Focused Sweep, Fika host/client sync, and a local TSC Dashboard for host configuration.

The dashboard is localhost-only by default. Remote use should be trusted LAN/VPN only and should never be port-forwarded. The mod does not include telemetry or automatic external calls.

Fika users must install the same version on the host, any headless host, and every client. Host config is authoritative while connected.

Optional Ko-fi text:

If you enjoy the project and want to support future work, you can leave a voluntary tip on Ko-fi. This is optional and does not unlock features, early access, or support priority.

https://ko-fi.com/tylevo

Do not include the Ko-fi link in a public upload until upstream BY-NC permission is confirmed.

## Forge Changelog

See `CHANGELOG.md`.

## Known Issues

See `docs/known-issues.md`.

## Upload Blockers

- Upstream derivative permission from SamSWAT/Arys must be confirmed.
- Asset/font/model redistribution rights must be confirmed or replaced.
- VirusTotal scans must be generated for the final ZIP and release DLLs after upload.

## VirusTotal Placeholders

- Final release ZIP: pending.
- Core DLL: pending.
- Fika DLL: pending.
- Server DLL: pending.
