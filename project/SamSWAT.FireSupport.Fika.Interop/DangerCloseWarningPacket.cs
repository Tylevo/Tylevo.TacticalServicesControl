using Fika.Core.Networking.LiteNetLib.Utils;
using SamSWAT.FireSupport.ArysReloaded.Integration;

namespace SamSWAT.FireSupport.ArysReloaded.Fika;

/// <summary>
/// Reliable host-to-client presentation event for one authenticated Seasonal
/// Danger Close opportunity. Clients never send or register this packet on the
/// authority server.
/// </summary>
public sealed class DangerCloseWarningPacket : INetSerializable
{
	public DangerCloseWarningKind Kind;
	public string OpportunityId = string.Empty;
	public int SecondsRemaining;

	public DangerCloseWarningPacket()
	{
	}

	public DangerCloseWarningPacket(DangerCloseWarningPublication publication)
	{
		Kind = publication.Kind;
		OpportunityId = publication.OpportunityId ?? string.Empty;
		SecondsRemaining = publication.SecondsRemaining;
	}

	public DangerCloseWarningPublication ToPublication()
	{
		return new DangerCloseWarningPublication(
			Kind,
			OpportunityId,
			SecondsRemaining);
	}

	public void Serialize(NetDataWriter writer)
	{
		writer.Put((int)Kind);
		writer.Put(OpportunityId ?? string.Empty);
		writer.Put(SecondsRemaining);
	}

	public void Deserialize(NetDataReader reader)
	{
		Kind = (DangerCloseWarningKind)reader.GetInt();
		OpportunityId = reader.GetString() ?? string.Empty;
		SecondsRemaining = reader.GetInt();
	}
}
