# Tylevo's Tactical Services Control v1.3.8 Public Beta

Targets SPT 4.1.4 / EFT 0.16.9.5.40743 with the existing dependencies.

The physical **TerraGroup TSC Uplink** is now sold by **UH-60 Pilot** in
Trading instead of Jaeger. The price remains **₽50,000**, available at
loyalty level 1 with a limit of five per restock. Pilot is unlocked by default
for now, leaving his existing trader identity available for a future questline.
His Trading and cargo-mail portrait uses the image supplied for this update.

Existing profiles with a locked Pilot entry are migrated at server startup.
Only Pilot's unlocked flag changes; loyalty, standing, sales, cargo dialogue,
and other traders are preserved. Profiles without a Pilot entry use SPT's
normal trader initialization. The migration requires the owned TSC identity
and an unlocked-by-default base, so a future quest-gated base will not be
overridden by this migration. Save failures restore the previous flag.

Pilot is registered before WTT imports the shop offer. The existing
`db/CustomAssortSchemes/jaeger_uav_uplink.json` filename is deliberately retained
with its destination changed to Pilot, so an overlay upgrade replaces the old
Jaeger listing instead of leaving a second file behind. A newly cloned Pilot
receives an empty assortment; repeated identity initialization preserves an
already populated shop. The BTR Driver and native cargo fallback stay separate.

The archive is `Tylevo.TacticalServicesControl-v1.3.8-SPT4.1.4-TESTER.zip`.
With EFT and the server closed, back up profiles and install the matching
four DLLs, updated assortment JSON, and new server asset
`assets/traders/uh60-pilot.png`. Restart the server for registration and profile
migration, then launch EFT. No quest completion or manual profile edit is needed.

The pre-raid bottom-bar store, phone UI, authorization purchases, service icons,
and A-10 targeting are unchanged. Verification is recorded in external build,
regression, package, and installation evidence. In-game checks should confirm
Pilot's portrait and unlocked shop, a normal paid Uplink purchase, and absence
of that offer on Jaeger; server read checks do not substitute for that UI check.
