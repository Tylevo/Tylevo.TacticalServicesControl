# Tylevo's Tactical Services Control v1.3.7 Public Beta

> **Historical documentation.** These instructions and results describe this
> earlier version. For TSC v1.3.10 on SPT 4.1.5, use the
> [current release notes](release-notes-v1.3.10.md) and
> [installation guide](dependencies.md). See the [archive index](archive/README.md)
> for earlier release availability.

Targets SPT 4.1.4 / EFT 0.16.9.5.40743 with the existing dependencies.

While on the main menu, open **TSC UPLINK** on the bottom bar immediately
left of **Character**. TSC no longer adds a center-menu row or rewrites native
menu positions. The entry uses EFT's normal footer styling and layout, with
a compact TSC logo. Its own cloned toggle group and event open the existing
storefront without changing Character or other native navigation selections.

The shortcut hides when leaving the main menu and rejects stale, disabled,
or in-raid callbacks. It returns on the next authenticated main-menu bind.
Seasonal Modifiers client suppression remains in place. The store's prices,
payment confirmation, profile/session validation, and purchase recovery are
unchanged. The phone, service icons, and A-10 behavior are retained.

The implementation resolves the persistent taskbar through PreloaderUI,
clones the complete Character wrapper, and inserts it in the native horizontal
layout. EFT's flexible spacer provides room for the new slot. The TSC icon's
layout size is bounded so a large source PNG cannot stretch the footer.

The expected archive is
`Tylevo.TacticalServicesControl-v1.3.7-SPT4.1.4-TESTER.zip`. Install the four
matched TSC DLLs with EFT closed. Restart the SPT server after replacement.

Build, regression, native prefab/IL inspection, taskbar harness, package, and
installation results are recorded in external evidence sidecars. Live checks
cover opening and closing the store repeatedly, Character/Trader navigation,
returning to the main menu, profile changes, and resolution changes. Native
prefab and harness checks do not replace the final in-game appearance test.
