from __future__ import annotations

import argparse
import hashlib
import sys
import zipfile
from pathlib import Path


ENTRY = "SPT/user/mods/Tylevo.TacticalServicesControl/bundles/raidops/uav_uplink_container.bundle"
SOURCE_LENGTH = 1_181_738
SOURCE_SHA256 = "F496805B01B0CBEC699435DE0B651BFED9F35595BE34312398A91DB600B66425"
OUTPUT_LENGTH = 1_167_863
OUTPUT_SHA256 = "8C9F8D8878076D4FFCB2687D62609F606552B3E9F3529FBE584DF79E43365861"
USABLE_HANDS_PREFAB = 3227168475352522817
ANIMATOR_STATIC_DATA = 1229230505816891976
ANIMATOR_CONTROLLER = 7971780929744478985
OLD_DEFAULT_STATES = [0, 1144313067, 2081823275]
NEW_DEFAULT_STATES = [0, 1372578019]
OUT_USE_HASH = 1865652397


def require(condition: bool, message: str) -> None:
    if not condition:
        raise RuntimeError(message)


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest().upper()


def unpack(raw: bytes):
    environment = UnityPy.load(raw)
    require(len(environment.files) == 1, "expected one outer Unity bundle")
    bundle = next(iter(environment.files.values()))
    serialized_files = [item for item in bundle.files.values() if hasattr(item, "objects")]
    require(len(serialized_files) == 1, "expected one serialized file")
    return environment, bundle, serialized_files[0]


def validate(serialized_file, repaired: bool) -> None:
    require(len(serialized_file.objects) == 193, "unexpected object count")
    prefab = serialized_file.objects[USABLE_HANDS_PREFAB].read_typetree()
    require(
        prefab["_originalAnimatorController"]
        == {"m_FileID": 0, "m_PathID": ANIMATOR_CONTROLLER},
        "unexpected animator controller pointer",
    )
    require(
        prefab["LayersDefaultStates"]
        == (NEW_DEFAULT_STATES if repaired else OLD_DEFAULT_STATES),
        "unexpected animator layer defaults",
    )

    controller = serialized_file.objects[ANIMATOR_CONTROLLER].read_typetree()["m_Controller"]
    require(
        len(controller["m_LayerArray"]) == 2
        and len(controller["m_StateMachineArray"]) == 2,
        "animator controller is not exactly two-layer",
    )
    hands = controller["m_StateMachineArray"][1]["data"]
    require(
        hands["m_DefaultState"] == 0
        and hands["m_StateConstantArray"][0]["data"]["m_NameID"] == 1372578019,
        "Hands default state is not Spawn",
    )

    static_data = serialized_file.objects[ANIMATOR_STATIC_DATA].read_typetree()
    event = static_data["_stateHashToEventsCollection"][0]["_animationEvents"][0]
    require(event["_functionName"] == "OutUse", "unexpected target animation event")
    require(
        event["_functionNameHash"] == (OUT_USE_HASH if repaired else 0),
        "unexpected OutUse function hash",
    )


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Validate and repair the pinned TSC Uplink bundle for SPT 4.1.2."
    )
    parser.add_argument("--source-zip", required=True, type=Path)
    parser.add_argument("--unitypy-dir", required=True, type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--write", action="store_true")
    args = parser.parse_args()
    require(not args.write or args.output is not None, "--write requires --output")

    require(
        (args.unitypy_dir / "unitypy-1.25.0.dist-info").is_dir(),
        "requires the pinned UnityPy 1.25.0 dependency directory",
    )
    sys.path.insert(0, str(args.unitypy_dir.resolve()))
    global UnityPy
    import UnityPy  # type: ignore

    with zipfile.ZipFile(args.source_zip, "r") as archive:
        source = archive.read(ENTRY)
    require(
        len(source) == SOURCE_LENGTH and sha256(source) == SOURCE_SHA256,
        "source bundle pin mismatch",
    )

    _, source_bundle, source_file = unpack(source)
    validate(source_file, repaired=False)
    original_objects = {
        path_id: obj.get_raw_data() for path_id, obj in source_file.objects.items()
    }
    original_resources = {
        name: bytes(item.bytes)
        for name, item in source_bundle.files.items()
        if not hasattr(item, "objects")
    }

    prefab = source_file.objects[USABLE_HANDS_PREFAB].read_typetree()
    prefab["LayersDefaultStates"] = NEW_DEFAULT_STATES
    source_file.objects[USABLE_HANDS_PREFAB].save_typetree(prefab)
    static_data = source_file.objects[ANIMATOR_STATIC_DATA].read_typetree()
    static_data["_stateHashToEventsCollection"][0]["_animationEvents"][0][
        "_functionNameHash"
    ] = OUT_USE_HASH
    source_file.objects[ANIMATOR_STATIC_DATA].save_typetree(static_data)
    candidate = source_bundle.save(packer="original")
    require(
        len(candidate) == OUTPUT_LENGTH and sha256(candidate) == OUTPUT_SHA256,
        "candidate bundle pin mismatch",
    )

    _, candidate_bundle, candidate_file = unpack(candidate)
    validate(candidate_file, repaired=True)
    changed = {
        path_id
        for path_id, raw in original_objects.items()
        if raw != candidate_file.objects[path_id].get_raw_data()
    }
    require(
        changed == {USABLE_HANDS_PREFAB, ANIMATOR_STATIC_DATA},
        "objects outside the two approved targets changed",
    )
    require(
        original_resources
        == {
            name: bytes(item.bytes)
            for name, item in candidate_bundle.files.items()
            if not hasattr(item, "objects")
        },
        "streamed resource bytes changed",
    )

    if args.write:
        output = args.output.resolve()
        require(not output.exists(), f"refusing to overwrite {output}")
        output.parent.mkdir(parents=True, exist_ok=True)
        with output.open("xb") as stream:
            stream.write(candidate)
        require(output.read_bytes() == candidate, "written candidate verification failed")
        print(f"WROTE {output}  {len(candidate)} bytes  SHA256={OUTPUT_SHA256}")
    else:
        print(
            f"VALIDATED IN MEMORY  {len(candidate)} bytes  SHA256={OUTPUT_SHA256}"
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
