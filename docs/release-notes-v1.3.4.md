# Tylevo's Tactical Services Control v1.3.4 Public Beta

Targets SPT 4.1.4 / EFT 0.16.9.5.40743 with the existing WTT and UnityToolkit
dependencies.

The pre-raid TSC Uplink store now follows the native phone's visual style.
Select a service card to see its artwork, description, current price,
availability, and owned authorizations. The purchase review control opens a
separate confirmation dialog before payment. Stash balance, refresh, close,
and dashboard access remain available. The store and dialog scale together
to fit the menu canvas.

The server remains responsible for payment, purchase limits, and stored
authorizations. Interrupted purchases retain their original recovery flow
and terms. UH-60 Cargo Transfer buys dispatch only, does not extract your PMC,
and charges a separate RUB handling fee when cargo is loaded.

This update retains the v1.3.3 native phone screens and Alt mouse controls,
v1.3.2 eased zoom, and v1.3.1 A-10 corrections. The user reported the in-raid
phone was responsive and looked and felt good; that feedback does not replace
the separate multiplayer acceptance matrix.

The expected archive is
`Tylevo.TacticalServicesControl-v1.3.4-SPT4.1.4-TESTER.zip`. Install the four TSC
DLLs as a matched version on the host and every Fika client.

Build, regression, layout, package, and installed-server results are recorded
in the candidate's external evidence sidecars. Live storefront acceptance
covers selecting all six services, confirming or cancelling a purchase,
refreshing balances, resolution changes, unavailable services, purchase
limits, and returning to the main menu.
