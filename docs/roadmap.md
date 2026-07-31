# Roadmap

## Completed For The v1.1.0 Candidate

- Authenticated pre-raid authorization store with confirmation and a dashboard shortcut.
- Persistent authorization hydration and server-backed consume/commit/refund lifecycle.
- Transactional Fika request acceptance and duplicate protection.
- Configurable UAV radar presentation: use the default held physical phone or keep the scanner square as a persistent HUD in any screen corner.
- Longer recon contracts: 8-minute standard UAV and 90-second Focused Sweep with distinct scan cadences.
- Requester-owned Fika recon links, including human-host and dedicated-headless isolation.
- Shared authority timing so the requester phone link and visible loiter aircraft expire together.
- RUB, USD, and EUR payment support.
- Standard-Extraction dispatch, wait, countdown, and speed timing plus
  extraction-free Cargo dispatch, wait, and speed timing.
- Proprietary-free regression and CI verification.

## Release Acceptance Remaining

- Complete the human-host and Fika-client matrices for authorization hydration, purchase, accept/reject, duplicate delivery, commit, and refund.
- Complete requester isolation and teardown coverage for UAV Phone and HUD modes.
- Complete standard-Extraction timing checks and Cargo
  timing/extraction-isolation checks in solo and Fika.
- Complete dedicated-headless testing with A-10 clearly treated as experimental.
- Produce and inspect a clean v1.1.0 package from the explicit two-root allowlist.

## Potential Next Features

- Mortar/artillery support.
- Remote third-person phone visual sync for other Fika players.
- Purpose-built Unity phone animations and improved deploy poses.
- Phone inventory-inspect presentation polish.
- New aircraft, helicopter, recon, or extraction service types.
- Broader support-balancing redesign.

These are roadmap items, not public beta bugs. A new service must define its solo, human-host, client, and dedicated-headless authority behavior before implementation.
