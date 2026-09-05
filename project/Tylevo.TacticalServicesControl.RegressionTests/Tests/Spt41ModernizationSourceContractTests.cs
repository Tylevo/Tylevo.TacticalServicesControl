internal static class Spt41ModernizationSourceContractTests
{
	[RegressionTest]
	private static void NativeEditorUsesCuratedVersionedCanonicalConfigFlow()
	{
		string provider = Read("project/SamSWAT.FireSupport.Server/FireSupportConfigEditorProvider.cs");
		string service = Read("project/SamSWAT.FireSupport.Server/FireSupportServerConfigService.cs");

		AssertEx.Contains(": IConfigEditorConfigProvider", provider);
		AssertEx.Contains("DisplayName = \"Tactical Services Control\"", provider);
		AssertEx.Contains("IgnoredSectionPaths", provider);
		AssertEx.Contains("LoadFromDiskAsync", provider);
		AssertEx.Contains("ApplyToRuntimeAsync = (edited, token) => ApplyAsync", provider);
		AssertEx.Contains("SaveToDiskAsync = (edited, token) => SaveAsync", provider);
		AssertEx.Contains("TryGetDiskConfigSnapshot", provider);
		AssertEx.Contains("TryApplyConfig", provider);
		AssertEx.Contains("TrySaveConfig", provider);
		AssertEx.Contains("configService.GetConfigSnapshot()", provider);
		AssertEx.Contains("edited.Revision", provider);
		AssertEx.False(
			provider.Contains("AdminDashboard", StringComparison.Ordinal),
			"The native editor must not expose dashboard security controls.");
		AssertEx.False(
			provider.Contains(
				"public Dictionary<string, int> Authorizations",
				StringComparison.Ordinal),
			"The native editor must not expose profile authorization state.");
		AssertEx.Contains("ConfigsEquivalentExceptRevision", service);
		AssertEx.Contains("expectedRevision.Value != _config.Revision", service);
	}

	[RegressionTest]
	private static void UplinkRepairAndPackagingAreExactAndFailClosed()
	{
		string repair = Read("tools/Repair-UplinkBundle.py");
		string manifest = Read("tools/package-layout.allowlist.json");
		string packager = Read("tools/New-ReleasePackage.ps1");

		AssertEx.Contains("USABLE_HANDS_PREFAB = 3227168475352522817", repair);
		AssertEx.Contains("ANIMATOR_STATIC_DATA = 1229230505816891976", repair);
		AssertEx.Contains("NEW_DEFAULT_STATES = [0, 1372578019]", repair);
		AssertEx.Contains("OUT_USE_HASH = 1865652397", repair);
		AssertEx.Contains("objects outside the two approved targets changed", repair);
		AssertEx.Contains("8C9F8D8878076D4FFCB2687D62609F606552B3E9F3529FBE584DF79E43365861", manifest);
		AssertEx.Contains("overrideSource", manifest);
		AssertEx.Contains("Bundle override source pin mismatch", packager);
	}

	[RegressionTest]
	private static void TargetingGuidancePreservesSafeConfirmControls()
	{
		string spotter = Read("project/SamSWAT.FireSupport/Unity/UI/FireSupportSpotter.cs");
		string settings = Read("project/SamSWAT.FireSupport/PluginSettings.cs");

		AssertEx.Contains("Confirm with Middle Mouse or Enter", spotter);
		AssertEx.Contains("Move the mouse, then confirm with Middle Mouse or Enter", spotter);
		AssertEx.Contains("Input.GetMouseButtonDown(0) && HasRangefinderInHands()", spotter);
		AssertEx.Contains("SPT.Common.Http.RequestHandler.Host", settings);
		AssertEx.Contains("Path = \"/tsc/admin\"", settings);
	}

	private static string Read(string relativePath)
	{
		string root = FindRepositoryRoot();
		string path = Path.Combine(
			root,
			relativePath.Replace('/', Path.DirectorySeparatorChar));
		AssertEx.True(File.Exists(path), $"Production source was not found: {path}");
		return File.ReadAllText(path);
	}

	private static string FindRepositoryRoot()
	{
		foreach (string seed in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
		{
			DirectoryInfo? current = new(seed);
			while (current != null)
			{
				if (File.Exists(Path.Combine(current.FullName, "Directory.Build.props")))
				{
					return current.FullName;
				}

				current = current.Parent;
			}
		}

		throw new RegressionAssertionException("Could not locate the source root.");
	}
}
