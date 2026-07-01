# Internal Naming

The public mod name is **Tylevo's Tactical Services Control**. Player-facing docs, install folders, dashboard labels, and in-game naming should use **TSC** or **Tactical Services Control**.

Some internal names intentionally remain tied to the original Fire Support lineage:

- Namespaces and assembly identities may remain `SamSWAT.FireSupport.ArysReloaded` for Unity bundle compatibility.
- Some bundle paths may continue to use `raidops`, including existing phone item bundle paths, when changing them could break already-working item binding.
- Item template IDs and server config migration paths should not be renamed without a separate migration plan.

This is intentional compatibility debt for the 0.9.0 public beta. Public/user-facing names should be TSC, while low-level internal names should only change when the compatibility risk is understood and tested.
