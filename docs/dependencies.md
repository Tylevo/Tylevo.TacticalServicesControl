# Installing TSC for SPT 4.1.5

**TSC v1.3.11 for SPT 4.1.5 is prepared but not yet published.** It requires
**UnityToolkit 2.0.2**, installed separately. That standalone Toolkit update
is also being prepared and is not yet published. Use the
[UnityToolkit project](https://forge.sp-tarkov.com/mod/1426/unitytoolkit)
for release availability; do not substitute the older 2.0.1 binary.

Install **WTT Client CommonLib and WTT Server CommonLib 3.0.6** separately from
the [official WTT release](https://github.com/WelcomeToThursday/WTT-CommonLib/releases/tag/v3.0.6),
including its serialization prepatcher. Fika is optional; solo play does not
need it.

## Coming from SPT 4.0.13

Install SPT 4.1.5 in a **new folder** and create a **fresh profile**. SPT lists
profile compatibility from 4.1.x onward and does not support 4.0.x mods on
4.1.5. Do not extract this update over your 4.0.13 installation. Keep the old
installation and its profiles as your backup. See the
[SPT 4.1.5 release guidance](https://github.com/SP-Tushonka/build/releases/tag/4.1.5).

Install the current dependencies and TSC package in the new installation.
Let TSC create fresh `config/` and `storage/` directories for your new profile.
You can use your old settings as a reference when configuring the dashboard,
but do not copy old authorization ledgers or cargo records into the new
profile's installation.

## Installation

1. Close the game, launcher, and SPT server.
2. Install SPT 4.1.5 and WTT CommonLib, including all of WTT's required components.
3. Once published, extract the **complete UnityToolkit 2.0.2 package** into
   the SPT root. It supplies its plugin, prepatcher, companion libraries,
   `Assemblies.jsonc`, and license notices. No additional overlay is needed.
4. Extract the **full TSC v1.3.11 ZIP**, once published, into the same SPT root
   so its `BepInEx` and `SPT_Runtime` folders merge with the existing folders.
   GitHub's automatic source archives are not installable mod packages.
5. If UnityToolkit is already installed, replace its files in the standard
   folders when prompted. Keep one installation; do not leave duplicate
   plugin or prepatcher copies in other folders.
6. Optionally extract the matching **Pilot Questline add-on ZIP** into the
   same root. It supplies server content only. Without it, Pilot sells the
   ₽50,000 Uplink immediately and services have no introduction requirement.
7. Start the SPT server, then the launcher and game.

After installing both packages, check for these folders directly inside your
SPT installation. TSC supplies the first two; UnityToolkit supplies the last two:

```text
BepInEx/plugins/Tylevo.TacticalServicesControl/
SPT_Runtime/user/mods/Tylevo.TacticalServicesControl/
BepInEx/plugins/UnityToolkit/
BepInEx/patchers/UnityToolkit/
```

Install all four TSC DLLs and their assets together. TSC replaces the old
SamSWAT Fire Support and Arys Reloaded packages; do not install them alongside
it. If updating TSC, remove older duplicate TSC DLLs from other mod folders.

The optional add-on lives at
`SPT_Runtime/user/mods/Tylevo.TacticalServicesControl/addons/pilot-questline/`.
For Fika, install it on the shared SPT server; everyone uses the same main
client download. The server chooses progression at startup. See the
[add-on guide](pilot-questline.md) for quest details and removal instructions.
Updating the main mod preserves an installed add-on, which must match the
current TSC/SPT versions.

## Updating an existing SPT 4.1.x installation

SPT supports patch updates within 4.1.x. Follow its
[update guide](https://wiki.sp-tushonka.com/en/SPT_4x/Updating_SPT) and back up
your profiles before updating SPT to 4.1.5.

With the server stopped, also back up the TSC mod files and its `config/` and
**complete `storage/` directory**. Storage contains purchased authorizations,
payment recovery records, and cargo delivery state. Extract the current TSC
package over its existing folders. The ZIP contains no profiles, mutable
configuration, storage, or admin tokens, so it preserves that existing state.
TSC migrates its supported configuration and ledger formats when the server
starts. A rollback needs matching backups of the mod files and saved state.

If your existing 4.1.x setup still uses
`SPT/user/mods/Tylevo.TacticalServicesControl/`, move its backed-up `config/`
and complete `storage/` directories into
`SPT_Runtime/user/mods/Tylevo.TacticalServicesControl/` before the first server
start. Keep the new release's DLLs and assets. If both locations contain state,
restore one consistent backup instead of merging ledgers. TSC does not read
the old folder automatically. This preservation advice applies to the same
compatible profile; a fresh profile should start with fresh TSC storage.

## Optional multiplayer

**Fika support has not been tested on the current SPT/Fika versions.** Fika
client 2.4.2 is the compilation reference for this release, not a claim that
multiplayer is verified. Install a matching Fika client/server pair and the
same TSC package on every participating machine if you are testing it. See
the [Fika guide](fika.md) and [known issues](known-issues.md) for restrictions.

## About UnityToolkit 2.0.2

Arys remains the author of UnityToolkit and has added Tylevo as a coauthor on
its existing Forge page to maintain the SPT update. Version 2.0.2 distinguishes
that standalone update from the original 2.0.1 release. TSC v1.3.11 does not
redistribute the Toolkit binaries or companion libraries inside its ZIP.

The earlier bundled v1.3.9 and v1.3.10 test releases have been withdrawn and
held as archived drafts. Use the separate Toolkit package for this update;
see the [permission record](../PERMISSIONS.md) for the distribution correction.

UnityToolkit remains under MIT; its companion libraries retain their own
licenses. See [permissions](../PERMISSIONS.md),
[third-party notices](../THIRD_PARTY_NOTICES.md), and the
[source and packaging guide](../tools/dependencies/unitytoolkit/README.md).

SPT 4.1.5 fixes server validation of older Unity asset bundles. Its separate
[client startup check](https://github.com/SP-Tushonka/modules/blob/4.1.5/SPT.PrePatch/PluginValidator.cs)
still requires compatible SPT assembly references, which is why the rebuilt
Toolkit update is required.

## Historical v1.3.8 installation: official Toolkit plus overlay

The old separate-download instructions are retained in the
[archived dependency guide](archive/dependencies-v1.3.9.md). They apply only to
older packages. Use the installation steps above for the current release.
