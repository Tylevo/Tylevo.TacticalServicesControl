using Fika.Core.Networking.LiteNetLib.Utils;
using SamSWAT.FireSupport.ArysReloaded.Unity;

namespace SamSWAT.FireSupport.ArysReloaded.Fika;

/// <summary>
/// Client request to cancel an authority operation that has not reached a
/// terminal decision. The host matches all identity fields and the sending
/// peer before it is allowed to cancel executor work.
/// </summary>
public sealed class FireSupportCancelPacket : INetSerializable
{
	public string SupportRequestId = string.Empty;
	public ESupportType SupportType;
	public int PassIndex;
	public string RequesterProfileId = string.Empty;
	public FireSupportRequestOrigin RequestOrigin = FireSupportRequestOrigin.Manual;

	public FireSupportCancelPacket()
	{
	}

	public FireSupportCancelPacket(FireSupportRequestPacket request)
	{
		SupportRequestId = request?.SupportRequestId ?? string.Empty;
		SupportType = request?.SupportType ?? ESupportType.Strafe;
		PassIndex = request?.PassIndex ?? 0;
		RequesterProfileId = request?.RequesterProfileId ?? string.Empty;
		RequestOrigin = request?.RequestOrigin ?? FireSupportRequestOrigin.Manual;
	}

	public void Serialize(NetDataWriter writer)
	{
		writer.Put(SupportRequestId ?? string.Empty);
		writer.Put((int)SupportType);
		writer.Put(PassIndex);
		writer.Put(RequesterProfileId ?? string.Empty);
		writer.Put((int)RequestOrigin);
	}

	public void Deserialize(NetDataReader reader)
	{
		SupportRequestId = reader.GetString() ?? string.Empty;
		SupportType = (ESupportType)reader.GetInt();
		PassIndex = reader.GetInt();
		RequesterProfileId = reader.GetString() ?? string.Empty;
		RequestOrigin = reader.AvailableBytes >= sizeof(int)
			? (FireSupportRequestOrigin)reader.GetInt()
			: FireSupportRequestOrigin.Manual;
	}
}
