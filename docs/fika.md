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
- Payment currency, prices, service availability, extraction timing, and recon tuning come from the raid authority. Per-player authorization counts are hydrated separately and are never broadcast as host-global state.
- The pre-raid store uses the authenticated SPT session and can only read or mutate the signed-in PMC's stash and ledger.

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
- UH-60 Extraction.
- Priority Exfil.
- UAV Recon.
- Focused Sweep.
- RUB, USD, and EUR carried/stash purchases for the correct requester.
- Pre-raid purchase followed by in-raid hydration and one consume/commit.
- Hold/release radar-phone restore, including movement while held, release during equip, and link expiry.
- Requester phone/aircraft lifetime parity with non-default host dashboard values.
- Distinct standard/priority extraction dispatch and countdown values.
- Host dashboard config sync.
- Stash payment charges the correct player.
- Disconnect cleanup.
