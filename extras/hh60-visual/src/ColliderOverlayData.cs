using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace TscHh60Visual
{
    // Optional local, asset-derived wire data. Never reads or changes a Unity mesh's
    // CPU buffers; unreadable/cooked meshes retain their normal gameplay state.
    internal static class ColliderOverlayData
    {
        private const string ExpectedBundleSha256 = "A2336CF5D251C69E7D27E1E5C818E432B6719BE4671F255EBA20E5A22BEF1803";
        private const long MaxJsonBytes = 16L * 1024 * 1024;
        private const int MaxMeshes = 100;
        private const int MaxEdgesPerMesh = 50000;
        private const int MaxTotalEdges = 100000;
        private static bool _attempted, _warned;
        private static Dictionary<string, Entry> _meshes;

        private sealed class Entry
        {
            internal int VertexCount;
            internal Vector3 Center, Size;
            internal Vector3[] EdgePoints;
        }

        internal static bool TryGet(Mesh mesh, out Vector3[] edgePoints)
        {
            edgePoints = null;
            if (mesh == null) return false;
            try
            {
                if (!_attempted)
                {
                    _attempted = true;
                    _meshes = Load();
                }
                Entry entry;
                if (_meshes == null || !_meshes.TryGetValue(mesh.name, out entry)) return false;
                var bounds = mesh.bounds;
                if (mesh.vertexCount != entry.VertexCount || !Close(bounds.center, entry.Center) || !Close(bounds.size, entry.Size))
                {
                    WarnOnce("Mesh identity/bounds do not match local collider wire data: " + mesh.name);
                    return false;
                }
                edgePoints = entry.EdgePoints;
                return true;
            }
            catch (Exception error)
            {
                WarnOnce(error.GetBaseException().Message);
                return false;
            }
        }

        internal static void Clear()
        {
            _meshes = null;
            _attempted = false;
            _warned = false;
        }

        private static Dictionary<string, Entry> Load()
        {
            var path = Path.Combine(Path.GetDirectoryName(Plugin.ScenePath), "collider-overlay.json");
            JObject document;
            // FileShare.Read prevents a writer growing/replacing this file during
            // the length check and parse on the supported Windows installation.
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length <= 0 || stream.Length > MaxJsonBytes)
                    throw new InvalidDataException("Collider overlay JSON exceeds the 16 MiB limit or is empty.");
                using (var text = new StreamReader(stream, new UTF8Encoding(false, true), true))
                using (var reader = new JsonTextReader(text) { MaxDepth = 16, DateParseHandling = DateParseHandling.None })
                {
                    document = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                    while (reader.Read())
                        if (reader.TokenType != JsonToken.Comment)
                            throw new InvalidDataException("Unexpected content after collider overlay JSON.");
                }
            }
            if (ReadInteger(document["schema"], "schema") != 1)
                throw new InvalidDataException("Unsupported collider overlay schema.");
            var sourceHash = ReadString(document["sourceBundleSha256"], "sourceBundleSha256");
            if (!string.Equals(sourceHash, ExpectedBundleSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Collider overlay source bundle hash does not match the verified stock bundle.");
            VerifyInstalledBundle();

            var sourceMeshes = document["meshes"] as JArray;
            if (sourceMeshes == null || sourceMeshes.Count > MaxMeshes)
                throw new InvalidDataException("Collider overlay must contain at most 100 meshes.");
            var result = new Dictionary<string, Entry>(StringComparer.Ordinal);
            int totalEdges = 0;
            foreach (var token in sourceMeshes)
            {
                var source = token as JObject;
                if (source == null) throw new InvalidDataException("Invalid collider overlay mesh entry.");
                var name = ReadString(source["name"], "mesh name");
                if (string.IsNullOrWhiteSpace(name) || result.ContainsKey(name))
                    throw new InvalidDataException("Empty or duplicate collider overlay mesh name.");
                var entry = new Entry
                {
                    VertexCount = ReadInteger(source["vertexCount"], "vertexCount"),
                    Center = ReadVector(source["boundsCenter"], "boundsCenter"),
                    Size = ReadVector(source["boundsSize"], "boundsSize")
                };
                if (entry.VertexCount <= 0 || entry.Size.x < 0 || entry.Size.y < 0 || entry.Size.z < 0)
                    throw new InvalidDataException("Collider overlay mesh count/bounds are invalid: " + name);
                ValidateBounds(entry.Center, entry.Size);
                var coordinates = source["edges"] as JArray;
                if (coordinates == null || coordinates.Count == 0 || coordinates.Count % 6 != 0 || coordinates.Count / 6 > MaxEdgesPerMesh)
                    throw new InvalidDataException("Collider overlay edge list is invalid or exceeds 50,000 edges: " + name);
                totalEdges += coordinates.Count / 6;
                if (totalEdges > MaxTotalEdges)
                    throw new InvalidDataException("Collider overlay exceeds 100,000 total edges.");
                entry.EdgePoints = new Vector3[coordinates.Count / 3];
                for (int i = 0; i < entry.EdgePoints.Length; i++)
                {
                    var point = new Vector3(ReadFloat(coordinates[i * 3], "edge x"),
                        ReadFloat(coordinates[i * 3 + 1], "edge y"), ReadFloat(coordinates[i * 3 + 2], "edge z"));
                    if (!Inside(point.x, entry.Center.x, entry.Size.x) || !Inside(point.y, entry.Center.y, entry.Size.y) || !Inside(point.z, entry.Center.z, entry.Size.z))
                        throw new InvalidDataException("Collider overlay edge lies outside declared mesh bounds: " + name);
                    entry.EdgePoints[i] = point;
                }
                result.Add(name, entry);
            }
            return result;
        }

        private static void VerifyInstalledBundle()
        {
            var helicopter = AccessTools.TypeByName("SamSWAT.FireSupport.ArysReloaded.Unity.UH60Behaviour");
            if (helicopter == null) throw new InvalidDataException("Cannot locate the installed TSC helicopter assembly.");
            var bundlePath = Path.Combine(Path.GetDirectoryName(helicopter.Assembly.Location), "assets", "content", "vehicles", "uh60_blackhawk.bundle");
            using (var stream = new FileStream(bundlePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var hash = SHA256.Create())
            {
                var actual = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "");
                if (!string.Equals(actual, ExpectedBundleSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Installed stock helicopter bundle does not match collider overlay data.");
            }
        }

        private static int ReadInteger(JToken token, string label)
        {
            if (token == null || token.Type != JTokenType.Integer)
                throw new InvalidDataException("Collider overlay " + label + " must be an integer.");
            return token.Value<int>();
        }

        private static string ReadString(JToken token, string label)
        {
            if (token == null || token.Type != JTokenType.String)
                throw new InvalidDataException("Collider overlay " + label + " must be a string.");
            return token.Value<string>();
        }

        private static float ReadFloat(JToken token, string label)
        {
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float))
                throw new InvalidDataException("Collider overlay " + label + " must be numeric.");
            var value = token.Value<double>();
            if (double.IsNaN(value) || double.IsInfinity(value) || value < -float.MaxValue || value > float.MaxValue)
                throw new InvalidDataException("Collider overlay " + label + " must be finite.");
            return (float)value;
        }

        private static Vector3 ReadVector(JToken token, string label)
        {
            var coordinates = token as JArray;
            if (coordinates == null || coordinates.Count != 3)
                throw new InvalidDataException("Collider overlay " + label + " requires three coordinates.");
            return new Vector3(ReadFloat(coordinates[0], label), ReadFloat(coordinates[1], label), ReadFloat(coordinates[2], label));
        }

        private static void ValidateBounds(Vector3 center, Vector3 size)
        {
            if (Math.Abs((double)center.x) + size.x * 0.5 > float.MaxValue ||
                Math.Abs((double)center.y) + size.y * 0.5 > float.MaxValue ||
                Math.Abs((double)center.z) + size.z * 0.5 > float.MaxValue)
                throw new InvalidDataException("Collider overlay bounds overflow finite coordinates.");
        }

        private static bool Inside(float point, float center, float size)
        {
            var tolerance = 0.0001 + 0.0001 * Math.Max(Math.Abs((double)center), size);
            return Math.Abs((double)point - center) <= size * 0.5 + tolerance;
        }

        private static bool Close(Vector3 left, Vector3 right)
        {
            return Close(left.x, right.x) && Close(left.y, right.y) && Close(left.z, right.z);
        }

        private static bool Close(float left, float right)
        {
            return !float.IsNaN(left) && !float.IsInfinity(left) &&
                Math.Abs((double)left - right) <= 0.0001 + 0.0001 * Math.Max(Math.Abs((double)left), Math.Abs((double)right));
        }

        private static void WarnOnce(string reason)
        {
            if (_warned) return;
            _warned = true;
            Plugin.Log?.LogWarning("Collider wire data unavailable; using bounds where needed: " + reason);
        }
    }
}
