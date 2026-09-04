using UnityEngine;

namespace EFT.Ballistics;

public static class BallisticsCalculatorConstants
{
	// Deliberately distinctive test sentinel. Runtime resolves the game's
	// Water/Terrain/HighPolyCollider/TransparentCollider/Deadbody/HitCollider mask.
	public const int HitMask = 0x123400;
}

// Test-only model of the inspected SPT 4.1.4 native trajectory API. Production
// calls the game's implementation directly and ships no coefficient table.
// The numeric G1 table and 10ms update rule were read from EFT build 40743.
public readonly struct TrajectoryInfo
{
	public readonly Vector3 position;
	public readonly Vector3 velocity;
	public readonly float time;

	public TrajectoryInfo(Vector3 position, Vector3 velocity, float time)
	{
		this.position = position;
		this.velocity = velocity;
		this.time = time;
	}
}

internal static class A10NativeTrajectoryTestControl
{
	public static int ActiveHistories;
	public static int TotalRentals;
	public static int TotalReturns;
	public static int TotalSteps;
	public static int MaximumActiveHistories;
	public static bool ThrowOnNext;
	public static (float Speed, float Mass, float Diameter, float Coefficient, bool IsBot) LastParameters;

	public static void Reset()
	{
		if (ActiveHistories != 0)
		{
			throw new InvalidOperationException("A native trajectory history leaked from an earlier evaluation.");
		}
		TotalRentals = 0;
		TotalReturns = 0;
		TotalSteps = 0;
		MaximumActiveHistories = 0;
		ThrowOnNext = false;
	}
}

public sealed class TrajectoryCalculator
{
	private bool _initialized;
	private float _coefficient;
	private float _slowdown;
	private float _massTimesTwo;
	private Vector3 _gravity;
	private bool _isBotShot;
	public TrajectoryInfo Current { get; private set; }
	public int MaxAllowedLength => 1300;

	public void Initialize(
		Vector3 zeroPosition,
		Vector3 zeroVelocity,
		float bulletMassGram,
		float bulletDiameterMilimeters,
		float ballisticCoefficient,
		bool isBotShot)
	{
		if (_initialized)
		{
			throw new InvalidOperationException("Initialize called without returning the previous native history.");
		}
		_initialized = true;
		A10NativeTrajectoryTestControl.ActiveHistories++;
		A10NativeTrajectoryTestControl.TotalRentals++;
		A10NativeTrajectoryTestControl.MaximumActiveHistories = Math.Max(
			A10NativeTrajectoryTestControl.MaximumActiveHistories,
			A10NativeTrajectoryTestControl.ActiveHistories);
		A10NativeTrajectoryTestControl.LastParameters =
			(zeroVelocity.magnitude, bulletMassGram, bulletDiameterMilimeters, ballisticCoefficient, isBotShot);

		float mass = bulletMassGram / 1000f;
		float diameter = bulletDiameterMilimeters / 1000f;
		_massTimesTwo = mass * 2f;
		_coefficient = mass * 0.0014223f / (diameter * diameter * ballisticCoefficient);
		_slowdown = 1.2f * (diameter * diameter * MathF.PI / 4f);
		_gravity = Physics.gravity;
		_isBotShot = isBotShot;
		Current = new TrajectoryInfo(zeroPosition, zeroVelocity, 0f);
	}

	public TrajectoryInfo Next()
	{
		if (!_initialized)
		{
			throw new InvalidOperationException("A trajectory cannot be sampled after its history was returned.");
		}
		A10NativeTrajectoryTestControl.TotalSteps++;
		if (A10NativeTrajectoryTestControl.ThrowOnNext)
		{
			A10NativeTrajectoryTestControl.ThrowOnNext = false;
			throw new InvalidOperationException("Injected native trajectory failure.");
		}
		Vector3 velocity = Current.velocity;
		float speed = velocity.magnitude;
		float drag = CalculateG1DragCoefficient(speed) * _coefficient;
		Vector3 acceleration = -_slowdown * drag * speed * speed / _massTimesTwo * velocity.normalized;
		if (!_isBotShot)
		{
			acceleration += _gravity;
		}
		Current = new TrajectoryInfo(
			Current.position + velocity * 0.01f + 0.00005f * acceleration,
			velocity + acceleration * 0.01f,
			Current.time + 0.01f);
		return Current;
	}

	public void ClearClass()
	{
		if (!_initialized)
		{
			throw new InvalidOperationException("Native history was returned twice.");
		}
		_initialized = false;
		A10NativeTrajectoryTestControl.ActiveHistories--;
		A10NativeTrajectoryTestControl.TotalReturns++;
	}

	private static float CalculateG1DragCoefficient(float speed)
	{
		int index = (int)MathF.Floor(speed / 343f / 0.05f);
		if (index <= 0)
		{
			return 0f;
		}
		if (index > G1.Length - 1)
		{
			return G1[^1].Coefficient;
		}

		// Preserve the installed game's indexing and interpolation, including
		// its uneven Mach table. Do not substitute a generic G1 library here.
		(float mach0, float drag0) = G1[index - 1];
		(float mach1, float drag1) = G1[index];
		float speed0 = mach0 * 343f;
		float speed1 = mach1 * 343f;
		return (drag1 - drag0) / (speed1 - speed0) * (speed - speed0) + drag0;
	}

	private static readonly (float Mach, float Coefficient)[] G1 =
	{
		(0f, 0.2629f),
		(0.05f, 0.2558f),
		(0.1f, 0.2487f),
		(0.15f, 0.2413f),
		(0.2f, 0.2344f),
		(0.25f, 0.2278f),
		(0.3f, 0.2214f),
		(0.35f, 0.2155f),
		(0.4f, 0.2104f),
		(0.45f, 0.2061f),
		(0.5f, 0.2032f),
		(0.55f, 0.202f),
		(0.6f, 0.2034f),
		(0.7f, 0.2165f),
		(0.725f, 0.223f),
		(0.75f, 0.2313f),
		(0.775f, 0.2417f),
		(0.8f, 0.2546f),
		(0.825f, 0.2706f),
		(0.85f, 0.2901f),
		(0.875f, 0.3136f),
		(0.9f, 0.3415f),
		(0.925f, 0.3734f),
		(0.95f, 0.4084f),
		(0.975f, 0.4448f),
		(1f, 0.4805f),
		(1.025f, 0.5136f),
		(1.05f, 0.5427f),
		(1.075f, 0.5677f),
		(1.1f, 0.5883f),
		(1.125f, 0.6053f),
		(1.15f, 0.6191f),
		(1.2f, 0.6393f),
		(1.25f, 0.6518f),
		(1.3f, 0.6589f),
		(1.35f, 0.6621f),
		(1.4f, 0.6625f),
		(1.45f, 0.6607f),
		(1.5f, 0.6573f),
		(1.55f, 0.6528f),
		(1.6f, 0.6474f),
		(1.65f, 0.6413f),
		(1.7f, 0.6347f),
		(1.75f, 0.628f),
		(1.8f, 0.621f),
		(1.85f, 0.6141f),
		(1.9f, 0.6072f),
		(1.95f, 0.6003f),
		(2f, 0.5934f),
		(2.05f, 0.5867f),
		(2.1f, 0.5804f),
		(2.15f, 0.5743f),
		(2.2f, 0.5685f),
		(2.25f, 0.563f),
		(2.3f, 0.5577f),
		(2.35f, 0.5527f),
		(2.4f, 0.5481f),
		(2.45f, 0.5438f),
		(2.5f, 0.5397f),
		(2.6f, 0.5325f),
		(2.7f, 0.5264f),
		(2.8f, 0.5211f),
		(2.9f, 0.5168f),
		(3f, 0.5133f),
		(3.1f, 0.5105f),
		(3.2f, 0.5084f),
		(3.3f, 0.5067f),
		(3.4f, 0.5054f),
		(3.5f, 0.504f),
		(3.6f, 0.503f),
		(3.7f, 0.5022f),
		(3.8f, 0.5016f),
		(3.9f, 0.501f),
		(4f, 0.5006f),
		(4.2f, 0.4998f),
		(4.4f, 0.4995f),
		(4.6f, 0.4992f),
		(4.8f, 0.499f),
		(5f, 0.4988f)
	};
}
