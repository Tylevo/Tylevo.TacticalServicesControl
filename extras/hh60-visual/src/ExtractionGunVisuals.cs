using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace TscHh60Visual
{
    // UH60 extraction owns only these donor gun objects. Shared GPU resources and
    // all original aircraft renderers, colliders, scripts and services stay owned
    // by their existing systems. HH60 already carries these guns in its visual.
    internal sealed class ExtractionGunVisuals : IDisposable
    {
        internal const string MarkerName = "TSC_EXTRACTION_GUNS";
        private readonly Transform _anchor;
        private GameObject _root;
        private bool _ready, _disposed;

        internal ExtractionGunVisuals(Transform animatedMain)
        {
            if (animatedMain == null) throw new ArgumentNullException(nameof(animatedMain));
            _anchor = animatedMain;
        }

        internal bool TryPrepare(out string reason)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ExtractionGunVisuals));
            if (_anchor == null) throw new InvalidOperationException("UH60 extraction anchor was destroyed.");
            if (_ready)
            {
                if (_root == null) throw new InvalidOperationException("UH60 extraction guns were destroyed.");
                reason = "UH60 extraction guns ready";
                return true;
            }

            SharedResources.Request(Plugin.ScenePath);
            if (SharedResources.Failure != null)
                throw new InvalidOperationException("Shared HH60 gun resources unavailable.", SharedResources.Failure);
            var cache = SharedResources.Current;
            if (cache == null)
            {
                reason = "waiting for shared HH60 gun resources";
                return false;
            }
            if (Plugin.FindDescendant(_anchor, MarkerName) != null)
                throw new InvalidDataException("Duplicate UH60 extraction gun marker.");

            try
            {
                Build(cache);
                _root.SetActive(true);
                _ready = true;
                reason = "UH60 extraction guns ready";
                return true;
            }
            catch
            {
                DestroyOwnedRoot();
                throw;
            }
        }

        private void Build(SharedResources cache)
        {
            var scene = cache.Scene;
            var dataById = scene.nodes.ToDictionary(data => data.id, StringComparer.Ordinal);
            var gunRoots = new HashSet<string>(StringComparer.Ordinal);
            foreach (string side in new[] { "left", "right" })
            {
                var roots = scene.nodes.Where(data => data.name == "helicopter_gun_box_" + side).ToArray();
                if (roots.Length != 1) throw new InvalidDataException("Expected one HH60 " + side + " gun mount.");
                var barrels = scene.nodes.Where(data => data.name == "helicopter_gun_" + side && data.parent == roots[0].id).ToArray();
                if (barrels.Length != 1) throw new InvalidDataException("Expected one HH60 " + side + " gun barrel.");
                gunRoots.Add(roots[0].id);
            }

            // Select the two complete gun subtrees, then their transform-only
            // ancestry. Never copy renderers from ancestors or sibling body/crew.
            var gunNodes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var data in scene.nodes)
                for (var current = data; current != null; current = string.IsNullOrEmpty(current.parent) ? null : dataById[current.parent])
                    if (gunRoots.Contains(current.id)) { gunNodes.Add(data.id); break; }
            var selectedNodes = new HashSet<string>(gunNodes, StringComparer.Ordinal);
            foreach (string rootId in gunRoots)
                for (var current = dataById[rootId]; current != null; current = string.IsNullOrEmpty(current.parent) ? null : dataById[current.parent])
                    selectedNodes.Add(current.id);
            var renderers = scene.renderers.Where(data => gunNodes.Contains(data.node)).ToArray();
            foreach (string rootId in gunRoots)
            {
                bool visible = renderers.Any(data => data.enabled && IsWithin(data.node, rootId, dataById));
                if (!visible) throw new InvalidDataException("HH60 extraction gun has no visible renderer: " + rootId);
            }

            _root = new GameObject(MarkerName);
            _root.SetActive(false);
            _root.transform.SetParent(_anchor, false);
            _root.layer = _anchor.gameObject.layer;
            var nodes = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (var data in scene.nodes)
            {
                if (!selectedNodes.Contains(data.id)) continue;
                // Preserve the donor root matrix without impersonating the full
                // HH60 marker used by model selection and gun lookup.
                var node = new GameObject(string.IsNullOrEmpty(data.parent) ? MarkerName + "_ROOT" : data.name);
                node.layer = _anchor.gameObject.layer;
                node.transform.SetParent(_root.transform, false);
                nodes.Add(data.id, node.transform);
            }
            foreach (var data in scene.nodes)
            {
                if (!selectedNodes.Contains(data.id)) continue;
                var node = nodes[data.id];
                node.SetParent(string.IsNullOrEmpty(data.parent) ? _root.transform : nodes[data.parent], false);
                node.localPosition = Vector(data.position);
                node.localRotation = new Quaternion(data.rotation[0], data.rotation[1], data.rotation[2], data.rotation[3]);
                node.localScale = Vector(data.scale);
            }
            foreach (var data in renderers)
            {
                var node = nodes[data.node];
                node.gameObject.AddComponent<MeshFilter>().sharedMesh = cache.Meshes[data.mesh];
                var renderer = node.gameObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = data.materials.Select(id => cache.Materials[id]).ToArray();
                renderer.enabled = data.enabled;
                renderer.shadowCastingMode = (ShadowCastingMode)data.shadowCastingMode;
                renderer.receiveShadows = data.receiveShadows;
            }
        }

        private static bool IsWithin(string nodeId, string ancestorId, Dictionary<string, NodeData> nodes)
        {
            for (var node = nodes[nodeId]; node != null; node = string.IsNullOrEmpty(node.parent) ? null : nodes[node.parent])
                if (node.id == ancestorId) return true;
            return false;
        }

        private static Vector3 Vector(float[] value) => new Vector3(value[0], value[1], value[2]);

        private void DestroyOwnedRoot()
        {
            if (_root != null) { _root.SetActive(false); Object.Destroy(_root); _root = null; }
            _ready = false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            DestroyOwnedRoot();
        }
    }
}
