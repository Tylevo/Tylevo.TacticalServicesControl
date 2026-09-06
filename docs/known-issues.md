# Known issues

Prepared candidate: **TSC v1.3.11 for SPT 4.1.5**, with standalone
**UnityToolkit 2.0.2**. Neither new package has been published. Build, package,
and runtime checks for this pair are pending in the
[validation record](validation/v1.3.11.md). Earlier local feedback for
v1.3.10 does not validate the new pair.

## Multiplayer

**Fika support has not been tested on the current SPT/Fika versions.**
Compiling against Fika 2.4.2 does not establish multiplayer compatibility.

- UH-60 Cargo Transfer is available to solo players and a requesting human
  host. Other Fika clients and dedicated-headless requesters cannot use it
  until item-dependent handling prices can be verified by the host.
- Dedicated-headless A-10 damage is experimental and must be enabled
  separately. Bot death/corpse synchronization and remote-player death or
  downed behavior still need live testing.
- Request acceptance, refunds, requester-only UAV state, extraction/cargo
  timing, stash payments, and non-host A-10 effects still need matched-version
  multiplayer testing. See the [Fika guide](fika.md).

Two recovery limits remain: if both acceptance paths and cancellation
settlement time out, a service that already ran can still be refunded.
Commit/refund retries are also held in memory, so a client crash, permanent
logout, or sufficiently long backend outage can refund an already delivered
service.

## Presentation and service checks

- The phone's inventory inspect model may need more polish.
- A-10 impacts, collisions, and replay effects need broader testing across maps.
- Phone and store layouts need broader coverage across resolutions and combat
  conditions. Pilot's registration and portrait have server checks; exact
  portrait framing and a paid purchase were not individually documented in
  earlier local test reports. The new candidate still needs its own runtime checks.

Mortar/artillery support and remote third-person phone animation sync are
not included.
