# Tylevo's Tactical Services Control v1.3.0

SPT 4.1.4 public-beta tester with optional Tylevo Seasonal Modifiers support.

Requires WTT CommonLib 3.0.6 and the SPT 4.1 rebuild of UnityToolkit 2.0.1;
optional multiplayer uses Fika client 2.4.2 with its compatible server.

TSC remains a complete standalone tactical-support mod. Its optional Danger
Close API v3 adds host-authored A-10 warnings, reliable Fika delivery, and a
dedicated fourth special slot for the TerraGroup Uplink. Slot 4 enables the
advance forecast; the final inbound warning is universal. The device is never
granted or locked, and normal manual TSC services still work when it is carried
outside that slot.

Seasonal Modifiers owns the main-menu presentation when installed, so TSC hides
its redundant pre-raid row without changing any in-raid service. Standalone TSC
installs keep the pre-raid storefront.

This is a tester candidate. The exact-version build and 168/168 regression tests
pass; final package, server bootstrap, live raid, migration UI, ballistic-impact,
and Fika acceptance are pending. See
`docs/port/SPT-4.1.4-PORT-LOG.md` for the current evidence.
