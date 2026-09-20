# Phone and service fixes

Candidate changes for the reported purchase, special-slot, deployment-flow,
and phone-readability issues. Runtime acceptance remains pending.

## Automated validation

September 20, 2026: `tools/verify-local.ps1` passed for the publishing branch
using the SPT 4.1.5 reference assemblies with deployment disabled. This branch
excludes the separate A-10 audio and extended-burst changes:

- 382 C# regression tests passed, including slot/filter preservation, payment
  migration and replay, nonpersistent payments, prepaid selection, phone
  cancellation/handoff, and zoom policy cases.
- 44 dashboard and preset tests passed, along with the repository's package
  and source-contract checks.
- Client, server, Fika, and Fika interop assemblies built with zero errors
  and six existing warnings.

These checks do not substitute for the in-game acceptance steps below.

## Expected behavior

- Service purchases use stash funds. Existing carried/hybrid wallet settings
  and imported presets normalize to stash without changing prices or credits.
  Carried-payment code and enum options are removed. The old client config key
  is deleted on load; editors and newly exported presets have no source setting.
  Historical source names are accepted only when reading old presets or receipts.
- The phone fits all special slots on supported pocket templates, including
  expanded and custom layouts. Existing slots, other allowed items, and saved
  inventory placement are preserved. Stock pockets retain the extra fourth slot.
- **F12 > TerraGroup Phone > Deploy after phone purchase** is off by default.
  When enabled, a confirmed purchase finishes its animation and continues into
  the existing deployment flow. Designation remains required for A-10 and UH-60;
  UAV services activate through their normal request flow.
- **Automatic deploy phone zoom** and **Deploy phone zoom FOV** independently
  control the upright deployment view. Defaults are enabled and 45 degrees.
  The original raid FOV returns before designation starts. Radar keeps raid FOV.

## Runtime acceptance

1. Load an older carried-money config. Check that SIC and the dashboard show
   stash payments and retained prices. Import an old carried-money preset too.
2. With money only in the stash, buy once from Pilot Services and once from the
   raid phone. Confirm one debit per purchase, the correct balance/credit count,
   and persistence after reconnecting. With insufficient stash funds and enough
   carried money, confirm the purchase is denied without removing carried money.
3. Repeat with USD/EUR and one GP/BTC service, including funds in stash containers.
4. Test the phone in stock slots 1–4 and in all six SPT Tweaks special slots.
   Existing items must stay put after restart. Check purchase, deploy, radar,
   and Danger Close answering from each special slot. Test SVM unrestricted slots.
5. With auto-deploy off, a purchase must leave its credit for later. With it on,
   test each of the six services, including Hybrid with cash preferred. Confirm
   one debit and one consumed credit, no extra UAV activation-phone animation,
   and the normal targeting/cooldown/availability rules.
6. Cancel before payment, cancel after payment but before deployment, fail a
   payment, or reopen the phone during handoff. No abandoned action should
   deploy later. Any completed purchase remains available when not deployed.
7. Open K with purchase zoom disabled and deployment zoom enabled. Vary its FOV,
   sprint/stop, close/reopen quickly, and cancel. Confirm smooth zoom and exact
   restoration; no camera shift after designation begins. Also test the longest
   zoom-out duration and pausing during the handoff.
8. Repeat purchases and deployment as a Fika client and on a dedicated host.
   Confirm the existing service restrictions, payment ownership, and one-shot
   dispatch remain intact.
9. With persistent authorizations disabled, test Direct Radial and Hybrid stash
   payments, a rejected dispatch followed by retry, and the per-raid request
   limit. Confirm a refunded paid credit can be reused without another debit.

The screenshot's `SYNC` message also covers missing authenticated balance or
ledger data. Those failures remain blocked instead of inventing a balance or
credit; separate messages now identify which response data is missing.
