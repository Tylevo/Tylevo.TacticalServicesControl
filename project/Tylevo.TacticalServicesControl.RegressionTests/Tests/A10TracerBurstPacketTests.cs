using Fika.Core.Networking.LiteNetLib.Utils;
using SamSWAT.FireSupport.ArysReloaded.Fika;
using SamSWAT.FireSupport.ArysReloaded.Unity;
using UnityEngine;

internal static class A10TracerBurstPacketTests
{
	[RegressionTest]
	private static void Full120RoundBurstFitsSixExistingPacketsWithoutTruncation()
	{
		A10TracerSegment[] shots = Enumerable.Range(0, A10ShotPlanner.ShotCount).Select(index =>
		{
			A10TracerSegment shot = MakeShot();
			shot.DelaySeconds = index * 60f / 1395f;
			return shot;
		}).ToArray();
		var burst = new A10TracerBurst(7, "strike-7:pass:0", 2468, 0, 100f, shots);
		var replay = new List<A10TracerSegment>();
		for (int offset = 0; offset < shots.Length; offset += 20)
		{
			A10TracerBurstPacket received = RoundTrip(new A10TracerBurstPacket(
				burst, offset, shots.Length, shots.Skip(offset).Take(20).ToArray()));
			AssertEx.Equal(120, received.TotalSegments);
			AssertEx.Equal(offset, received.SegmentOffset);
			AssertEx.Equal(20, received.Segments.Length);
			replay.AddRange(received.Segments);
		}
		AssertEx.Equal(shots.Length, replay.Count);
		for (int i = 0; i < shots.Length; i++)
			AssertEx.Near(shots[i].ImpactDelaySeconds, replay[i].ImpactDelaySeconds, 0.0001f);
	}

	[RegressionTest]
	private static void RoundTripUsesImpactArrivalWithoutAddingFlightTimeTwice()
	{
		A10TracerSegment shot = MakeShot();
		A10TracerBurstPacket received = RoundTrip(MakePacket(shot));
		A10TracerSegment replay = received.Segments.Single();

		AssertEx.Near(3.65f, replay.DelaySeconds, 0.0001f);
		AssertEx.Near(0f, replay.FlightTimeSeconds, 0.0001f);
		AssertEx.Near(shot.ImpactDelaySeconds, replay.ImpactDelaySeconds, 0.0001f);
		AssertEx.Equal(shot.ProjectileOrigin, replay.ProjectileOrigin);
		AssertEx.Equal(shot.ProjectileDirection, replay.ProjectileDirection);
		AssertEx.Equal(shot.TracerStart, replay.TracerStart);
		AssertEx.Equal(shot.TracerEnd, replay.TracerEnd);
		AssertEx.True(replay.IsValid);

		// A relay/re-encode of an already received segment must preserve arrival.
		A10TracerSegment relayed = RoundTrip(received).Segments.Single();
		AssertEx.Near(shot.ImpactDelaySeconds, relayed.ImpactDelaySeconds, 0.0001f);
	}

	[RegressionTest]
	private static void PacketRetainsFourVectorsAndOneFloatPerSegment()
	{
		A10TracerSegment shot = MakeShot();
		var writer = new NetDataWriter();
		MakePacket(shot).Serialize(writer);
		var reader = new NetDataReader(writer.ToArray());

		AssertEx.Equal(7, reader.GetInt());
		AssertEx.Equal("strike-7:pass:0", reader.GetString());
		AssertEx.Equal(2468, reader.GetInt());
		AssertEx.Equal(0, reader.GetInt());
		AssertEx.Near(100f, reader.GetFloat(), 0.0001f);
		AssertEx.Equal(0, reader.GetInt());
		AssertEx.Equal(1, reader.GetInt());
		AssertEx.Equal(1, reader.GetInt());
		AssertEx.Equal(52, reader.AvailableBytes);
		AssertEx.Equal(shot.ProjectileOrigin, reader.GetUnmanaged<Vector3>());
		AssertEx.Equal(shot.ProjectileDirection, reader.GetUnmanaged<Vector3>());
		AssertEx.Equal(shot.TracerStart, reader.GetUnmanaged<Vector3>());
		AssertEx.Equal(shot.TracerEnd, reader.GetUnmanaged<Vector3>());
		AssertEx.Near(shot.ImpactDelaySeconds, reader.GetFloat(), 0.0001f);
		AssertEx.Equal(0, reader.AvailableBytes);
	}

	private static A10TracerBurstPacket RoundTrip(A10TracerBurstPacket source)
	{
		var writer = new NetDataWriter();
		source.Serialize(writer);
		var reader = new NetDataReader(writer.ToArray());
		var received = new A10TracerBurstPacket();
		received.Deserialize(reader);
		AssertEx.Equal(0, reader.AvailableBytes);
		return received;
	}

	private static A10TracerBurstPacket MakePacket(A10TracerSegment shot)
	{
		A10TracerSegment[] segments = [shot];
		var burst = new A10TracerBurst(7, "strike-7:pass:0", 2468, 0, 100f, segments);
		return new A10TracerBurstPacket(burst, 0, 1, segments);
	}

	private static A10TracerSegment MakeShot()
	{
		return new A10TracerSegment(
			new Vector3(1400f, 320f, 25f),
			new Vector3(-0.98f, -0.2f, 0f),
			new Vector3(41f, 9f, 25f),
			new Vector3(0f, 0f, 25f),
			0.35f)
		{
			FlightTimeSeconds = 3.3f,
			IntendedImpact = new Vector3(0f, 0f, 25f)
		};
	}
}
