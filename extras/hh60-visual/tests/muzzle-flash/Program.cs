using System;
using System.Linq;
using Comfort.Common;
using Diz.DependencyManager;
using Diz.Resources;
using EFT;
using TscHh60Visual;
using UnityEngine;
using Object = UnityEngine.Object;

internal static class Program
{
    private const string ExtractionMarker = "TSC_EXTRACTION_GUNS";
    private static int _checks;
    private static void Check(bool condition, string message) { _checks++; if (!condition) throw new Exception(message); }

    private sealed class Assets : IEasyAssets
    {
        internal readonly GameObject Template = new GameObject("utes_muzzleflash");
        internal readonly Material Material = new Material { name = "MuzzleFlashTest", shader = new Shader(), mainTexture = new Texture() };
        internal readonly DependencyGraph<IEasyBundle>.Token Token = new DependencyGraph<IEasyBundle>.Token();
        internal Assets()
        {
            var jet = Template.AddComponent<MuzzleJet>();
            jet.Chance = 1;
            jet.Particles = new[] { new MuzzleJet.Particle { Position = 0, Size = 1 }, new MuzzleJet.Particle { Position = 1, Size = 1 } };
        }
        public DependencyGraph<IEasyBundle>.Token Retain(string[] keys) => Token;
        public T GetAsset<T>(string bundle, string name) where T : Object => (name == Template.name ? (Object)Template : Material) as T;
    }

    private static GameObject Guns(GameObject aircraft, string markerName)
    {
        var marker = new GameObject(markerName); marker.transform.SetParent(aircraft.transform, false);
        foreach (var side in new[] { "left", "right" })
        {
            var mount = new GameObject("helicopter_gun_box_" + side); mount.transform.SetParent(marker.transform, false);
            var barrel = new GameObject("helicopter_gun_" + side); barrel.transform.SetParent(mount.transform, false);
        }
        return marker;
    }
    private static Transform Barrel(GameObject marker, string side) => Plugin.FindDescendant(marker.transform, "helicopter_gun_" + side);
    private static EscortMuzzleFlash Load(GameObject aircraft, string marker, out Assets assets)
    {
        assets = new Assets(); Singleton<IEasyAssets>.Instance = assets;
        var result = new EscortMuzzleFlash(aircraft, marker);
        var routine = result.Load(() => true);
        try { Check(!routine.MoveNext(), "already-loaded synthetic assets should complete without yielding"); }
        finally { (routine as IDisposable)?.Dispose(); }
        return result;
    }
    private static void CheckPulses(EscortMuzzleFlash flash, GameObject guns, string scope)
    {
        Check(flash.Available, scope + ": production flash loader must bind the requested guns: " + flash.Failure);
        foreach (var side in new[] { "left", "right" })
        {
            var barrel = Barrel(guns, side);
            var renderer = barrel.gameObject.GetComponentInChildren<MeshRenderer>(true);
            Check(renderer != null && !renderer.enabled, scope + ": flash starts hidden on " + side);
            int random = UnityEngine.Random.state;
            var origin = barrel.TransformPoint(new Vector3(0, 1, 0));
            flash.Pulse(barrel, origin, Vector3.up);
            Check(flash.Available && renderer.enabled, scope + ": exact selected barrel must pulse " + side);
            Check((renderer.transform.parent.position - origin).sqrMagnitude < .000001f, scope + ": flash follows the selected muzzle");
            Check(UnityEngine.Random.state == random, scope + ": presentation must preserve shared RNG");
            flash.Tick(.12f);
            Check(!renderer.enabled, scope + ": pulse expires without another shot");
        }
    }
    private static void FreshUh60()
    {
        var aircraft = new GameObject("b_vhc_main");
        var guns = Guns(aircraft, ExtractionMarker);
        using var flash = Load(aircraft, ExtractionMarker, out var assets);
        CheckPulses(flash, guns, "fresh UH60");
        var meshes = guns.GetComponentsInChildren<MeshFilter>(true).Select(filter => filter.sharedMesh).ToArray();
        flash.Dispose();
        Check(guns != null && Barrel(guns, "left") != null, "cleanup retains extraction-owned gun hierarchy");
        Check(guns.GetComponentsInChildren<MeshRenderer>(true).Length == 0 && meshes.All(mesh => mesh == null), "cleanup releases only flash meshes/objects");
        Check(assets.Token.Releases == 1 && assets.Template != null && assets.Material != null, "cleanup releases retain once without destroying shared assets");
    }
    private static void CachedModelSwitch()
    {
        var aircraft = new GameObject("b_vhc_main");
        var cached = Guns(aircraft, Plugin.MarkerName);
        using (var original = Load(aircraft, Plugin.MarkerName, out _)) CheckPulses(original, cached, "HH60 before switch");
        cached.SetActive(false);
        var extraction = Guns(aircraft, ExtractionMarker);
        using (var switched = Load(aircraft, ExtractionMarker, out _))
        {
            CheckPulses(switched, extraction, "UH60 with cached HH60");
            Check(cached.GetComponentsInChildren<MeshRenderer>(true).Length == 0, "UH60 flash preparation must leave cached HH60 barrels untouched");
        }
        Object.Destroy(extraction);
        cached.SetActive(true);
        using (var returned = Load(aircraft, Plugin.MarkerName, out _)) CheckPulses(returned, cached, "HH60 after return");
        Check(cached != null, "switching effects never destroys cached appearance");
    }
    private static void MissingOrInactiveSelection()
    {
        var aircraft = new GameObject("b_vhc_main");
        var other = Guns(aircraft, Plugin.MarkerName);
        using (var missing = Load(aircraft, ExtractionMarker, out var assets))
        {
            Check(!missing.Available && assets.Token.Releases == 1, "missing requested marker must fail and release resources");
            Check(other.GetComponentsInChildren<MeshRenderer>(true).Length == 0, "missing requested marker must never fall back to another hierarchy");
        }
        other.SetActive(false);
        using var hidden = Load(aircraft, Plugin.MarkerName, out var hiddenAssets);
        Check(!hidden.Available && hiddenAssets.Token.Releases == 1, "inactive selected marker must not bind hidden barrels");
    }
    public static int Main()
    {
        try
        {
            FreshUh60(); CachedModelSwitch(); MissingOrInactiveSelection();
            Console.WriteLine("PASS " + _checks + " production muzzle-flash hierarchy, pulse and cleanup checks (synthetic Unity/assets; no native rendering).");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
