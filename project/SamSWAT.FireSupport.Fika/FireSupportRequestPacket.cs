using Fika.Core.Networking.LiteNetLib.Utils;
using SamSWAT.FireSupport.ArysReloaded.Unity;
using UnityEngine;

namespace SamSWAT.FireSupport.ArysReloaded.Fika;

public class FireSupportRequestPacket : INetSerializable
{
	public ESupportType SupportType;
	public Vector3 Position;
	public Vector3 Direction;
	public Vector3 Rotation;
	public int VisualSeed;
	public float DurationSeconds;
	public int PassIndex;

	public FireSupportRequestPacket()
	{
	}

	public FireSupportRequestPacket(
		ESupportType supportType,
		Vector3 position,
		Vector3 direction,
		Vector3 rotation,
		int visualSeed,
		float durationSeconds,
		int passIndex = 0)
	{
		SupportType = supportType;
		Position = position;
		Direction = direction;
		Rotation = rotation;
		VisualSeed = visualSeed;
		DurationSeconds = durationSeconds;
		PassIndex = passIndex;
	}

	public void Serialize(NetDataWriter writer)
	{
		writer.Put((int)SupportType);
		writer.PutUnmanaged(Position);
		writer.PutUnmanaged(Direction);
		writer.PutUnmanaged(Rotation);
		writer.Put(VisualSeed);
		writer.Put(DurationSeconds);
		writer.Put(PassIndex);
	}

	public void Deserialize(NetDataReader reader)
	{
		SupportType = (ESupportType)reader.GetInt();
		Position = reader.GetUnmanaged<Vector3>();
		Direction = reader.GetUnmanaged<Vector3>();
		Rotation = reader.GetUnmanaged<Vector3>();
		VisualSeed = reader.GetInt();
		DurationSeconds = reader.GetFloat();
		PassIndex = reader.GetInt();
	}
}
