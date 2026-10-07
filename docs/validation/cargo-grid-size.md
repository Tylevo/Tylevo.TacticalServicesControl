# UH-60 cargo grid size validation

Status: **OPEN - in-game acceptance on SPT 4.1.5 has not been run.**

The shared settings are `priorityExfil.gridWidth` (0-10 columns) and
`priorityExfil.gridHeight` (0-30 rows), both whole numbers with default 0.
Zero preserves the native size of that dimension. The grid counts inventory
cells: a 10-by-10 grid has 100 cells, and larger items occupy multiple cells.

## Automated coverage

Verified September 8, 2026 against the current SPT 4.1.5 working source:

- Full solution build passed with deployment disabled.
- Full CI verification passed: 302 C# regression tests and 10 dashboard tests.
  The seven new cargo config cases cover compatibility and editor behavior.
- The real SPT 4.1.5 `ConfigEditorService` passed 27 checks using the final
  server DLL and isolated config files: field serialization, independent
  native defaults, Apply versus Save, invalid bounds, and stale edits across
  SIC and the dashboard service. This was an in-process integration check,
  not a browser or game session.

- Server regression tests cover defaults, dimension bounds, serialization,
  SIC projection, and dashboard schema exposure.
- `node --test tools/tests/dashboard.test.mjs` executes the production
  dashboard script to cover rendering, nested dimension edits, boundary
  normalization, explicit-zero saves and reloads, and unrelated-value retention.

These checks do not establish native EFT screen layout or in-game delivery.

## In-game acceptance

Use a test profile and record the installed client/server build with results.
Run the cargo cases solo and with a requesting human Fika host wherever that
topology is supported by the existing cargo-transfer feature.

| Case | Action and expected result | Status |
| --- | --- | --- |
| Native default | Set both dimensions to 0. Open cargo and confirm the original native grid size. | OPEN |
| Smaller grid | Set 2 columns and 2 rows in the dashboard and Save Config. The next empty cargo screen has 4 cells; item footprints and rotation still work. | OPEN |
| Larger grid | In SIC, open Tactical Services Control > priorityExfil, set GridWidth/ GridHeight to 10/10, and Apply to Runtime. Reopen cargo and verify all 100 cells are usable and the screen remains readable. | OPEN |
| Maximum | Set 10/30 and verify all rows remain accessible without clipping or broken scrolling. | OPEN |
| Independent default | Test 0/10 and 5/0, then return to 0/0. Each zero restores only that axis to its native dimension. | OPEN |
| Existing screen | Change settings while cargo is open. It retains its current grid until it closes; the next opening uses the new values. | OPEN |
| Submitted cargo | Submit distinguishable cargo, shrink the grid, then reopen and submit more cargo. Previously submitted items remain intact and all submissions are delivered once through the existing delivery flow. | OPEN |
| Cancel and forced close | Stage items in a large grid, then cancel or leave the loading zone. All staged cargo returns through EFT's normal flow; after emptying, the next screen honors a smaller setting. | OPEN |
| Persistence and editors | Save to Disk in SIC, restart SPT, and confirm the dashboard shows the same dimensions. Save another size in the dashboard and refresh SIC to confirm parity. | OPEN |
| Scope | Confirm ordinary UH-60 player extraction and native BTR/Transit screens retain their existing behavior. | OPEN |

Attach screenshots for grid dimensions and layout, plus item/delivery evidence
for the submitted-cargo case, before closing acceptance.
