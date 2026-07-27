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
	public float ScanIntervalSeconds;
	public float RangeMeters;
	public int PassIndex;
	public string SupportRequestId = string.Empty;
	public string RequesterProfileId = string.Empty;
	public int HelicopterTimingRevision;
	public float HelicopterDispatchDelaySeconds;
	public int HelicopterWaitTimeSeconds;
	public float HelicopterExtractTimeSeconds;
	public float HelicopterSpeedMultiplier;

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
		string supportRequestId = "",
		float scanIntervalSeconds = 0f,
		float rangeMeters = 0f,
		HelicopterTimingSnapshot? helicopterTimingSnapshot = null,
		int helicopterTimingRevision = 0)
	{
		SupportType = supportType;
		Position = position;
		Direction = direction;
		Rotation = rotation;
		VisualSeed = visualSeed;
		DurationSeconds = durationSeconds;
		ScanIntervalSeconds = scanIntervalSeconds;
		RangeMeters = rangeMeters;
		PassIndex = passIndex;
		RequesterProfileId = requesterProfileId ?? string.Empty;
		SupportRequestId = string.IsNullOrWhiteSpace(supportRequestId)
			? Guid.NewGuid().ToString("N")
			: supportRequestId.Trim();
		if (helicopterTimingSnapshot.HasValue)
		{
			SetHelicopterTiming(
				helicopterTimingSnapshot.Value,
				helicopterTimingRevision);
		}
	}

	public void SetHelicopterTiming(
		HelicopterTimingSnapshot timingSnapshot,
		int revision)
	{
		HelicopterTimingRevision = revision;
		HelicopterDispatchDelaySeconds = timingSnapshot.DispatchDelaySeconds;
		HelicopterWaitTimeSeconds = timingSnapshot.WaitTimeSeconds;
		HelicopterExtractTimeSeconds = timingSnapshot.ExtractTimeSeconds;
		HelicopterSpeedMultiplier = timingSnapshot.SpeedMultiplier;
	}

	public HelicopterTimingSnapshot GetHelicopterTiming()
	{
		return new HelicopterTimingSnapshot(
			SupportType,
			HelicopterDispatchDelaySeconds,
			HelicopterWaitTimeSeconds,
			HelicopterExtractTimeSeconds,
			HelicopterSpeedMultiplier);
	}

	public void Serialize(NetDataWriter writer)
	{
		writer.Put((int)SupportType);
		writer.PutUnmanaged(Position);
		writer.PutUnmanaged(Direction);
		writer.PutUnmanaged(Rotation);
		writer.Put(VisualSeed);
		writer.Put(DurationSeconds);
		writer.Put(ScanIntervalSeconds);
		writer.Put(RangeMeters);
		writer.Put(PassIndex);
		writer.Put(SupportRequestId ?? string.Empty);
		writer.Put(RequesterProfileId ?? string.Empty);
		writer.Put(HelicopterTimingRevision);
		writer.Put(HelicopterDispatchDelaySeconds);
		writer.Put(HelicopterWaitTimeSeconds);
		writer.Put(HelicopterExtractTimeSeconds);
		writer.Put(HelicopterSpeedMultiplier);
	}

	public void Deserialize(NetDataReader reader)
	{
		SupportType = (ESupportType)reader.GetInt();
		Position = reader.GetUnmanaged<Vector3>();
		Direction = reader.GetUnmanaged<Vector3>();
		Rotation = reader.GetUnmanaged<Vector3>();
		VisualSeed = reader.GetInt();
		DurationSeconds = reader.GetFloat();
		ScanIntervalSeconds = reader.GetFloat();
		RangeMeters = reader.GetFloat();
		PassIndex = reader.GetInt();
		SupportRequestId = reader.GetString() ?? string.Empty;
		RequesterProfileId = reader.GetString() ?? string.Empty;
		HelicopterTimingRevision = reader.GetInt();
		HelicopterDispatchDelaySeconds = reader.GetFloat();
		HelicopterWaitTimeSeconds = reader.GetInt();
		HelicopterExtractTimeSeconds = reader.GetFloat();
		HelicopterSpeedMultiplier = reader.GetFloat();
	}

	public void EnsureRequestId()
	{
		if (string.IsNullOrWhiteSpace(SupportRequestId))
		{
			SupportRequestId = Guid.NewGuid().ToString("N");
		}
	}
}
