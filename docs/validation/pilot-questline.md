# Pilot questline validation — 2026-09-06

This report records the initial mandatory-quest implementation. The questline
is now an optional server add-on; see the [add-on validation report](pilot-questline-addon.md)
for the current base/add-on package layout and mode behavior. Counts and
hashes below refer to the earlier candidate.

Source: the TSC 1.3.11 working tree based on `8ea87ff`, including the existing
Pilot Services changes. Target: SPT 4.1.5, WTT CommonLib 3.0.6, UnityToolkit
2.0.2, and Fika 2.4.2. This is a local implementation candidate, not a
published release or a clean-revision build attestation.

## Automated checks

- Full `verify-local.ps1` passed with `SkipTscDeploy=true` throughout.
- **279 regression tests passed**, including native quest data contracts,
  strict completed-quest permissions, profile-bound permits, denied purchases
  and consumption, retained transaction recovery, client state, and Fika
  packet/dispatch contracts.
- **7 dashboard interaction tests passed**. JSON, whitespace, release metadata,
  source hygiene, deploy guards, and synthetic package contract checks passed.
- All four runtime components built against the local SPT 4.1.5 reference set:
  Core, Server, Fika Interop, and Fika bootstrap. The full build reported
  **5 warnings and 0 errors**; warnings concern existing obsolete inventory
  APIs and nullable regression seams.
- The assembled local package layout passed: **178 files, 4 DLLs, 8 bundles**.
  It includes all five new quest/localization/assort files. Carried-forward
  bundles were checked against the existing pinned sizes and SHA-256 values.

CI discovery included new reviewed source through a temporary Git index. The
user's actual index and existing staged work were preserved. No release ZIP
was created; clean-release packaging and attestation guards remain unchanged.

Local evidence is under the workspace's `work/tsc-pilot-questline/`:
`full-validation.log`, `package-validation.log`, and
`package-input-hashes.json`. The local test layout is `package-layout/`.

## Native server checks

**66 native HTTP checks passed, 0 failed** against an isolated official SPT
4.1.5 runtime with WTT 3.0.6 and a disposable profile. Earlier fresh-profile
checks also confirmed Pilot is locked and Open Channel is absent at level 1.

- WTT imported all quest data and locales. At level 5 only Open Channel was
  initially offered. Native handovers consumed exactly the seven requested
  non-FIR parts across the first two quests, recorded completion, unlocked
  Pilot, and exposed the next quest.
- Native reward mail, XP, and reputation matched the configured base rewards.
  Normal monetary reward bonuses remained active (the fixture received
  ₽20,400 from the ₽20,000 base Mechanic reward).
- Back on the Air acceptance mailed one repeater and opened its ₽20,000
  offer. A direct attempt to buy the still-locked phone granted nothing and
  deducted no money; the repeater purchase charged exactly ₽20,000.
- A direct native `QuestComplete` event tested final reward integration:
  one phone, base ₽125,000 reward mail, 4,000 XP, +0.04 Pilot reputation,
  unlocked ₽50,000 replacement purchases, and no replay of the acceptance
  repeater. **This event did not test or simulate field placement/extraction.**
- The authenticated snapshot remained locked before final completion, then
  issued a real permission token after Success. The host verification endpoint
  accepted that token for its bound requester and rejected a different
  requester. Invalid tokens were also rejected; no tokens were logged or saved.

Evidence: `server-smoke/native-summary.json` and
`server-smoke/quest-validation.json` under the same local evidence directory.
The tested server DLL matches the current build, SHA-256
`e7c5da0736b4f02d5a7c4d74221b89830ea0c25cfbaee39d150c678d86e68b43`.
The isolated server was stopped and loopback port 6994 was confirmed closed.

## Gameplay acceptance still required

The [questline checklist](../pilot-questline.md#validation) remains open for
the actual weather-station installation, interrupted placement, survival
after death, vanilla Signal coexistence, and solo/Fika gameplay. Native
server checks cannot prove client quest counters, world interactions, phone
layout, or real headless multiplayer behavior. Do not describe these as
validated until they have been exercised in game.

No live game installation or player profile was modified by this validation.
