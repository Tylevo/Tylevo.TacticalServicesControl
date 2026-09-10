using SamSWAT.FireSupport.ArysReloaded.Unity;

namespace SamSWAT.FireSupport.ArysReloaded;

/// <summary>Portable gameplay settings. Applying these remains an ordinary revision-checked config edit.</summary>
public static class FireSupportPresetCatalog
{
	public const string Format = "tsc-preset";
	public const int FormatVersion = 1;
	private static readonly string[] s_services = { "A10", "DoublePass", "Uav", "FocusedSweep", "Extraction", "PriorityExfil" };

	public static FireSupportPresetCollection Create(RaidOpsFireSupportServerConfig defaults)
	{
		Dictionary<string, object> balanced = ProjectSettings(defaults);
		var casual = Copy(balanced);
		SetPrices(casual, 75000, 125000, 25000, 15000, 75000, 40000);
		casual["requestCooldownSeconds"] = 60;
		casual["purchasePersistence.maxStoredAuthorizationsPerService"] = 5;
		SetRecon(casual, "uav", 600, 250f, 3f);
		SetRecon(casual, "focusedSweep", 120, 125f, 0.5f);
		SetExtraction(casual, 5f, 45, 8f, 1.15f);
		SetCargo(casual, 2f, 30, 1.5f);

		var hardcore = Copy(balanced);
		SetPrices(hardcore, 500000, 800000, 200000, 100000, 400000, 250000);
		hardcore["requestCooldownSeconds"] = 600;
		hardcore["purchasePersistence.maxStoredAuthorizationsPerService"] = 1;
		SetRecon(hardcore, "uav", 240, 150f, 7.5f);
		SetRecon(hardcore, "focusedSweep", 60, 75f, 1.5f);
		SetExtraction(hardcore, 20f, 20, 15f, 0.85f);
		SetCargo(hardcore, 10f, 15, 1.15f);

		var barter = Copy(balanced);
		SetPrices(barter, 1, 2, 10000, 1, 1, 2);
		barter["paymentCurrency"] = "RUB";
		barter["paymentSource"] = "StashRoubles";
		barter["serviceCurrencies.A10"] = "GP";
		barter["serviceCurrencies.DoublePass"] = "GP";
		barter["serviceCurrencies.Uav"] = "RUB";
		barter["serviceCurrencies.FocusedSweep"] = "GP";
		barter["serviceCurrencies.Extraction"] = "BTC";
		barter["serviceCurrencies.PriorityExfil"] = "GP";

		return new FireSupportPresetCollection
		{
			Presets = new[]
			{
				Preset("balanced", "Balanced", "Lower rouble prices with the standard service timing and limits.", balanced),
				Preset("casual", "Casual", "Cheaper support, shorter cooldowns, more stored authorizations and stronger recon.", casual),
				Preset("hardcore", "Hardcore", "Expensive support, longer cooldowns, one stored authorization and reduced recon.", hardcore),
				Preset("barter", "Barter", "Pay from the stash with GP coins and Bitcoin; UAV support costs 10,000 roubles.", barter),
				Preset("classic", "Classic 1.3.12", "Restore the original rouble prices, carried wallet, service timing and gameplay defaults.", CreateClassicSettings())
			}
		};
	}

	internal static Dictionary<string, object> ProjectSettings(RaidOpsFireSupportServerConfig config)
	{
		// Explicit allowlist: dashboard security, profile state, revisions and dormant wiring
		// cannot become portable merely because another property is added to the config DTO.
		var settings = new Dictionary<string, object>(StringComparer.Ordinal)
		{
			["paymentMode"] = config.PaymentMode,
			["paymentCurrency"] = config.PaymentCurrency,
			["paymentSource"] = config.PaymentSource,
			["requestCooldownSeconds"] = config.RequestCooldownSeconds,
			["purchasePersistence.enabled"] = config.PurchasePersistence.Enabled,
			["purchasePersistence.maxStoredAuthorizationsPerService"] = config.PurchasePersistence.MaxStoredAuthorizationsPerService,
			["purchasePersistence.pendingUseTimeoutSeconds"] = config.PurchasePersistence.PendingUseTimeoutSeconds,
			["purchasePersistence.spendCreditsBeforeCash"] = config.PurchasePersistence.SpendCreditsBeforeCash,
			["purchasePersistence.allowAutoPurchaseOnUse"] = config.PurchasePersistence.AllowAutoPurchaseOnUse,
			["priorityExfil.gridWidth"] = config.PriorityExfil.GridWidth,
			["priorityExfil.gridHeight"] = config.PriorityExfil.GridHeight,
			["doublePass.secondPassDelaySeconds"] = config.DoublePass.SecondPassDelaySeconds
		};
		foreach (string service in s_services)
		{
			settings[$"prices.{service}"] = config.Prices[service];
			settings[$"serviceCurrencies.{service}"] = config.ServiceCurrencies[service];
			settings[$"enabled.{service}"] = config.Enabled[service];
		}
		SetRecon(settings, "uav", config.Uav.DurationSeconds, config.Uav.RangeMeters, config.Uav.ScanIntervalSeconds);
		SetRecon(settings, "focusedSweep", config.FocusedSweep.DurationSeconds, config.FocusedSweep.RangeMeters, config.FocusedSweep.ScanIntervalSeconds);
		SetExtraction(settings, config.Extraction.DispatchDelaySeconds, config.Extraction.WaitTimeSeconds, config.Extraction.ExtractTimeSeconds, config.Extraction.SpeedMultiplier);
		SetCargo(settings, config.PriorityExfil.DispatchDelaySeconds, config.PriorityExfil.WaitTimeSeconds, config.PriorityExfil.SpeedMultiplier);
		return settings;
	}

	private static Dictionary<string, object> CreateClassicSettings()
	{
		// Freeze every exposed 1.3.12 gameplay value so future default changes cannot alter Classic.
		var settings = new Dictionary<string, object>(StringComparer.Ordinal)
		{
			["paymentMode"] = "PhoneAuthorizations",
			["paymentCurrency"] = "RUB",
			["paymentSource"] = "CarriedRoubles",
			["requestCooldownSeconds"] = 300,
			["purchasePersistence.enabled"] = true,
			["purchasePersistence.maxStoredAuthorizationsPerService"] = 2,
			["purchasePersistence.pendingUseTimeoutSeconds"] = 180,
			["purchasePersistence.spendCreditsBeforeCash"] = true,
			["purchasePersistence.allowAutoPurchaseOnUse"] = true,
			["priorityExfil.gridWidth"] = 0,
			["priorityExfil.gridHeight"] = 0,
			["doublePass.secondPassDelaySeconds"] = 14f
		};
		foreach (string service in s_services)
		{
			settings[$"serviceCurrencies.{service}"] = "Inherit";
			settings[$"enabled.{service}"] = true;
		}
		SetPrices(settings, 250000, 450000, 125000, 90000, 300000, 450000);
		SetRecon(settings, "uav", 480, 200f, 5f);
		SetRecon(settings, "focusedSweep", 90, 100f, 0.75f);
		SetExtraction(settings, 8f, 30, 10f, 1f);
		SetCargo(settings, 3f, 20, 1.35f);
		return settings;
	}

	private static Dictionary<string, object> Copy(Dictionary<string, object> settings) => new(settings, StringComparer.Ordinal);

	private static FireSupportPreset Preset(string id, string name, string description, Dictionary<string, object> settings) =>
		new() { Id = id, Name = name, Description = description, Settings = settings };

	private static void SetPrices(Dictionary<string, object> settings, int a10, int doublePass, int uav, int focusedSweep, int extraction, int cargo)
	{
		settings["prices.A10"] = a10;
		settings["prices.DoublePass"] = doublePass;
		settings["prices.Uav"] = uav;
		settings["prices.FocusedSweep"] = focusedSweep;
		settings["prices.Extraction"] = extraction;
		settings["prices.PriorityExfil"] = cargo;
	}

	private static void SetRecon(Dictionary<string, object> settings, string service, int duration, float range, float interval)
	{
		settings[$"{service}.durationSeconds"] = duration;
		settings[$"{service}.rangeMeters"] = range;
		settings[$"{service}.scanIntervalSeconds"] = interval;
	}

	private static void SetExtraction(Dictionary<string, object> settings, float dispatch, int wait, float extraction, float speed)
	{
		settings["extraction.dispatchDelaySeconds"] = dispatch;
		settings["extraction.waitTimeSeconds"] = wait;
		settings["extraction.extractTimeSeconds"] = extraction;
		settings["extraction.speedMultiplier"] = speed;
	}

	private static void SetCargo(Dictionary<string, object> settings, float dispatch, int wait, float speed)
	{
		settings["priorityExfil.dispatchDelaySeconds"] = dispatch;
		settings["priorityExfil.waitTimeSeconds"] = wait;
		settings["priorityExfil.speedMultiplier"] = speed;
	}
}

public sealed class FireSupportPresetCollection
{
	public string Format => FireSupportPresetCatalog.Format;
	public int FormatVersion => FireSupportPresetCatalog.FormatVersion;
	public IReadOnlyList<FireSupportPreset> Presets { get; init; } = Array.Empty<FireSupportPreset>();
}

public sealed class FireSupportPreset
{
	public string Id { get; init; } = string.Empty;
	public string Name { get; init; } = string.Empty;
	public string Description { get; init; } = string.Empty;
	public string Format => FireSupportPresetCatalog.Format;
	public int FormatVersion => FireSupportPresetCatalog.FormatVersion;
	public Dictionary<string, object> Settings { get; init; } = new(StringComparer.Ordinal);
}
