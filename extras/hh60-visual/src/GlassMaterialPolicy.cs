using System;

namespace TscHh60Visual
{
    // Exact allowlist: never change crew eyewear, weapon optics, metal, or rotor blur.
    internal static class GlassMaterialPolicy
    {
        internal const string WindowId = "BuildPlayer-Icebreaker_cutscene_01.sharedAssets:8";
        internal const string WindowName = "Helicopter_Sikorsky_HH60_glass";
        internal const string OriginalName = "MI_VH_BlackHawk_Glass";
        internal const string OriginalShader = "Global Fog/Transparent Reflective Specular";

        internal static bool IsHh60Window(string id, string name)
        {
            return string.Equals(id, WindowId, StringComparison.Ordinal)
                && string.Equals(name, WindowName, StringComparison.Ordinal);
        }

        internal static bool IsOriginalGlass(string name, string shader)
        {
            return (string.Equals(name, OriginalName, StringComparison.Ordinal)
                || string.Equals(name, OriginalName + " (Instance)", StringComparison.Ordinal))
                && string.Equals(shader, OriginalShader, StringComparison.Ordinal);
        }
    }
}
