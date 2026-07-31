# Fika

Fika is a soft dependency. The TSC Fika plugin ships in every package but only activates when `com.fika.core` is loaded; on single-player installs it logs an informational message and stays idle.

Install the same TSC version on:

- Host.
- Headless host, if used.
- Every client.

## Behavior

- Host config is authoritative while joined.
- Client local config does not override host settings during a joined raid.
- Dashboard changes on the host sync to clients.
- Disconnect clears synced overrides.
- Damage remains host-authoritative.
- Clients wait for the raid authority to accept support requests before starting requester visuals or finalizing authorization use.
- Rejection, timeout, cancellation, and executor-start failure settle through an idempotent commit/refund lifecycle keyed by `SupportRequestId`.
- Duplicate request, acceptance, tracer, impact, commit, or refund delivery cannot execute or settle the same request twice.
- A dedicated headless host may run the separately gated experimental A-10 damage executor. Single-player and human-host raids retain the original runtime/ballistic path.
- UAV recon links and physical-phone radar data are requester-local. A human host cannot view a client's feed, and a dedicated headless host never creates one.
- The authority-provided UAV duration drives both the requester phone link and the loiter aircraft lifetime; stale local defaults cannot shorten only the phone feed.
- Payment currency, prices, service availability, standard-Extraction timing,
  Cargo dispatch/wait/speed timing, and recon tuning come from the raid
  authority. Per-player authorization counts are hydrated separately and are
  never broadcast as host-global state.
- The pre-raid store uses the authenticated SPT session and can only read or mutate the signed-in PMC's stash and ledger.
- For supported UH-60 Cargo requesters, the local F12 **Transfer fee source**
  selects either EFT's default carried-RUB purchase or an authenticated stash
  RUB debit. The native handling quote is separate from the Cargo dispatch
  authorization and its configurable currency.
- Stash handling fees use an idempotent server prepare/commit/refund journal.
  Replays cannot charge twice, cancellation charges nothing, and a missing
  endpoint on an older server fails closed instead of falling back to carried
  cash.
- A verified Cargo submission ends the landed wait immediately. The human host
  publishes one request-bound reliable departure event so every observer's
  local UH-60 visual leaves with the host; cancel and payment failure publish
  nothing and retain the remaining retry window.

## Live Validation Status

Solo SPT and the pre-raid store have user-reported smoke coverage. The current
matched-version human-host, Fika-client, and dedicated-headless matrices remain
open. Dedicated-headless A-10 must continue to be described as experimental
until those tests prove execution, duplication protection, authorization
settlement, and teardown across representative maps and mod combinations.

## Regression Checklist

- Host/client same version.
- A-10 Strafe.
- A-10 Double Pass.
- A-10 tracers visible to non-host.
- A-10 damage executes once for duplicate request packets.
- Dedicated-headless A-10 damage and tracer timing, when the experimental mode is enabled.
- Dedicated-headless lethal bot hit: one death, stopped AI brain, one lootable corpse, and matching corpse state on every observer; no upright half-dead actor.
- Dedicated-headless remote-human hit: the owning client alone applies the Fika damage packet and publishes one native death/corpse sync, or one native downed state when Fika revival is enabled.
- UH-60 Extraction.
- UH-60 Cargo Transfer as a requesting human host with the default `Carried`
  fee source and with `Stash`.
- Successful host Cargo payment/move makes the host and every observer UH-60
  leave once; cancel or rejected payment leaves the aircraft available.
- Cargo `Stash` exact-balance, insufficient-balance, nested-currency,
  cancel-before-submit, double-click/replay, and missing-endpoint cases.
- Stock BTR transfer while Cargo `Stash` mode is selected; it must retain its
  native behavior and never enter TSC's stash-fee journal.
- UAV Recon.
- Focused Sweep.
- RUB, USD, and EUR carried/stash purchases for the correct requester.
- Pre-raid purchase followed by in-raid hydration and one consume/commit.
- Hold/release radar-phone restore, including movement while held, release during equip, and link expiry.
- Requester phone/aircraft lifetime parity with non-default host dashboard values.
- Distinct Extraction/Cargo dispatch and wait values; Cargo must never start a countdown or extract.
- Non-host/headless Cargo Transfer remains fail-closed before grid mutation,
  fee preparation, authorization consumption, or dispatch until authoritative
  item-price synchronization is implemented.
- Host dashboard config sync.
- Stash payment charges the correct player.
- Disconnect cleanup.
