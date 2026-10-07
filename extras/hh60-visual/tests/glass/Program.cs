using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using TscHh60Visual;

internal static class Program
{
    private static int passes;
    private static void Check(string name, bool condition)
    {
        if (!condition) throw new InvalidOperationException("FAIL " + name);
        passes++;
        Console.WriteLine("PASS " + name);
    }

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                CheckPredicates();
                CheckSyntheticPayload();
                Console.WriteLine("RESULT " + passes + "/" + passes + " PASS; synthetic managed policy/mapping only, no game assets or Unity runtime");
                return 0;
            }
            if (args.Length < 2 || args[0] != "--input")
                throw new ArgumentException("Usage: GlassPolicyTests [--input /path/to/local/scene.json [--mapping baseline|modified|rollback]]");
            string input = Path.GetFullPath(args[1]);
            byte[] bytes = File.ReadAllBytes(input);
            using var document = JsonDocument.Parse(bytes);
            if (args.Length == 4 && args[2] == "--mapping")
            {
                if (!new[] { "baseline", "modified", "rollback" }.Contains(args[3]))
                    throw new ArgumentException("Unknown mapping mode");
                PrintMapping(document.RootElement, bytes, args[3]);
                return 0;
            }
            if (args.Length != 2) throw new ArgumentException("Unexpected arguments");
            CheckPredicates();
            CheckRealPayload(document.RootElement);
            Console.WriteLine("INPUT_SHA256 " + Convert.ToHexString(SHA256.HashData(bytes)));
            Console.WriteLine("RESULT " + passes + "/" + passes + " PASS; managed policy/mapping only, no Unity rendering or game execution");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e.ToString()); return 1; }
    }

    private static void CheckPredicates()
    {
        string id = GlassMaterialPolicy.WindowId, name = GlassMaterialPolicy.WindowName;
        string original = GlassMaterialPolicy.OriginalName, shader = GlassMaterialPolicy.OriginalShader;
        Check("window exact ID and name", GlassMaterialPolicy.IsHh60Window(id, name));
        Check("window correct ID wrong name rejected", !GlassMaterialPolicy.IsHh60Window(id, "wrong"));
        Check("window wrong ID correct name rejected", !GlassMaterialPolicy.IsHh60Window("wrong", name));
        Check("window both wrong rejected", !GlassMaterialPolicy.IsHh60Window("wrong", "wrong"));
        Check("window case-sensitive ID", !GlassMaterialPolicy.IsHh60Window(id.ToLowerInvariant(), name));
        Check("window case-sensitive name", !GlassMaterialPolicy.IsHh60Window(id, name.ToLowerInvariant()));
        Check("window suffix not accepted", !GlassMaterialPolicy.IsHh60Window(id, name + " (Instance)"));
        Check("window whitespace not accepted", !GlassMaterialPolicy.IsHh60Window(id, name + " "));
        Check("window partial ID not accepted", !GlassMaterialPolicy.IsHh60Window(":8", name));
        Check("window null ID rejected", !GlassMaterialPolicy.IsHh60Window(null, name));
        Check("window null name rejected", !GlassMaterialPolicy.IsHh60Window(id, null));
        Check("window both null rejected", !GlassMaterialPolicy.IsHh60Window(null, null));
        Check("window empty rejected", !GlassMaterialPolicy.IsHh60Window("", ""));
        Check("native exact material and shader", GlassMaterialPolicy.IsOriginalGlass(original, shader));
        Check("native Instance material accepted", GlassMaterialPolicy.IsOriginalGlass(original + " (Instance)", shader));
        Check("native wrong shader rejected", !GlassMaterialPolicy.IsOriginalGlass(original, "EFT/Glass"));
        Check("native Instance wrong shader rejected", !GlassMaterialPolicy.IsOriginalGlass(original + " (Instance)", "Standard"));
        Check("native wrong material rejected", !GlassMaterialPolicy.IsOriginalGlass("Helicopter_Sikorsky_HH60_glass", shader));
        Check("native case-sensitive material", !GlassMaterialPolicy.IsOriginalGlass(original.ToLowerInvariant(), shader));
        Check("native case-sensitive shader", !GlassMaterialPolicy.IsOriginalGlass(original, shader.ToLowerInvariant()));
        Check("native whitespace not accepted", !GlassMaterialPolicy.IsOriginalGlass(original + " ", shader));
        Check("native duplicate Instance not accepted", !GlassMaterialPolicy.IsOriginalGlass(original + " (Instance) (Instance)", shader));
        Check("native Clone suffix not accepted", !GlassMaterialPolicy.IsOriginalGlass(original + " (Clone)", shader));
        Check("native null name rejected", !GlassMaterialPolicy.IsOriginalGlass(null, shader));
        Check("native null shader rejected", !GlassMaterialPolicy.IsOriginalGlass(original, null));
        Check("native both null rejected", !GlassMaterialPolicy.IsOriginalGlass(null, null));
        Check("native empty rejected", !GlassMaterialPolicy.IsOriginalGlass("", ""));
    }

    private static Dictionary<string, JsonElement> Materials(JsonElement root) =>
        root.GetProperty("materials").EnumerateArray().ToDictionary(m => m.GetProperty("id").GetString(), StringComparer.Ordinal);
    private static HashSet<string> Windows(JsonElement root) =>
        new HashSet<string>(root.GetProperty("materials").EnumerateArray()
            .Where(m => GlassMaterialPolicy.IsHh60Window(m.GetProperty("id").GetString(), m.GetProperty("name").GetString()))
            .Select(m => m.GetProperty("id").GetString()), StringComparer.Ordinal);

    private static void CheckSyntheticPayload()
    {
        // Artificial material and renderer records. No geometry, textures, character
        // poses or copied scene manifest are required for this public CI fixture.
        string window = GlassMaterialPolicy.WindowId;
        using var fixture = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            materials = new[]
            {
                new { id = window, name = GlassMaterialPolicy.WindowName },
                new { id = "metal", name = "synthetic-frame" },
                new { id = "eyewear", name = "synthetic-crew-glass" },
                new { id = "optic", name = "synthetic-weapon-glass" },
                new { id = "decoy", name = GlassMaterialPolicy.WindowName },
                new { id = "other", name = "synthetic-transparent" }
            },
            renderers = new[]
            {
                new { node = "cockpit", owner = "aircraft", materials = new[] { window } },
                new { node = "left-door", owner = "aircraft", materials = new[] { window, "metal" } },
                new { node = "right-door", owner = "aircraft", materials = new[] { window, "metal" } },
                new { node = "person", owner = "crew", materials = new[] { "eyewear", "optic", "metal" } },
                new { node = "unrelated", owner = "aircraft", materials = new[] { "decoy", "other" } }
            }
        }));
        var root = fixture.RootElement;
        var materials = Materials(root);
        var windows = Windows(root);
        Check("synthetic exactly one allowlisted aircraft material", windows.SetEquals(new[] { window }));
        var originals = materials.ToDictionary(p => p.Key, p => new object(), StringComparer.Ordinal);
        object nativeGlass = new object();
        var changed = new HashSet<string>(StringComparer.Ordinal);
        int unchanged = 0;
        foreach (var renderer in root.GetProperty("renderers").EnumerateArray())
        {
            string node = renderer.GetProperty("node").GetString();
            string owner = renderer.GetProperty("owner").GetString();
            var ids = renderer.GetProperty("materials").EnumerateArray().Select(m => m.GetString()).ToArray();
            object[] before = ids.Select(id => originals[id]).ToArray();
            // Mirrors the production slot selection. It does not simulate Unity.
            object[] after = ids.Select(id => windows.Contains(id) ? nativeGlass : originals[id]).ToArray();
            for (int slot = 0; slot < ids.Length; slot++)
            {
                bool differs = !ReferenceEquals(before[slot], after[slot]);
                if (differs)
                {
                    changed.Add(node + ":" + slot);
                    Check("synthetic replaced window shares native material " + node, ReferenceEquals(after[slot], nativeGlass));
                    Check("synthetic replaced slot is aircraft window " + node, owner == "aircraft" && ids[slot] == window);
                }
                else unchanged++;
                if (ids[slot] == "metal")
                    Check("synthetic metal identity preserved " + node, !differs);
                if (owner == "crew")
                    Check("synthetic crew identity preserved " + ids[slot], !differs);
                if (node == "unrelated")
                    Check("synthetic nonallowlisted glass unchanged " + ids[slot], !differs);
            }
        }
        Check("synthetic only cockpit and two door window slots changed",
            changed.SetEquals(new[] { "cockpit:0", "left-door:0", "right-door:0" }));
        Check("synthetic seven unrelated slots unchanged", unchanged == 7);
        Check("synthetic original material references not mutated",
            originals.Values.Distinct().Count() == materials.Count && !originals.Values.Contains(nativeGlass));
    }

    private static void CheckRealPayload(JsonElement root)
    {
        var materials = Materials(root);
        var windowIds = Windows(root);
        Check("real payload exactly one allowlisted material", windowIds.Count == 1);
        Check("real payload 61 materials", materials.Count == 61);
        Check("real payload 117 renderers", root.GetProperty("renderers").GetArrayLength() == 117);
        var unrelatedGlass = materials.Values.Where(m => m.GetProperty("name").GetString().Contains("glass", StringComparison.OrdinalIgnoreCase)
            && m.GetProperty("id").GetString() != GlassMaterialPolicy.WindowId).ToArray();
        Check("real payload includes crew eyewear and optic glass", unrelatedGlass.Any(m => m.GetProperty("name").GetString() == "Item_equipment_glasses_oakley_glass")
            && unrelatedGlass.Any(m => m.GetProperty("name").GetString() == "scope_base_aimpoint_micro_t1_LOD0_glass"));
        foreach (var material in unrelatedGlass)
            Check("real non-aircraft glass rejected: " + material.GetProperty("name").GetString(),
                !GlassMaterialPolicy.IsHh60Window(material.GetProperty("id").GetString(), material.GetProperty("name").GetString()));

        var refs = materials.ToDictionary(p => p.Key, p => new object(), StringComparer.Ordinal);
        object nativeGlass = new object();
        int changed = 0, untouched = 0, crew = 0, preservedFrames = 0;
        var replaced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var renderer in root.GetProperty("renderers").EnumerateArray())
        {
            string node = renderer.GetProperty("node").GetString();
            var ids = renderer.GetProperty("materials").EnumerateArray().Select(m => m.GetString()).ToArray();
            object[] before = ids.Select(id => refs[id]).ToArray();
            // This mirrors Plugin.BuildVisual's Select mapping; it does not execute Unity APIs.
            object[] after = ids.Select(id => windowIds.Contains(id) ? nativeGlass : refs[id]).ToArray();
            for (int slot = 0; slot < ids.Length; slot++)
            {
                if (!ReferenceEquals(before[slot], after[slot]))
                {
                    changed++;
                    replaced.Add(node + ":" + slot);
                    Check("real changed slot " + node + ":" + slot + " is window0 only", slot == 0 && ids[slot] == GlassMaterialPolicy.WindowId);
                    Check("real shared native material identity " + node, ReferenceEquals(after[slot], nativeGlass));
                }
                else
                {
                    untouched++;
                    if (renderer.GetProperty("owner").GetString() != "aircraft") crew++;
                }
                if ((node == "9528" || node == "8319") && slot == 1)
                {
                    preservedFrames++;
                    Check("real door-frame slot1 unchanged " + node,
                        ids[slot] == "BuildPlayer-Icebreaker_cutscene_01.sharedAssets:7" && ReferenceEquals(before[slot], after[slot]));
                }
            }
        }
        Check("real exactly three replacements", changed == 3);
        Check("real expected cockpit/right/left slots", replaced.SetEquals(new[] { "6881:0", "9528:0", "8319:0" }));
        Check("real both door-frame slots preserved", preservedFrames == 2);
        int totalSlots = root.GetProperty("renderers").EnumerateArray().Sum(r => r.GetProperty("materials").GetArrayLength());
        Check("real every other slot preserves identity", untouched == totalSlots - 3);
        int crewSlots = root.GetProperty("renderers").EnumerateArray().Where(r => r.GetProperty("owner").GetString() != "aircraft").Sum(r => r.GetProperty("materials").GetArrayLength());
        Check("real every crew slot preserves identity", crew == crewSlots && crew > 0);
        Check("real source materials unmodified", refs.Values.Distinct().Count() == materials.Count && !refs.Values.Contains(nativeGlass));
        Console.WriteLine("SCOPE " + changed + " changed window slots; " + untouched + " unchanged slots; " + crew + " unchanged crew slots; " + preservedFrames + " unchanged door-frame slots");
    }

    private static void PrintMapping(JsonElement root, byte[] bytes, string mode)
    {
        var windows = Windows(root);
        var nodes = root.GetProperty("nodes").EnumerateArray().ToDictionary(n => n.GetProperty("id").GetString(), n => n.GetProperty("name").GetString());
        int changed = 0, unchanged = 0;
        var entries = new List<object>();
        foreach (var r in root.GetProperty("renderers").EnumerateArray())
        {
            string node = r.GetProperty("node").GetString();
            int slot = 0;
            foreach (var m in r.GetProperty("materials").EnumerateArray())
            {
                string id = m.GetString();
                bool replace = mode == "modified" && windows.Contains(id);
                if (replace) changed++; else unchanged++;
                entries.Add(new { node, name = nodes[node], slot = slot++, before = id,
                    after = replace ? "native:" + GlassMaterialPolicy.OriginalName : id, changed = replace });
            }
        }
        Console.WriteLine(JsonSerializer.Serialize(new { mode, validation = "managed-material-mapping-only",
            inputSha256 = Convert.ToHexString(SHA256.HashData(bytes)), changedSlots = changed, unchangedSlots = unchanged, mappings = entries },
            new JsonSerializerOptions { WriteIndented = true }));
    }
}
