using SamSWAT.FireSupport.ArysReloaded;

internal static class PilotPolicyTestFixture
{
	public static TscPilotQuestlinePolicy Create(bool questlineRequired = false)
	{
		var policy = new TscPilotQuestlinePolicy();
		policy.Initialize(questlineRequired ? RepositoryRoot : Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
			"1.3.11", "4.1.5");
		policy.Activate();
		return policy;
	}

	public static string RepositoryRoot
	{
		get
		{
			foreach (string seed in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
				for (DirectoryInfo? directory = new(seed); directory != null; directory = directory.Parent)
					if (File.Exists(Path.Combine(directory.FullName, "project/SamSWAT.FireSupport.Server/ServerMod.cs")))
						return directory.FullName;
			throw new RegressionAssertionException("Could not locate the TSC source root.");
		}
	}
}
