using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SamSWAT.FireSupport.ArysReloaded.Utils;

internal static class AssetLoader
{
    private static readonly Dictionary<string, AssetBundle> s_loadedBundles = new();
    private static readonly HashSet<string> s_ownedBundleKeys = new();
    private static readonly Dictionary<string, SemaphoreSlim> s_bundleLoadLocks = new();
    private static readonly object s_bundleLockGate = new();

    private static async UniTask<AssetBundle> LoadBundleAsync(string bundleName)
    {
        return await LoadBundleAsync(bundleName, bundleName, bundleName);
    }

    private static async UniTask<AssetBundle> LoadNativeBundleAsync(string bundleName)
    {
        string normalizedBundleName = bundleName
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        string cacheKey = "native:" + normalizedBundleName;

        if (TryGetCachedBundle(cacheKey, out AssetBundle cachedBundle))
        {
            return cachedBundle;
        }

        SemaphoreSlim gate = GetBundleGate(cacheKey);
        await gate.WaitAsync();
        try
        {
            if (TryGetCachedBundle(cacheKey, out cachedBundle))
            {
                return cachedBundle;
            }

            AssetBundle alreadyLoadedBundle = FindLoadedBundle(bundleName);
            if (alreadyLoadedBundle != null)
            {
                s_loadedBundles[cacheKey] = alreadyLoadedBundle;
                SamSWAT.FireSupport.ArysReloaded.Unity.TscDiagnostics.LogLcd($"Native asset bundle already loaded: {bundleName}");
                return alreadyLoadedBundle;
            }

            string[] candidatePaths =
            [
                Path.Combine(Application.dataPath, "StreamingAssets", "Windows", normalizedBundleName),
                Path.GetFullPath(Path.Combine(FireSupportPlugin.Directory, "..", "..", "..", "EscapeFromTarkov_Data", "StreamingAssets", "Windows", normalizedBundleName))
            ];

            foreach (string bundlePath in candidatePaths)
            {
                if (!File.Exists(bundlePath))
                {
                    continue;
                }

                return await LoadBundleFromFileAsync(bundlePath, cacheKey, bundlePath, alreadyHoldingGate: true);
            }

            FireSupportPlugin.LogSource.LogError($"Can't find native bundle: {bundleName}.\nChecked: {string.Join(", ", candidatePaths)}");
            return null;
        }
        finally
        {
            gate.Release();
        }
    }

    private static async UniTask<AssetBundle> LoadBundleAsync(string bundlePath, string cacheKey, string logName)
    {
        if (TryGetCachedBundle(cacheKey, out AssetBundle cachedBundle))
        {
            return cachedBundle;
        }

        SemaphoreSlim gate = GetBundleGate(cacheKey);
        await gate.WaitAsync();
        try
        {
            if (TryGetCachedBundle(cacheKey, out cachedBundle))
            {
                return cachedBundle;
            }

            AssetBundle alreadyLoadedBundle = FindLoadedBundle(logName) ?? FindLoadedBundle(bundlePath) ?? FindLoadedBundle(cacheKey);
            if (alreadyLoadedBundle != null)
            {
                s_loadedBundles[cacheKey] = alreadyLoadedBundle;
                SamSWAT.FireSupport.ArysReloaded.Unity.TscDiagnostics.LogLcd($"Asset bundle already loaded: {logName}");
                return alreadyLoadedBundle;
            }

            return await LoadBundleFromFileAsync(bundlePath, cacheKey, logName, alreadyHoldingGate: true);
        }
        finally
        {
            gate.Release();
        }
    }

    private static async UniTask<AssetBundle> LoadBundleFromFileAsync(
        string bundlePath,
        string cacheKey,
        string logName,
        bool alreadyHoldingGate)
    {
        if (!alreadyHoldingGate)
        {
            SemaphoreSlim gate = GetBundleGate(cacheKey);
            await gate.WaitAsync();
            try
            {
                return await LoadBundleFromFileAsync(bundlePath, cacheKey, logName, alreadyHoldingGate: true);
            }
            finally
            {
                gate.Release();
            }
        }

        if (TryGetCachedBundle(cacheKey, out AssetBundle cachedBundle))
        {
            return cachedBundle;
        }

        string resolvedPath = Path.IsPathRooted(bundlePath)
            ? bundlePath
            : Path.Combine(FireSupportPlugin.Directory, bundlePath);

        AssetBundleCreateRequest bundleRequest = AssetBundle.LoadFromFileAsync(resolvedPath);
        while (!bundleRequest.isDone)
        {
            await UniTask.Yield();
        }

        AssetBundle requestedBundle = bundleRequest.assetBundle;
        if (requestedBundle != null)
        {
            s_loadedBundles[cacheKey] = requestedBundle;
            s_ownedBundleKeys.Add(cacheKey);
            SamSWAT.FireSupport.ArysReloaded.Unity.TscDiagnostics.LogLcd($"Asset bundle loaded: {logName}");
            return requestedBundle;
        }

        // Unity returns null with "same files already loaded" when another load won the race.
        // Re-scan loaded bundles before treating this as a hard failure.
        AssetBundle alreadyLoadedBundle = FindLoadedBundle(logName) ?? FindLoadedBundle(resolvedPath) ?? FindLoadedBundle(bundlePath);
        if (alreadyLoadedBundle != null)
        {
            s_loadedBundles[cacheKey] = alreadyLoadedBundle;
            SamSWAT.FireSupport.ArysReloaded.Unity.TscDiagnostics.LogLcd($"Asset bundle resolved from already loaded instance after load race: {logName}");
            return alreadyLoadedBundle;
        }

        FireSupportPlugin.LogSource.LogError($"Can't load bundle: {logName} (does it exist?), unknown error.");
        return null;
    }

    private static bool TryGetCachedBundle(string cacheKey, out AssetBundle bundle)
    {
        if (s_loadedBundles.TryGetValue(cacheKey, out bundle) && bundle != null)
        {
            return true;
        }

        if (s_loadedBundles.ContainsKey(cacheKey))
        {
            s_loadedBundles.Remove(cacheKey);
            s_ownedBundleKeys.Remove(cacheKey);
        }

        bundle = null;
        return false;
    }

    private static SemaphoreSlim GetBundleGate(string cacheKey)
    {
        lock (s_bundleLockGate)
        {
            if (!s_bundleLoadLocks.TryGetValue(cacheKey, out SemaphoreSlim gate))
            {
                gate = new SemaphoreSlim(1, 1);
                s_bundleLoadLocks[cacheKey] = gate;
            }

            return gate;
        }
    }

    private static AssetBundle FindLoadedBundle(string bundleName)
    {
        if (string.IsNullOrWhiteSpace(bundleName))
        {
            return null;
        }

        string requestedName = NormalizeBundleName(bundleName);
        string requestedFileName = Path.GetFileName(requestedName);
        foreach (AssetBundle loadedBundle in AssetBundle.GetAllLoadedAssetBundles())
        {
            string loadedName = NormalizeBundleName(loadedBundle.name);
            if (string.IsNullOrEmpty(loadedName))
            {
                continue;
            }

            if (loadedName == requestedName ||
                loadedName == requestedFileName ||
                loadedName.EndsWith("/" + requestedFileName))
            {
                return loadedBundle;
            }
        }

        return null;
    }

    private static string NormalizeBundleName(string bundleName)
    {
        return bundleName
            .Replace('\\', '/')
            .Replace(Path.DirectorySeparatorChar, '/')
            .ToLowerInvariant();
    }

    public static UniTask<GameObject> LoadAssetAsync(string bundle, string assetName = null)
    {
        return LoadAssetAsync<GameObject>(bundle, assetName);
    }

    public static async UniTask<T> LoadAssetAsync<T>(string bundle, string assetName = null) where T : Object
    {
        AssetBundle ab = await LoadBundleAsync(bundle);
        return await LoadAssetFromBundleAsync<T>(ab, bundle, assetName);
    }

    public static UniTask<GameObject> LoadNativeAssetAsync(string bundle, string assetName = null)
    {
        return LoadNativeAssetAsync<GameObject>(bundle, assetName);
    }

    public static async UniTask<T> LoadNativeAssetAsync<T>(string bundle, string assetName = null) where T : Object
    {
        AssetBundle ab = await LoadNativeBundleAsync(bundle);
        return await LoadAssetFromBundleAsync<T>(ab, bundle, assetName);
    }

    private static async UniTask<T> LoadAssetFromBundleAsync<T>(AssetBundle ab, string bundle, string assetName) where T : Object
    {
        if (ab == null)
        {
            return null;
        }

        AssetBundleRequest assetBundleRequest = string.IsNullOrEmpty(assetName)
            ? ab.LoadAllAssetsAsync()
            : ab.LoadAssetAsync<T>(assetName);
        while (!assetBundleRequest.isDone)
        {
            await UniTask.Yield();
        }

        T requestedObj = assetBundleRequest.asset as T;
        if (requestedObj == null && assetBundleRequest.allAssets.Length > 0)
        {
            requestedObj = assetBundleRequest.allAssets[0] as T;
        }

        if (requestedObj == null && !string.IsNullOrEmpty(assetName))
        {
            AssetBundleRequest fallbackRequest = ab.LoadAllAssetsAsync();
            while (!fallbackRequest.isDone)
            {
                await UniTask.Yield();
            }

            if (fallbackRequest.allAssets.Length > 0)
            {
                requestedObj = fallbackRequest.allAssets[0] as T;
                SamSWAT.FireSupport.ArysReloaded.Unity.TscDiagnostics.LogLcd(
                    $"Loaded fallback Object from bundle: {bundle}, requested asset: {assetName}, fallback asset: {requestedObj?.name ?? "null"}.");
            }
        }

        if (requestedObj == null)
        {
            string assetDescription = string.IsNullOrEmpty(assetName) ? "first asset" : assetName;
            FireSupportPlugin.LogSource.LogError($"Can't load Object from bundle: {bundle}, asset: {assetDescription}.");
            return null;
        }

        return requestedObj;
    }

    public static void UnloadBundle(string bundleName, bool unloadAllLoadedObjects = true)
    {
        if (s_loadedBundles.TryGetValue(bundleName, out AssetBundle ab))
        {
            if (s_ownedBundleKeys.Contains(bundleName))
            {
                ab.Unload(unloadAllLoadedObjects);
                s_ownedBundleKeys.Remove(bundleName);
            }

            s_loadedBundles.Remove(bundleName);
        }
        else
        {
            FireSupportPlugin.LogSource.LogError($"AssetBundle '{bundleName}' already unloaded");
        }
    }

    public static void UnloadAllBundles(bool unloadAllLoadedObjects = true)
    {
        foreach (KeyValuePair<string, AssetBundle> bundle in s_loadedBundles)
        {
            if (s_ownedBundleKeys.Contains(bundle.Key) && bundle.Value != null)
            {
                bundle.Value.Unload(unloadAllLoadedObjects);
            }
        }

        s_loadedBundles.Clear();
        s_ownedBundleKeys.Clear();
    }
}
