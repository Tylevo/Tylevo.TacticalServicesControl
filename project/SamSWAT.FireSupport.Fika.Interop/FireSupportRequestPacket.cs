using Fika.Core.Networking.LiteNetLib.Utils;
using SamSWAT.FireSupport.ArysReloaded.Unity;
using System;
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
	public string SupportRequestId = string.Empty;
	public string RequesterProfileId = string.Empty;

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
		int passIndex = 0,
		string requesterProfileId = "",
		string supportRequestId = "")
	{
		SupportType = supportType;
		Position = position;
		Direction = direction;
		Rotation = rotation;
		VisualSeed = visualSeed;
		DurationSeconds = durationSeconds;
		PassIndex = passIndex;
		RequesterProfileId = requesterProfileId ?? string.Empty;
		SupportRequestId = string.IsNullOrWhiteSpace(supportRequestId)
			? Guid.NewGuid().ToString("N")
			: supportRequestId.Trim();
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
		writer.Put(SupportRequestId ?? string.Empty);
		writer.Put(RequesterProfileId ?? string.Empty);
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
		SupportRequestId = reader.GetString() ?? string.Empty;
		RequesterProfileId = reader.GetString() ?? string.Empty;
		EnsureRequestId();
	}

	public void EnsureRequestId()
	{
		if (string.IsNullOrWhiteSpace(SupportRequestId))
		{
			SupportRequestId = Guid.NewGuid().ToString("N");
		}
	}
}
