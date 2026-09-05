# Tylevo's Tactical Services Control v1.3.10 Public Beta

Unreleased candidate for SPT 4.1.5 / EFT 0.16.9.5.40743. New client and raid
acceptance is pending, and multiplayer on the current SPT/Fika versions
remains untested. The published [v1.3.9 release](https://github.com/Tylevo/Tylevo.TacticalServicesControl/releases/tag/v1.3.9)
continues to target SPT 4.1.4.

This candidate updates TSC's version to 1.3.10 and its declared SPT target
to 4.1.5. The normal build configuration remains `SPT-4.1 Release`. The
target update adds no gameplay changes and retains the themed SIC dashboard,
separate native config Apply/Save actions, revision protection, and atomic
configuration writes introduced in v1.3.9.

The complete candidate archive is named
`Tylevo.TacticalServicesControl-v1.3.10-SPT4.1.5-TESTER.zip`. It retains the
same installation folders and bundles UnityToolkit 2.0.1 with the reviewed
SPT 4.1 plugin/prepatcher rebuild, companion libraries, and license notices.
Arys's permission to bundle the rebuild was confirmed by the maintainer on
September 5, 2026. There is no separate Toolkit or overlay download.

WTT CommonLib 3.0.6 remains separately required, including its client,
server, and serialization prepatcher components. Fika is optional; client
2.4.2 remains the build reference, with its corresponding server component
needed for multiplayer testing. See the [dependency guide](dependencies.md).

Close the game and server before updating. Back up profiles and TSC's
configuration and complete storage directory, update SPT to 4.1.5, then
install the full matched candidate package. Keep one Toolkit installation
in its standard folders. This target update does not reset TSC configuration,
authorizations, payment records, or cargo storage.

The [SPT 4.1.5 release](https://github.com/SP-Tushonka/build/releases/tag/4.1.5)
fixes server validation of older Unity bundles. Inspection of the released
client prepatch DLL confirmed that its separate startup version check still
accepts the rebuilt Toolkit's SPT 4.1 reference and rejects the original
SPT 4.0 reference. That inspection is not a client or raid test.

The candidate passed its full build against verified SPT 4.1.5 references
with four existing warnings and no errors. All 238 regression tests passed,
as did 26 isolated server checks covering version and health, TSC configuration,
the themed dashboard, SIC registration, and both server bundles. See the
[v1.3.10 validation record](validation/v1.3.10.md) and
[4.1.5 reference log](port/SPT-4.1.5-PORT-LOG.md).

Client launch, phone controls, support services in raid, and current Fika
multiplayer remain untested on 4.1.5. Server and build checks do not establish
gameplay compatibility. The v1.3.9 validation record remains historical evidence
for the earlier target.
