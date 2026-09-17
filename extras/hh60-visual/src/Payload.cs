using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;

namespace TscHh60Visual
{
    // Pure managed validation: this file deliberately has no Unity reference. All byte
    // lengths, hashes, dimensions, floats and indices are checked before native calls.
    public sealed class SceneData
    {
        public int schema;
        public string rootName;
        public NodeData[] nodes;
        public MeshData[] meshes;
        public TextureData[] textures;
        public MaterialData[] materials;
        public RendererData[] renderers;
        public FileData[] files;
        public object[] crew;
    }
    public sealed class NodeData { public string id, name, parent; public float[] position, rotation, scale; }
    public sealed class MeshData
    {
        public string id, name, file;
        public int vertexCount, indexCount;
        public SubmeshData[] submeshes;
        [JsonIgnore] public float[] positions, normals, uv0;
        [JsonIgnore] public int[] indices;
    }
    public sealed class SubmeshData { public int offset, count; }
    public sealed class TextureData
    {
        public string id, name, file, format;
        public string dimension = "2D";
        public int[] faceMipSizes;
        public int width, height, mipCount;
        public bool linear;
        [JsonIgnore] public int formatValue;
        [JsonIgnore] public byte[] bytes;
    }
    public sealed class MaterialData
    {
        public string id, name, shader;
        public MaterialTexture[] textures;
        public FloatData[] floats;
        public ColorData[] colors;
        public string[] keywords;
        public int renderQueue = -1;
    }
    public sealed class MaterialTexture { public string property, texture; public float[] scale, offset; }
    public sealed class FloatData { public string name; public float value; }
    public sealed class ColorData { public string name; public float[] value; }
    public sealed class RendererData
    {
        public string node, mesh;
        public string[] materials;
        public bool enabled = true;
        public bool receiveShadows = true;
        public int shadowCastingMode = 1;
    }
    public sealed class FileData { public string file, sha256; }

    public static class PayloadReader
    {
        private const long MaxPayloadBytes = 512L * 1024 * 1024;
        public static SceneData Load(string scenePath)
        {
            var info = new FileInfo(scenePath);
            Require(info.Exists && info.Length > 0 && info.Length <= 8 * 1024 * 1024, "scene.json missing or excessive size");
            var scene = JsonConvert.DeserializeObject<SceneData>(File.ReadAllText(scenePath), new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.None, MaxDepth = 64,
                MissingMemberHandling = MissingMemberHandling.Ignore
            });
            Require(scene != null && scene.schema == 2 && scene.rootName == "TSC_HH60_VISUAL", "unsupported scene schema/rootName");
            Count(scene.nodes, 1, 8192, "nodes"); Count(scene.meshes, 1, 512, "meshes");
            Count(scene.textures, 0, 512, "textures"); Count(scene.materials, 1, 512, "materials");
            Count(scene.renderers, 1, 2048, "renderers"); Count(scene.files, 1, 1024, "files");
            var nodes = Map(scene.nodes, n => n.id, "node");
            var meshes = Map(scene.meshes, n => n.id, "mesh");
            var textures = Map(scene.textures, n => n.id, "texture");
            var materials = Map(scene.materials, n => n.id, "material");
            var fileMap = Map(scene.files, n => n.file, "file");
            var root = Path.GetFullPath(Path.GetDirectoryName(scenePath)) + Path.DirectorySeparatorChar;
            long total = 0;
            foreach (var file in scene.files)
            {
                Require(file.sha256 != null && file.sha256.Length == 64 && file.sha256.All(Uri.IsHexDigit), "invalid SHA256: " + file.file);
                var path = ResolveFile(root, file.file);
                var f = new FileInfo(path);
                Require(f.Exists && f.Length >= 0 && f.Length <= MaxPayloadBytes, "missing/oversize file: " + file.file);
                total += f.Length;
                Require(total <= MaxPayloadBytes, "payload exceeds 512 MiB budget");
                using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create())
                    Require(string.Equals(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", ""), file.sha256, StringComparison.OrdinalIgnoreCase), "SHA256 mismatch: " + file.file);
            }
            foreach (var node in scene.nodes)
            {
                Name(node.name, "node name"); Vector(node.position, 3, "position", 100000);
                Vector(node.rotation, 4, "rotation", 2); Vector(node.scale, 3, "scale", 10000);
                var q = node.rotation.Sum(v => (double)v * v);
                Require(q > 0.99 && q < 1.01, "non-unit quaternion: " + node.id);
                Require(node.scale.All(v => Math.Abs(v) > 0.0000001), "zero node scale: " + node.id);
                var seen = new HashSet<string>(StringComparer.Ordinal) { node.id };
                var parent = node.parent;
                while (!string.IsNullOrEmpty(parent))
                {
                    Require(nodes.ContainsKey(parent), "unknown node parent: " + parent);
                    Require(seen.Add(parent), "node hierarchy cycle: " + node.id);
                    Require(seen.Count < 512, "node hierarchy too deep");
                    parent = nodes[parent].parent;
                }
            }
            foreach (var mesh in scene.meshes)
            {
                Require(fileMap.ContainsKey(mesh.file), "unhashed mesh: " + mesh.file);
                Require(mesh.vertexCount > 0 && mesh.vertexCount <= 1000000, "invalid vertexCount: " + mesh.id);
                Count(mesh.submeshes, 1, 128, "submeshes");
                var data = File.ReadAllBytes(ResolveFile(root, mesh.file));
                long vertexBytes = mesh.vertexCount * 32L;
                Require(data.LongLength > vertexBytes && (data.LongLength - vertexBytes) % 4 == 0, "mesh byte length: " + mesh.id);
                int indexCount = checked((int)((data.LongLength - vertexBytes) / 4));
                Require(indexCount <= 6000000 && (mesh.indexCount == 0 || mesh.indexCount == indexCount), "invalid index count: " + mesh.id);
                mesh.indexCount = indexCount;
                mesh.positions = new float[mesh.vertexCount * 3]; mesh.normals = new float[mesh.vertexCount * 3];
                mesh.uv0 = new float[mesh.vertexCount * 2]; mesh.indices = new int[indexCount];
                Require(BitConverter.IsLittleEndian, "little-endian host required");
                int offset = 0;
                Buffer.BlockCopy(data, offset, mesh.positions, 0, mesh.positions.Length * 4); offset += mesh.positions.Length * 4;
                Buffer.BlockCopy(data, offset, mesh.normals, 0, mesh.normals.Length * 4); offset += mesh.normals.Length * 4;
                Buffer.BlockCopy(data, offset, mesh.uv0, 0, mesh.uv0.Length * 4); offset += mesh.uv0.Length * 4;
                Buffer.BlockCopy(data, offset, mesh.indices, 0, mesh.indices.Length * 4);
                Floats(mesh.positions, "mesh positions", 100000); Floats(mesh.normals, "mesh normals", 2);
                Floats(mesh.uv0, "mesh UV", 1000000);
                Require(mesh.indices.All(i => i >= 0 && i < mesh.vertexCount), "mesh index outside vertex buffer: " + mesh.id);
                int end = 0;
                foreach (var submesh in mesh.submeshes)
                {
                    Require(submesh.offset == end && submesh.count > 0 && submesh.count % 3 == 0 && (long)end + submesh.count <= indexCount, "invalid triangle submesh: " + mesh.id);
                    end += submesh.count;
                }
                Require(end == indexCount, "unused indices: " + mesh.id);
            }
            foreach (var tex in scene.textures)
            {
                Require(fileMap.ContainsKey(tex.file), "unhashed texture: " + tex.file);
                Require(tex.width > 0 && tex.height > 0 && tex.width <= 8192 && tex.height <= 8192, "texture dimensions: " + tex.id);
                int fullMipCount = 1, maxDimension = Math.Max(tex.width, tex.height);
                while ((maxDimension >>= 1) > 0) fullMipCount++;
                Require(tex.mipCount == 1 || tex.mipCount == fullMipCount, "texture must have one mip or a complete chain: " + tex.id);
                tex.formatValue = ParseFormat(tex.format);
                Require(tex.dimension == "2D" || tex.dimension == "Cube", "unknown texture dimension: " + tex.id);
                if (tex.dimension == "Cube") Require(tex.width == tex.height, "non-square Cubemap: " + tex.id);
                long faceBytes = TextureByteCount(tex.width, tex.height, tex.mipCount, tex.formatValue);
                if (tex.faceMipSizes != null)
                {
                    Require(tex.faceMipSizes.Length == tex.mipCount, "invalid faceMipSizes count: " + tex.id);
                    for (int mip = 0; mip < tex.mipCount; mip++)
                        Require(tex.faceMipSizes[mip] == TextureByteCount(Math.Max(1, tex.width >> mip), Math.Max(1, tex.height >> mip), 1, tex.formatValue), "invalid faceMipSizes: " + tex.id);
                }
                tex.bytes = File.ReadAllBytes(ResolveFile(root, tex.file));
                Require(tex.bytes.LongLength == faceBytes * (tex.dimension == "Cube" ? 6 : 1), "raw texture size mismatch: " + tex.id);
            }
            foreach (var material in scene.materials)
            {
                Name(material.name, "material name"); Name(material.shader, "shader");
                Require(material.renderQueue >= -1 && material.renderQueue <= 5000, "invalid renderQueue");
                material.textures = material.textures ?? Array.Empty<MaterialTexture>();
                material.floats = material.floats ?? Array.Empty<FloatData>();
                material.colors = material.colors ?? Array.Empty<ColorData>();
                material.keywords = material.keywords ?? Array.Empty<string>();
                Count(material.textures, 0, 128, "material textures"); Count(material.floats, 0, 256, "material floats");
                Count(material.colors, 0, 256, "material colors"); Count(material.keywords, 0, 128, "material keywords");
                foreach (var tex in material.textures)
                {
                    Name(tex.property, "texture property"); Require(textures.ContainsKey(tex.texture), "unknown material texture");
                    Vector(tex.scale, 2, "texture scale", 10000); Vector(tex.offset, 2, "texture offset", 10000);
                }
                foreach (var f in material.floats) { Name(f.name, "float property"); Floats(new[] { f.value }, "material float", 100000000); }
                foreach (var c in material.colors) { Name(c.name, "color property"); Vector(c.value, 4, "material color", 100000); }
                foreach (var keyword in material.keywords) Name(keyword, "keyword");
            }
            var renderedNodes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var renderer in scene.renderers)
            {
                Require(nodes.ContainsKey(renderer.node) && renderedNodes.Add(renderer.node), "unknown/duplicate renderer node");
                Require(meshes.ContainsKey(renderer.mesh), "unknown renderer mesh");
                Count(renderer.materials, meshes[renderer.mesh].submeshes.Length, meshes[renderer.mesh].submeshes.Length, "renderer materials");
                Require(renderer.materials.All(materials.ContainsKey), "unknown renderer material");
                Require(renderer.shadowCastingMode >= 0 && renderer.shadowCastingMode <= 3, "invalid shadow mode");
            }
            return scene;
        }
        private static int ParseFormat(string name)
        {
            int value;
            if (!int.TryParse(name, out value))
            {
                switch (name)
                {
                    case "Alpha8": value = 1; break; case "RGB24": value = 3; break;
                    case "RGBA32": value = 4; break; case "ARGB32": value = 5; break;
                    case "DXT1": value = 10; break; case "DXT5": value = 12; break;
                    case "BC6H": value = 24; break; case "BC7": value = 25; break;
                    case "BC4": value = 26; break; case "BC5": value = 27; break;
                    case "BGRA32": value = 14; break; case "R8": value = 63; break;
                    default: throw new InvalidDataException("unsupported texture format: " + name);
                }
            }
            Require(new[] { 1, 3, 4, 5, 10, 12, 14, 24, 25, 26, 27, 63 }.Contains(value), "unsupported texture format: " + name);
            return value;
        }
        public static long TextureByteCount(int width, int height, int mips, int format)
        {
            long bytes = 0;
            for (int i = 0; i < mips; i++)
            {
                if (format == 10 || format == 26) bytes += ((width + 3L) / 4) * ((height + 3L) / 4) * 8;
                else if (format == 12 || format == 24 || format == 25 || format == 27) bytes += ((width + 3L) / 4) * ((height + 3L) / 4) * 16;
                else bytes += (long)width * height * (format == 1 || format == 63 ? 1 : format == 3 ? 3 : 4);
                width = Math.Max(1, width / 2); height = Math.Max(1, height / 2);
            }
            return bytes;
        }
        private static string ResolveFile(string root, string relative)
        {
            Require(!string.IsNullOrWhiteSpace(relative) && !Path.IsPathRooted(relative) && relative.IndexOf(':') < 0, "unsafe payload path");
            var path = Path.GetFullPath(Path.Combine(root, relative));
            Require(path.StartsWith(root, StringComparison.OrdinalIgnoreCase), "payload path escapes root");
            var info = new FileInfo(path);
            Require(!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) == 0, "payload file cannot be a reparse point");
            return path;
        }
        private static Dictionary<string, T> Map<T>(T[] items, Func<T, string> key, string label) where T : class
        {
            var result = new Dictionary<string, T>(StringComparer.Ordinal);
            foreach (var item in items)
            {
                Require(item != null, "null " + label);
                string id = key(item); Name(id, label + " ID");
                Require(!result.ContainsKey(id), "duplicate " + label + ": " + id); result.Add(id, item);
            }
            return result;
        }
        private static void Name(string name, string field) { Require(!string.IsNullOrEmpty(name) && name.Length <= 1024 && name.IndexOf('\0') < 0, "invalid " + field); }
        private static void Vector(float[] v, int size, string label, float max) { Require(v != null && v.Length == size, "invalid " + label); Floats(v, label, max); }
        private static void Floats(float[] v, string label, float max) { Require(v.All(f => !float.IsNaN(f) && !float.IsInfinity(f) && Math.Abs(f) <= max), "non-finite/excessive " + label); }
        private static void Count<T>(T[] v, int min, int max, string label) { Require(v != null && v.Length >= min && v.Length <= max, "invalid " + label + " count"); }
        private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    }
}
