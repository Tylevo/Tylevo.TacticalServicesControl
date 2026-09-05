# Tylevo's Tactical Services Control v1.3.3 Public Beta

> **Historical documentation.** These instructions and results describe this
> earlier version. For TSC v1.3.10 on SPT 4.1.5, use the
> [current release notes](release-notes-v1.3.10.md) and
> [installation guide](dependencies.md). See the [archive index](archive/README.md)
> for earlier release availability.

Targets SPT 4.1.4 / EFT 0.16.9.5.40743 with the existing WTT and UnityToolkit
dependencies. This tester replaces the phone's static purchase screens with
native panels and text, retaining the service image assets and the deploy
menu's dark, ivory, amber, and green palette.

Purchase browsing and review remain landscape. Final confirmation still uses
the existing portrait hand animation, upward arrows, and payment commit
timing. Prices, configured currency, wallet balance, held authorizations,
service availability, and recon parameters come from the active game state.

Hold Left Alt while browsing to move a cursor on the phone. Click a service to
select it, then use the review and confirmation controls. In the deployment
menu, select an owned authorization and use the explicit deploy control.
Release Alt to restore camera look. Keyboard controls remain available.
The mouse modifier, enable setting, and sensitivity are configurable in F12.

The pointer is hidden and pending clicks are cancelled during final payment
animation and whenever phone input ownership is lost. Mouse selection uses
the same purchase and deployment validation paths as the keyboard controls.

The v1.3.2 eased phone zoom and v1.3.1 A-10 ballistic corrections are retained.
Deploy and held-radar phone poses retain their established behavior.

The expected archive is
`Tylevo.TacticalServicesControl-v1.3.3-SPT4.1.4-TESTER.zip`. Install all four TSC
DLLs as a matched version on the host and every Fika client.

Build, regression, package, and installed-server results are recorded in the
candidate's external evidence sidecars. In-game acceptance should cover mouse
selection at the installed FOV, rapid Alt release and re-press, clicking and
dragging off a button, inventory/pause/focus transitions, keyboard navigation,
unavailable services, final payment cancellation, and Fika visual replay.
