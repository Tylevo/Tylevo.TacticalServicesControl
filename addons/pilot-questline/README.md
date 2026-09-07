# Pilot Questline add-on

Optional progression for **Tactical Services Control 1.3.11 on SPT 4.1.5**. Install the matching main TSC mod first, including its normal dependencies. This add-on contains server data only; it does not require an additional client download or DLL. For Fika, install it on the shared SPT server. All players keep the same main TSC client.

## Install

1. Stop the SPT server.
2. Extract the add-on archive and merge its `SPT_Runtime` folder into the game root containing the existing `SPT_Runtime` folder.
3. Confirm that `addon.json` is at `SPT_Runtime/user/mods/Tylevo.TacticalServicesControl/addons/pilot-questline/addon.json`.
4. Restart the server.

A fresh profile is recommended for the intended introduction. Existing profiles can retain an already visible Pilot; the server still enforces the quest requirements for TSC access and replacement phone purchases. There is no profile migration or automatic quest completion.

## Progression

| Quest | Required work | Unlock |
|---|---|---|
| Open Channel — Mechanic, level 5 | Hand over 2 Wires and 2 Capacitors. | Pilot and his repair quest. |
| Some Assembly Required — Pilot | Hand over 1 Broken GPhone, 1 Electronic components, and 1 Screwdriver. | Quest complete; Back on the Air becomes available. |
| Back on the Air — Pilot | Install the supplied Radio repeater at Shoreline's weather-station antenna, survive and extract, then complete the quest with Pilot. | 1 working TSC Uplink, replacement phone purchases, and all enabled services. |

Both handover quests accept ordinary items without a found-in-raid requirement. Pilot holds the assembled handset until the ground connection is restored. Repeater placement takes 40 seconds. A completed installation persists through death, so a later successful Shoreline extraction can finish the survival objective. Run-through, death, MIA, and disconnect do not qualify.

Pilot supplies one repeater when you accept Back on the Air. Replacement repeaters cost ₽20,000 after acceptance and remain available afterward. Replacement TSC phones cost ₽50,000 after the final quest is completed. Service prices and limits still follow the main mod's server settings. A borrowed phone does not grant access before completion.

## Remove

Back up your profile before removing the add-on. Stop the server, remove the entire `SPT_Runtime/user/mods/Tylevo.TacticalServicesControl/addons/pilot-questline` folder, and restart. The main mod returns to immediate Pilot and service access, with the phone available for ₽50,000. Removing only the manifest or some data files is an incomplete installation and will not disable progression safely.

SPT removes quest records when their definitions are missing. Reinstalling the add-on after playing without it can therefore require completing the introduction again; restore a compatible backed-up profile if you want to retain that progress. The add-on does not migrate or reconstruct deleted quest history. Existing phone items are not removed. TSC phones have no normal random loot spawns in either installation mode.
