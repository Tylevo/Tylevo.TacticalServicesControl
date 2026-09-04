# TSC Uplink special slot

TSC adds a data-driven `SpecialSlot4` to both SPT 4.1.2 player pockets
templates (standard and The Unheard Edition). The slot accepts only the
TerraGroup TSC Uplink. The Uplink is removed from the filters for
`SpecialSlot1` through `SpecialSlot3` on those templates.

The slot does not grant, force-equip, or lock the Uplink. Players still obtain
the device normally and may remove it normally. Manual TSC services continue
to discover a carried Uplink independently of this dedicated slot.

At server startup, after SPT has loaded profiles and normal mod item
registration has completed, TSC reasserts the exclusive slot filter and then
performs an idempotent legacy migration. A single Uplink directly equipped on
supported pockets in `SpecialSlot1`, `SpecialSlot2`, or `SpecialSlot3` moves to `SpecialSlot4` and
the profile is saved. Uplinks in the stash, backpacks, or other containers are
never auto-equipped. If the destination is occupied, or multiple legacy
Uplinks make the move ambiguous, TSC leaves every item in place and logs a
warning. A failed profile save rolls the in-memory move back to its original
slot.

No lost-on-death configuration is changed. SPT 4.1.2 classifies slot IDs that
contain `SpecialSlot` under `specialSlotItems`; with the stock
`specialSlotItems: false` setting, an Uplink in `SpecialSlot4` is retained on
death just like items in the three stock special slots.
