#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public readonly struct A10TrajectoryPoint
{
	public Vector3 Position { get; }
	public float TimeSeconds { get; }

	public A10TrajectoryPoint(Vector3 position, float timeSeconds)
	{
		Position = position;
		TimeSeconds = timeSeconds;
	}
}

public readonly struct A10TrajectoryEvaluation
{
	public Vector3 Position { get; }
	public float FlightTimeSeconds { get; }
	public IReadOnlyList<A10TrajectoryPoint> Path { get; }

	public A10TrajectoryEvaluation(
		Vector3 position,
		float flightTimeSeconds,
		IReadOnlyList<A10TrajectoryPoint>? path = null)
	{
		Position = position;
		FlightTimeSeconds = flightTimeSeconds;
		Path = path ?? Array.Empty<A10TrajectoryPoint>();
	}
}

/// <summary>
/// Samples a projectile at its first crossing of the requested horizontal range.
/// Runtime uses EFT's native trajectory calculator; the solver owns no drag table.
/// Implementations must bound flight time and must not create live projectiles.
/// </summary>
public interface IA10TrajectoryEvaluator
{
	bool IsValid { get; }

	bool TryEvaluate(
		Vector3 origin,
		Vector3 direction,
		float horizontalDistance,
		bool capturePath,
		out A10TrajectoryEvaluation evaluation);
}

public readonly struct A10BallisticSolution
{
	public Vector3 Direction { get; }
	public float FlightTimeSeconds { get; }
	public IReadOnlyList<A10TrajectoryPoint> Path { get; }
	public int EvaluationCount { get; }

	public A10BallisticSolution(
		Vector3 direction,
		A10TrajectoryEvaluation evaluation,
		int evaluationCount)
	{
		Direction = direction;
		FlightTimeSeconds = evaluation.FlightTimeSeconds;
		Path = evaluation.Path;
		EvaluationCount = evaluationCount;
	}
}

/// <summary>
/// Finds the first reachable elevation above direct aim for a downward-gravity
/// trajectory with no wind. A failed solve is explicit: never fire an
/// uncompensated direction as a fallback. The evaluator remains caller-owned.
/// </summary>
public static class A10BallisticSolver
{
	public const int MaximumEvaluations = 28;
	public const float VerticalToleranceMeters = 0.025f;
	public const float MaximumHorizontalRangeMeters = 5000f;
	private const double MaximumElevationRadians = 1.48352986419518; // 85 degrees
	private const double InitialBracketStepRadians = 0.0174532925199433; // 1 degree

	public static bool TrySolve(
		Vector3 origin,
		Vector3 target,
		IA10TrajectoryEvaluator evaluator,
		out A10BallisticSolution solution)
	{
		solution = default;
		if (evaluator == null || !evaluator.IsValid || !IsFinite(origin) || !IsFinite(target))
		{
			return false;
		}

		Vector3 delta = target - origin;
		float horizontalRange = (float)Math.Sqrt((double)delta.x * delta.x + (double)delta.z * delta.z);
		if (!IsFinite(horizontalRange) || horizontalRange < 0.25f ||
		    horizontalRange > MaximumHorizontalRangeMeters)
		{
			return false;
		}

		Vector3 horizontalDirection = new Vector3(delta.x / horizontalRange, 0f, delta.z / horizontalRange);
		double lowerAngle = Math.Atan2(delta.y, horizontalRange);
		if (lowerAngle >= MaximumElevationRadians)
		{
			return false;
		}

		int evaluations = 0;
		if (!TrySample(origin, horizontalDirection, horizontalRange, lowerAngle, evaluator,
			ref evaluations, false, out A10TrajectoryEvaluation lower))
		{
			return false;
		}

		float lowerError = lower.Position.y - target.y;
		if (Math.Abs(lowerError) <= VerticalToleranceMeters)
		{
			return Finish(origin, target, horizontalDirection, horizontalRange, lowerAngle,
				evaluator, ref evaluations, out solution);
		}

		// Native no-gravity bot trajectories should hit on direct aim. An
		// evaluator with lift/upward gravity is outside this elevation solver.
		if (lowerError > 0f)
		{
			return false;
		}

		double upperAngle = lowerAngle;
		double step = InitialBracketStepRadians;
		bool bracketed = false;
		for (int attempt = 0; attempt < 8 && evaluations < MaximumEvaluations - 2; attempt++)
		{
			upperAngle = Math.Min(lowerAngle + step, MaximumElevationRadians);
			bool reachedRange = TrySample(origin, horizontalDirection, horizontalRange, upperAngle,
				evaluator, ref evaluations, false, out A10TrajectoryEvaluation upper);
			if (!reachedRange)
			{
				// A lofted candidate may exceed the flight-time/range limit before
				// the low arc does. Search back toward the last reachable angle.
				bracketed = TryFindReachableUpper(origin, target, horizontalDirection, horizontalRange,
					ref lowerAngle, ref upperAngle, evaluator, ref evaluations);
				break;
			}

			float upperError = upper.Position.y - target.y;
			if (Math.Abs(upperError) <= VerticalToleranceMeters)
			{
				return Finish(origin, target, horizontalDirection, horizontalRange, upperAngle,
					evaluator, ref evaluations, out solution);
			}
			if (upperError > 0f)
			{
				bracketed = true;
				break;
			}
			if (upperAngle >= MaximumElevationRadians)
			{
				break;
			}

			lowerAngle = upperAngle;
			step *= 2d;
		}

		if (!bracketed)
		{
			return false;
		}

		while (evaluations < MaximumEvaluations - 1)
		{
			double angle = (lowerAngle + upperAngle) * 0.5d;
			if (!TrySample(origin, horizontalDirection, horizontalRange, angle, evaluator,
				ref evaluations, false, out A10TrajectoryEvaluation candidate))
			{
				return false;
			}

			float error = candidate.Position.y - target.y;
			if (Math.Abs(error) <= VerticalToleranceMeters)
			{
				return Finish(origin, target, horizontalDirection, horizontalRange, angle,
					evaluator, ref evaluations, out solution);
			}
			if (error < 0f)
			{
				lowerAngle = angle;
			}
			else
			{
				upperAngle = angle;
			}
		}

		return false;
	}

	private static bool TryFindReachableUpper(
		Vector3 origin,
		Vector3 target,
		Vector3 horizontalDirection,
		float horizontalRange,
		ref double lowerAngle,
		ref double upperAngle,
		IA10TrajectoryEvaluator evaluator,
		ref int evaluations)
	{
		for (int attempt = 0; attempt < 8 && evaluations < MaximumEvaluations - 2; attempt++)
		{
			double angle = (lowerAngle + upperAngle) * 0.5d;
			if (!TrySample(origin, horizontalDirection, horizontalRange, angle, evaluator,
				ref evaluations, false, out A10TrajectoryEvaluation candidate))
			{
				upperAngle = angle;
				continue;
			}
			if (candidate.Position.y >= target.y)
			{
				upperAngle = angle;
				return true;
			}
			lowerAngle = angle;
		}
		return false;
	}

	private static bool Finish(
		Vector3 origin,
		Vector3 target,
		Vector3 horizontalDirection,
		float horizontalRange,
		double angle,
		IA10TrajectoryEvaluator evaluator,
		ref int evaluations,
		out A10BallisticSolution solution)
	{
		solution = default;
		if (!TrySample(origin, horizontalDirection, horizontalRange, angle, evaluator,
			ref evaluations, true, out A10TrajectoryEvaluation evaluation) ||
		    evaluation.Path == null || evaluation.Path.Count < 2 ||
		    Vector3.Distance(evaluation.Position, target) > VerticalToleranceMeters * 2f)
		{
			return false;
		}

		solution = new A10BallisticSolution(DirectionAtElevation(horizontalDirection, angle), evaluation, evaluations);
		return true;
	}

	private static bool TrySample(
		Vector3 origin,
		Vector3 horizontalDirection,
		float horizontalRange,
		double angle,
		IA10TrajectoryEvaluator evaluator,
		ref int evaluations,
		bool capturePath,
		out A10TrajectoryEvaluation evaluation)
	{
		evaluation = default;
		if (evaluations >= MaximumEvaluations)
		{
			return false;
		}
		evaluations++;
		return evaluator.TryEvaluate(origin, DirectionAtElevation(horizontalDirection, angle),
			       horizontalRange, capturePath, out evaluation) &&
		       IsFinite(evaluation.Position) && IsFinite(evaluation.FlightTimeSeconds) &&
		       evaluation.FlightTimeSeconds > 0f;
	}

	private static Vector3 DirectionAtElevation(Vector3 horizontalDirection, double angle)
	{
		return (horizontalDirection * (float)Math.Cos(angle) + Vector3.up * (float)Math.Sin(angle)).normalized;
	}

	public static bool IsFinite(float value)
	{
		return !float.IsNaN(value) && !float.IsInfinity(value);
	}

	public static bool IsFinite(Vector3 value)
	{
		return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
	}
}
