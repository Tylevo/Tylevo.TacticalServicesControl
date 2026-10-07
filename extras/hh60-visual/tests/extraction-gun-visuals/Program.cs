using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using TscHh60Visual;
using UnityEngine;
using Object = UnityEngine.Object;

internal static class Program
{
    private static int _checks;
    private static void Check(bool value, string message)
    {
        _checks++;
        if (!value) throw new Exception(message);
    }

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length != 1) throw new ArgumentException("Provide the path to your locally prepared scene.json as the only argument.");
            string path = args[0];
            string json = File.ReadAllText(path);
            SharedResources Cache()
            {
                var cache = new SharedResources
                {
                    Scene = JsonSerializer.Deserialize<SceneData>(json, new JsonSerializerOptions { IncludeFields = true })
                };
                foreach (var renderer in cache.Scene.renderers)
                    cache.Meshes[renderer.mesh] = new Mesh { name = renderer.mesh };
                foreach (var material in cache.Scene.materials)
                    cache.Materials[material.id] = new Material { name = material.name };
                return cache;
            }

            var aircraft = new GameObject("original UH60") { layer = 8 };
            var original = aircraft.AddComponent<MeshRenderer>();
            var originalMesh = aircraft.AddComponent<MeshFilter>();
            originalMesh.sharedMesh = new Mesh();
            original.sharedMaterials = new[] { new Material() };
            var originalMaterial = original.sharedMaterials[0];
            using (var guns = new ExtractionGunVisuals(aircraft.transform))
            {
                Check(!guns.TryPrepare(out _), "resource wait must be nonblocking");
                Check(aircraft.transform.Children.Count == 0, "resource wait must not allocate a partial marker");
                SharedResources.Failure = new IOException("expected shared cache failure");
                bool failed = false;
                try { guns.TryPrepare(out _); } catch (InvalidOperationException error) { failed = error.InnerException == SharedResources.Failure; }
                Check(failed, "shared cache failures must propagate without changing the UH60");
                SharedResources.Failure = null;
                SharedResources.Current = Cache();
                Check(guns.TryPrepare(out _), "completed cache should prepare donor guns");
                var marker = Plugin.FindDescendant(aircraft.transform, ExtractionGunVisuals.MarkerName);
                Check(marker != null && marker.gameObject.activeSelf, "finished marker should become active");
                Check(marker.parent == aircraft.transform, "guns must follow the animated main transform");
                Check(Plugin.FindDescendant(marker, "TSC_HH60_VISUAL") == null, "gun subtree must not impersonate full HH60 marker");
                Check(marker.GetComponentsInChildren<Transform>(true).Length == 15, "expected marker plus fourteen donor transforms");
                Check(marker.GetComponentsInChildren<MeshRenderer>(true).Length == 4, "expected only four gun-part renderers");
                Check(marker.GetComponentsInChildren<Component>(true).All(c => c is Transform || c is MeshRenderer || c is MeshFilter), "guns must not copy scripts or colliders");
                Check(marker.GetComponentsInChildren<Transform>(true).All(t => t.gameObject.layer == 8), "all donor objects use the aircraft layer");

                string[] ids = { "root", "5988", "5783", "6750", "6831", "7302", "6416", "7236", "6439", "8951", "7568", "7275", "6481", "7210" };
                foreach (string id in ids)
                {
                    var data = SharedResources.Current.Scene.nodes.Single(n => n.id == id);
                    string name = id == "root" ? ExtractionGunVisuals.MarkerName + "_ROOT" : data.name;
                    var node = Plugin.FindDescendant(marker, name);
                    Check(node != null, "missing donor node: " + id);
                    Check(node.localPosition.Equals(new Vector3(data.position[0], data.position[1], data.position[2])), "donor position changed: " + id);
                    Check(node.localRotation.Equals(new Quaternion(data.rotation[0], data.rotation[1], data.rotation[2], data.rotation[3])), "donor rotation changed: " + id);
                    Check(node.localScale.Equals(new Vector3(data.scale[0], data.scale[1], data.scale[2])), "donor scale changed: " + id);
                    if (data.parent == null) Check(node.parent == marker, "donor root parent changed");
                    else
                    {
                        var parent = SharedResources.Current.Scene.nodes.Single(n => n.id == data.parent);
                        Check(node.parent.name == (parent.id == "root" ? ExtractionGunVisuals.MarkerName + "_ROOT" : parent.name), "donor ancestry changed: " + id);
                    }
                    var rendererData = SharedResources.Current.Scene.renderers.SingleOrDefault(r => r.node == id);
                    if (rendererData != null && new[] { "6831", "6416", "7568", "6481" }.Contains(id))
                    {
                        Check(node.GetComponent<MeshFilter>().sharedMesh == SharedResources.Current.Meshes[rendererData.mesh], "mesh must be borrowed");
                        Check(node.GetComponent<MeshRenderer>().sharedMaterials.SequenceEqual(rendererData.materials.Select(m => SharedResources.Current.Materials[m])), "materials must be borrowed");
                    }
                }
                int count = GameObject.CreatedCount;
                Check(guns.TryPrepare(out _) && GameObject.CreatedCount == count, "ready calls must not duplicate objects");
                Check(original.enabled && !original.forceRenderingOff && original.sharedMaterials[0] == originalMaterial, "original UH60 rendering must stay untouched");
                using (var duplicate = new ExtractionGunVisuals(aircraft.transform))
                {
                    bool rejected = false;
                    try { duplicate.TryPrepare(out _); } catch (InvalidDataException) { rejected = true; }
                    Check(rejected && marker != null, "duplicate owner must not replace existing marker");
                }
            }
            Check(aircraft != null && original != null && aircraft.transform.Children.Count == 0, "dispose must only remove owned gun objects");
            Check(originalMaterial != null && SharedResources.Current.Meshes.Values.All(m => m != null) && SharedResources.Current.Materials.Values.All(m => m != null), "dispose must retain shared assets");

            var missingMaterial = SharedResources.Current.Materials;
            missingMaterial.Clear();
            using (var incomplete = new ExtractionGunVisuals(aircraft.transform))
            {
                bool rejected = false;
                try { incomplete.TryPrepare(out _); } catch (System.Collections.Generic.KeyNotFoundException) { rejected = true; }
                Check(rejected && aircraft.transform.Children.Count == 0, "partial build failure must remove every owned object");
                Check(original.enabled && !original.forceRenderingOff, "partial failure must retain UH60 visuals");
            }
            SharedResources.Current = Cache();
            using (var deadAnchor = new ExtractionGunVisuals(aircraft.transform))
            {
                Object.Destroy(aircraft);
                bool rejected = false;
                try { deadAnchor.TryPrepare(out _); } catch (InvalidOperationException) { rejected = true; }
                Check(rejected, "destroyed aircraft must not receive gun objects");
            }
            Console.WriteLine("PASS " + _checks + " extraction gun visual lifecycle and installed payload checks (managed Unity doubles).");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}

namespace TscHh60Visual
{
    internal static class Plugin
    {
        internal const string ScenePath = "synthetic scene path";
        internal static Transform FindDescendant(Transform root, string name) =>
            root.GetComponentsInChildren<Transform>(true).FirstOrDefault(node => node.name == name);
    }
}
