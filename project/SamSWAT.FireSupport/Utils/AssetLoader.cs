using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SamSWAT.FireSupport.ArysReloaded.Utils;

internal static class AssetLoader
{
	private static readonly Dictionary<string, AssetBundle> s_loadedBundles = new();
	private static readonly HashSet<string> s_ownedBundleKeys = new();

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

		if (s_loadedBundles.TryGetValue(cacheKey, out AssetBundle cachedBundle))
		{
			return cachedBundle;
		}

		AssetBundle alreadyLoadedBundle = FindLoadedNativeBundle(bundleName);
		if (alreadyLoadedBundle != null)
		{
			s_loadedBundles.Add(cacheKey, alreadyLoadedBundle);
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

			return await LoadBundleAsync(bundlePath, cacheKey, bundlePath);
		}

		FireSupportPlugin.LogSource.LogError($"Can't find native bundle: {bundleName}. Checked: {string.Join(", ", candidatePaths)}");
		return null;
	}

	private static AssetBundle FindLoadedNativeBundle(string bundleName)
	{
		string requestedName = bundleName.Replace('\\', '/').ToLowerInvariant();
		string requestedFileName = Path.GetFileName(requestedName);

		foreach (AssetBundle loadedBundle in AssetBundle.GetAllLoadedAssetBundles())
		{
			string loadedName = loadedBundle.name?.Replace('\\', '/').ToLowerInvariant();
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

	private static async UniTask<AssetBundle> LoadBundleAsync(string bundlePath, string cacheKey, string logName)
	{
		if (s_loadedBundles.TryGetValue(cacheKey, out AssetBundle bundle))
		{
			return bundle;
		}


		AssetBundleCreateRequest bundleRequest = AssetBundle.LoadFromFileAsync(
			Path.IsPathRooted(bundlePath) ? bundlePath : Path.Combine(FireSupportPlugin.Directory, bundlePath));

		while (!bundleRequest.isDone)
		{
			await UniTask.Yield();
		}

		AssetBundle requestedBundle = bundleRequest.assetBundle;

		if (requestedBundle != null)
		{
			s_loadedBundles.Add(cacheKey, requestedBundle);
			s_ownedBundleKeys.Add(cacheKey);
			SamSWAT.FireSupport.ArysReloaded.Unity.TscDiagnostics.LogLcd($"Asset bundle loaded: {logName}");
			return requestedBundle;
		}

		FireSupportPlugin.LogSource.LogError($"Can't load bundle: {logName} (does it exist?), unknown error.");
		return null;
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
			? ab.LoadAllAssetsAsync<T>()
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
			AssetBundleRequest fallbackRequest = ab.LoadAllAssetsAsync<T>();
			while (!fallbackRequest.isDone)
			{
				await UniTask.Yield();
			}

			if (fallbackRequest.allAssets.Length > 0)
			{
				requestedObj = fallbackRequest.allAssets[0] as T;
				SamSWAT.FireSupport.ArysReloaded.Unity.TscDiagnostics.LogLcd($"Loaded fallback Object from bundle: {bundle}, requested asset: {assetName}, fallback asset: {requestedObj?.name ?? "null"}.");
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
			if (s_ownedBundleKeys.Contains(bundle.Key))
			{
				bundle.Value.Unload(unloadAllLoadedObjects);
			}
		}

		s_loadedBundles.Clear();
		s_ownedBundleKeys.Clear();
	}
}
