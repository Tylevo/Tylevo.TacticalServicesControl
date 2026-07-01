using Fika.Core.Networking.LiteNetLib.Utils;
using SamSWAT.FireSupport.ArysReloaded.Unity;
using UnityEngine;

namespace SamSWAT.FireSupport.ArysReloaded.Fika;

public class StartUavLoiterPacket : INetSerializable
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

	public StartUavLoiterPacket()
	{
	}

	public StartUavLoiterPacket(UavA10LoiterRequest request)
	{
		AircraftType = request.AircraftType;
		Center = request.Center;
		DurationSeconds = request.DurationSeconds;
		Radius = request.Radius;
		Altitude = request.Altitude;
		OrbitPeriod = request.OrbitPeriod;
		IngressDuration = request.IngressDuration;
		IngressDistance = request.IngressDistance;
		EngineVolume = request.EngineVolume;
		ModelRotationOffset = request.ModelRotationOffset;
		StartAngle = request.StartAngle;
		Direction = request.Direction;
		StartTime = request.StartTime;
	}

	public UavA10LoiterRequest ToRequest()
	{
		return new UavA10LoiterRequest(
			AircraftType,
			Center,
			DurationSeconds,
			Radius,
			Altitude,
			OrbitPeriod,
			IngressDuration,
			IngressDistance,
			EngineVolume,
			ModelRotationOffset,
			StartAngle,
			Direction,
			StartTime);
	}

	public void Serialize(NetDataWriter writer)
	{
		writer.Put((int)AircraftType);
		writer.PutUnmanaged(Center);
		writer.Put(DurationSeconds);
		writer.Put(Radius);
		writer.Put(Altitude);
		writer.Put(OrbitPeriod);
		writer.Put(IngressDuration);
		writer.Put(IngressDistance);
		writer.Put(EngineVolume);
		writer.PutUnmanaged(ModelRotationOffset);
		writer.Put(StartAngle);
		writer.Put(Direction);
		writer.Put(StartTime);
	}

	public void Deserialize(NetDataReader reader)
	{
		AircraftType = (UavLoiterAircraftType)reader.GetInt();
		Center = reader.GetUnmanaged<Vector3>();
		DurationSeconds = reader.GetFloat();
		Radius = reader.GetFloat();
		Altitude = reader.GetFloat();
		OrbitPeriod = reader.GetFloat();
		IngressDuration = reader.GetFloat();
		IngressDistance = reader.GetFloat();
		EngineVolume = reader.GetFloat();
		ModelRotationOffset = reader.GetUnmanaged<Vector3>();
		StartAngle = reader.GetFloat();
		Direction = reader.GetInt();
		StartTime = reader.GetFloat();
	}
}
