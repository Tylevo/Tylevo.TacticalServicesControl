using System;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using TscHh60Visual;
using UnityEngine;
using Object = UnityEngine.Object;

internal static class Program
{
    private static int passes;
    private static void Check(string label, bool condition)
    {
        if (!condition) throw new InvalidOperationException("FAIL " + label);
        passes++; Console.WriteLine("PASS " + label);
    }

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 0)
            {
                if (args.Length != 2 || args[0] != "--expect-core-sha") throw new ArgumentException("Expected --expect-core-sha <SHA256>.");
                VerifyCoreIdentity(args[1]);
            }
            OriginalIsLazyAndSelectionIsDeferredToUpdate();
            SwitchDuringSharedCacheWaitFinishesHidden();
            SwitchDuringInstanceBuildAndRepeatWithoutAllocations();
            InactivePooledAircraftUseTheLatestSelection();
            BenchVisualStaysHh60WhenTransportSelectionChanges();
            FailuresRestoreOriginalFlags();
            DestroyWhileWaitingStopsTheInstanceCoroutine();
            Console.WriteLine("RESULT " + passes + "/" + passes + " PASS; actual Plugin.cs selection/build lifecycle with synthetic Unity, cache and scheduler. No native rendering, payload decode, BepInEx patching, or game execution.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void VerifyCoreIdentity(string expected)
    {
        Check("compiled Plugin constant uses the requested Core identity", Plugin.ExpectedTscSha256 == expected);
        var checkHash = typeof(Plugin).GetMethod("MatchesHash", BindingFlags.NonPublic | BindingFlags.Static);
        string location = typeof(Program).Assembly.Location;
        using var stream = System.IO.File.OpenRead(location);
        using var sha = System.Security.Cryptography.SHA256.Create();
        string actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        Check("runtime guard accepts exact file bytes", (bool)checkHash.Invoke(null, new object[] { location, actual }));
        Check("runtime guard rejects different file bytes", !(bool)checkHash.Invoke(null, new object[] { location, expected }));
    }

    private static void OriginalIsLazyAndSelectionIsDeferredToUpdate()
    {
        using var fixture = new Fixture();
        int objects = GameObject.CreatedCount;
        Check("original selection leaves original flags intact", fixture.OriginalFlagsRestored);
        Check("original selection does not request resource cache", SharedResources.Requests == 0);
        Check("original selection does not schedule construction", TestRuntime.PendingCount == 0 && fixture.Marker == null);
        Plugin.Model.Value = HelicopterModel.HH60;
        Check("config event does no Unity work before host Update", GameObject.CreatedCount == objects && SharedResources.Requests == 0);
        fixture.Update();
        Check("host Update starts HH60 construction", SharedResources.Requests == 1 && TestRuntime.PendingCount == 1);
        Check("original is visible until replacement is complete", fixture.OriginalFlagsRestored && fixture.Marker != null && !fixture.Marker.activeSelf);
        TestRuntime.Drain();
        Check("HH60 selection shows complete replacement", fixture.Marker.activeSelf && fixture.Original.All(renderer => renderer.forceRenderingOff));
        var renderers = fixture.Marker.GetComponentsInChildren<MeshRenderer>(true);
        Check("all three synthetic window slots borrow native glass", renderers.Length == 3 && renderers.All(renderer => ReferenceEquals(renderer.sharedMaterials[0], fixture.NativeGlass)));
        Check("unrelated door material remains shared cache material", ReferenceEquals(renderers[1].sharedMaterials[1], fixture.Cache.Materials["metal"]));
    }

    private static void SwitchDuringSharedCacheWaitFinishesHidden()
    {
        using var fixture = new Fixture();
        SharedResources.Current = null;
        fixture.Select(HelicopterModel.HH60);
        Check("waiting for shared cache retains original", fixture.OriginalFlagsRestored && fixture.Marker == null && TestRuntime.PendingCount == 1);
        fixture.Select(HelicopterModel.UH60);
        TestRuntime.Tick();
        Check("switch to original does not cancel or poison pending build", fixture.OriginalFlagsRestored && SharedResources.Failure == null && TestRuntime.PendingCount == 1);
        SharedResources.Current = fixture.Cache;
        TestRuntime.Drain();
        Check("cache becoming ready completes HH60 hidden", fixture.Marker != null && !fixture.Marker.activeSelf && fixture.OriginalFlagsRestored);
        int objects = GameObject.CreatedCount;
        fixture.Select(HelicopterModel.HH60);
        Check("HH60 can show after hidden completion", fixture.Marker.activeSelf && fixture.Original.All(renderer => renderer.forceRenderingOff));
        Check("hidden completed visual is reused", GameObject.CreatedCount == objects && SharedResources.Requests == 1);
    }

    private static void SwitchDuringInstanceBuildAndRepeatWithoutAllocations()
    {
        using var fixture = new Fixture();
        fixture.Select(HelicopterModel.HH60);
        Check("partial node construction stays inactive", fixture.Marker != null && !fixture.Marker.activeSelf && fixture.OriginalFlagsRestored);
        fixture.Select(HelicopterModel.UH60);
        TestRuntime.Drain();
        Check("switch during node construction retains original after completion", fixture.OriginalFlagsRestored && !fixture.Marker.activeSelf);
        GameObject marker = fixture.Marker;
        int objects = GameObject.CreatedCount;
        for (int index = 0; index < 8; index++)
        {
            fixture.Select(HelicopterModel.HH60);
            Check("HH60 swap hides all original renderers " + index, fixture.Original.All(renderer => renderer.forceRenderingOff) && marker.activeSelf);
            fixture.Select(HelicopterModel.UH60);
            Check("UH60 swap restores each original force-off flag " + index, fixture.OriginalFlagsRestored && !marker.activeSelf);
        }
        Check("repeated swaps allocate no extra GameObjects", GameObject.CreatedCount == objects);
        Check("repeated swaps reuse exact replacement and cache", ReferenceEquals(marker, fixture.Marker) && SharedResources.Requests == 1);
        Check("repeated swaps retain exactly one marker", fixture.Aircraft.GetComponentsInChildren<Transform>(true).Count(node => node.name == Plugin.MarkerName) == 1);
    }

    private static void InactivePooledAircraftUseTheLatestSelection()
    {
        using var fixture = new Fixture();
        fixture.Select(HelicopterModel.HH60);
        TestRuntime.Drain();
        GameObject marker = fixture.Marker;
        int objects = GameObject.CreatedCount;
        fixture.Aircraft.SetActive(false);
        fixture.Select(HelicopterModel.UH60);
        Check("original selection updates inactive pooled aircraft", fixture.OriginalFlagsRestored && !marker.activeSelf);
        fixture.Aircraft.SetActive(true);
        Check("pooled reuse honours original selection", fixture.OriginalFlagsRestored && !marker.activeInHierarchy);
        fixture.Aircraft.SetActive(false);
        fixture.Select(HelicopterModel.HH60);
        Check("HH60 selection is prepared while aircraft inactive", marker.activeSelf && !marker.activeInHierarchy);
        fixture.Aircraft.SetActive(true);
        Check("pooled reuse honours HH60 selection", marker.activeInHierarchy && fixture.Original.All(renderer => renderer.forceRenderingOff));
        Check("pooled reuse does not recreate visual", ReferenceEquals(marker, fixture.Marker) && GameObject.CreatedCount == objects);
    }

    private static void FailuresRestoreOriginalFlags()
    {
        using (var fixture = new Fixture())
        {
            SharedResources.Current = null;
            SharedResources.Failure = new InvalidOperationException("synthetic resource failure");
            fixture.Select(HelicopterModel.HH60);
            TestRuntime.Drain();
            Check("shared cache failure retains original", fixture.OriginalFlagsRestored && fixture.Marker == null);
            Check("shared cache failure is reported", Plugin.Log.Errors.Count == 1);
            fixture.Select(HelicopterModel.UH60); fixture.Select(HelicopterModel.HH60);
            Check("failed instance does not repeatedly request construction", SharedResources.Requests == 1 && TestRuntime.PendingCount == 0);
        }
        using (var fixture = new Fixture())
        {
            fixture.Cache.Scene.renderers = fixture.Cache.Scene.renderers.Take(2).ToArray();
            fixture.Select(HelicopterModel.HH60);
            Check("invalid partial visual starts hidden", fixture.Marker != null && !fixture.Marker.activeSelf);
            TestRuntime.Drain();
            Check("invalid glass-slot count removes partial visual", fixture.Marker == null);
            Check("partial-build failure restores exact original flags", fixture.OriginalFlagsRestored);
            Check("partial-build failure is reported", Plugin.Log.Errors.Count == 1);
        }
    }

    private static void BenchVisualStaysHh60WhenTransportSelectionChanges()
    {
        using var fixture = new Fixture();
        fixture.Visual.PinToHh60();
        Check("bench visual waits for complete build", !fixture.Visual.IsReady);
        fixture.Select(HelicopterModel.UH60);
        TestRuntime.Drain();
        Check("bench completes as HH60 while transports select UH60", fixture.Visual.IsReady && fixture.Marker.activeSelf);
        fixture.Select(HelicopterModel.HH60);
        fixture.Select(HelicopterModel.UH60);
        Check("transport selection cannot hide the bench gun hierarchy", fixture.Marker.activeSelf && !fixture.Visual.HasFailed);
        fixture.Visual.AbortAndRestore();
        Check("pinned bench still restores original flags during disposal", fixture.OriginalFlagsRestored && !fixture.Visual.IsReady);
    }

    private static void DestroyWhileWaitingStopsTheInstanceCoroutine()
    {
        using var fixture = new Fixture();
        SharedResources.Current = null;
        fixture.Select(HelicopterModel.HH60);
        Object.Destroy(fixture.Visual);
        Check("destroying instance stops its persistent-host coroutine", TestRuntime.PendingCount == 0);
        SharedResources.Current = fixture.Cache;
        TestRuntime.Drain();
        fixture.Select(HelicopterModel.UH60);
        Check("destroyed instance cannot later add a visual", fixture.Marker == null && fixture.OriginalFlagsRestored);
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly Plugin Host;
        internal readonly GameObject Aircraft;
        internal readonly VisualInstance Visual;
        internal readonly Renderer[] Original;
        internal readonly Material NativeGlass;
        internal readonly SharedResources Cache;
        private readonly bool[] _originalFlags;
        internal GameObject Marker => Plugin.FindDescendant(Aircraft.transform, Plugin.MarkerName)?.gameObject;
        internal bool OriginalFlagsRestored => Original.Select(renderer => renderer.forceRenderingOff).SequenceEqual(_originalFlags);

        internal Fixture()
        {
            TestRuntime.Reset(); SharedResources.Shutdown();
            Host = new GameObject("persistent-plugin-host").AddComponent<Plugin>();
            // Bypass only external bootstrap (assembly hash, Harmony, payload path).
            // Actual production Update, SetModel, coroutine, build and cleanup run.
            typeof(Plugin).GetField("_host", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, Host);
            typeof(Plugin).GetField("_compatible", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(Host, true);
            Plugin.Log = Host.Logger;
            Plugin.Model = new ConfigEntry<HelicopterModel>(HelicopterModel.UH60);
            Plugin.Model.SettingChanged += (EventHandler)Delegate.CreateDelegate(typeof(EventHandler), Host,
                typeof(Plugin).GetMethod("OnModelChanged", BindingFlags.Instance | BindingFlags.NonPublic));
            Aircraft = new GameObject("pooled-aircraft");
            Transform anchor = Child(Aircraft.transform, "b_vhc_main");
            Child(anchor, "b_vhc_rotor"); Child(anchor, "b_vhc_rotor_tail");
            NativeGlass = new Material { name = GlassMaterialPolicy.OriginalName,
                shader = new Shader { name = GlassMaterialPolicy.OriginalShader } };
            var body = anchor.gameObject.AddComponent<MeshRenderer>();
            body.sharedMaterials = new[] { NativeGlass };
            var originallyHidden = Child(anchor, "originally-hidden").gameObject.AddComponent<SkinnedMeshRenderer>();
            originallyHidden.forceRenderingOff = true;
            Original = new Renderer[] { body, originallyHidden };
            _originalFlags = Original.Select(renderer => renderer.forceRenderingOff).ToArray();
            Cache = MakeCache(); SharedResources.Current = Cache;
            Visual = Aircraft.AddComponent<VisualInstance>();
            Visual.Initialize(Aircraft.transform, anchor, "synthetic/scene.json");
            Visual.SetModel(HelicopterModel.UH60);
        }
        internal void Update() => TestRuntime.Invoke(Host, "Update");
        internal void Select(HelicopterModel model) { Plugin.Model.Value = model; Update(); }
        public void Dispose() { TestRuntime.Invoke(Host, "OnDestroy"); TestRuntime.Reset(); }
        private static Transform Child(Transform parent, string name)
        {
            var child = new GameObject(name); child.transform.SetParent(parent, false); return child.transform;
        }
        private static SharedResources MakeCache()
        {
            string[] names = { "cockpit", "left-door", "right-door", "helicopter_top_rotor", "helicopter_tail_rotor" };
            var cache = new SharedResources
            {
                Scene = new SceneData
                {
                    nodes = names.Select(name => new NodeData { id = name, name = name, parent = null,
                        position = new float[] { 0, 0, 0 }, rotation = new float[] { 0, 0, 0, 1 }, scale = new float[] { 1, 1, 1 } }).ToArray(),
                    materials = new[] { new MaterialData { id = GlassMaterialPolicy.WindowId, name = GlassMaterialPolicy.WindowName },
                        new MaterialData { id = "metal", name = "metal" } },
                    renderers = names.Take(3).Select(name => new RendererData { node = name, mesh = "mesh",
                        materials = name == "left-door" ? new[] { GlassMaterialPolicy.WindowId, "metal" } : new[] { GlassMaterialPolicy.WindowId } }).ToArray()
                }
            };
            cache.Meshes.Add("mesh", new Mesh());
            cache.Materials.Add("metal", new Material { name = "metal" });
            return cache;
        }
    }
}
