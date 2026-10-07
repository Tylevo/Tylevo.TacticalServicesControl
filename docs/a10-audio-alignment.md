# A-10 audio alignment

This change addresses two reported symptoms: the impact recording starts before
the rounds land, and its active burst continues after the visible impacts end.
The supplied `woods.mov` is a 4.907-second edited comparison. Its author already
shifted the audio to align the start, so it cannot establish the original onset
error. It is useful evidence of the duration mismatch.

## Comparison and cause

[SamSWAT's original A10Behaviour](https://github.com/SamSWAT911/FireSupport/blob/f2a7c0e08586f772c2fdf9576d327441167dbe3c/project/SamSWAT.FireSupport/Unity/Vehicles/A10Behaviour.cs)
waits two seconds after starting its 50-round burst before playing the impact
recording, then five seconds before the distant cannon report. Its projectile
origin and aircraft timing differ from current TSC.

Before this change, TSC started the impact recording after only
`listenerDistance / 343`. This omitted the projectile's flight time. The current
moving-muzzle trajectory also compresses the arrival span: later rounds have
less distance to travel. Simply matching the firing duration to the recording
would still produce a ground burst that is too short.

The A-10 bundle in the published 1.3.13 archive contains three
`GAU8_Explosions_loop_*` recordings, each approximately 8.89 seconds long.
Measured 250 ms audio windows show the loud active burst ending around 2.7 seconds,
followed by decay and a very quiet tail. The complete file length is therefore
not a suitable target for sustained firing.

Bundle SHA-256:
`3E8B1621F4830AEFFD34F7F340B99874C81B5E7E8EA7515AD976169FF8BB79D9`.

## Result

The full original recording plays unchanged. No trimming, synthetic fade,
pitch adjustment, or asset-bundle replacement is applied.

The shared shot plan now contains 120 rounds. The designated corridor remains
44.1 m long and 15 m wide; longitudinal spacing is reduced to fit the additional
rounds into the existing footprint. All modes consume this shared count.

Using the existing inspected-native trajectory test model, flat ground,
320 m altitude, a first muzzle 1450 m from the target, and shipped GAU-8 values
(1395 RPM, 1070 m/s, 280 g, 30 mm, ballistic coefficient 0.316):

| Planned rounds | Launch span | First impact | Last impact | Ground impact span |
| --- | ---: | ---: | ---: | ---: |
| 50 | 2.108 s | 3.625 s | 4.703 s | 1.078 s |
| 120 | 5.118 s | 3.625 s | 6.345 s | 2.720 s |

These are model results, not measurements from a new raid. Terrain, obstacles,
the prefab muzzle offset, frame timing and modified ammunition affect them.

Audio and firing share one computed plan and local launch clock. Playback waits
for the earliest valid planned impact plus sound propagation from the target
to the listener. Invalid plans do not trigger the impact soundtrack. The
original five-second gap before the BRRRT is restored. Muzzle particles stop
and departure flares begin when the firing loop finishes, independently of the
listener's distance. Delaying the impact recording must not also postpone the
aircraft's flare sequence until it is nearly over the target.

This intentionally increases each pass from 50 to 120 damaging rounds: 70 more
rounds, or 2.4 times the count. Damage per round is unchanged. A double pass now
plans 240 rounds. More projectiles also mean more ballistic and network work.
Fika still uses the existing 20-segment packet layout, now six full packets per
unobstructed pass.

## Verification and remaining acceptance

Regression coverage includes nearby/distant listeners, invalid timing, earliest
arrival order, separate passes, the active audio window, continuous 120-round
arrival timing, unchanged corridor bounds, headless arrival alignment, full
packet round-trips, and a bounded full-burst trajectory workload.

`tools/verify-local.ps1` passed with the documented SPT 4.1.5 references and
deployment disabled: 368 C# regressions, 42 Node checks, and all four runtime
assemblies built successfully (seven existing warnings, zero errors).
Evidence for this working change is stored in
`work/a10-audio-alignment/verify-local.log` in the containing RaidOps workspace.

Before releasing, check an unedited single pass and double pass in a raid,
both near and far from the target. Confirm the onset, sustained impacts, full
echo tail, muzzle particles, and frame rate. Repeat on Fika human-host and
headless sessions. Client audio still follows its local predicted plan; this
change does not replace Fika's existing clock alignment or introduce audio
driven by authoritative hit callbacks. Moving cover, ricochets and network
delays can still diverge from prediction. The first audio-alignment build was
installed locally with the user's authorization; it has not been published.

## Flyover follow-up

The local 120-round test reached sampled collisions 0, 60 and 119 and reported
a 2.737-second planned impact span. A second test confirmed the aircraft and
flares remained visible. This rules out a fixed 50-round loop cutoff, but does
not by itself confirm the engine playback.

Reference-to-recording correlation identifies the original engine clip during
the approach in both the old 50-round and new 120-round test recordings. Its
later contribution disappears around the impacts and remains absent after the
cannon report, when the original engine clip is still strong. Ordinary masking
alone does not explain the quiet departure; source stoppage, voice virtualization
and mixer attenuation cannot be distinguished from those recordings.

The installed prefab's engine is a separate, enabled child AudioSource with
priority 160. It is outside the muzzle-particle hierarchy. Native BetterAudio
uses priority 64 through 128 for PlayAtPoint recordings. Unity can virtualize
lower-priority sources when the real-voice budget is exhausted; a virtual source
advances silently. See [Unity's AudioSource.priority documentation](https://docs.unity3d.com/ScriptReference/AudioSource-priority.html).

The strike now explicitly sets engine priority 64, pitch 1 and Doppler 0. The
last two preserve the inspected prefab's original playback settings. This
protects the continuous flyby against lower-priority impact voices without
changing the clips or the global voice limit. Voice competition is a candidate
cause, not yet a measured root cause. The existing environment mixer routing
and player audio settings remain in effect.

Bounded phase diagnostics record playback time, playing/virtual/enabled state,
volume, mute, priority, player-camera distance, listener volume/pause, mixer,
and flare activation. Startup also records the real and virtual voice limits.
Check `impacts-started`, `burst-complete`, `cannon-report`, `leaving` and `cleanup`
in the next raid log. Live confirmation of the audible flyover is still required.

The follow-up passed the same 368 C# regressions, 42 Node checks and four
runtime builds (seven existing warnings, zero errors). Full evidence is in
`work/a10-audio-alignment/verify-flyover-priority.log` in the RaidOps workspace.
With the game closed, the core DLL was backed up and replaced on
2026-09-14 at 01:23:49 UTC. The verified installed SHA-256 is
`E6A11A9BAB94F5CE4223F248EE322C26E51F925CB9461AED49AF650557339B9E`.
Installation details and the rollback copy are recorded in
`work/a10-audio-alignment/installed-a10-flyover-priority.json`.

## Missing impacts after the flyover adjustment

The next local test reported an audible engine but missing impact audio. The
engine snapshots at impacts, burst completion, cannon report and departure were
playing, enabled and nonvirtual at volume 1. The game reported 196 real voices
and 256 virtual voices. Sampled rounds 0, 60 and 119 still collided, with a
2.742-second predicted impact span. No gun-recording source state was captured
by that build, so neither voice contention nor mixer attenuation is established
as the cause of the missing recording.

Inspection of the installed SPT 4.1.5 native assembly confirmed the existing
PlayAtPoint overload, range, volumes and full-clip release behavior. Its return
value is a BetterSource. At this listener distance the impact source initially
receives priority 82, and the distant cannon report receives priority 128.

A10StrikeAudio now gives both returned sources the same priority 64 as the
working engine. BetterSource.SetPriority also updates SuperSource's second
channel. Clip selection, volume, range, mixer routing, native one-shot playback,
120-round timing and release ownership remain unchanged. This corrects the
unequal priority introduced by protecting only the engine; audibility still
requires live confirmation.

Recording diagnostics capture the selected clip and source state immediately,
then after one and three seconds. They report playback, virtualization, volume,
mute, priority, fade, occlusion and mixer state. Native SimpleSource uses
PlayOneShot, so its AudioSource.clip/time are deliberately not treated as the
recording's identity or progress. The observer detaches on OnReleased and never
follows a returned source into another pooled playback. Headset mixer values are
read without modifying global audio settings.

Validation passed: 368 C# regressions, 42 Node checks and all four runtime
builds, with seven existing warnings and zero errors. Evidence:
`work/a10-audio-alignment/verify-strike-recordings.log` in the RaidOps workspace.
The core DLL was backed up and installed with the game closed at
2026-09-14 02:59:49 UTC. Verified installed SHA-256:
`D193D1ADA54D601F3F93234A577C07501D188D472FEA1E247DDBCBEA9AE378E3`.
The installation and rollback record is
`work/a10-audio-alignment/installed-a10-strike-recordings.json`.

## Cannon audibility correction

The user clarified that ground impacts are audible; the missing sound is the
cannon's firing report. In the next log, GAU8_firing_v2_01 remained playing,
enabled, nonvirtual and unmuted at startup, one second and three seconds.
Its source volume, fade and occlusion factors were all 1. GunsVolume was 0 dB,
and the compressor send levels were -80 dB. Raising priority had not addressed
this remaining symptom.

The actual Gunshots resource preset in SPT 4.1.5 uses a custom attenuation curve.
At normalized distances 0.07 and 0.10, its gains are approximately 0.440 and
0.389: a reduction of 7-8 dB for the observed cannon position/range. Native
SuperSource also clamps the original requested gain of 2 to 1. Meanwhile the
engine is inside its full-volume distance, near its recorded peak. Asset
analysis shows the cannon's first second is only 3.1 dB stronger than that engine
section at equal gain, and its next second is 17.8 dB weaker. This supports a mix
problem; source playback logs alone cannot measure the final audible output.

Only the cannon report now uses a dedicated world AudioSource. It keeps the
original sound, position, five-second interval after the impacts, pitch and
GunshotMixerGroup. Explicit logarithmic attenuation uses a 450 m minimum and
3,200 m maximum distance. PlayOneShot applies the originally requested gain of
2 directly, avoiding the native SuperSource clamp. The resulting report is
deliberately about 13-14 dB stronger at the last test's distance. The engine and
ground-impact paths, clip assets and 120-round barrage are unchanged by this
correction.

The unparented sound object belongs to this recording and is destroyed after
the full clip plus a short margin, using Unity's audio clock, or on raid
cancellation. It does not change or retain any pooled source settings. See
[Unity's audio clock](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/AudioSettings-dspTime.html)
and [PlayOneShot gain](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/AudioSource.PlayOneShot.html).

Live acceptance must confirm the BRRRT is distinct from the engine without
harsh clipping, and that its full decay remains audible. Diagnostic snapshots
are logged at startup, one second and three seconds. This is a targeted mix
adjustment, not a claim that the previous clip failed to play.

Validation passed with 368 C# regressions, 42 Node checks and four runtime
builds (seven existing warnings, zero errors). Evidence is in
`work/a10-audio-alignment/verify-cannon-mix.log`. With the game closed, the
core DLL was backed up and installed at 2026-09-14 03:15:37 UTC. Verified hash:
`ECA1FAB0273F3B88251A686A24896478D1127EAFB040A733D84ADE3ECC0A25D3`.
Installation and rollback details are recorded in
`work/a10-audio-alignment/installed-a10-cannon-mix.json`.
