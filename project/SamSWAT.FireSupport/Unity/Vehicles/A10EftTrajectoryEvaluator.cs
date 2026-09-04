using EFT.Ballistics;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

/// <summary>
/// Reuses EFT's exact gravity/G1 trajectory implementation without creating,
/// registering, or firing any Shot. Every trial returns its native history to
/// the pool, including failed/unreachable trials. Use only on Unity's thread.
/// </summary>
public sealed class A10EftTrajectoryEvaluator : IA10TrajectoryEvaluator, IDisposable
{
	public const float MaximumFlightTimeSeconds = 12f;
	private const float NativeTimeStepSeconds = 0.01f;
	private readonly TrajectoryCalculator _calculator = new();
	private readonly float _muzzleSpeed;
	private readonly float _bulletMassGram;
	private readonly float _bulletDiameterMillimeters;
	private readonly float _ballisticCoefficient;
	private readonly float _flightTimeLimit;
	private readonly bool _isBotShot;
	private readonly bool _validParameters;
	private bool _initialized;
	private bool _disposed;

	public bool IsValid => _validParameters && !_disposed;

	public A10EftTrajectoryEvaluator(
		float muzzleSpeed,
		float bulletMassGram,
		float bulletDiameterMillimeters,
		float ballisticCoefficient,
		float ammoLifetimeSeconds,
		bool isBotShot = false)
	{
		_muzzleSpeed = muzzleSpeed;
		_bulletMassGram = bulletMassGram;
		_bulletDiameterMillimeters = bulletDiameterMillimeters;
		_ballisticCoefficient = ballisticCoefficient;
		_flightTimeLimit = Math.Min(ammoLifetimeSeconds, MaximumFlightTimeSeconds);
		_isBotShot = isBotShot;
		_validParameters = PositiveFinite(muzzleSpeed) && PositiveFinite(bulletMassGram) &&
		                   PositiveFinite(bulletDiameterMillimeters) && PositiveFinite(ballisticCoefficient) &&
		                   PositiveFinite(ammoLifetimeSeconds) && _flightTimeLimit >= NativeTimeStepSeconds;
	}

	public bool TryEvaluate(
		Vector3 origin,
		Vector3 direction,
		float horizontalDistance,
		bool capturePath,
		out A10TrajectoryEvaluation evaluation)
	{
		evaluation = default;
		if (!IsValid || !A10BallisticSolver.IsFinite(origin) || !A10BallisticSolver.IsFinite(direction) ||
		    !PositiveFinite(horizontalDistance) || direction.sqrMagnitude < 0.0001f)
		{
			return false;
		}

		Vector3 gravity = Physics.gravity;
		if (!_isBotShot && (!A10BallisticSolver.IsFinite(gravity) || gravity.y > 0f ||
		                  Math.Abs(gravity.x) > 0.0001f || Math.Abs(gravity.z) > 0.0001f))
		{
			return false;
		}

		direction = direction.normalized;
		Vector3 heading = new Vector3(direction.x, 0f, direction.z).normalized;
		if (heading.sqrMagnitude < 0.0001f)
		{
			return false;
		}

		try
		{
			_calculator.Initialize(origin, direction * _muzzleSpeed, _bulletMassGram,
				_bulletDiameterMillimeters, _ballisticCoefficient, _isBotShot);
			_initialized = true;
			var path = capturePath ? new List<A10TrajectoryPoint>(400) : null;
			path?.Add(new A10TrajectoryPoint(origin, 0f));
			TrajectoryInfo previous = _calculator.Current;
			float previousDistance = 0f;
			int maxSteps = Math.Min(_calculator.MaxAllowedLength - 1,
				(int)Math.Ceiling(_flightTimeLimit / NativeTimeStepSeconds));
			for (int index = 0; index < maxSteps; index++)
			{
				TrajectoryInfo next = _calculator.Next();
				if (!A10BallisticSolver.IsFinite(next.position) || !A10BallisticSolver.IsFinite(next.time))
				{
					return false;
				}

				Vector3 displacement = next.position - origin;
				float nextDistance = displacement.x * heading.x + displacement.z * heading.z;
				if (nextDistance >= horizontalDistance && nextDistance > previousDistance)
				{
					float fraction = (horizontalDistance - previousDistance) / (nextDistance - previousDistance);
					float time = previous.time + (next.time - previous.time) * fraction;
					if (time > _flightTimeLimit)
					{
						return false;
					}
					Vector3 position = previous.position + (next.position - previous.position) * fraction;
					path?.Add(new A10TrajectoryPoint(position, time));
					evaluation = new A10TrajectoryEvaluation(position, time, path);
					return true;
				}

				if (nextDistance <= previousDistance || next.time >= _flightTimeLimit)
				{
					return false;
				}
				path?.Add(new A10TrajectoryPoint(next.position, next.time));
				previous = next;
				previousDistance = nextDistance;
			}
			return false;
		}
		finally
		{
			ReturnHistory();
		}
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}
		ReturnHistory();
		_disposed = true;
	}

	private void ReturnHistory()
	{
		if (_initialized)
		{
			_initialized = false;
			_calculator.ClearClass();
		}
	}

	private static bool PositiveFinite(float value)
	{
		return A10BallisticSolver.IsFinite(value) && value > 0f;
	}
}
