"""Export local debug wireframes from the verified, unmodified TSC UH60 bundle."""
from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path

import numpy as np
from UnityPy.helpers.MeshHelper import MeshHandler

from bundle_reader import Bundle, Slice, UnityPy, safe_output


SOURCE_BUNDLE_SHA256 = "A2336CF5D251C69E7D27E1E5C818E432B6719BE4671F255EBA20E5A22BEF1803"
MAX_EDGES_PER_MESH = 50_000
MAX_TOTAL_EDGES = 100_000
MAX_JSON_BYTES = 16 * 1024 * 1024


def verify_bundle(path):
    path = Path(path).expanduser().resolve(strict=True)
    if not path.is_file():
        raise ValueError("--bundle must be a regular file")
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    if digest.hexdigest().upper() != SOURCE_BUNDLE_SHA256:
        raise ValueError("Unsupported UH60 bundle SHA256; use the verified stock TSC bundle")
    return path


def extract_edges(vertices, triangles, *, sharp_degrees=15.0, max_edges=MAX_EDGES_PER_MESH):
    """Return exact mesh endpoints for boundary, sharp and nonmanifold edges.

    Equal positions are welded exactly, without rounding or moving endpoints.
    Degenerate faces have no defined normal, so retain their nonzero edges
    conservatively rather than concealing a possible collision boundary.
    """
    vertices = np.asarray(vertices, dtype=np.float64)
    triangles = np.asarray(triangles)
    if vertices.ndim != 2 or vertices.shape[1] != 3 or len(vertices) == 0:
        raise ValueError("Expected a nonempty Nx3 vertex array")
    if not np.isfinite(vertices).all():
        raise ValueError("Nonfinite collider vertex")
    if triangles.ndim != 2 or triangles.shape[1] != 3 or len(triangles) == 0:
        raise ValueError("Expected a nonempty Nx3 triangle array")
    if not np.issubdtype(triangles.dtype, np.integer):
        raise ValueError("Triangle indices must be integers")
    if triangles.min() < 0 or triangles.max() >= len(vertices):
        raise ValueError("Triangle index outside vertex array")
    if not math.isfinite(sharp_degrees) or not 0 < sharp_degrees < 180:
        raise ValueError("Sharp angle must be between zero and 180 degrees")
    if not isinstance(max_edges, int) or max_edges < 0:
        raise ValueError("Edge limit must be a nonnegative integer")

    welded, remap = np.unique(vertices, axis=0, return_inverse=True)
    faces = remap[triangles]
    normals = np.cross(welded[faces[:, 1]] - welded[faces[:, 0]],
                       welded[faces[:, 2]] - welded[faces[:, 0]])
    lengths = np.linalg.norm(normals, axis=1)
    if not np.isfinite(lengths).all():
        raise ValueError("Nonfinite collider face normal")
    valid = lengths > 0
    normals[valid] /= lengths[valid, None]
    adjacency = {}
    for face_index, (a, b, c) in enumerate(faces):
        # A collapsed triangle must not count the same edge twice.
        keys = {tuple(sorted((int(i), int(j)))) for i, j in ((a, b), (b, c), (c, a)) if i != j}
        for key in keys:
            adjacency.setdefault(key, []).append(face_index)

    result = []
    threshold = math.cos(math.radians(sharp_degrees))
    for (a, b), neighbors in sorted(adjacency.items()):
        retain = len(neighbors) != 2
        if not retain:
            first, second = neighbors
            retain = (not valid[first] or not valid[second]
                      or float(np.dot(normals[first], normals[second])) < threshold)
        if not retain:
            continue
        if len(result) // 6 >= max_edges:
            raise ValueError("Collider edge limit exceeded; refusing to truncate geometry")
        result.extend(welded[a].tolist())
        result.extend(welded[b].tolist())
    return result


def vector(value):
    return [float(value.x), float(value.y), float(value.z)]


def export(bundle_path):
    bundle = Bundle(bundle_path)
    try:
        environment = UnityPy.Environment(path=str(bundle_path.parent))
        for node in bundle.nodes:
            environment.load_file(Slice(bundle, node), name=node["path"])

        meshes = {}
        collider_count = 0
        for obj in environment.objects:
            if obj.type.name != "MeshCollider":
                continue
            collider_count += 1
            collider = obj.read()
            if collider.m_Convex:
                raise ValueError("Convex collider requires cooked hull geometry; unsupported")
            if not collider.m_Mesh:
                raise ValueError("MeshCollider is missing its mesh")
            mesh = collider.m_Mesh.deref()
            if mesh.type.name != "Mesh":
                raise ValueError("MeshCollider reference is not a mesh")
            meshes[(mesh.assets_file.name, mesh.path_id)] = mesh
        if not meshes or len(meshes) > 100:
            raise ValueError("Missing or excessive collider meshes")

        output = []
        names = set()
        total_edges = 0
        for key, obj in sorted(meshes.items()):
            mesh = obj.read()
            if not mesh.m_Name or mesh.m_Name in names:
                raise ValueError("Collider mesh names must be nonempty and unique")
            names.add(mesh.m_Name)
            submeshes = mesh.m_SubMeshes
            if not submeshes or any(int(sub.topology) != 0 or sub.indexCount <= 0
                                    or sub.indexCount % 3 != 0 for sub in submeshes):
                raise ValueError("Collider mesh contains invalid or non-triangle topology: " + mesh.m_Name)
            handler = MeshHandler(mesh)
            handler.process()
            vertices = np.asarray(handler.m_Vertices, dtype=np.float64)
            if vertices.ndim != 2 or vertices.shape[1] < 3:
                raise ValueError("Invalid collider vertex layout: " + mesh.m_Name)
            vertices = vertices[:, :3]
            if len(vertices) != mesh.m_VertexData.m_VertexCount:
                raise ValueError("Decoded collider vertex count differs from source: " + mesh.m_Name)
            groups = [np.asarray(group) for group in handler.get_triangles()]
            if len(groups) != len(submeshes):
                raise ValueError("Decoded collider submesh count differs from source: " + mesh.m_Name)
            for group, sub in zip(groups, submeshes):
                if group.size != sub.indexCount or not np.issubdtype(group.dtype, np.integer):
                    raise ValueError("Decoded collider indices differ from source: " + mesh.m_Name)
            triangles = np.concatenate([group.reshape(-1, 3) for group in groups], axis=0)

            center = vector(mesh.m_LocalAABB.m_Center)
            size = [2 * value for value in vector(mesh.m_LocalAABB.m_Extent)]
            if not np.isfinite(center + size).all() or any(value < 0 for value in size):
                raise ValueError("Invalid serialized collider bounds: " + mesh.m_Name)
            allowance = .0001 + .0001 * np.abs(np.asarray(size))
            if (np.abs(vertices - center) > np.asarray(size) / 2 + allowance).any():
                raise ValueError("Collider vertices exceed serialized bounds: " + mesh.m_Name)
            edges = extract_edges(vertices, triangles)
            total_edges += len(edges) // 6
            if total_edges > MAX_TOTAL_EDGES:
                raise ValueError("Total collider edge limit exceeded; refusing to truncate geometry")
            output.append(dict(name=mesh.m_Name, vertexCount=len(vertices),
                               boundsCenter=center, boundsSize=size, edges=edges))
            print(f"{mesh.m_Name}: {len(vertices)} vertices, {len(triangles)} triangles, {len(edges) // 6} edges")

        return dict(schema=1, sourceBundleSha256=SOURCE_BUNDLE_SHA256, meshes=output), collider_count
    finally:
        bundle.f.close()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--bundle", required=True, type=Path, help="Existing verified stock uh60_blackhawk.bundle")
    parser.add_argument("--output", required=True, type=Path, help="New local collider-overlay.json; never commit generated geometry")
    args = parser.parse_args()
    source = verify_bundle(args.bundle)
    output = safe_output(source, args.output, directory=False)
    report, colliders = export(source)
    encoded = json.dumps(report, allow_nan=False, separators=(",", ":")).encode("utf8")
    if len(encoded) > MAX_JSON_BYTES:
        raise ValueError("Collider overlay exceeds the runtime JSON size limit")
    output.parent.mkdir(parents=True, exist_ok=True)
    with output.open("xb") as stream:
        stream.write(encoded)
    print(json.dumps(dict(bundle=str(source), bundleSha256=SOURCE_BUNDLE_SHA256,
                          output=str(output), colliders=colliders, meshes=len(report["meshes"]),
                          edges=sum(len(mesh["edges"]) // 6 for mesh in report["meshes"]),
                          bytes=len(encoded)), indent=2))


if __name__ == "__main__":
    main()
