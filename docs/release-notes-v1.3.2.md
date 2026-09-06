# Tylevo's Tactical Services Control v1.3.2 Public Beta

> **Historical documentation.** These instructions and results describe this
> earlier version. For the prepared TSC v1.3.11 / SPT 4.1.5 update, use the
> [release notes](release-notes-v1.3.11.md) and [installation guide](dependencies.md).
> TSC v1.3.11 requires standalone UnityToolkit 2.0.2; both new packages are
> unpublished. See the [archive index](archive/README.md) for older availability.

Targets SPT 4.1.4 / EFT 0.16.9.5.40743 with the existing WTT and UnityToolkit
dependencies. This tester smooths the authorization phone's incoming zoom
while retaining the v1.3.1 A-10 targeting corrections.

With authorization-screen zoom enabled, a 0.08-second lead-in is followed by
eased camera FOV and hand framing over 0.75 seconds by default. F12 provides
**Phone zoom in seconds** (0.25-1.5, default 0.75) and **Phone zoom out seconds**
(0.15-0.8, default 0.35). The outgoing FOV transition uses the native camera
path. Closing or rapidly reopening the phone preserves the original raid FOV
as the restore target. Deploy and radar phones retain their existing behavior.

The purchase screens and payment flow remain as currently implemented. The
proposed native purchase interface is a concept preview, not a v1.3.2 feature.

The expected archive is
`Tylevo.TacticalServicesControl-v1.3.2-SPT4.1.4-TESTER.zip`. Install all four TSC
DLLs as a matched version on the host and every Fika client.

The regression suite passes 198/198 tests. Build and package results are
recorded in the candidate's external evidence sidecars; in-game phone
acceptance remains pending. In-game checks should cover a normal purchase-phone open
and close, quick reopen during restoration, zoom disabled, both timing-range
endpoints, and unchanged deploy/radar presentation. Retain the solo and Fika
ballistic checks described in [the v1.3.1 notes](a10-ballistics-v1.3.1.md).
