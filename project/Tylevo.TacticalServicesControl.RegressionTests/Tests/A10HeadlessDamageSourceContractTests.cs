internal static class A10HeadlessDamageSourceContractTests
{
	private const string DamagePassPath =
		"project/SamSWAT.FireSupport/Unity/Vehicles/A10DamageOnlyPass.cs";
	private const string FikaIntegrationPath =
		"project/SamSWAT.FireSupport.Fika.Interop/FikaIntegration.cs";

	[RegressionTest]
	private static void HeadlessFallbackNeverMutatesTheHealthControllerDirectly()
	{
		string damagePass = ReadProductionSource(DamagePassPath);

		AssertEx.Contains(
			"A10HeadlessDamageCommandDispatcher.TryDispatch",
			damagePass);
		AssertEx.Contains(
			"method=FikaPlayerBridge",
			damagePass);
		AssertEx.Contains("DirectFallbackBallisticSettleSeconds", damagePass);
		AssertEx.Contains("reason=TargetKilledByBallistics", damagePass);
		AssertEx.Contains("reason=BallisticHealthChanged", damagePass);
		AssertEx.Contains("candidate.InitialHealth", damagePass);
		AssertEx.False(
			damagePass.Contains("ActiveHealthController", StringComparison.Ordinal),
			"Headless fallback must not bypass EFT.Player/Fika damage lifecycle through ActiveHealthController.");
		AssertEx.False(
			damagePass.Contains("TryApplyActiveHealthDamage", StringComparison.Ordinal),
			"The retired raw-health helper must not return.");
		AssertEx.False(
			damagePass.Contains(".OnDead(", StringComparison.Ordinal) ||
			damagePass.Contains(".Kill(", StringComparison.Ordinal),
			"TSC must let the authoritative player health pipeline decide death and corpse state.");
	}

	[RegressionTest]
	private static void FikaAuthorityRoutesByConcreteOwnershipType()
	{
		string integration = ReadProductionSource(FikaIntegrationPath);

		AssertEx.Contains(
			"A10HeadlessDamageCommandDispatcher.Handler = TryRouteA10HeadlessDamageCommand",
			integration);
		AssertEx.Contains(
			"server.CoopHandler.Players.TryGetValue(command.TargetNetId, out FikaPlayer targetPlayer)",
			integration);
		AssertEx.Contains("FikaTargetProfileMismatch", integration);
		AssertEx.Contains("FikaTargetAlreadyDead", integration);
		AssertEx.Contains("case FikaBot bot:", integration);
		AssertEx.Contains("bot.ApplyDamageInfo(", integration);
		AssertEx.Contains("case ObservedPlayer observedPlayer:", integration);
		AssertEx.Contains("observedPlayer.HandleExplosive(", integration);
		AssertEx.Contains(
			"case FikaPlayer localPlayer when localPlayer.IsYourPlayer:",
			integration);
		AssertEx.Contains("localPlayer.ApplyDamageInfo(", integration);
		AssertEx.Contains("UnsupportedFikaTarget", integration);

		int botRoute = integration.IndexOf("case FikaBot bot:", StringComparison.Ordinal);
		int deadTargetGuard = integration.IndexOf("FikaTargetAlreadyDead", StringComparison.Ordinal);
		int observedRoute = integration.IndexOf("case ObservedPlayer observedPlayer:", StringComparison.Ordinal);
		int localRoute = integration.IndexOf(
			"case FikaPlayer localPlayer when localPlayer.IsYourPlayer:",
			StringComparison.Ordinal);
		AssertEx.True(
			deadTargetGuard >= 0 && botRoute > deadTargetGuard && observedRoute > botRoute && localRoute > observedRoute,
			"Concrete FikaBot and ObservedPlayer ownership routes must be selected before the generic local-player route.");

		AssertEx.False(
			integration.Contains("ActiveHealthController.ApplyDamage", StringComparison.Ordinal),
			"Fika interop must not reintroduce raw health mutation.");
		AssertEx.False(
			integration.Contains(".OnDead(", StringComparison.Ordinal) ||
			integration.Contains(".Kill(", StringComparison.Ordinal),
			"Fika interop must not manufacture a death outside the native health sync pipeline.");
	}

	[RegressionTest]
	private static void RemoteHumansKeepFikasExplosiveDamagePacketPath()
	{
		string integration = ReadProductionSource(FikaIntegrationPath);

		AssertEx.Contains(
			"command.DamageInfo.DamageType is not (EDamageType.Artillery or EDamageType.Landmine)",
			integration);
		AssertEx.Contains("ObservedTargetRequiresExplosiveDamage", integration);
		AssertEx.Contains("ObservedPlayer.HandleExplosive", integration);
		AssertEx.False(
			integration.Contains("observedPlayer.ApplyDamageInfo(", StringComparison.Ordinal),
			"The headless server must not mutate a remote human's observed health controller; its owner receives Fika's reliable explosive DamagePacket instead.");
	}

	private static string ReadProductionSource(string relativePath)
	{
		string root = FindRepositoryRoot();
		string fullPath = Path.Combine(
			root,
			relativePath.Replace('/', Path.DirectorySeparatorChar));
		if (!File.Exists(fullPath))
		{
			throw new RegressionAssertionException(
				$"Required production source was not found: {fullPath}");
		}

		return File.ReadAllText(fullPath);
	}

	private static string FindRepositoryRoot()
	{
		string[] seeds =
		[
			Environment.CurrentDirectory,
			AppContext.BaseDirectory
		];

		foreach (string seed in seeds)
		{
			DirectoryInfo? directory = new(seed);
			while (directory != null)
			{
				if (File.Exists(Path.Combine(
					    directory.FullName,
					    "SamSWAT.FireSupport.ArysReloaded.sln")))
				{
					return directory.FullName;
				}

				directory = directory.Parent;
			}
		}

		throw new RegressionAssertionException(
			"Could not locate the TacticalServicesControl source root.");
	}
}
