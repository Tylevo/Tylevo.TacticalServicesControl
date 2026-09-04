using EFT.Ballistics;
using SamSWAT.FireSupport.ArysReloaded.Unity;
using UnityEngine;

internal static class A10BallisticSolverTests
{
	private static readonly (Vector3 Origin, Vector3 Target, float Shortfall)[] LoggedShots =
	{
		(new Vector3(1363.19f, 321.39f, 350.15f), new Vector3(38.07f, 6.81f, -86.39f), 132.54f),
		(new Vector3(1062.53f, 321.39f, 252.46f), new Vector3(67.95f, 25.85f, -68.17f), 49.14f)
	};

	[RegressionTest]
	private static void LoggedStraightAimFallsShortUnderTheInspectedEftGravityAndG1Model()
	{
		foreach ((Vector3 origin, Vector3 target, float expectedShortfall) in LoggedShots)
		{
			var calculator = new TrajectoryCalculator();
			calculator.Initialize(origin, (target - origin).normalized * 1070f, 280f, 30f, 0.316f, false);
			try
			{
				TrajectoryInfo previous = calculator.Current;
				bool crossed = false;
				for (int index = 0; index < 1200; index++)
				{
					TrajectoryInfo next = calculator.Next();
					if (next.position.y <= target.y)
					{
						float fraction = (previous.position.y - target.y) / (previous.position.y - next.position.y);
						Vector3 impact = previous.position + (next.position - previous.position) * fraction;
						float horizontalError = MathF.Sqrt(
							(impact.x - target.x) * (impact.x - target.x) +
							(impact.z - target.z) * (impact.z - target.z));
						// Reference values were independently reproduced in double precision
						// from the installed IL; this fixture uses native-style float vectors.
						AssertEx.Near(expectedShortfall, horizontalError, 0.15f);
						crossed = true;
						break;
					}
					previous = next;
				}
				AssertEx.True(crossed);
			}
			finally
			{
				calculator.ClearClass();
			}
		}
	}

	[RegressionTest]
	private static void LoggedShotsReachTheTargetWithACompensatedNativeTrajectory()
	{
		foreach (var (origin, target, _) in LoggedShots)
		{
			using var evaluator = CreateEvaluator();
			AssertEx.True(A10BallisticSolver.TrySolve(origin, target, evaluator, out A10BallisticSolution solution));
			AssertSolution(origin, target, solution);
			AssertEx.True(solution.Direction.y > (target - origin).normalized.y + 0.005f,
				"Long-range GAU-8 shots need lift; returning the original normalized ray is a regression.");
			AssertEx.True(solution.FlightTimeSeconds > 2f,
				"The long-range result must account for the installed ammo's substantial drag.");
			AssertEx.True(evaluator.TryEvaluate(origin, solution.Direction, HorizontalRange(origin, target),
				false, out A10TrajectoryEvaluation independentReplay));
			AssertEx.True(Vector3.Distance(target, independentReplay.Position) < 0.05f);
			AssertEx.Near(solution.FlightTimeSeconds, independentReplay.FlightTimeSeconds, 0.0001f);
		}
	}

	[RegressionTest]
	private static void SolverHandlesShortLongElevatedAndRotatedTargets()
	{
		(float Range, float HeightDifference)[] geometries =
		{
			(75f, -60f), (250f, -120f), (650f, 50f), (1450f, -320f), (1900f, -200f)
		};
		foreach ((float range, float height) in geometries)
		{
			foreach (float degrees in new[] { 0f, 37f, 90f, 180f, 271f })
			{
				double radians = degrees * Math.PI / 180d;
				Vector3 origin = new Vector3(-180f, 400f, 720f);
				Vector3 target = origin + new Vector3(
					(float)Math.Sin(radians) * range, height, (float)Math.Cos(radians) * range);
				using var evaluator = CreateEvaluator();
				AssertEx.True(A10BallisticSolver.TrySolve(origin, target, evaluator, out A10BallisticSolution solution),
					$"Expected reachable target range={range} height={height} heading={degrees}.");
				AssertSolution(origin, target, solution);
			}
		}
	}

	[RegressionTest]
	private static void EffectiveMuzzleVelocityChangesTheRequiredElevationAndFlightTime()
	{
		Vector3 origin = new Vector3(0f, 320f, 0f);
		Vector3 target = new Vector3(0f, 0f, 1450f);
		using var slower = CreateEvaluator(1070f * 0.9f);
		using var faster = CreateEvaluator(1070f * 1.1f);
		AssertEx.True(A10BallisticSolver.TrySolve(origin, target, slower, out A10BallisticSolution slow));
		AssertEx.True(A10BallisticSolver.TrySolve(origin, target, faster, out A10BallisticSolution fast));
		AssertSolution(origin, target, slow);
		AssertSolution(origin, target, fast);
		AssertEx.True(slow.Direction.y > fast.Direction.y);
		AssertEx.True(slow.FlightTimeSeconds > fast.FlightTimeSeconds);
		AssertEx.Near(1177f, A10NativeTrajectoryTestControl.LastParameters.Speed, 0.002f);
		AssertEx.Near(280f, A10NativeTrajectoryTestControl.LastParameters.Mass, 0.0001f);
		AssertEx.Near(30f, A10NativeTrajectoryTestControl.LastParameters.Diameter, 0.0001f);
		AssertEx.Near(0.316f, A10NativeTrajectoryTestControl.LastParameters.Coefficient, 0.0001f);
	}

	[RegressionTest]
	private static void BotOwnerUsesNativeDragWithoutAddingPlayerGravityCorrection()
	{
		Vector3 origin = new Vector3(0f, 320f, 0f);
		Vector3 target = new Vector3(0f, 0f, 1450f);
		using var evaluator = new A10EftTrajectoryEvaluator(1070f, 280f, 30f, 0.316f, 40f, isBotShot: true);
		AssertEx.True(A10BallisticSolver.TrySolve(origin, target, evaluator, out A10BallisticSolution solution));
		AssertSolution(origin, target, solution);
		AssertEx.True(Vector3.Distance((target - origin).normalized, solution.Direction) < 0.00001f);
		AssertEx.True(solution.FlightTimeSeconds > 2f);
		AssertEx.True(A10NativeTrajectoryTestControl.LastParameters.IsBot);
	}

	[RegressionTest]
	private static void InvalidParametersAndCoordinatesFailBeforeAnyNativeHistoryRental()
	{
		A10NativeTrajectoryTestControl.Reset();
		(float Speed, float Mass, float Diameter, float Coefficient, float Lifetime)[] invalid =
		{
			(0f, 280f, 30f, 0.316f, 40f),
			(float.NaN, 280f, 30f, 0.316f, 40f),
			(float.PositiveInfinity, 280f, 30f, 0.316f, 40f),
			(1070f, -1f, 30f, 0.316f, 40f),
			(1070f, 280f, 0f, 0.316f, 40f),
			(1070f, 280f, 30f, 0f, 40f),
			(1070f, 280f, 30f, float.NaN, 40f),
			(1070f, 280f, 30f, 0.316f, -1f),
			(1070f, 280f, 30f, 0.316f, float.PositiveInfinity)
		};
		foreach (var parameters in invalid)
		{
			using var evaluator = new A10EftTrajectoryEvaluator(parameters.Speed, parameters.Mass,
				parameters.Diameter, parameters.Coefficient, parameters.Lifetime);
			AssertEx.False(evaluator.IsValid);
			AssertEx.False(A10BallisticSolver.TrySolve(new Vector3(0f, 320f, 0f),
				new Vector3(0f, 0f, 1450f), evaluator, out _));
		}
		using (var evaluator = CreateEvaluator())
		{
			AssertEx.False(A10BallisticSolver.TrySolve(new Vector3(float.NaN, 320f, 0f), Vector3.zero, evaluator, out _));
			AssertEx.False(A10BallisticSolver.TrySolve(Vector3.zero, new Vector3(0f, 0f, float.PositiveInfinity), evaluator, out _));
			AssertEx.False(A10BallisticSolver.TrySolve(Vector3.zero, Vector3.up * 100f, evaluator, out _));
			AssertEx.False(A10BallisticSolver.TrySolve(Vector3.zero, Vector3.forward * 5001f, evaluator, out _));
		}
		AssertEx.Equal(0, A10NativeTrajectoryTestControl.TotalRentals);
	}

	[RegressionTest]
	private static void UnreachableShotsRespectFlightLimitsAndReturnBorrowedNativeHistory()
	{
		A10NativeTrajectoryTestControl.Reset();
		using (var evaluator = new A10EftTrajectoryEvaluator(1070f, 280f, 30f, 0.316f, 0.1f))
		{
			AssertEx.False(A10BallisticSolver.TrySolve(new Vector3(0f, 320f, 0f),
				new Vector3(0f, 0f, 1450f), evaluator, out _));
		}
		using (var evaluator = new A10EftTrajectoryEvaluator(20f, 280f, 30f, 0.316f, 40f))
		{
			AssertEx.False(A10BallisticSolver.TrySolve(new Vector3(0f, 320f, 0f),
				new Vector3(0f, 2000f, 3000f), evaluator, out _));
		}
		AssertEx.True(A10NativeTrajectoryTestControl.TotalRentals > 0);
		AssertEx.Equal(A10NativeTrajectoryTestControl.TotalRentals, A10NativeTrajectoryTestControl.TotalReturns);
		AssertEx.Equal(0, A10NativeTrajectoryTestControl.ActiveHistories);
		AssertEx.True(A10NativeTrajectoryTestControl.TotalSteps <= 1220,
			"Unreachable flights must stop at ammo lifetime / the 12-second native history limit.");
	}

	[RegressionTest]
	private static void EvaluatorDisposalIsIdempotentAndPreventsFurtherNativeSampling()
	{
		A10NativeTrajectoryTestControl.Reset();
		var evaluator = CreateEvaluator();
		AssertEx.True(A10BallisticSolver.TrySolve(new Vector3(0f, 320f, 0f),
			new Vector3(0f, 0f, 1450f), evaluator, out _));
		int rentals = A10NativeTrajectoryTestControl.TotalRentals;
		evaluator.Dispose();
		evaluator.Dispose();
		AssertEx.False(evaluator.IsValid);
		AssertEx.False(A10BallisticSolver.TrySolve(new Vector3(0f, 320f, 0f),
			new Vector3(0f, 0f, 1450f), evaluator, out _));
		AssertEx.Equal(rentals, A10NativeTrajectoryTestControl.TotalRentals);
		AssertEx.Equal(rentals, A10NativeTrajectoryTestControl.TotalReturns);
	}

	[RegressionTest]
	private static void NativeEvaluationFailuresReturnHistoryBeforePropagating()
	{
		A10NativeTrajectoryTestControl.Reset();
		using var evaluator = CreateEvaluator();
		A10NativeTrajectoryTestControl.ThrowOnNext = true;
		AssertEx.Throws<InvalidOperationException>(() => A10BallisticSolver.TrySolve(
			new Vector3(0f, 320f, 0f), new Vector3(0f, 0f, 1450f), evaluator, out _));
		AssertEx.Equal(1, A10NativeTrajectoryTestControl.TotalRentals);
		AssertEx.Equal(1, A10NativeTrajectoryTestControl.TotalReturns);
		AssertEx.Equal(0, A10NativeTrajectoryTestControl.ActiveHistories);
		AssertEx.True(A10BallisticSolver.TrySolve(new Vector3(0f, 320f, 0f),
			new Vector3(0f, 0f, 1450f), evaluator, out _));
	}

	[RegressionTest]
	private static void UnsupportedOrNonfiniteGravityCannotSilentlyProduceAnIncorrectAim()
	{
		try
		{
			foreach (Vector3 gravity in new[] { new Vector3(1f, -9.81f, 0f), Vector3.up,
				new Vector3(0f, float.NaN, 0f) })
			{
				Physics.gravity = gravity;
				using var evaluator = CreateEvaluator();
				AssertEx.False(A10BallisticSolver.TrySolve(new Vector3(0f, 320f, 0f),
					new Vector3(0f, 0f, 1450f), evaluator, out _));
			}
		}
		finally
		{
			Physics.Reset();
		}
	}

	[RegressionTest]
	private static void FiftyRoundMovingMuzzleSolveHasABoundedNativeWorkBudget()
	{
		A10NativeTrajectoryTestControl.Reset();
		using var evaluator = CreateEvaluator();
		for (int index = 0; index < 50; index++)
		{
			Vector3 origin = new Vector3(0f, 320f, -1450f + index * (150f * 60f / 1395f));
			Vector3 target = new Vector3((index % 5 - 2) * 2.5f, 0f, (index - 24.5f) * 0.9f);
			AssertEx.True(A10BallisticSolver.TrySolve(origin, target, evaluator, out A10BallisticSolution solution));
			AssertSolution(origin, target, solution);
			AssertEx.True(solution.EvaluationCount <= A10BallisticSolver.MaximumEvaluations);
		}
		AssertEx.True(A10NativeTrajectoryTestControl.TotalRentals <= 50 * A10BallisticSolver.MaximumEvaluations);
		AssertEx.True(A10NativeTrajectoryTestControl.TotalSteps < 350000,
			$"Unexpected planning cost: {A10NativeTrajectoryTestControl.TotalSteps} native 10ms steps.");
		AssertEx.Equal(1, A10NativeTrajectoryTestControl.MaximumActiveHistories);
		AssertEx.Equal(0, A10NativeTrajectoryTestControl.ActiveHistories);
		AssertEx.Equal(A10NativeTrajectoryTestControl.TotalRentals, A10NativeTrajectoryTestControl.TotalReturns);
	}

	private static A10EftTrajectoryEvaluator CreateEvaluator(float speed = 1070f)
	{
		return new A10EftTrajectoryEvaluator(speed, 280f, 30f, 0.316f, 40f);
	}

	private static float HorizontalRange(Vector3 origin, Vector3 target)
	{
		Vector3 delta = target - origin;
		return MathF.Sqrt(delta.x * delta.x + delta.z * delta.z);
	}

	private static void AssertSolution(Vector3 origin, Vector3 target, A10BallisticSolution solution)
	{
		AssertEx.Near(1f, solution.Direction.magnitude, 0.00001f);
		AssertEx.True(solution.Path.Count >= 2);
		AssertEx.True(Vector3.Distance(origin, solution.Path[0].Position) < 0.0001f);
		AssertEx.Near(0f, solution.Path[0].TimeSeconds, 0.0001f);
		AssertEx.True(Vector3.Distance(target, solution.Path[^1].Position) < 0.05f);
		AssertEx.Near(solution.FlightTimeSeconds, solution.Path[^1].TimeSeconds, 0.0001f);
		AssertEx.True(solution.FlightTimeSeconds <= A10EftTrajectoryEvaluator.MaximumFlightTimeSeconds);
		for (int index = 1; index < solution.Path.Count; index++)
		{
			AssertEx.True(solution.Path[index].TimeSeconds > solution.Path[index - 1].TimeSeconds);
			AssertEx.True(A10BallisticSolver.IsFinite(solution.Path[index].Position));
		}
	}
}
