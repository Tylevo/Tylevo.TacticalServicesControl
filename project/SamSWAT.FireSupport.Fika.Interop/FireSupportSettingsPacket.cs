using System;
using System.Collections.Generic;
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
	public float ExtractionDispatchDelaySeconds;
	public int HelicopterWaitTimeSeconds;
	public float ExtractionExtractTimeSeconds;
	public float HelicopterSpeedMultiplier;
	public float PriorityExfilDispatchDelaySeconds;
	public int PriorityExfilHelicopterWaitTimeSeconds;
	public float PriorityExfilExtractTimeSeconds;
	public float PriorityExfilHelicopterSpeedMultiplier;
	public int RequestCooldownSeconds;
	public PaymentMode PaymentMode;
	public PaymentSource PaymentSource;
	public string ServerConfigUrl;
	public PaymentCurrency PaymentCurrency;
	public Dictionary<string, string> ServiceCurrencies = new();
	public const int ServiceCurrencyTailBytes = 7 * sizeof(int);
	private static readonly ESupportType[] s_services = { ESupportType.Strafe, ESupportType.DoubleStrafe,
		ESupportType.Uav, ESupportType.FocusedSweep, ESupportType.Extract, ESupportType.PriorityExfil };
	public int ServiceSemanticsVersion = FireSupportServiceSemantics.CurrentVersion;

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
		writer.Put(ExtractionDispatchDelaySeconds);
		writer.Put(HelicopterWaitTimeSeconds);
		writer.Put(ExtractionExtractTimeSeconds);
		writer.Put(HelicopterSpeedMultiplier);
		writer.Put(PriorityExfilDispatchDelaySeconds);
		writer.Put(PriorityExfilHelicopterWaitTimeSeconds);
		writer.Put(PriorityExfilExtractTimeSeconds);
		writer.Put(PriorityExfilHelicopterSpeedMultiplier);
		writer.Put(RequestCooldownSeconds);
		writer.Put((int)PaymentMode);
		writer.Put((int)PaymentSource);
		writer.Put(ServerConfigUrl ?? string.Empty);
		writer.Put((int)PaymentCurrency);
		// Legacy readers must see unsupported progression semantics before they
		// can spend carried cash using a misinterpreted global currency.
		writer.Put(ServiceSemanticsVersion >= FireSupportServiceSemantics.ServiceCurrencyVersion
			? FireSupportServiceSemantics.LegacyVersion : ServiceSemanticsVersion);
		if (ServiceSemanticsVersion >= FireSupportServiceSemantics.ServiceCurrencyVersion)
		{
			writer.Put(ServiceSemanticsVersion);
			foreach (ESupportType type in s_services)
			{
				string code = ServicePaymentPolicy.GetCurrencyCode(PaymentCurrency.ToString(), ServiceCurrencies, type);
				writer.Put(PaymentCurrencyInfo.TryParse(code, out PaymentCurrency currency) ? (int)currency : -1);
			}
		}
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
		ExtractionDispatchDelaySeconds = reader.GetFloat();
		HelicopterWaitTimeSeconds = reader.GetInt();
		ExtractionExtractTimeSeconds = reader.GetFloat();
		HelicopterSpeedMultiplier = reader.GetFloat();
		PriorityExfilDispatchDelaySeconds = reader.GetFloat();
		PriorityExfilHelicopterWaitTimeSeconds = reader.GetInt();
		PriorityExfilExtractTimeSeconds = reader.GetFloat();
		PriorityExfilHelicopterSpeedMultiplier = reader.GetFloat();
		RequestCooldownSeconds = reader.GetInt();
		PaymentMode = (PaymentMode)reader.GetInt();
		PaymentSource = (PaymentSource)reader.GetInt();
		ServerConfigUrl = reader.GetString();
		PaymentCurrency = reader.AvailableBytes >= sizeof(int)
			? (PaymentCurrency)reader.GetInt()
			: global::SamSWAT.FireSupport.ArysReloaded.Unity.PaymentCurrency.RUB;
		ServiceSemanticsVersion = reader.AvailableBytes >= sizeof(int)
			? reader.GetInt()
			: FireSupportServiceSemantics.LegacyVersion;
		ServiceCurrencies = new Dictionary<string, string>();
		if (reader.AvailableBytes != 0)
		{
			if (reader.AvailableBytes != ServiceCurrencyTailBytes) throw new InvalidOperationException("Invalid TSC currency settings payload.");
			int advertisedSemantics = reader.GetInt();
			bool valid = FireSupportServiceSemantics.SupportsServiceCurrencies(advertisedSemantics);
			foreach (ESupportType type in s_services)
			{
				PaymentCurrency currency = (PaymentCurrency)reader.GetInt();
				valid &= PaymentCurrencyInfo.TryParse(currency.ToString(), out _);
				ServiceCurrencies[ServicePaymentPolicy.GetServiceKey(type)] = currency.ToString();
			}
			ServiceSemanticsVersion = valid ? advertisedSemantics : FireSupportServiceSemantics.LegacyVersion;
		}
		if (!PaymentCurrencyInfo.TryParse(PaymentCurrency.ToString(), out _))
			ServiceSemanticsVersion = FireSupportServiceSemantics.LegacyVersion;
	}
}
