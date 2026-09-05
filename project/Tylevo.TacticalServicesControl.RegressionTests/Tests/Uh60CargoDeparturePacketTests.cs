using Fika.Core.Networking.LiteNetLib.Utils;
using SamSWAT.FireSupport.ArysReloaded.Fika;
using SamSWAT.FireSupport.ArysReloaded.Unity;

internal static class Uh60CargoDeparturePacketTests
{
	[RegressionTest]
	private static void PacketRoundTripsTheAcceptedCargoIdentity()
	{
		var expected = new Uh60CargoDeparturePacket(
			"authorization-1:pass:0",
			"profile-1",
			successfulTransfer: true);
		var writer = new NetDataWriter();
		expected.Serialize(writer);

		var actual = new Uh60CargoDeparturePacket();
		var reader = new NetDataReader(writer.ToArray());
		actual.Deserialize(reader);

		AssertEx.Equal(expected.SupportRequestId, actual.SupportRequestId);
		AssertEx.Equal(
			expected.RequesterProfileId,
			actual.RequesterProfileId);
		AssertEx.Equal(
			ESupportType.PriorityExfil,
			actual.SupportType);
		AssertEx.True(actual.SuccessfulTransfer);
		AssertEx.Equal(0, reader.AvailableBytes);
	}

	[RegressionTest]
	private static void EarlyRemoteDepartureRemainsAvailableForLateBinding()
	{
		Uh60CargoDepartureNetworking.ResetRemoteDepartures();
		Uh60CargoDepartureNetworking.ApplyRemoteDeparture(
			"authorization-2:pass:0",
			successfulTransfer: false);

		AssertEx.True(
			Uh60CargoDepartureNetworking.TryGetRemoteDeparture(
				"authorization-2:pass:0",
				out bool successfulTransfer));
		AssertEx.False(successfulTransfer);

		Uh60CargoDepartureNetworking.ResetRemoteDepartures();
		AssertEx.False(
			Uh60CargoDepartureNetworking.TryGetRemoteDeparture(
				"authorization-2:pass:0",
				out _));
	}

	[RegressionTest]
	private static void LocalDeparturePublicationReportsTransportFailure()
	{
		Uh60CargoDepartureNetworking.DepartureHandler reject =
			(_, _, _) => false;
		Uh60CargoDepartureNetworking.DeparturePublished += reject;
		try
		{
			AssertEx.False(
				Uh60CargoDepartureNetworking.TryPublishDeparture(
					"authorization-3:pass:0",
					"profile-3",
					successfulTransfer: true));
		}
		finally
		{
			Uh60CargoDepartureNetworking.DeparturePublished -= reject;
		}

		AssertEx.True(
			Uh60CargoDepartureNetworking.TryPublishDeparture(
				"authorization-3:pass:0",
				"profile-3",
				successfulTransfer: true));
	}
}
