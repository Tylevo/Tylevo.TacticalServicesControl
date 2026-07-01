using Fika.Core.Networking.LiteNetLib.Utils;
using SamSWAT.FireSupport.ArysReloaded.Unity;

namespace SamSWAT.FireSupport.ArysReloaded.Fika;

public class UavPhoneVisualPacket : INetSerializable
{
	public string ProfileId = string.Empty;
	public string AccountId = string.Empty;
	public ESupportType SupportType;
	public UavPhoneVisualPhase Phase;
	public double StartTime;
	public float Duration;
	public bool Success;

	public UavPhoneVisualPacket()
	{
	}

	public UavPhoneVisualPacket(UavPhoneVisualEvent visualEvent)
	{
		ProfileId = visualEvent.ProfileId ?? string.Empty;
		AccountId = visualEvent.AccountId ?? string.Empty;
		SupportType = visualEvent.SupportType;
		Phase = visualEvent.Phase;
		StartTime = visualEvent.StartTime;
		Duration = visualEvent.Duration;
		Success = visualEvent.Success;
	}

	public void Serialize(NetDataWriter writer)
	{
		writer.Put(ProfileId ?? string.Empty);
		writer.Put(AccountId ?? string.Empty);
		writer.Put((int)SupportType);
		writer.Put((int)Phase);
		writer.Put(StartTime);
		writer.Put(Duration);
		writer.Put(Success);
	}

	public void Deserialize(NetDataReader reader)
	{
		ProfileId = reader.GetString();
		AccountId = reader.GetString();
		SupportType = (ESupportType)reader.GetInt();
		Phase = (UavPhoneVisualPhase)reader.GetInt();
		StartTime = reader.GetDouble();
		Duration = reader.GetFloat();
		Success = reader.GetBool();
	}
}
