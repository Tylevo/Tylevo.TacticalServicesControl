using Fika.Core.Networking.LiteNetLib.Utils;
using SamSWAT.FireSupport.ArysReloaded.Unity;

namespace SamSWAT.FireSupport.ArysReloaded.Fika;

public class FireSupportSettingsPacket : INetSerializable
{
	public bool IsRequest;
	public int Revision;
	public int StrafeCostRoubles;
	public int DoubleStrafeCostRoubles;
	public int ExtractionCostRoubles;
	public int PriorityExfilCostRoubles;
	public int UavCostRoubles;
	public int FocusedSweepCostRoubles;
	public bool EnablePriorityExfil;
	public bool EnableDoublePass;
	public bool EnableFocusedSweep;
	public int UavDurationSeconds;
	public float UavScanIntervalSeconds;
	public float UavRangeMeters;
	public int FocusedSweepDurationSeconds;
	public float FocusedSweepScanIntervalSeconds;
	public float FocusedSweepRangeMeters;
	public float DoubleStrafeSecondPassDelaySeconds;
	public int HelicopterWaitTimeSeconds;
	public int PriorityExfilHelicopterWaitTimeSeconds;
	public float PriorityExfilDispatchDelaySeconds;
	public float HelicopterExtractTimeSeconds;
	public float HelicopterSpeedMultiplier;
	public float PriorityExfilHelicopterSpeedMultiplier;
	public int RequestCooldownSeconds;
	public PaymentMode PaymentMode;
	public PaymentSource PaymentSource;
	public string ServerConfigUrl;

	public FireSupportSettingsPacket()
	{
	}

	public static FireSupportSettingsPacket CreateRequest()
	{
		return new FireSupportSettingsPacket
		{
			IsRequest = true
		};
	}

	public void Serialize(NetDataWriter writer)
	{
		writer.Put(IsRequest);
		writer.Put(Revision);
		writer.Put(StrafeCostRoubles);
		writer.Put(DoubleStrafeCostRoubles);
		writer.Put(ExtractionCostRoubles);
		writer.Put(PriorityExfilCostRoubles);
		writer.Put(UavCostRoubles);
		writer.Put(FocusedSweepCostRoubles);
		writer.Put(EnablePriorityExfil);
		writer.Put(EnableDoublePass);
		writer.Put(EnableFocusedSweep);
		writer.Put(UavDurationSeconds);
		writer.Put(UavScanIntervalSeconds);
		writer.Put(UavRangeMeters);
		writer.Put(FocusedSweepDurationSeconds);
		writer.Put(FocusedSweepScanIntervalSeconds);
		writer.Put(FocusedSweepRangeMeters);
		writer.Put(DoubleStrafeSecondPassDelaySeconds);
		writer.Put(HelicopterWaitTimeSeconds);
		writer.Put(PriorityExfilHelicopterWaitTimeSeconds);
		writer.Put(PriorityExfilDispatchDelaySeconds);
		writer.Put(HelicopterExtractTimeSeconds);
		writer.Put(HelicopterSpeedMultiplier);
		writer.Put(PriorityExfilHelicopterSpeedMultiplier);
		writer.Put(RequestCooldownSeconds);
		writer.Put((int)PaymentMode);
		writer.Put((int)PaymentSource);
		writer.Put(ServerConfigUrl ?? string.Empty);
	}

	public void Deserialize(NetDataReader reader)
	{
		IsRequest = reader.GetBool();
		Revision = reader.GetInt();
		StrafeCostRoubles = reader.GetInt();
		DoubleStrafeCostRoubles = reader.GetInt();
		ExtractionCostRoubles = reader.GetInt();
		PriorityExfilCostRoubles = reader.GetInt();
		UavCostRoubles = reader.GetInt();
		FocusedSweepCostRoubles = reader.GetInt();
		EnablePriorityExfil = reader.GetBool();
		EnableDoublePass = reader.GetBool();
		EnableFocusedSweep = reader.GetBool();
		UavDurationSeconds = reader.GetInt();
		UavScanIntervalSeconds = reader.GetFloat();
		UavRangeMeters = reader.GetFloat();
		FocusedSweepDurationSeconds = reader.GetInt();
		FocusedSweepScanIntervalSeconds = reader.GetFloat();
		FocusedSweepRangeMeters = reader.GetFloat();
		DoubleStrafeSecondPassDelaySeconds = reader.GetFloat();
		HelicopterWaitTimeSeconds = reader.GetInt();
		PriorityExfilHelicopterWaitTimeSeconds = reader.GetInt();
		PriorityExfilDispatchDelaySeconds = reader.GetFloat();
		HelicopterExtractTimeSeconds = reader.GetFloat();
		HelicopterSpeedMultiplier = reader.GetFloat();
		PriorityExfilHelicopterSpeedMultiplier = reader.GetFloat();
		RequestCooldownSeconds = reader.GetInt();
		PaymentMode = (PaymentMode)reader.GetInt();
		PaymentSource = (PaymentSource)reader.GetInt();
		ServerConfigUrl = reader.GetString();
	}
}
