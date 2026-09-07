# Pilot Services manual test checklist

**Status: aircraft artwork visually approved; full checklist pending.** This
checklist covers the move of TSC's pre-raid authorization shop into
**Traders > Pilot > Services**. The user approved the actual-model aircraft
renders in game with Core `1.3.11-pilot-services.4`. That approval covers their
appearance; the full navigation, purchase, recovery, and resolution checks
below remain unrecorded. Record the tested build, resolution/UI scale, and
results before marking any item complete.

The current local test targets are Core `1.3.11-pilot-services.6` and Server
`1.3.11-pilot-services.5`. They include the restored airfield banner, native
trader balance synchronization, simpler service icons, and a shared close-up
crop for both Pilot portraits. The approved aircraft renders and the code-drawn
radar remain in place. The new icons and portrait framing still need in-game
acceptance.

Use a backed-up test profile with known stash funds and authorization counts.
Test base TSC without any introduction quests: Pilot and service purchases
should be available immediately. Then install the optional
[Pilot Questline add-on](pilot-questline.md) and complete its introduction for
service purchase tests. Also check a profile after Open Channel but before Back on the Air: Pilot's
Services tab must explain "Complete Back on the Air for Pilot" and block new
purchases while retaining transaction recovery.
The physical Uplink remains in Pilot's Trading tab; the Services tab sells
support authorizations. Phone purchase and deployment controls remain unchanged.

In menus, balance synchronization waits for pending native inventory operations
to finish, then reads an authenticated absolute cash snapshot from the server.
The client applies it through native inventory events so the TSC balance and
native trader header should both reflect the purchase. Synchronization does
not add a charge. Its in-game checks remain pending.

## Navigation and lifecycle

- [ ] Pilot is unlocked and the **Services** tab is enabled. Opening it shows
  the compact service list on the left and the selected service's details on
  the right, with the correct PMC balance and authorization counts.
- [ ] Switch between Pilot's **Trading**, **Tasks**, and **Services** tabs
  repeatedly. Each tab shows its own content, with no overlapping TSC panel,
  duplicate controls, stale selection, or blocked input.
- [ ] Switch to other traders and back. Check **Ragman's Services** specifically:
  his native service content still works and never shows Pilot's TSC shop.
- [ ] Use **Back**, **Escape**, and the trader screen's close control from the
  list and purchase review. Reopen Pilot Services several times. No hidden
  panel captures input, and navigation never starts an unintended purchase.
- [ ] Return to the main menu. Its spacing is normal and no old standalone
  TSC shop shortcut or duplicate shop window remains.

## Visual layout and fallback

- [ ] The Pilot banner spans the full width of the Services content area.
  The restored airfield image is cropped correctly and leaves the text
  readable. Both the native trader card and Services banner show the same
  close-up helmet-and-face framing without distortion. Text and controls do
  not obscure his face or overflow the header. Switch traders, reopen the
  screen, and resize it: other traders keep their original portraits and the
  Pilot never becomes progressively more zoomed in.
- [ ] All six service icons have transparent backgrounds and remain readable
  at the normal UI scale. Each row, purchase review, and phone screen uses the
  correct symbol. Strafe and Double Pass, Extraction and Cargo Transfer, and
  Recon and Focused Sweep are easy to distinguish at their smallest size.
- [ ] Labels use EFT's native **Bender** font where available. Money fields use
  Arial Bold because Bender lacks the ruble glyph. Prices, counts, service
  names, and confirmation text are readable, with no missing currency glyphs,
  clipped letters, or inconsistent scaling. Check RUB, USD, and EUR, including
  balances populated after refresh and before/after amounts in purchase review.
- [ ] The selected service uses a clear gold highlight. Moving between rows
  updates both the selection and the detail panel; hover does not leave a
  second row looking selected.
- [ ] A service at its authorization limit shows **Limit Reached** in red and
  cannot be purchased. Changing selection or refreshing after a count change
  updates the label and action state without leaving stale red text.
- [ ] The detail panel shows the rendered A-10 model for Strafe/Double Pass
  and the rendered UH-60 model for Extraction/Cargo Transfer. The images use
  the aircraft already shipped in TSC. Their silhouettes and materials remain
  readable against the panel, retain their aspect ratios, and stay clear of
  the price and purchase controls.
- [ ] Recon and Focused Sweep show the radar drawn in code. Its rings and
  contacts remain sharp at each tested UI scale, stay inside the artwork
  area, and update correctly when switching service families.

- [ ] Check the supported resolutions and UI scales, including a smaller
  window and an ultrawide layout. Resize with Services open. The service list,
  banner, portrait, detail artwork, balance, prices, counts, and confirmation
  controls stay inside the trader content area; long descriptions do not
  overlap actions. Close and reopen after resizing to check both layout paths.
- [ ] In a disposable test installation, test missing or unreadable
  `pilot-portrait.png`, `pilot-banner.png`, `a10-detail.png`, and
  `uh60-detail.png`. The fallback keeps the service list, details, and purchase
  controls usable without a blank blocking panel or repeated errors. Restore
  all four assets afterward. The radar needs no separate PNG.
  If native Bender cannot be resolved, a readable font fallback must preserve
  the same controls.

## Purchases and limits

- [ ] Select each service. Its description, price, availability, and held/limit
  count match server configuration. Disabled services and full limits cannot
  be purchased.
- [ ] Confirm one purchase. Exactly one configured stash payment is charged
  and one authorization is granted to the signed-in PMC. The balance and count
  refresh correctly. Reopen the tab and verify the state persists.
- [ ] Repeat with RUB, USD, and EUR. After each purchase, the TSC balance and
  native trader header show the correct balance for that currency. Refreshing
  or reopening Services does not charge again or change authorization counts.
- [ ] Spend a cash stack completely, then inspect the inventory and both
  balance displays. The exhausted stack is removed, with no stale amount or
  duplicate stack. Repeat with cash inside nested stash money containers.
- [ ] Move cash between stash containers and refresh immediately. Pending
  inventory operations finish before synchronization; the refresh must not
  undo the move or restore an old stack amount.
- [ ] Switch tabs while a balance refresh is pending, then return to Services
  and refresh again. The current view stays usable, both balances converge to
  the server state, and no extra payment or authorization is created.
- [ ] Cancel the purchase review, including via Back/Escape where available.
  The stash and authorization count remain unchanged.
- [ ] Check exact funds, insufficient funds, a reached authorization limit,
  and repeated confirmation clicks. A rejected or duplicate request cannot
  debit twice, grant twice, or leave the purchase controls stuck.
- [ ] Enter a raid after buying support. The authorization is available in
  the **K** deployment view. The **U**, Alt/mouse, number-key, and confirmation
  controls still work as before.

## Interrupted requests and recovery

- [ ] Interrupt a purchase response or disconnect the test server during a
  request, then restore it and reopen Services. Closing or switching tabs
  must not silently turn an uncertain result into a second purchase.
- [ ] Confirm that recovery resolves the original request before a retry can
  charge again. The final server state has either one debit with one matching
  authorization, or a settled failure/refund. No duplicate grant, lost funds,
  permanent busy state, or response applied to another trader is allowed.

## Result record

- Build/source revision: pending
- SPT / Toolkit versions: pending
- Profile type and test funds/counts: pending
- Resolutions and UI scales: pending
- Completed checks, failures, and relevant log excerpts: pending

Keep profile identifiers, credentials, and complete player saves out of public
test reports. Fika multiplayer remains a separate, untested acceptance scope.
