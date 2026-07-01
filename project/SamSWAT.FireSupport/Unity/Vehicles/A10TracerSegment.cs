using UnityEngine;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public struct A10TracerSegment
{
	public Vector3 ProjectileOrigin;
	public Vector3 ProjectileDirection;
	public Vector3 TracerStart;
	public Vector3 TracerEnd;
	public float DelaySeconds;
	public bool IsValid;

	public A10TracerSegment(
		Vector3 projectileOrigin,
		Vector3 projectileDirection,
		Vector3 tracerStart,
		Vector3 tracerEnd,
		float delaySeconds,
		bool isValid = true)
	{
		ProjectileOrigin = projectileOrigin;
		ProjectileDirection = projectileDirection;
		TracerStart = tracerStart;
		TracerEnd = tracerEnd;
		DelaySeconds = delaySeconds;
		IsValid = isValid;
	}

	public static A10TracerSegment Invalid(Vector3 projectileOrigin, Vector3 projectileDirection, float delaySeconds)
	{
		return new A10TracerSegment(
			projectileOrigin,
			projectileDirection,
			Vector3.zero,
			Vector3.zero,
			delaySeconds,
			isValid: false);
	}
}
