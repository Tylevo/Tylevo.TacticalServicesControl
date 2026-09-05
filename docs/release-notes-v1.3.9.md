# Tylevo's Tactical Services Control v1.3.9 Public Beta

Unreleased SPT 4.1.4 beta candidate. SPT 4.1.5 and Fika multiplayer on the current
SPT/Fika versions have not been tested.

UnityToolkit 2.0.1 is now included in the TSC ZIP, with its plugin and
prepatcher rebuilt against SPT 4.1, companion libraries, and license notices.
There is no separate Toolkit or compatibility-overlay download. The maintainer
confirmed Arys's explicit permission to bundle the rebuilt Toolkit on
September 5, 2026. UnityToolkit remains under MIT; companion libraries keep
their own licenses.

Install WTT CommonLib separately, including its client, server, and
serialization prepatcher components. Fika is optional and also installed
separately. Extract the complete candidate ZIP into the SPT 4.1.4 root with
the game and server stopped, replacing existing Toolkit files in their
standard folders when prompted. Keep one Toolkit installation. See the
[dependency guide](dependencies.md) for the file layout.

The SIC home page now has a **Tactical Services Control** entry under Mod pages.
It opens the existing TerraGroup dashboard, with its amber and green palette,
service cards, typography, and controls. Links in the sidebar return to SIC
or open its standard config editor.

The native **Config Editor > Mods > Tactical Services Control** entry remains
available for gameplay settings. Its Apply and Save actions now have separate
effects: Apply changes runtime settings, Save writes the file, and Load Disk
reads saved values. See [dashboard instructions](dashboard.md) for the sequence
and when to refresh the editor after saving.

Dashboard saves now check the revision, so an older browser session cannot
overwrite newer SIC changes. Each native editor gets a separate snapshot.
Config writes replace the file atomically before changing runtime state;
failed writes and invalid disk reloads preserve the active configuration.
Unsaved dashboard changes survive save failures, and reload/navigation warns
before discarding them. Dashboard operations cannot overlap.

This update does not move or reset player profiles, purchased authorizations,
payment records, or cargo storage. The TSC client and server folders remain
the same as v1.3.8; the ZIP now also supplies the standard
`BepInEx/plugins/UnityToolkit/` and `BepInEx/patchers/UnityToolkit/` folders.
The published v1.3.8 package is unchanged and still uses its earlier separate
Toolkit and overlay installation steps.

Build and integration results, including the remaining visual and Fika
checks, are recorded in the [validation notes](validation/v1.3.9.md).
