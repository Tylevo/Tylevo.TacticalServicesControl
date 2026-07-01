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

Creative Commons BY-NC 4.0 for TSC-specific material unless a third-party notice or upstream permission requires a different or more restrictive arrangement.

Include:

- `LICENSE`
- `THIRD_PARTY_NOTICES.md`
- `PERMISSIONS.md`
- source repository link
- VirusTotal links for the final ZIP and release DLLs

## Description

Tylevo's Tactical Services Control adds the TerraGroup TSC Uplink, a phone-based authorization flow for Fire Support services, stash and carried rouble payment support, A-10 Strafe, A-10 Double Pass, UH-60 Extraction, Priority Exfil, UAV Recon, Focused Sweep, Fika host/client sync, and a local TSC Dashboard for host configuration.

This project is a derivative rework of SamSWAT's original Fire Support and SamSWAT's Fire Support - Arys Reloaded by Arys, released with upstream permission and full attribution. Phone/use-device material includes MIT-licensed Manimal Hacker Mod material by danauraborealis. Full notices are included in `THIRD_PARTY_NOTICES.md`.

The dashboard is localhost-only by default. Remote use should be trusted LAN/VPN only and should never be port-forwarded. The mod does not include telemetry or automatic external calls.

Fika users must install the same version on the host, any headless host, and every client. Host config is authoritative while connected.

## Optional Ko-fi Text

If you enjoy the project and want to support future work, you can leave a voluntary tip on Ko-fi. This is optional and does not unlock features, early access, downloads, updates, or support priority.

https://ko-fi.com/tylevo

## Forge Changelog

See `CHANGELOG.md`.

## Known Issues

See `docs/known-issues.md`.

## Release Checklist

- Upstream derivative permission: recorded in `PERMISSIONS.md`.
- Manimal/MIT notice: included in `THIRD_PARTY_NOTICES.md`.
- Source repository: pending final public link.
- VirusTotal final ZIP: pending.
- VirusTotal Core DLL: pending.
- VirusTotal Fika DLL: pending.
- VirusTotal Server DLL: pending.
- Final two-person Fika smoke test: pending for each public build.
