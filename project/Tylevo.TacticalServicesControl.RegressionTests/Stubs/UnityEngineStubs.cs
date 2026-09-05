using System;
using System.Runtime.InteropServices;

namespace UnityEngine;

[StructLayout(LayoutKind.Sequential)]
public struct Vector3 : IEquatable<Vector3>
{
	public Vector3(float x, float y, float z)
	{
		this.x = x;
		this.y = y;
		this.z = z;
	}

	public float x;
	public float y;
	public float z;

	public static Vector3 zero => default;
	public static Vector3 up => new(0f, 1f, 0f);
	public static Vector3 down => new(0f, -1f, 0f);
	public static Vector3 right => new(1f, 0f, 0f);
	public static Vector3 forward => new(0f, 0f, 1f);

	public float sqrMagnitude => x * x + y * y + z * z;
	public float magnitude => MathF.Sqrt(sqrMagnitude);
	public Vector3 normalized => Normalize(this);

	public void Normalize()
	{
		this = Normalize(this);
	}

	public static Vector3 Normalize(Vector3 value)
	{
		float length = value.magnitude;
		return length > 0.00001f ? value / length : zero;
	}

	public static Vector3 Cross(Vector3 left, Vector3 right)
	{
		return new Vector3(
			left.y * right.z - left.z * right.y,
			left.z * right.x - left.x * right.z,
			left.x * right.y - left.y * right.x);
	}

	public static float Distance(Vector3 left, Vector3 right)
	{
		return (left - right).magnitude;
	}

	public static float Dot(Vector3 left, Vector3 right)
	{
		return left.x * right.x + left.y * right.y + left.z * right.z;
	}

	public static Vector3 operator +(Vector3 left, Vector3 right)
	{
		return new Vector3(left.x + right.x, left.y + right.y, left.z + right.z);
	}

	public static Vector3 operator -(Vector3 left, Vector3 right)
	{
		return new Vector3(left.x - right.x, left.y - right.y, left.z - right.z);
	}

	public static Vector3 operator -(Vector3 value)
	{
		return new Vector3(-value.x, -value.y, -value.z);
	}

	public static Vector3 operator *(Vector3 value, float scalar)
	{
		return new Vector3(value.x * scalar, value.y * scalar, value.z * scalar);
	}

	public static Vector3 operator *(float scalar, Vector3 value)
	{
		return value * scalar;
	}

	public static Vector3 operator /(Vector3 value, float scalar)
	{
		return new Vector3(value.x / scalar, value.y / scalar, value.z / scalar);
	}

	public bool Equals(Vector3 other)
	{
		return x.Equals(other.x) &&
		       y.Equals(other.y) &&
		       z.Equals(other.z);
	}

	public override bool Equals(object? obj)
	{
		return obj is Vector3 other && Equals(other);
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(x, y, z);
	}
}

public static class Mathf
{
	public static float Max(float left, float right)
	{
		return MathF.Max(left, right);
	}

	public static float Min(float left, float right)
	{
		return MathF.Min(left, right);
	}

	public static int Clamp(int value, int minimum, int maximum)
	{
		return Math.Clamp(value, minimum, maximum);
	}
}

public struct RaycastHit
{
	public Vector3 point;
	public float distance;
}

public enum QueryTriggerInteraction
{
	UseGlobal,
	Ignore,
	Collide
}

public static class Physics
{
	public readonly record struct RaycastQuery(
		Vector3 Origin,
		Vector3 Direction,
		float MaximumDistance,
		int LayerMask,
		QueryTriggerInteraction TriggerInteraction);

	public static Vector3 gravity { get; set; } = new(0f, -9.81f, 0f);
	public static Func<RaycastQuery, RaycastHit?>? RaycastHandler { get; set; }
	public static Func<RaycastQuery, RaycastHit[]>? RaycastAllHandler { get; set; }

	public static void Reset()
	{
		gravity = new Vector3(0f, -9.81f, 0f);
		RaycastHandler = null;
		RaycastAllHandler = null;
	}

	public static bool Raycast(
		Vector3 origin,
		Vector3 direction,
		out RaycastHit hitInfo,
		float maxDistance,
		int layerMask,
		QueryTriggerInteraction queryTriggerInteraction)
	{
		RaycastHit? result = RaycastHandler?.Invoke(new RaycastQuery(
			origin, direction, maxDistance, layerMask, queryTriggerInteraction));
		hitInfo = result.GetValueOrDefault();
		return result.HasValue;
	}

	public static RaycastHit[] RaycastAll(
		Vector3 origin,
		Vector3 direction,
		float maxDistance,
		int layerMask,
		QueryTriggerInteraction queryTriggerInteraction)
	{
		return RaycastAllHandler?.Invoke(new RaycastQuery(
			origin, direction, maxDistance, layerMask, queryTriggerInteraction)) ?? Array.Empty<RaycastHit>();
	}
}
