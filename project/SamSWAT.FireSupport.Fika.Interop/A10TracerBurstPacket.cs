using Fika.Core.Networking.LiteNetLib.Utils;
using SamSWAT.FireSupport.ArysReloaded.Unity;
using System;
using UnityEngine;

namespace SamSWAT.FireSupport.ArysReloaded.Fika;

public class A10TracerBurstPacket : INetSerializable
{
	public int BurstId;
	public int VisualSeed;
	public int PassIndex;
	public float FireStartNetworkTime;
	public int SegmentOffset;
	public int TotalSegments;
	public A10TracerSegment[] Segments = Array.Empty<A10TracerSegment>();

	public A10TracerBurstPacket()
	{
	}

	public A10TracerBurstPacket(
		A10TracerBurst burst,
		int segmentOffset,
		int totalSegments,
		A10TracerSegment[] segments)
	{
		BurstId = burst.BurstId;
		VisualSeed = burst.VisualSeed;
		PassIndex = burst.PassIndex;
		FireStartNetworkTime = burst.FireStartNetworkTime;
		SegmentOffset = segmentOffset;
		TotalSegments = totalSegments;
		Segments = segments ?? Array.Empty<A10TracerSegment>();
	}

	public void Serialize(NetDataWriter writer)
	{
		writer.Put(BurstId);
		writer.Put(VisualSeed);
		writer.Put(PassIndex);
		writer.Put(FireStartNetworkTime);
		writer.Put(SegmentOffset);
		writer.Put(TotalSegments);
		writer.Put(Segments.Length);

		for (int i = 0; i < Segments.Length; i++)
		{
			A10TracerSegment segment = Segments[i];
			writer.PutUnmanaged(segment.ProjectileOrigin);
			writer.PutUnmanaged(segment.ProjectileDirection);
			writer.PutUnmanaged(segment.TracerStart);
			writer.PutUnmanaged(segment.TracerEnd);
			writer.Put(segment.DelaySeconds);
		}
	}

	public void Deserialize(NetDataReader reader)
	{
		BurstId = reader.GetInt();
		VisualSeed = reader.GetInt();
		PassIndex = reader.GetInt();
		FireStartNetworkTime = reader.GetFloat();
		SegmentOffset = reader.GetInt();
		TotalSegments = reader.GetInt();
		int receivedCount = Math.Max(0, reader.GetInt());
		int storedCount = Mathf.Clamp(receivedCount, 0, 20);
		Segments = new A10TracerSegment[storedCount];

		for (int i = 0; i < receivedCount; i++)
		{
			Vector3 projectileOrigin = reader.GetUnmanaged<Vector3>();
			Vector3 projectileDirection = reader.GetUnmanaged<Vector3>();
			Vector3 tracerStart = reader.GetUnmanaged<Vector3>();
			Vector3 tracerEnd = reader.GetUnmanaged<Vector3>();
			float delaySeconds = reader.GetFloat();
			if (i < storedCount)
			{
				Segments[i] = new A10TracerSegment(
					projectileOrigin,
					projectileDirection,
					tracerStart,
					tracerEnd,
					delaySeconds);
			}
		}
	}
}
