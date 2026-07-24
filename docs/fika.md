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
- Clients wait for the raid authority to accept A-10 requests and only render the accepted visual/tracer replay.
- A dedicated headless host may run the separately gated experimental A-10 damage executor. Single-player and human-host raids retain the original runtime/ballistic path.
- UAV recon links and physical-phone radar data are requester-local. A human host cannot view a client's feed, and a dedicated headless host never creates one.
- The authority-provided UAV duration drives both the requester phone link and the loiter aircraft lifetime; stale local defaults cannot shorten only the phone feed.

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
- Hold/release radar-phone restore, including movement while held, release during equip, and link expiry.
- Requester phone/aircraft lifetime parity with non-default host dashboard values.
- Host dashboard config sync.
- Stash payment charges the correct player.
- Disconnect cleanup.
