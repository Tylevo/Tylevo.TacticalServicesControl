# Pilot shop expansion (development)

Pilot's Trading tab now has a small selection of aircrew accessories and survival supplies alongside the Uplink. These are existing SPT 4.1.5 items with their normal behavior and appearance.

| Item | Price | Per restock |
| --- | ---: | ---: |
| RayBench Aviator glasses | ₽9,000 | 2 |
| RayBench Aviator glasses, green lenses | ₽15,000 | 1 |
| OPSMEN Earmor M32 headset | ₽32,000 | 2 |
| CAT tourniquet | ₽9,000 | 3 |
| Leatherman Multitool | ₽16,000 | 2 |
| Green RSP-30 signal cartridge | ₽8,000 | 2 |
| Hunting matches | ₽5,000 | 2 |
| EYE MK.2 compass | ₽210,000 | 1 |

All eight offers use Pilot's existing loyalty level 1. The limits are per player per restock. The compass stays close to its native retail price; the glasses are an intentional early cosmetic option. Buying Pilot's shop stock does not use TSC service authorizations or the dashboard's service-currency settings.

Without the questline add-on, the shop is available immediately. With the add-on, meet Pilot through Open Channel to access his ordinary stock. The Uplink and replacement repeater retain their existing quest requirements and prices. No add-on data changes are required for this stock expansion.

The green flare remains the ordinary game item; it does not call TSC support. Glasses, headset, navigation tools and survival supplies retain their native equipment, medical, barter and extraction behavior.

The existing `jaeger_uav_uplink.json` filename is retained so an overlay update replaces the old installed assortment. It still registers only Pilot, preserves the Uplink offer ID, and gives each new item a stable, separate purchase-limit ID. No vanilla item template or other trader inventory is changed.

Verification passed against a fresh disposable SPT 4.1.5 runtime using the published 1.3.13 server binary with only this assortment overlaid: all eight native item purchases, exact prices, exhausted personal purchase limits and the published 1.3.12 add-on's existing locks. The development source also passed the repository's CI checks. No live installation or release archive was changed.

SPT checks nonstackable bulk purchases one item at a time. A direct request for two Aviators with only one purchase remaining added one item, rejected the other and skipped payment. This upstream transaction behavior is recorded in the development evidence; the stock uses ordinary native restrictions and adds no separate checkout path.

Release preparation still needs the normal version update. If the next main version reuses add-on 1.3.12, extend and test the explicit compatibility policy at that time; its current exception names main 1.3.13 only.
