# Tylevo's Tactical Services Control v1.2.0 Public Beta

This SPT 4.1.2 tester release adds optional integration with Tylevo Seasonal
Modifiers. TSC still works independently and does not require Seasonal
Modifiers.

When the Seasonal “Danger Close” global is active, TSC periodically accepts
host-authoritative ambient A-10 tasks. Manual A-10 Strafe and Double Pass calls
are locked for the modifier's duration, while UAV Recon, Focused Sweep, UH-60
Extraction, and UH-60 Cargo Transfer continue to work normally.

Danger Close API v2 reports the eventual authority/executor acceptance of each queued
ambient request. Seasonal manual-event controls therefore consume their raid
cap and display success only when TSC actually accepts the pass.
Each ambient attempt must use a request ID that is globally unique for that
raid; replayed accepted IDs are intentionally deduplicated and do not launch a
second aircraft pass.

Ambient tasks carry an explicit multiplayer origin, reject requests forged by
remote peers, use neutral projectile ownership, and permit only one active A-10
pass. A source-leased integration API prevents one optional mod from releasing
another mod's lock.

The proprietary-free suite passes 110 regression tests. Live solo,
listen-host, client, and dedicated-headless raid acceptance remains required
before promoting this tester build to a stable release.
