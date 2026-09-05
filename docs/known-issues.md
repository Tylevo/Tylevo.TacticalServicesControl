# Known Issues

Current release: **TSC 1.3.8 for SPT 4.1.4**. The release remains a beta/tester
build. See [current validation](validation/v1.3.8.md) for the automated,
server, and user-reported gameplay evidence and its limits.

- Phone inventory inspect model may still need polish.
- Mortar/artillery support is planned but not included.
- Dedicated-headless Fika A-10 damage remains experimental and separately gated from the original single-player/human-host path. The known raw-health half-death path has been replaced with Fika's player-owned damage routes, but lethal bot corpse sync and remote-human death/downed behavior still require matched-build live acceptance.
- Human-host, Fika-client, and dedicated-headless live acceptance for
  transactional requests, requester-owned UAV state, standard-Extraction and
  Cargo timing isolation, and multi-currency payment is not yet complete.
- If both authority-acceptance result paths and cancellation settlement are lost beyond their bounded waits, an authority-executed service can still be refunded and become free.
- Commit/refund retries are volatile. A client crash, permanent logout, or backend outage that outlasts pending expiry can refund an already delivered service.
- Remote third-person phone animation sync is planned but not included.
- Public beta: back up profiles before testing payment modes.

Stash payment and non-host A-10 tracer visibility are implemented, but the
current automated suite does not exercise either path end to end. Both remain
in the matched-version live multiplayer acceptance matrix.

The Pilot shop's registration, saved unlock, listing, and portrait have passed
native server checks. Final portrait framing and a paid purchase in the game
UI remain to be checked. The bottom-bar shortcut has native prefab and
lifecycle fixture coverage; resolution-specific rendering is still a live check.
