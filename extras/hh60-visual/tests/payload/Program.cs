using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using TscHh60Visual;

internal static class Program
{
    private static int _passed;
    private static string _root;
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--validate")
        {
            try
            {
                var scene = PayloadReader.Load(Path.GetFullPath(args[1]));
                Console.WriteLine("PASS real payload: schema=" + scene.schema + ", nodes=" + scene.nodes.Length + ", meshes=" + scene.meshes.Length + ", textures=" + scene.textures.Length + ", materials=" + scene.materials.Length + ", renderers=" + scene.renderers.Length + ", crew=" + (scene.crew?.Length ?? 0));
                Console.WriteLine("All source hashes, finite floats, node hierarchy, triangle indices, material references, raw texture formats/dimensions/mip sizes passed managed validation. No Unity runtime or in-raid test performed.");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 2; }
        }
        if (args.Length > 0)
        {
            Console.Error.WriteLine("Usage: PayloadTests [--validate /path/to/local/scene.json]");
            return 2;
        }
        _root = Path.Combine(Path.GetTempPath(), "tsc-hh60-validator-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        try
        {
            Check("valid triangle", false, (_, __) => {});
            Check("valid BC7 2D", false, (s, p) => AddTexture(s, p, "2D"));
            Check("valid BC7 Cube", false, (s, p) => AddTexture(s, p, "Cube"));
            Check("SHA mismatch", true, (s, p) => s.files[0].sha256 = new string('0', 64));
            Check("index past vertex count", true, (s, p) => RewriteMesh(s, p, 104, BitConverter.GetBytes(100)));
            Check("negative unsigned index", true, (s, p) => RewriteMesh(s, p, 104, BitConverter.GetBytes(-1)));
            Check("nonfinite vertex", true, (s, p) => RewriteMesh(s, p, 0, BitConverter.GetBytes(float.NaN)));
            Check("nonfinite normal", true, (s, p) => RewriteMesh(s, p, 36, BitConverter.GetBytes(float.PositiveInfinity)));
            Check("nonfinite uv", true, (s, p) => RewriteMesh(s, p, 72, BitConverter.GetBytes(float.NaN)));
            Check("truncated mesh", true, (s, p) => { File.WriteAllBytes(Path.Combine(p, "mesh.bin"), new byte[103]); Rehash(s, p); });
            Check("mismatching index count", true, (s, p) => s.meshes[0].indexCount = 6);
            Check("nontriangle submesh", true, (s, p) => s.meshes[0].submeshes[0].count = 2);
            Check("noncontiguous submesh", true, (s, p) => s.meshes[0].submeshes[0].offset = 1);
            Check("parent cycle", true, (s, p) => s.nodes[0].parent = "root");
            Check("unknown parent", true, (s, p) => s.nodes[0].parent = "absent");
            Check("duplicate node", true, (s, p) => s.nodes = new[] { s.nodes[0], s.nodes[0] });
            Check("zero quaternion", true, (s, p) => s.nodes[0].rotation = new float[4]);
            Check("unknown mesh", true, (s, p) => s.renderers[0].mesh = "absent");
            Check("unknown material", true, (s, p) => s.renderers[0].materials[0] = "absent");
            Check("duplicate renderer", true, (s, p) => s.renderers = new[] { s.renderers[0], s.renderers[0] });
            Check("path traversal", true, (s, p) => { s.files[0].file = "../escape.bin"; s.meshes[0].file = "../escape.bin"; });
            Check("wrong BC7 length", true, (s, p) => { AddTexture(s, p, "2D"); File.WriteAllBytes(Path.Combine(p, "texture.bin"), new byte[15]); Rehash(s, p); });
            Check("wrong Cube length", true, (s, p) => { AddTexture(s, p, "Cube"); File.WriteAllBytes(Path.Combine(p, "texture.bin"), new byte[16]); Rehash(s, p); });
            Check("wrong Cube face mip size", true, (s, p) => { AddTexture(s, p, "Cube"); s.textures[0].faceMipSizes[2] = 8; });
            Check("partial mip chain", true, (s, p) => { AddTexture(s, p, "Cube"); s.textures[0].mipCount = 2; });
            Check("unsupported texture format", true, (s, p) => { AddTexture(s, p, "2D"); s.textures[0].format = "9999"; });
            Check("oversize texture dimension", true, (s, p) => { AddTexture(s, p, "2D"); s.textures[0].width = 16384; });
            Check("texture dimensions invalid", true, (s, p) => { AddTexture(s, p, "2D"); s.textures[0].height = 0; });
            Check("texture without declared hash", true, (s, p) => { AddTexture(s, p, "2D"); s.files = new[] { s.files[0] }; });
            Check("wrong schema", true, (s, p) => s.schema = 1);
            Console.WriteLine("PASS " + _passed + "/30 managed validator tests. Native Unity construction and gameplay are NOT tested.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            // Delete only this runner's uniquely allocated child of the temp root.
            string target = Path.GetFullPath(_root);
            string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.Equals(Path.GetDirectoryName(target), temp, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(target).StartsWith("tsc-hh60-validator-", StringComparison.Ordinal))
                throw new InvalidOperationException("Refusing fixture cleanup outside the allocated temporary directory.");
            Directory.Delete(target, recursive: true);
        }
    }
    private static void Check(string label, bool shouldFail, Action<SceneData, string> mutate)
    {
        string path = Path.Combine(_root, (_passed + 1).ToString("D2")); Directory.CreateDirectory(path);
        var scene = Fixture(path); mutate(scene, path);
        string scenePath = Path.Combine(path, "scene.json"); File.WriteAllText(scenePath, JsonConvert.SerializeObject(scene));
        Exception caught = null;
        try { PayloadReader.Load(scenePath); } catch (InvalidDataException error) { caught = error; }
        if (shouldFail && caught == null) throw new Exception("FAILED: accepted " + label);
        if (!shouldFail && caught != null) throw new Exception("FAILED: rejected " + label, caught);
        _passed++; Console.WriteLine("PASS " + label + (caught == null ? "" : " -> " + caught.Message));
    }
    private static SceneData Fixture(string path)
    {
        var data = new byte[108];
        float[] f = { 0,0,0, 1,0,0, 0,1,0, 0,0,1, 0,0,1, 0,0,1, 0,0, 1,0, 0,1 };
        Buffer.BlockCopy(f, 0, data, 0, 96); Buffer.BlockCopy(new[] { 0, 1, 2 }, 0, data, 96, 12);
        File.WriteAllBytes(Path.Combine(path, "mesh.bin"), data);
        return new SceneData
        {
            schema = 2, rootName = "TSC_HH60_VISUAL",
            nodes = new[] { new NodeData { id="root", name="node", parent=null, position=new float[3], rotation=new[]{0f,0f,0f,1f}, scale=new[]{1f,1f,1f} } },
            meshes = new[] { new MeshData { id="mesh", name="mesh", file="mesh.bin", vertexCount=3, indexCount=3, submeshes=new[] { new SubmeshData { offset=0, count=3 } } } },
            textures = Array.Empty<TextureData>(), materials = new[] { new MaterialData { id="mat", name="material", shader="Standard" } },
            renderers = new[] { new RendererData { node="root", mesh="mesh", materials=new[]{"mat"} } },
            files = new[] { new FileData { file="mesh.bin", sha256=Hash(data) } }, crew=Array.Empty<object>()
        };
    }
    private static void AddTexture(SceneData scene, string path, string dimension)
    {
        byte[] bytes = new byte[dimension == "Cube" ? 288 : 16]; File.WriteAllBytes(Path.Combine(path, "texture.bin"), bytes);
        scene.textures = new[] { new TextureData { id="tex", name="texture", file="texture.bin", format="25", width=4, height=4, dimension=dimension, mipCount=dimension=="Cube"?3:1, faceMipSizes=dimension=="Cube"?new[]{16,16,16}:null } };
        scene.files = scene.files.Concat(new[] { new FileData { file="texture.bin", sha256=Hash(bytes) } }).ToArray();
        scene.materials[0].textures = new[] { new MaterialTexture { property="_MainTex", texture="tex", scale=new[]{1f,1f}, offset=new float[2] } };
    }
    private static void RewriteMesh(SceneData scene, string path, int offset, byte[] value)
    {
        var bytes = File.ReadAllBytes(Path.Combine(path, "mesh.bin")); Array.Copy(value, 0, bytes, offset, value.Length);
        File.WriteAllBytes(Path.Combine(path, "mesh.bin"), bytes); Rehash(scene, path);
    }
    private static void Rehash(SceneData scene, string path) { foreach (var f in scene.files) f.sha256 = Hash(File.ReadAllBytes(Path.Combine(path, f.file))); }
    private static string Hash(byte[] data) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", ""); }
}
