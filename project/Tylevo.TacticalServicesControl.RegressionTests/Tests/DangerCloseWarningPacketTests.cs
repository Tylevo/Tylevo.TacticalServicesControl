using Fika.Core.Networking.LiteNetLib.Utils;
using SamSWAT.FireSupport.ArysReloaded.Fika;
using SamSWAT.FireSupport.ArysReloaded.Integration;

internal static class DangerCloseWarningPacketTests
{
	[RegressionTest]
	private static void TransportKindsRemainAuthorityToPeerOnly()
	{
		AssertEx.SequenceEqual(
			new[]
			{
				DangerCloseWarningKind.Advance,
				DangerCloseWarningKind.Cancel,
				DangerCloseWarningKind.Inbound
			},
			Enum.GetValues<DangerCloseWarningKind>());
	}

	[RegressionTest]
	private static void PacketRoundTripsEveryLifecycleKind()
	{
		foreach ((DangerCloseWarningKind kind, int seconds) in new[]
		         {
		             (DangerCloseWarningKind.Advance, 90),
		             (DangerCloseWarningKind.Cancel, 0),
		             (DangerCloseWarningKind.Inbound, 0)
		         })
		{
			var expected = new DangerCloseWarningPacket(
				new DangerCloseWarningPublication(
					kind,
					"seasonal:raid-1:opportunity-1",
					seconds));
			var writer = new NetDataWriter();
			expected.Serialize(writer);

			var actual = new DangerCloseWarningPacket();
			var reader = new NetDataReader(writer.ToArray());
			actual.Deserialize(reader);

			AssertEx.Equal(expected.Kind, actual.Kind);
			AssertEx.Equal(expected.OpportunityId, actual.OpportunityId);
			AssertEx.Equal(expected.SecondsRemaining, actual.SecondsRemaining);
			AssertEx.Equal(0, reader.AvailableBytes);
		}
	}
}
