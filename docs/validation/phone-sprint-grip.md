# Upright phone grip during sprint

SPT 4.1.5, September 8, 2026. The maintainer accepted the tested phone movement
and sprint zoom behavior after installing the final motion/zoom candidate.
The changes ship in 1.3.12. Broader cases below remain open where no individual
gameplay result was recorded.

The reported first-person radar phone floats separately from the visible hand
when sprinting. The phone's upright view pins an authored outro frame, while
EFT independently animates the player and applies hand IK. The phone asset has
no sprint clip; its housing and screen share a `Phone` subtree separate from
the authored `Base HumanLPalm`. This supports an animation-rig mismatch as the
cause; subsequent gameplay feedback confirmed the improved attachment.

The candidate carries the authored phone-to-palm offset onto the rendered
first-person left palm after EFT's animation pass. It moves only the phone
subtree and keeps its animation hierarchy intact. Each new frame and cleanup
restores the original local phone pose so corrections cannot accumulate.

This applies to the local, first-person upright deployment menu, radar monitor,
and Danger Close warning phone views. Equip, stow, authorization animations,
other players, and the full player rig retain their existing behavior. An
unavailable or ambiguous hand binding leaves the original phone presentation
active. A normal Info log records successful binding with the launch mode;
verbose diagnostics include bone paths. Failures are logged without cancelling
phone use.

## September 8 retest

The video `2026-09-08 19-28-19.mp4` shows the deployment authorization menu.
The matching game log contains DeployMenu and ManualAuthorization sessions,
with no UavRadarMonitor session. The installed DLL matched the first candidate,
but that candidate's radar-only guard prevented the grip correction from
activating for the shown deployment phone. This retest therefore did not
exercise the correction itself.

The revised candidate uses the same upright-mode predicate as the shared phone
reveal path, covering all three held portrait views. Deployment cleanup now
restores the authored prop pose immediately before the outro, matching the
other upright modes. Look for `TSC upright phone grip follow enabled.
mode=DeployMenu.` in the next game log; the message does not require verbose
phone logging.

## Walking sway and purchase zoom follow-up

The video `2026-09-08 19-38-32.mp4` and matching game log confirm deployment
grip binding activated. The user reports the sprint attachment looks better.
The remaining walking/turning movement affects the visible hand and phone
together. The final motion/zoom follow-up was subsequently accepted as described below.

This candidate reduces native walking, overweight walking, movement sway,
and mouse-turn sway to 25% while the local first-person upright grip binding is
active. These are per-call overrides during native procedural effector
processing; all values are restored immediately afterward, including exceptions.
The native sprint body animation and the prop-to-palm binding remain active.
This avoids smoothing the prop separately from the hand.

The horizontal ManualAuthorization purchase phone also eases back to the
original raid FOV and hand framing during actual sprint, then returns to its
purchase zoom after sprint ends. Both directions use the existing zoom-in
smoothstep and configured `Phone zoom in seconds` duration. The initial raise
retains its delay; closing the phone retains the separate zoom-out setting.
Reversing sprint during a fade starts from the current visible presentation.
Upright phone FOV and disabled automatic zoom retain their existing behavior.
New sprint transitions stop once stowing starts, so releasing sprint cannot
zoom toward a closing phone. Each sprint transition records its state and FOV
targets in the normal game log.

## Maintainer acceptance, September 8

After testing the installed motion/zoom candidate, the maintainer reported
that it was looking great with no complaints and authorized the release.
This accepts the reported upright phone movement and horizontal purchase
sprint zoom in that local setup. It does not independently verify every
upright launch mode, clothing, interruption, cleanup, or Fika case.

## Verification

- Full SPT 4.1.5 solution build passed with deployment disabled.
- Full CI checks passed, including 305 C# regression tests and 10 dashboard
  tests. These verify adjacent behavior, not the rendered grip.
- Native assembly and bundle inspection verified the separate palm/phone
  hierarchy and the final animation update order.

## In-game acceptance

| Case | Expected result | Status |
| --- | --- | --- |
| Deployment, radar, Danger Close | Each upright view records a grip binding and follows the visible hand. | OPEN |
| Standing and walking | Phone remains in the left hand during the tested movement. | ACCEPTED in maintainer setup; extended drift test unrecorded |
| Walk, sprint, stop | Phone housing and screen follow the left hand through transitions. | ACCEPTED in maintainer setup |
| Open while sprinting | Normal equip completes and the upright phone connects to the current hand pose. | OPEN |
| Repeated direction changes | No growing offset, shaking from double application, or detached screen. | OPEN |
| Upright walking and turning | Gentler hand/phone bob and sway. | ACCEPTED in maintainer setup; stow restoration not individually recorded |
| Horizontal purchase sprint, stop | Raid FOV/framing during sprint, purchase zoom after stopping; equal fade timing in both directions. | ACCEPTED in maintainer setup |
| Purchase sprint during raise or fade | Current presentation reverses smoothly, without a jump or repeated restart. | OPEN |
| Close purchase while sprinting | The previous weapon and original raid FOV return normally. | OPEN |
| Stow and reopen | Closing deployment or releasing the radar key restores the previous item; reopening repeats the correct grip. | OPEN |
| Inventory, death, raid end | No lingering render hook or stale transform on the previous phone. | OPEN |
| Different clothing and POV | Available first-person hand skins bind correctly; temporary non-first-person views receive no phone correction. | OPEN |

Build results do not close these visual acceptance cases. The first candidate
client DLL was installed locally on September 8 with a verified backup. The
deployment-phone retest established the scope error described above. The revised
client was built, passed the same checks, and installed at 19:34 local time with
the first candidate backed up and both hashes verified. The next video and log confirmed deployment grip binding and improved attachment. The motion/zoom candidate was installed at
19:49 local time after all 305 regressions and 10 dashboard tests passed. The
previous improved-grip client was backed up and both hashes verified. The maintainer then accepted the walking sway reduction and purchase sprint
zoom. The unrecorded matrix cases remain open.
