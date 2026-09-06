# Third-Party Notices

Tylevo's Tactical Services Control is a derivative rework of SamSWAT's original Fire Support and SamSWAT's Fire Support - Arys Reloaded by Arys.

## Upstream Fire Support Work

- SamSWAT: original Fire Support concept and implementation.
- Arys: SamSWAT's Fire Support - Arys Reloaded / SPT 4.x basis.

This project is distributed as a separate derivative/continuation project with upstream permission recorded by the maintainer. TSC retains visible credit to SamSWAT and Arys in the README, Forge page, source repository, release archive, and this notice file.

TSC-specific additions include the TerraGroup TSC Uplink, phone authorization
flow, stash/carry payment support, Fika sync work, dashboard configuration, UAV
Recon, Focused Sweep, Cargo Transfer (released `PriorityExfil` compatibility
slot), Double Pass, phone UI assets, and release maintenance.

## Project License

Tylevo's Tactical Services Control is released under Creative Commons Attribution-NonCommercial 4.0 International (CC BY-NC 4.0), matching the upstream Arys Reloaded source license. Arys clarified that the Forge/SPT Hub BY-NC 3.0 listing was due to historical site limitations before the Forge migration.

## UnityToolkit — separate dependency

TSC v1.3.11 requires the standalone **UnityToolkit 2.0.2** update for SPT 4.1.5.
Its binaries and companion libraries are not included in the TSC ZIP. Arys
remains the author and has added Tylevo as a coauthor on the existing Toolkit
page to maintain the update. Both new packages are prepared and unpublished.

The earlier claim of explicit permission to bundle Toolkit in v1.3.9 and
v1.3.10 was a maintainer/assistant misunderstanding. Those packages are held
as archived drafts; see the [corrected permission record](PERMISSIONS.md).
This does not change UnityToolkit's MIT license.

- Author: Arys
- Source: https://github.com/ArysWasTaken/UnityToolkit
- Releases: https://github.com/ArysWasTaken/UnityToolkit/releases
- Update in preparation: 2.0.2, based on the upstream 2.0.1 source.
- License: MIT License, reproduced below from the upstream source.

UnityToolkit's companion libraries retain their respective licenses and
attribution. The standalone Toolkit package carries their complete notices.
TSC's CC BY-NC license does not replace those terms. The MIT notice below is
retained for attribution and historical provenance.

MIT License

Copyright (c) 2025 Arys

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

## Runtime Dependencies Installed Separately

UnityToolkit 2.0.2 and WTT CommonLib 3.0.6 are required separately. WTT's
client, server, and serialization prepatcher components are all needed.
Project Fika is optional for multiplayer testing. None of these dependency
binaries are bundled in the TSC ZIP. The new TSC 1.3.11/Toolkit 2.0.2 pair
still needs its own validation; current Fika multiplayer remains untested.

## Manimal Hacker Mod

The UAV activation phone/use-prefab work includes material adapted from Manimal Hacker Mod.

- Author: danauraborealis
- Source: https://github.com/danauraborealis/ManimalHackerMod
- License: MIT License

MIT License

Copyright (c) 2026 danauraborealis

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

## Accurate Circular Radar / Tyrian Radar Standalone

The UAV radar HUD bundle and blip styling are adapted from Accurate Circular Radar / Tyrian Radar Standalone if those assets/code remain in the release.

- Source: https://github.com/Leonana69/Tyrian-Radar-Standalone
- Forge: https://forge.sp-tarkov.com/mod/1100/accurate-circular-radar
- License listed on Forge: Creative Commons BY 3.0

## Asset And Font Audit

The maintainer has reviewed the redistributed assets for this public beta release. If any additional upstream restrictions are identified later, affected assets should be replaced or removed in a follow-up release.

Bundled assets may include material derived from or compatible with the upstream Fire Support/Arys Reloaded basis. Third-party assets keep their own notices and permission requirements.

## User-Provided Danger Close Ringtone

`project/SamSWAT.FireSupport/LocalOnly/danger-close-ringtone.mp3` was supplied
by the maintainer for local/test use. Local deployment copies it to
`assets/content/ui/phone/audio/danger-close-ringtone.mp3`. Its filename
identifies it as a Dokkaebi-themed sound effect, but no source or redistribution
license has been documented. It is excluded from release archives and must be
replaced with an original or explicitly licensed recording, or have its
redistribution rights confirmed, before any public release.

## Disclaimer

This mod is not affiliated with Battlestate Games, SPT, Project Fika, SamSWAT, Arys, danauraborealis, or the Accurate Circular Radar/Tyrian Radar authors. Credits identify upstream work and compatibility targets only.
