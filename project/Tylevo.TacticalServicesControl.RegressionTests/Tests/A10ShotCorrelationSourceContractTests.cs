internal static class A10ShotCorrelationSourceContractTests
{
	private const string BehaviourPath =
		"project/SamSWAT.FireSupport/Unity/Vehicles/A10Behaviour.cs";
	private const string ClientPredictionPath =
		"project/SamSWAT.FireSupport/Unity/Vehicles/A10ClientVisualPredictionExecutor.cs";
	private const string DamagePassPath =
		"project/SamSWAT.FireSupport/Unity/Vehicles/A10DamageOnlyPass.cs";
	private const string PlannerPath =
		"project/SamSWAT.FireSupport/Unity/Vehicles/A10ShotPlanner.cs";
	private const string TracerNetworkingPath =
		"project/SamSWAT.FireSupport/Unity/Vehicles/A10TracerNetworking.cs";

	[RegressionTest]
	private static void AircraftDelegatesCorrelatedShotConstructionToTheSharedPlanner()
	{
		string behaviour = ReadProductionSource(BehaviourPath);
		string compact = CompactWhitespace(behaviour);

		AssertEx.Contains("A10ShotPlanner.BuildImpactPlan(", compact);
		AssertEx.Contains("A10ShotPlanner.BuildMovingMuzzlePlan(", compact);
		AssertEx.False(
			compact.Contains(
				"gau8Transform.position + gau8Transform.forward * 515",
				StringComparison.Ordinal),
			"The authoritative muzzle origin must not be teleported 515 metres ahead of the visible aircraft.");
		AssertEx.False(
			behaviour.Contains("private float NextSpread(", StringComparison.Ordinal),
			"A10Behaviour must not retain a second, independently randomized shot planner.");
	}

	[RegressionTest]
	private static void AircraftMovesShotOriginsInTheSameRootFrameAsManualTranslation()
	{
		string behaviour = ReadProductionSource(BehaviourPath);
		string compact = CompactWhitespace(behaviour);

		AssertEx.Contains(
			"Transform muzzle = gau8Transform != null ? gau8Transform : transform",
			compact);
		AssertEx.Contains(
			"aircraftForward = A10ShotPlanner.NormalizeAircraftForward(transform.forward)",
			compact,
			"Per-shot muzzle travel must use the aircraft root's forward vector because ManualUpdate moves the root in self space.");
		AssertEx.Contains(
			"A10ShotPlanner.BuildMovingMuzzlePlan( muzzle.position, aircraftForward, impactPlan, timeBetweenShots)",
			compact,
			"The visible muzzle position must remain the first projectile origin.");
		AssertEx.Contains(
			"transform.Translate(0, 0, _currentSpeed * Time.deltaTime, Space.Self)",
			compact);
		AssertEx.False(
			compact.Contains(
				"A10ShotPlanner.NormalizeAircraftForward(muzzle.forward)",
				StringComparison.Ordinal),
			"A child muzzle's local rotation must not steer the aircraft's world-space per-shot movement.");
	}

	[RegressionTest]
	private static void ClientStartsTracerTimingOnlyAfterVisualRuntimeLaunchSucceeds()
	{
		string prediction = ReadProductionSource(ClientPredictionPath);
		string compact = CompactWhitespace(prediction);
		const string executeCall = "await s_visualRuntime.ExecuteAsync(";
		const string markCall = "A10TracerNetworking.MarkClientVisualPassStarted(";
		int executeIndex = compact.IndexOf(executeCall, StringComparison.Ordinal);
		int markIndex = compact.IndexOf(markCall, StringComparison.Ordinal);

		AssertEx.Contains("public async UniTask<bool> ExecuteAsync(", compact);
		AssertEx.True(
			executeIndex >= 0 && markIndex > executeIndex,
			"The visual runtime must finish launching before the client records its aircraft/tracer fire-start clock.");

		string launchGate = compact[executeIndex..markIndex];
		AssertEx.Contains("if (!", launchGate);
		AssertEx.Contains(
			"return false;",
			launchGate,
			"A failed or cancelled visual launch must return without marking a client visual pass as started.");
		AssertEx.True(
			compact.IndexOf("return true;", markIndex, StringComparison.Ordinal) > markIndex,
			"A successful launch must mark the pass and then report success.");
	}

	[RegressionTest]
	private static void PlannerBuildsFiftyTimedShotsFromMovingMuzzleOrigins()
	{
		string planner = ReadProductionSource(PlannerPath);
		string compact = CompactWhitespace(planner);

		AssertEx.Contains("const int ShotCount = 50", compact);
		AssertEx.Contains("BuildMovingMuzzlePlan(", compact);
		AssertEx.Contains(
			"projectileOrigin = firstMuzzleOrigin + safeAircraftForward * StrafeSpeed * shotDelay",
			compact,
			"Every shot origin must advance with the aircraft for that shot's scheduled delay.");
		AssertEx.Contains("shotDelay = index * safeTimeBetweenShots", compact);
		AssertEx.False(
			planner.Contains("Gau8ForwardOffset", StringComparison.Ordinal),
			"The retired fixed 515 metre muzzle offset must not return.");
	}

	[RegressionTest]
	private static void PlannerKeepsSeededImpactIntentAndNormalizedDirectionsDeterministic()
	{
		string planner = ReadProductionSource(PlannerPath);
		string compact = CompactWhitespace(planner);

		AssertEx.Contains("IReadOnlyList<Vector3> BuildImpactPlan(", compact);
		AssertEx.Contains("new System.Random(seed", compact);
		AssertEx.Contains("for (int index = 0; index < ShotCount; index++)", compact);
		AssertEx.Contains(
			"Vector3 direction = Vector3.Normalize(impactPoint - projectileOrigin)",
			compact);
		AssertEx.Contains(
			"new A10TracerSegment(projectileOrigin, direction, tracerStart, tracerEnd, shotDelay)",
			compact);
	}

	[RegressionTest]
	private static void ProjectileDamageAndNetworkReplayConsumeTheSameShotSegments()
	{
		string behaviour = ReadProductionSource(BehaviourPath);
		string compact = CompactWhitespace(behaviour);

		AssertEx.Contains("segments = shotPlan.Where(", compact);
		AssertEx.Contains("new A10TracerBurst(", compact);
		AssertEx.Contains("segments);", compact);
		AssertEx.Contains("foreach (A10TracerSegment shot in shotPlan)", compact);
		AssertEx.Contains(
			"_weapon.FireProjectile(shot.ProjectileOrigin, shot.ProjectileDirection)",
			compact);
	}

	[RegressionTest]
	private static void HeadlessVisualAndDamagePlansShareOneImpactIntent()
	{
		string damagePass = ReadProductionSource(DamagePassPath);
		string compact = CompactWhitespace(damagePass);

		AssertEx.Contains(
			"IReadOnlyList<Vector3> impactPlan = A10ShotPlanner.BuildImpactPlan(",
			compact);
		AssertEx.True(
			CountOccurrences(compact, "A10ShotPlanner.BuildMovingMuzzlePlan(") >= 2,
			"Headless visual replay and authoritative damage must both project the same impact intent from their respective muzzle origins.");
		AssertEx.True(
			CountOccurrences(compact, "impactPlan") >= 3,
			"The shared impact plan must be created once and consumed by both visual and damage shot plans.");
		AssertEx.False(
			compact.Contains("A10ShotPlanner.BuildImpactAnchoredReplay(", StringComparison.Ordinal),
			"Headless visuals must not independently randomize a second impact corridor.");
	}

	[RegressionTest]
	private static void AircraftPredictionAndPlannerShareFlightTimingConstants()
	{
		string planner = ReadProductionSource(PlannerPath);
		string behaviour = ReadProductionSource(BehaviourPath);
		string networking = ReadProductionSource(TracerNetworkingPath);

		AssertEx.Contains("public const float StrafeSpeed = 150f", CompactWhitespace(planner));
		AssertEx.Contains("public const float GunFireDelaySeconds = 8f", CompactWhitespace(planner));
		AssertEx.Contains("A10ShotPlanner.StrafeSpeed", behaviour);
		AssertEx.Contains("A10ShotPlanner.GunFireDelaySeconds", networking);
	}

	private static int CountOccurrences(string source, string value)
	{
		int count = 0;
		int startIndex = 0;
		while ((startIndex = source.IndexOf(value, startIndex, StringComparison.Ordinal)) >= 0)
		{
			count++;
			startIndex += value.Length;
		}

		return count;
	}

	private static string CompactWhitespace(string source)
	{
		return string.Join(
			' ',
			source.Split(
				(char[]?)null,
				StringSplitOptions.RemoveEmptyEntries));
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
