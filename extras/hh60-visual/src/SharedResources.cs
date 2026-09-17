using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace TscHh60Visual
{
    // Shared once per plugin lifetime. A persistent plugin-host coroutine owns creation;
    // pooled/inactive aircraft only await readiness and never own GPU asset lifetime.
    internal sealed class SharedResources
    {
        internal static SharedResources Current;
        internal static Exception Failure;
        private static string _path;
        private static Coroutine _build;
        private static SharedResources _pending;
        internal SceneData Scene;
        internal readonly Dictionary<string, Mesh> Meshes = new Dictionary<string, Mesh>(StringComparer.Ordinal);
        internal readonly Dictionary<string, Texture> Textures = new Dictionary<string, Texture>(StringComparer.Ordinal);
        internal readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>(StringComparer.Ordinal);
        private readonly List<Object> Owned = new List<Object>();

        internal static void Request(string scenePath)
        {
            string path = Path.GetFullPath(scenePath);
            if (_path != null)
            {
                if (!string.Equals(path, _path, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Cannot mix HH60 payload directories in one plugin lifetime.");
                return;
            }
            _path = path;
            _build = Plugin.StartHostCoroutine(BuildGuarded(path));
        }

        private static IEnumerator BuildGuarded(string path)
        {
            var cache = new SharedResources();
            _pending = cache;
            var builder = Build(cache, path);
            while (true)
            {
                object current = null;
                bool moved = false;
                Exception failure = null;
                try
                {
                    if (!Plugin.Enabled.Value) throw new InvalidOperationException("HH60 adapter disabled during shared asset build.");
                    moved = builder.MoveNext();
                    if (moved) current = builder.Current;
                }
                catch (Exception error) { failure = error; }
                if (failure != null)
                {
                    Failure = failure; cache.Dispose(); _pending = null; _build = null;
                    Plugin.Log.LogError("Shared HH60 asset build rejected; all instances keep original TSC visuals: " + failure);
                    yield break;
                }
                if (!moved)
                {
                    Current = cache; _pending = null; _build = null;
                    Plugin.Log.LogInfo("Shared HH60 resource cache ready; later TSC pool instances reuse the same meshes, textures and materials.");
                    yield break;
                }
                yield return current;
            }
        }

        private static IEnumerator Build(SharedResources cache, string scenePath)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            // Validation is entirely managed; malformed buffers never reach Unity.
            var scene = PayloadReader.Load(scenePath);
            Plugin.Log.LogInfo("HH60 payload validated: " + scene.meshes.Length + " meshes, " + scene.textures.Length + " textures, " + (scene.crew?.Length ?? 0) + " crew entries in " + timer.ElapsedMilliseconds + " ms.");
            var shaders = new Dictionary<string, Shader>(StringComparer.Ordinal);
            foreach (var data in scene.materials)
            {
                if (shaders.ContainsKey(data.shader)) continue;
                var shader = Plugin.FindNativeShader(data.shader);
                if (shader == null) throw new InvalidDataException("Native shader unavailable: " + data.shader);
                shaders.Add(data.shader, shader);
            }
            foreach (var tex in scene.textures)
                if (!SystemInfo.SupportsTextureFormat((TextureFormat)tex.formatValue))
                    throw new InvalidDataException("GPU texture format unsupported: " + tex.format + " / " + tex.name);
            var meshes = cache.Meshes;
            foreach (var data in scene.meshes)
            {
                var mesh = new Mesh { name = data.name, indexFormat = data.vertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                cache.Owned.Add(mesh);
                var vertices = new Vector3[data.vertexCount]; var normals = new Vector3[data.vertexCount];
                var uv = new Vector2[data.vertexCount];
                for (int i = 0; i < data.vertexCount; i++)
                {
                    vertices[i] = new Vector3(data.positions[i * 3], data.positions[i * 3 + 1], data.positions[i * 3 + 2]);
                    normals[i] = new Vector3(data.normals[i * 3], data.normals[i * 3 + 1], data.normals[i * 3 + 2]);
                    uv[i] = new Vector2(data.uv0[i * 2], data.uv0[i * 2 + 1]);
                }
                mesh.vertices = vertices; mesh.normals = normals; mesh.uv = uv;
                mesh.subMeshCount = data.submeshes.Length;
                for (int i = 0; i < data.submeshes.Length; i++)
                {
                    var sub = data.submeshes[i]; var indices = new int[sub.count];
                    Array.Copy(data.indices, sub.offset, indices, 0, sub.count);
                    mesh.SetTriangles(indices, i, false);
                }
                mesh.RecalculateBounds(); mesh.RecalculateTangents(); mesh.UploadMeshData(true);
                meshes.Add(data.id, mesh);
                // Drop managed validation arrays as each native mesh becomes available.
                data.positions = data.normals = data.uv0 = null; data.indices = null;
                yield return null;
            }
            var textures = cache.Textures;
            foreach (var data in scene.textures)
            {
                Texture texture;
                if (data.dimension == "Cube")
                {
                    var cube = new Cubemap(data.width, (TextureFormat)data.formatValue, data.mipCount > 1);
                    cache.Owned.Add(cube);
                    int faceBytes = checked((int)PayloadReader.TextureByteCount(data.width, data.height, data.mipCount, data.formatValue));
                    for (int face = 0; face < 6; face++)
                    {
                        int offset = checked(face * faceBytes);
                        for (int mip = 0; mip < data.mipCount; mip++)
                        {
                            cube.SetPixelData(data.bytes, mip, (CubemapFace)face, offset);
                            offset += checked((int)PayloadReader.TextureByteCount(Math.Max(1, data.width >> mip), Math.Max(1, data.height >> mip), 1, data.formatValue));
                        }
                    }
                    cube.Apply(false, true);
                    texture = cube;
                }
                else
                {
                    var image = new Texture2D(data.width, data.height, (TextureFormat)data.formatValue, data.mipCount > 1, data.linear);
                    cache.Owned.Add(image);
                    image.LoadRawTextureData(data.bytes);
                    image.Apply(false, true); // Preserve validated donor mip data; release CPU copy.
                    texture = image;
                }
                texture.name = data.name;
                texture.wrapMode = TextureWrapMode.Repeat;
                texture.filterMode = data.mipCount > 1 ? FilterMode.Trilinear : FilterMode.Bilinear;
                texture.anisoLevel = 2;
                textures.Add(data.id, texture);
                data.bytes = null;
                yield return null;
            }
            var materials = cache.Materials;
            foreach (var data in scene.materials)
            {
                var material = new Material(shaders[data.shader]) { name = data.name + " [HH60 v2]" };
                cache.Owned.Add(material);
                foreach (var item in data.textures)
                {
                    if (!material.HasProperty(item.property)) continue;
                    material.SetTexture(item.property, textures[item.texture]);
                    material.SetTextureScale(item.property, new Vector2(item.scale[0], item.scale[1]));
                    material.SetTextureOffset(item.property, new Vector2(item.offset[0], item.offset[1]));
                }
                foreach (var item in data.floats) if (material.HasProperty(item.name)) material.SetFloat(item.name, item.value);
                foreach (var item in data.colors) if (material.HasProperty(item.name)) material.SetColor(item.name, new Color(item.value[0], item.value[1], item.value[2], item.value[3]));
                material.shaderKeywords = data.keywords;
                material.renderQueue = data.renderQueue;
                materials.Add(data.id, material);
            }
            cache.Scene = scene;
            Plugin.Log.LogInfo("HH60 shared resource construction completed in " + timer.ElapsedMilliseconds + " ms.");
        }

        internal static void Shutdown()
        {
            if (_build != null) { Plugin.StopHostCoroutine(_build); _build = null; }
            _pending?.Dispose(); _pending = null;
            Current?.Dispose(); Current = null;
            Failure = null; _path = null;
        }

        private void Dispose()
        {
            foreach (var owned in Owned) if (owned != null) Object.Destroy(owned);
            Owned.Clear(); Meshes.Clear(); Textures.Clear(); Materials.Clear(); Scene = null;
        }
    }
}
