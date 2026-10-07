# Pilot Services appearance and purchase history

Local UI test build: Core `1.3.11-services-ui.9`, Server `1.3.11-services-ui.8`, for SPT 4.1.5.
The plugin version remains numeric `1.3.11`; the test label is informational metadata only.
The user confirmed the `.8` UI looks good in game. The `.9` follow-up aligns
Services and History with Refresh and compacts the Pilot/TERRAGROUP labels;
that last spacing adjustment awaits an in-game check.
This trial preserves the currently unlocked Pilot;
the separate questline under development is not included in this local build.

## Appearance

- A compact Pilot header with Services and History tabs replaces the aviation banner.
- Services, History, and Refresh share the same vertical position and button height.
- Muted beige selections, ivory text, subdued prices, and olive availability labels follow the native trader palette.
- The page and storefront background are transparent. The existing trader screen supplies the blurred environment; no camera or environment settings are changed.
- Individual rows and details use translucent surfaces with one-pixel edges. The actual aircraft renders, shared close-up portrait, and service icon assets are retained.
- Purchase confirmation uses the same palette, with an opaque dialog over a content-area scrim.

## Purchase history

History shows up to 50 recent completed authorization purchases, eight per page,
with local timestamps, quantity, and the original amount and currency paid.
It reads the authenticated PMC's saved purchase records. It does not substitute
current prices or infer purchases from authorization balances.

Pending purchases, failed purchases, refunds, and usage records are excluded.
Replayed purchases appear once. Ordinary in-raid configuration polling does not
request history. Missing history is shown as unavailable, separately from an
empty history. Profile/session changes clear the displayed records.

## Verification

- Client and server compile successfully; the client retains two existing obsolete inventory API warnings.
- All 264 regression tests for the isolated UI build pass, including seven purchase-history tests.
- The first `.7` trial was rejected at startup because its test label was incorrectly used as the plugin version. The `.8` rebuild separates the numeric plugin version from the informational label and adds installed-BepInEx metadata validation before installation. The Fika helper's missing-Core error was a consequence of Core being skipped.
- Services, History pagination, and confirmation were visually reviewed in a local browser preview using sample data. The preview uses a placeholder environment, not a game capture.
- Installation replaces only the Core and Server DLLs and verifies their hashes, with backups of both originals. No assets, profiles, settings, or dependency files are installed by this update.

## Next game check

- Open **Traders > Pilot > Services**. Check background blur, text contrast, all six services, and the selected-service details at your usual resolution and UI scale.
- Open History, page through available records, then return to Services. Check the empty state if the profile has no retained purchases.
- Buy one available authorization. Confirm the count, both stash-balance displays, and a single new History entry update after the purchase finishes.
- Cancel a purchase review, press Escape, switch trader tabs, visit Ragman's Services, and return. Verify that navigation and the native Services screen still behave normally.

Nothing from this UI trial has been published.
