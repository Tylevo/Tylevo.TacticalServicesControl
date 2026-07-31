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
