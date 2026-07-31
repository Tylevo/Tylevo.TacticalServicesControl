using Fika.Core.Networking.LiteNetLib.Utils;
using SamSWAT.FireSupport.ArysReloaded.Unity;

namespace SamSWAT.FireSupport.ArysReloaded.Fika;

/// <summary>
/// Reliable host-to-client signal that an accepted UH-60 Cargo request has
/// completed EFT's native item move and should leave immediately.
/// </summary>
public sealed class Uh60CargoDeparturePacket : INetSerializable
{
	public string SupportRequestId = string.Empty;
	public string RequesterProfileId = string.Empty;
	public ESupportType SupportType = ESupportType.PriorityExfil;
	public bool SuccessfulTransfer;

	public Uh60CargoDeparturePacket()
	{
	}

	public Uh60CargoDeparturePacket(
		string supportRequestId,
		string requesterProfileId,
		bool successfulTransfer)
	{
		SupportRequestId = supportRequestId ?? string.Empty;
		RequesterProfileId = requesterProfileId ?? string.Empty;
		SuccessfulTransfer = successfulTransfer;
	}

	public void Serialize(NetDataWriter writer)
	{
		writer.Put(SupportRequestId ?? string.Empty);
		writer.Put(RequesterProfileId ?? string.Empty);
		writer.Put((int)SupportType);
		writer.Put(SuccessfulTransfer);
	}

	public void Deserialize(NetDataReader reader)
	{
		SupportRequestId = reader.GetString() ?? string.Empty;
		RequesterProfileId = reader.GetString() ?? string.Empty;
		SupportType = (ESupportType)reader.GetInt();
		SuccessfulTransfer = reader.GetBool();
	}
}
