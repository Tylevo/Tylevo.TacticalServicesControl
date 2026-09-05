# Using TSC

TSC offers A-10 Strafe, A-10 Double Pass, UH-60 Extraction, UH-60 Cargo
Transfer, UAV Recon, and Focused Sweep. Buy a service authorization before
the raid or through the Uplink during one, then deploy it when needed.

## Get the Uplink

Buy the **TerraGroup TSC Uplink** from **UH-60 Pilot** in Trading for
**₽50,000** at loyalty level 1, up to five per restock. Pilot has no quest
requirement for now. The server unlocks existing locked Pilot entries at
startup, and Pilot also delivers your UH-60 cargo mail.

Bring the Uplink into the raid to use its purchase, deployment, and radar
controls. You can keep it in the dedicated fourth special slot.

## Default controls

| Action | Control |
| --- | --- |
| Open the purchase phone | `U` |
| Open deployment for owned services | `K` |
| Raise the phone for active UAV radar in Phone mode | Hold `J` |
| Browse the phone with the mouse | Hold `Left Alt`, then left-click |
| Open Tactical Services from the purchase home screen | `LMB` |
| Choose UH-60 Services, Fire Support, or UAV Recon | `1`, `2`, or `3` |
| Choose the standard or alternate service in a category | `1` or `2` |
| Confirm a reviewed purchase | `Enter`, or hold Alt and click confirm |
| Select an owned service in deployment mode | `1`–`6` |
| Deploy the selected service | `LMB` or `Enter`, or hold Alt and click deploy |
| Confirm a camera targeting step | Middle mouse (`Mouse 2`) or `Enter` |
| Cancel camera targeting | `Alt + RMB` or `Backspace` |

`RMB` goes back on the purchase phone; `Escape` closes it. In deployment mode,
`RMB`, `Backspace`, or `Escape` stows the phone without spending an authorization.
Release Alt to look around again. Open `F12` to change the purchase, deploy,
radar, and spotter-confirm bindings.

## Buy support before a raid

Open **TSC UPLINK** on the main menu's bottom bar, immediately left of
**Character**. The shortcut only appears on the main menu.

1. Wait for your stash balance and purchased authorizations to load.
2. Select a service card to see its artwork, description, price, availability,
   and how many authorizations you own.
3. Open the purchase review, check the price and projected balance, then
   confirm. Cancelling the dialog does not send a purchase request.

Purchases use the signed-in PMC's stash and persistent authorization ledger.
They are available when you enter a raid with the same PMC. The pre-raid store
requires persistent authorizations and a server-backed stash payment source.
Its **Dashboard** button opens the active SPT server's TSC dashboard.

## Buy support in raid

Press `U` to open the Uplink. Hold `Left Alt` to use the phone cursor, open
**Tactical Services**, and choose a category and service. For keyboard
navigation, tap `LMB` on the home screen, then use `1`, `2`, or `3` for the
category and `1` or `2` for the service.

Check the details on the review screen, then hold Alt and click confirm or
press `Enter`. The phone turns upright and plays the swipe animation
automatically; you do not need to drag anything. The swipe commits payment
using the configured currency and wallet source. Closing the phone after
payment has gone through does not undo the purchase.

## Deploy support

Press `K` to open deployment mode, which lists only services you own. Hold
`Left Alt`, select a service, and click deploy. You can also press `1`–`6` to
select a service, then `LMB` or `Enter` to deploy it.

For A-10 and UH-60 services, use the camera to mark the target. Confirm each
step with middle mouse or `Enter`. `Alt + RMB` or `Backspace` cancels targeting.
UAV Recon and Focused Sweep start as soon as they are deployed.

## View UAV radar

Only the requester sees the radar. In the default **Phone** display mode,
hold `J` to raise the Uplink and view the live radar. Release it to return to
your weapon. Walking or sprinting does not lower the phone while you hold
the key, and the recon timer keeps running while it is stowed.

The optional **HUD** mode shows the live scanner square in one of four screen
corners for the active recon session. Select the mode and corner under
**UAV Radar Display** in `F12`. UAV support also includes the A-10 loiter visual.

## Send cargo home

**UH-60 Cargo Transfer** lands at your marked loading zone and offers
**SEND ITEMS VIA UH-60**. It sends items home without extracting your PMC.

The service authorization pays for dispatch. EFT charges a separate
item-handling fee when you submit cargo, always in RUB, regardless of the
configured authorization currency. In `F12` under **Helicopter Cargo**,
**Transfer fee source** defaults to **Carried**, using EFT's normal carried
cash payment. Select **Stash** to pay from your authenticated PMC stash
through the TSC server.

The helicopter leaves as soon as EFT confirms that the paid items reached
its saved delivery grid. If you cancel or payment fails, you can try again
during the remaining landed time. Cargo arrives after the raid through
**UH-60 Pilot** mail. Normal **BTR Driver** deliveries stay separate; TSC falls
back to BTR delivery if an accepted cargo delivery cannot be routed through
Pilot safely.

Cargo Transfer is available in solo play and for a human Fika host requesting
their own transfer. Other Fika clients and dedicated-headless requesters cannot
use it. Current Fika support remains untested; see [known issues](known-issues.md).

## Phone presentation settings

Use `F12` for personal phone controls and framing. Mouse selection has settings
for its modifier and sensitivity, and can be turned off. The cursor stays on
the display as the handset moves.

Purchase screens stay horizontal until the final upright swipe confirmation.
The `K` deployment view and held `J` radar open upright and keep your raid FOV.
Optional purchase-screen zoom starts after a 0.08-second delay and eases the
camera FOV and hand framing into place over 0.75 seconds by default.

- **Phone zoom in seconds:** 0.25–1.5 seconds; default 0.75.
- **Phone zoom out seconds:** 0.15–0.8 seconds; default 0.35.

Closing the phone restores the original raid FOV, including after a quick
reopen. These zoom settings do not change deployment or radar views.

## Payments and server settings

The [TSC dashboard](dashboard.md) controls service prices, availability,
cooldowns, timing, and authorization settings. Open it through SIC in the
SPT launcher, or from the in-game store's **Dashboard** button.

TSC has `PhoneAuthorizations` and `Hybrid` payment modes, with RUB, USD, or EUR
payments from carried cash or the stash where configured. The phone shows the
active price and payment source. Pre-raid purchases use the authenticated PMC
stash; the server determines the price and currency.

Changing currency does **not** convert the price numbers. Review every service
price before saving a different currency. Cargo Transfer's separate handling
fee remains RUB-only and uses its own **Carried/Stash** setting in `F12`.
