# Tylevo's Tactical Services Control v1.3.9 Public Beta

SPT 4.1.4 beta candidate. Fika multiplayer remains untested on the current
SPT/Fika versions.

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
payment records, or cargo storage. Dependencies and the installation layout
remain the same as v1.3.8.

Build and integration results, including the remaining visual and Fika
checks, are recorded in the [validation notes](validation/v1.3.9.md).
