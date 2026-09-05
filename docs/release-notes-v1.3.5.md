# Tylevo's Tactical Services Control v1.3.5 Public Beta

Targets SPT 4.1.4 / EFT 0.16.9.5.40743 with the existing WTT and UnityToolkit
dependencies.

The pre-raid store and native phone now use the supplied redesigned tactical
icon pack. The 512 x 512 PNG artwork has stronger silhouettes and ivory,
amber, and olive accents. Its dark rounded tiles are baked into the supplied
PNGs and are retained; these are opaque images, not transparent cutouts.
Normal and selected icon slots use the same original colors. The existing
selection borders, labels, and controls continue to identify the selection.

Extraction uses the helicopter with a pickup arrow. Cargo Transfer uses the
helicopter carrying a crate, also used for the UH-60 Pilot delivery avatar.
Service and status icons share the new artwork. Runtime code, layouts,
payments, controls, animations, and ballistics are unchanged.

The expected archive is
`Tylevo.TacticalServicesControl-v1.3.5-SPT4.1.4-TESTER.zip`. Install the full
package, including its icon assets and four matched TSC DLLs. Restart EFT
after replacing icons; client sprites are cached for the process lifetime.
The Pilot avatar refreshes when the SPT server restarts.

Build, asset mapping, package, and installation results are recorded in the
candidate's external evidence sidecars. Visual previews check the supplied
art at UI sizes. Confirm the final appearance in the store and on the phone
in game; browser previews do not reproduce the phone's render texture,
lighting, or perspective.

## Asset mapping

The two active folders, `neutral_512` and `amber_512`, each contain twenty
updated icons under the existing package paths. Matching filenames copy
directly from `redesigned_tactical_icon_pack.zip`, with these exceptions:

| Supplied image | Runtime filename | Meaning |
| --- | --- | --- |
| `extraction_pickup.png` | `extraction.png` | UH-60 extraction |
| `cargo_pickup.png` | `priority_exfil.png` | UH-60 Cargo Transfer and Pilot avatar |
| `cargo_pickup.png` | `supply_evac.png` | Existing cargo asset alias |

The supplied `priority_exfil.png` depicts a timed pickup and is not selected
for the current cargo service. The unused legacy `mask_512` assets remain
unchanged. All imported PNG bytes are preserved without image processing.
