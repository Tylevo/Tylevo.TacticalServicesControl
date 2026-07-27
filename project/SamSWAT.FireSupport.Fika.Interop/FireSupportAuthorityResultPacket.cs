using Fika.Core.Networking.LiteNetLib.Utils;
using SamSWAT.FireSupport.ArysReloaded.Unity;
using UnityEngine;

namespace SamSWAT.FireSupport.ArysReloaded.Fika;

/// <summary>
/// Terminal host decision for a support request. The request identity fields
/// are echoed so a client never completes one pending deployment from a
/// response that belongs to another service, pass, or player.
/// </summary>
public sealed class FireSupportAuthorityResultPacket : INetSerializable
{
	public string SupportRequestId = string.Empty;
	public ESupportType SupportType;
	public int PassIndex;
	public string RequesterProfileId = string.Empty;
	public bool Accepted;
	public string Reason = string.Empty;
	public Vector3 Position;
	public Vector3 Direction;
	public Vector3 Rotation;
	public int VisualSeed;
	public float DurationSeconds;
	public float ScanIntervalSeconds;
	public float RangeMeters;
	public int HelicopterTimingRevision;
	public float HelicopterDispatchDelaySeconds;
	public int HelicopterWaitTimeSeconds;
	public float HelicopterExtractTimeSeconds;
	public float HelicopterSpeedMultiplier;

	public FireSupportAuthorityResultPacket()
	{
	}

	public FireSupportAuthorityResultPacket(
		FireSupportRequestPacket request,
		bool accepted,
		string reason)
	{
		SupportRequestId = request?.SupportRequestId ?? string.Empty;
		SupportType = request?.SupportType ?? ESupportType.Strafe;
		PassIndex = request?.PassIndex ?? 0;
		RequesterProfileId = request?.RequesterProfileId ?? string.Empty;
		Accepted = accepted;
		Reason = reason ?? string.Empty;
		Position = request?.Position ?? Vector3.zero;
		Direction = request?.Direction ?? Vector3.zero;
		Rotation = request?.Rotation ?? Vector3.zero;
		VisualSeed = request?.VisualSeed ?? 0;
		DurationSeconds = request?.DurationSeconds ?? 0f;
		ScanIntervalSeconds = request?.ScanIntervalSeconds ?? 0f;
		RangeMeters = request?.RangeMeters ?? 0f;
		HelicopterTimingRevision = request?.HelicopterTimingRevision ?? 0;
		HelicopterDispatchDelaySeconds = request?.HelicopterDispatchDelaySeconds ?? 0f;
		HelicopterWaitTimeSeconds = request?.HelicopterWaitTimeSeconds ?? 0;
		HelicopterExtractTimeSeconds = request?.HelicopterExtractTimeSeconds ?? 0f;
		HelicopterSpeedMultiplier = request?.HelicopterSpeedMultiplier ?? 0f;
	}

	public FireSupportRequestPacket ToSupportRequest()
	{
		var request = new FireSupportRequestPacket(
			SupportType,
			Position,
			Direction,
			Rotation,
			VisualSeed,
			DurationSeconds,
			PassIndex,
			RequesterProfileId,
			SupportRequestId,
			ScanIntervalSeconds,
			RangeMeters);
		request.HelicopterTimingRevision = HelicopterTimingRevision;
		request.HelicopterDispatchDelaySeconds = HelicopterDispatchDelaySeconds;
		request.HelicopterWaitTimeSeconds = HelicopterWaitTimeSeconds;
		request.HelicopterExtractTimeSeconds = HelicopterExtractTimeSeconds;
		request.HelicopterSpeedMultiplier = HelicopterSpeedMultiplier;
		return request;
	}

	public void Serialize(NetDataWriter writer)
	{
		writer.Put(SupportRequestId ?? string.Empty);
		writer.Put((int)SupportType);
		writer.Put(PassIndex);
		writer.Put(RequesterProfileId ?? string.Empty);
		writer.Put(Accepted);
		writer.Put(Reason ?? string.Empty);
		writer.PutUnmanaged(Position);
		writer.PutUnmanaged(Direction);
		writer.PutUnmanaged(Rotation);
		writer.Put(VisualSeed);
		writer.Put(DurationSeconds);
		writer.Put(ScanIntervalSeconds);
		writer.Put(RangeMeters);
		writer.Put(HelicopterTimingRevision);
		writer.Put(HelicopterDispatchDelaySeconds);
		writer.Put(HelicopterWaitTimeSeconds);
		writer.Put(HelicopterExtractTimeSeconds);
		writer.Put(HelicopterSpeedMultiplier);
	}

	public void Deserialize(NetDataReader reader)
	{
		SupportRequestId = reader.GetString() ?? string.Empty;
		SupportType = (ESupportType)reader.GetInt();
		PassIndex = reader.GetInt();
		RequesterProfileId = reader.GetString() ?? string.Empty;
		Accepted = reader.GetBool();
		Reason = reader.GetString() ?? string.Empty;
		Position = reader.GetUnmanaged<Vector3>();
		Direction = reader.GetUnmanaged<Vector3>();
		Rotation = reader.GetUnmanaged<Vector3>();
		VisualSeed = reader.GetInt();
		DurationSeconds = reader.GetFloat();
		ScanIntervalSeconds = reader.GetFloat();
		RangeMeters = reader.GetFloat();
		HelicopterTimingRevision = reader.GetInt();
		HelicopterDispatchDelaySeconds = reader.GetFloat();
		HelicopterWaitTimeSeconds = reader.GetInt();
		HelicopterExtractTimeSeconds = reader.GetFloat();
		HelicopterSpeedMultiplier = reader.GetFloat();
	}
}
