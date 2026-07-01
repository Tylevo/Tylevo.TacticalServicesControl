using UnityEngine;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public enum UavLoiterAircraftType
{
	A10,
	Uh60
}

public struct UavA10LoiterRequest
{
	public UavLoiterAircraftType AircraftType;
	public Vector3 Center;
	public float DurationSeconds;
	public float Radius;
	public float Altitude;
	public float OrbitPeriod;
	public float IngressDuration;
	public float IngressDistance;
	public float EngineVolume;
	public Vector3 ModelRotationOffset;
	public float StartAngle;
	public int Direction;
	public float StartTime;

	public UavA10LoiterRequest(
		UavLoiterAircraftType aircraftType,
		Vector3 center,
		float durationSeconds,
		float radius,
		float altitude,
		float orbitPeriod,
		float ingressDuration,
		float ingressDistance,
		float engineVolume,
		Vector3 modelRotationOffset,
		float startAngle,
		int direction,
		float startTime)
	{
		AircraftType = aircraftType;
		Center = center;
		DurationSeconds = durationSeconds;
		Radius = radius;
		Altitude = altitude;
		OrbitPeriod = orbitPeriod;
		IngressDuration = ingressDuration;
		IngressDistance = ingressDistance;
		EngineVolume = engineVolume;
		ModelRotationOffset = modelRotationOffset;
		StartAngle = startAngle;
		Direction = direction >= 0 ? 1 : -1;
		StartTime = startTime;
	}
}
