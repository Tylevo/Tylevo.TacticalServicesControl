# Seasonal Modifiers integration API

TSC 1.3.0 exposes an optional, reflection-friendly Danger Close API. Neither
TSC nor Seasonal Modifiers has a hard assembly dependency on the other.

## Main-menu coexistence

When the Seasonal client plugin is present, it owns the main-menu presentation.
TSC detects that soft dependency through BepInEx, removes its separate
**TSC UPLINK** row, restores the native menu stack, and stops that menu
controller's layout scans. This does not disable the in-raid Uplink or any TSC
service. When Seasonal is absent, TSC retains its normal pre-raid storefront.

## Client API v3

The public type is
`SamSWAT.FireSupport.ArysReloaded.Integration.SeasonalModifiersBridge` in the
TSC Core assembly. Integrations must require `ApiVersion == 3` before using the
warning lifecycle methods.

```csharp
public static int ApiVersion { get; }
public static bool IsDangerCloseAuthority { get; }
public static bool IsDangerCloseActive { get; }
public static bool TrySetDangerCloseActive(
    bool active,
    string sourceId,
    out string reason);
public static bool TryPublishDangerCloseAdvanceWarning(
    string opportunityId,
    int secondsRemaining,
    string sourceId,
    out string reason);
public static bool TryCancelDangerCloseAdvanceWarning(
    string opportunityId,
    string sourceId,
    out string reason);
public static bool TryPublishDangerCloseInboundWarning(
    string requestId,
    string sourceId,
    out string reason);
public static bool TryDispatchDangerCloseA10(
    UnityEngine.Vector3 target,
    UnityEngine.Vector3 direction,
    string requestId,
    out string reason);
public static bool TryDispatchDangerCloseA10(
    UnityEngine.Vector3 target,
    UnityEngine.Vector3 direction,
    string requestId,
    System.Action<bool, string> onProcessed,
    out string reason);
```

`TrySetDangerCloseActive` acquires or releases a lease owned by `sourceId`.
Releasing one source never removes another integration's lease. While at least
one lease is active, manual Strafe and Double Pass calls are locked; UAV,
Focused Sweep, UH-60 Extraction, and UH-60 Cargo Transfer are unchanged.

Only call dispatch while `IsDangerCloseAuthority` is true. That is the solo
player or a human Fika listen host; Fika clients and dedicated-headless peers
must not originate the ambient schedule. Remote peers that forge the ambient
origin are rejected by the host authority.

Every dispatch attempt must use a request ID that is globally unique for the
raid. Use a GUID or a source-prefixed monotonic counter. IDs are 1-96
characters and may contain letters, digits, `-`, `_`, and `:`. Reusing an ID
that was already accepted is deduplicated and does not launch a second pass.

The synchronous return value describes queueing only. `true` with reason
`Queued` means TSC reserved the no-overlap slot and began asynchronous authority
processing; it does not mean an aircraft pass was accepted. API v3's
`onProcessed(accepted, reason)` callback reports the later authority or local
executor result exactly once after a reserved request is processed. Early
synchronous validation failures return `false` and do not invoke the callback.
The legacy overload without a callback remains available for API v1 callers.

Use the same globally unique ID for an opportunity's advance forecast, its
possible cancellation, and its later dispatch request. Advance publication is
authority-only and returns success after validation even when no current peer
qualifies to see it. Only a local Uplink directly equipped in TSC's
`SpecialSlot4` presents the advance forecast. A cancellation is source-owned
and idempotent; a peer presents the stand-down only when it previously
presented that opportunity's advance forecast.

On an eligible local peer, the advance also starts a bounded 15-second incoming
call. The configured UAV-radar key (J by default) answers it and raises the
Uplink through the same upright, hands-safe presentation used by the UAV radar.
The answered screen shows the current forecast ETA and tells the player to seek
cover. Pressing the same key again stows the phone; subsequent presses reopen
the live countdown until its original forecast expires or the matching
stand-down or accepted inbound event arrives. Answering and toggling are local
presentation state only: they send no Fika packet, publish no bridge event, and
never change the scheduled dispatch time. Timeout, stand-down, accepted inbound,
and raid/network reset all stop the ringtone; a busy or failed phone presentation
retains the normal visible advance warning.

The maintainer-supplied Dokkaebi-themed clip can be placed at
`project/SamSWAT.FireSupport/LocalOnly/danger-close-ringtone.mp3` for local/test
builds. It is excluded from public release archives until its redistribution
rights are documented. When the file is absent, TSC skips the incoming call and
keeps the normal visible advance warning.

Call `TryPublishDangerCloseInboundWarning` only after the v3 dispatch callback
reports that the matching `SeasonalAmbient` request was accepted. The inbound
alert is universal and does not require an equipped Uplink. Manual event-handler
strikes should skip the advance call and publish only this accepted inbound
alert.

On a Fika listen host, TSC sends advance, cancellation, and inbound lifecycle
events to every client with `ReliableOrdered` delivery. Each client evaluates
its own `SpecialSlot4`; the host never trusts a client eligibility claim.
Clients have no registered server packet with which to originate or cancel a
warning. All warning replay/deduplication state is cleared at network and raid
boundaries.

Ambient passes carry an authenticated `SeasonalAmbient` network origin while
their physical shots use the authority/requester's validated EFT player bridge
as required by SPT 4.1 impact processing. This ballistic owner does not change
the ambient request's payment, authorization, or manual-service semantics. The
A-10 slot remains reserved until the accepted aircraft lifecycle completes or
the raid cancels it.

## Server marker v3

The server assembly exposes
`SamSWAT.FireSupport.ArysReloaded.Integration.SeasonalModifiersServerBridge`
with `public static int ApiVersion { get; } == 3`. Seasonal Modifiers can scan
for this marker before making Danger Close eligible in its server-side global
catalog. Missing or incompatible markers should fail closed.
