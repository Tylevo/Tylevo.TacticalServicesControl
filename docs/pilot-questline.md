# Optional Pilot Questline add-on

The main TSC download opens Pilot immediately. Buy the Uplink for ₽50,000 and
use configured services at their normal prices and authorization limits.
Neither mode adds phones to container, loose, or bot loot.

Install the separate Pilot Questline add-on to earn access through three
quests. Mechanic restores contact with Pilot, Pilot assembles the handset,
and the player brings its Shoreline ground relay online. The introduction is
designed for fresh profiles.

## Install or remove the add-on

Install the main TSC download and its dependencies first. With the game and
server stopped, extract the matching Pilot Questline add-on ZIP into your SPT
root and merge its `SPT_Runtime` folder. The content lands in
`SPT_Runtime/user/mods/Tylevo.TacticalServicesControl/addons/pilot-questline/`.
Start the server again to enable progression. Fika uses this one server choice
for everyone; clients keep the same main TSC download.

To return to immediate access, stop the game and server, back up your profile
and TSC storage if you may restore this progression later, remove only the
`addons/pilot-questline` folder above, and restart. Main TSC updates do not
remove an installed add-on. Use the add-on that matches your TSC/SPT versions.
An incomplete or incompatible add-on stops initialization instead of silently
opening access. There is no client setting that bypasses server progression.

Installing the add-on does not erase existing phones, authorizations, or
transaction history. A profile that already met Pilot can retain his
visibility, but phone purchases and manual service access require the final
quest's completion while the add-on is installed. A fresh profile gives the
intended introduction order. **SPT removes the add-on's quest history when its
definitions are absent**, including completed quests. After running without
the add-on, reinstalling it starts progression again unless you restore a
matching backup. Already awarded items remain; TSC adds no migration or
automatic quest completion.

## Questline

| Quest | Requirements | Completion rewards |
| --- | --- | --- |
| **Open Channel** — Mechanic | Level 5; hand over 2 Wires and 2 Capacitors. | Pilot access, 2,500 XP, ₽20,000, +0.01 Mechanic reputation. |
| **Some Assembly Required** — Pilot | Complete Open Channel; hand over 1 Broken GPhone, 1 Electronic components, and 1 Screwdriver. | 3,000 XP, ₽25,000, +0.03 Pilot reputation. |
| **Back on the Air** — Pilot | Complete Some Assembly Required; install the supplied Radio repeater at Shoreline's weather-station antenna for 40 seconds, then survive Shoreline and report to Pilot. | 1 working TSC Uplink, replacement phone purchases, access to configured services, 4,000 XP, ₽125,000, +0.04 Pilot reputation. |

Listed rewards are base values; normal game reward bonuses still apply.

The first two quests accept items without a found-in-raid requirement. Each
ends when its handovers are completed and the player claims completion; the
second quest has no field objective. Pilot keeps the assembled handset until
Back on the Air is completed.

Back on the Air supplies one Radio repeater on acceptance. Pilot then sells
replacements for ₽20,000, including after completion. Completed installation
persists through death; a later successful Shoreline extraction can finish
the survival objective. Run-through, death, MIA, and disconnect do not count.
An interrupted installation must be retried.

The final quest awards the normal working phone once. Replacement phones cost
₽50,000 at Pilot's loyalty level 1, up to five per restock. Working phones no
longer enter random loot through TSC. Keep the device in its dedicated fourth
special slot or carry it in your inventory.

All globally enabled services become available together. Their prices,
authorization limits, payment sources, and cargo restrictions still apply.
The final cash reward covers one UAV Recon at the default ₽125,000 price;
there are no automatic authorization credits. Losing or lending the phone
does not change quest progress or grant another player request permission.

## Multiplayer and refresh

Only the requester needs device permission. A locked host can execute support
for an unlocked requester, and players may board an authorized extraction
without completing these quests. Ambient aircraft events are independent of
this introduction.

Install matching TSC server, Core, and Fika components for every participant.
Manual support uses service protocol 2; older peers are rejected before new
payment. The server applies its selected access mode and issues a temporary
profile permission token. Refresh TSC state after a server restart or a stale
permission error. Tokens are not currency or service authorizations.

## Implementation references

The quest IDs are `66f51f3a0000000000000b01`,
`66f51f3a0000000000000b02`, and `66f51f3a0000000000000b03`, in order.
The base archive contains no quest or repeater stock data. The optional
`addons/pilot-questline/` content supplies its version manifest, quests,
locales, and repeater assortment. WTT 3.0.6 registers that content through its
native loaders only when the add-on is installed. Back on the Air
uses Shoreline's existing `place_SIGNAL_03_1` zone with Radio repeater
`63a0b2eabea67a6d93009e52`; it does not modify vanilla Signal quests.
Native final-quest Success is the permission source. AvailableForFinish
does not unlock requests or purchases.

## Validation

Build and automated evidence are recorded in the
[add-on validation report](validation/pilot-questline-addon.md), with original
quest handling evidence in the [questline validation report](validation/pilot-questline.md).
The following require actual gameplay acceptance before publication:

- [ ] A fresh level-5 PMC sees Open Channel while Pilot is locked; completing
  it unlocks Pilot and the repair quest.
- [ ] Without the add-on, a fresh PMC can buy the ₽50,000 phone and use
  configured services without any introduction quests.
- [ ] Installing and removing the add-on with the server stopped selects the
  expected mode after restart, including previously used profiles.
- [ ] Partial handovers persist, accept non-FIR items, and consume only the
  requested quantities; the repair quest finishes entirely through handover.
- [ ] The weather-station antenna offers the 40-second repeater installation
  independently of vanilla Signal progression, including when both are active.
- [ ] Acceptance grants one repeater once; replacements cost ₽20,000.
- [ ] Interrupted placement can be retried. Completed placement survives death;
  only a subsequent Survived Shoreline result finishes the survival objective.
- [ ] Completing Back on the Air grants one phone once, exposes ₽50,000
  replacements, and enables configured services with the normal prices.
- [ ] Borrowed devices and AvailableForFinish status cannot bypass the gate
  through phone, radial, cash, or authorization dispatch.
- [ ] Solo and Fika human/headless hosts enforce the requester's permission;
  locked passengers can board; mixed protocol versions fail before payment.
- [ ] Profile changes and server restarts cannot reuse another profile's
  permission; purchase recovery, commit, and refund remain available.

Later service branches, side quests, migration, and a sandbox switch are
outside this introduction.
