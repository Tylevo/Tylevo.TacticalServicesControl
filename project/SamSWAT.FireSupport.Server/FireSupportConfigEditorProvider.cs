using SamSWAT.FireSupport.ArysReloaded.Unity;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Web.Models.Configs;
using SPTarkov.Server.Web.Services;
using System.Text.Json.Serialization;

namespace SamSWAT.FireSupport.ArysReloaded;

[Injectable(InjectionType.Singleton)]
public sealed class FireSupportConfigEditorProvider(
	FireSupportServerConfigService configService) : IConfigEditorConfigProvider
{
	private const string ConfigId = "com.tylevo.tacticalservicescontrol";

	public IEnumerable<ConfigEditorConfigRegistration> GetConfigs()
	{
		// SIC asks for registrations for each operation. Keep its serialized view
		// local so concurrent editor sessions cannot overwrite one another's DTO.
		FireSupportConfigEditorView runtimeView =
			FireSupportConfigEditorView.FromConfig(configService.GetConfigSnapshot());
		yield return new ConfigEditorConfigRegistration
		{
			Id = ConfigId,
			DisplayName = "Tactical Services Control",
			RuntimeConfig = runtimeView,
			RuntimeType = typeof(FireSupportConfigEditorView),
			FileName = "tsc-config.json",
			IgnoredSectionPaths = new HashSet<string>(StringComparer.Ordinal)
			{
				"/revision"
			},
			LoadFromDiskAsync = token => LoadFromDiskAsync(runtimeView, token),
			ApplyToRuntimeAsync = (edited, token) => ApplyAsync(edited, token, runtimeView),
			SaveToDiskAsync = (edited, token) => SaveAsync(edited, token, runtimeView)
		};
	}

	private ValueTask<object?> LoadFromDiskAsync(
		FireSupportConfigEditorView runtimeView,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (!configService.TryGetDiskConfigSnapshot(out RaidOpsFireSupportServerConfig diskConfig, out string error))
		{
			throw new InvalidOperationException(error);
		}

		FireSupportConfigEditorView runtime = FireSupportConfigEditorView.FromConfig(configService.GetConfigSnapshot());
		CopyView(runtime, runtimeView);
		// The service captures disk values and their current edit generation under
		// one lock. Retain that revision so a later concurrent edit stays detectable.
		return ValueTask.FromResult<object?>(FireSupportConfigEditorView.FromConfig(diskConfig));
	}

	private ValueTask ApplyAsync(
		object editedConfig,
		CancellationToken cancellationToken,
		FireSupportConfigEditorView runtimeView)
	{
		return UpdateAsync(editedConfig, cancellationToken, runtimeView, saveToDisk: false);
	}

	private ValueTask SaveAsync(
		object editedConfig,
		CancellationToken cancellationToken,
		FireSupportConfigEditorView runtimeView)
	{
		return UpdateAsync(editedConfig, cancellationToken, runtimeView, saveToDisk: true);
	}

	private ValueTask UpdateAsync(
		object editedConfig,
		CancellationToken cancellationToken,
		FireSupportConfigEditorView runtimeView,
		bool saveToDisk)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (editedConfig is not FireSupportConfigEditorView edited)
		{
			throw new InvalidOperationException(
				"SPT supplied an unexpected Tactical Services Control config type.");
		}

		string error;
		RaidOpsFireSupportServerConfig current;
		if (saveToDisk)
		{
			if (!configService.TryGetDiskConfigSnapshot(out current, out error))
			{
				throw new InvalidOperationException(error);
			}
		}
		else
		{
			current = configService.GetConfigSnapshot();
		}

		// Overlay only the curated values on the target being edited, preserving
		// settings the native editor does not expose in either runtime or disk.
		RaidOpsFireSupportServerConfig candidate = edited.ApplyTo(current);
		bool updated = saveToDisk
			? configService.TrySaveConfig(candidate, out error, edited.Revision)
			: configService.TryApplyConfig(candidate, out error, edited.Revision);
		if (!updated)
		{
			throw new InvalidOperationException(error);
		}

		CopyView(
			FireSupportConfigEditorView.FromConfig(configService.GetConfigSnapshot()),
			runtimeView);
		return ValueTask.CompletedTask;
	}

	private static void CopyView(
		FireSupportConfigEditorView source,
		FireSupportConfigEditorView destination)
	{
		destination.Revision = source.Revision;
		destination.PaymentMode = source.PaymentMode;
		destination.PaymentSource = source.PaymentSource;
		destination.PaymentCurrency = source.PaymentCurrency;
		destination.RequestCooldownSeconds = source.RequestCooldownSeconds;
		destination.Prices = new Dictionary<string, int>(source.Prices);
		destination.Enabled = new Dictionary<string, bool>(source.Enabled);
		destination.PurchasePersistence = source.PurchasePersistence;
		destination.Uav = source.Uav;
		destination.FocusedSweep = source.FocusedSweep;
		destination.Extraction = source.Extraction;
		destination.PriorityExfil = source.PriorityExfil;
		destination.DoublePass = source.DoublePass;
	}
}

public sealed class FireSupportConfigEditorView
{
	[JsonPropertyName("revision")]
	public int Revision { get; set; }

	[JsonPropertyName("paymentMode")]
	public string PaymentMode { get; set; } = string.Empty;

	[JsonPropertyName("paymentSource")]
	public string PaymentSource { get; set; } = string.Empty;

	[JsonPropertyName("paymentCurrency")]
	public string PaymentCurrency { get; set; } = string.Empty;

	[JsonPropertyName("requestCooldownSeconds")]
	public int RequestCooldownSeconds { get; set; }

	[JsonPropertyName("prices")]
	public Dictionary<string, int> Prices { get; set; } = new();

	[JsonPropertyName("enabled")]
	public Dictionary<string, bool> Enabled { get; set; } = new();

	[JsonPropertyName("purchasePersistence")]
	public FireSupportPurchasePersistenceEditorView PurchasePersistence { get; set; } = new();

	[JsonPropertyName("uav")]
	public FireSupportUavEditorView Uav { get; set; } = new();

	[JsonPropertyName("focusedSweep")]
	public FireSupportUavEditorView FocusedSweep { get; set; } = new();

	[JsonPropertyName("extraction")]
	public FireSupportExtractionEditorView Extraction { get; set; } = new();

	[JsonPropertyName("priorityExfil")]
	public FireSupportCargoEditorView PriorityExfil { get; set; } = new();

	[JsonPropertyName("doublePass")]
	public FireSupportDoublePassEditorView DoublePass { get; set; } = new();

	public static FireSupportConfigEditorView FromConfig(
		RaidOpsFireSupportServerConfig config)
	{
		return new FireSupportConfigEditorView
		{
			Revision = config.Revision,
			PaymentMode = config.PaymentMode,
			PaymentSource = config.PaymentSource,
			PaymentCurrency = config.PaymentCurrency,
			RequestCooldownSeconds = config.RequestCooldownSeconds,
			Prices = new Dictionary<string, int>(config.Prices),
			Enabled = new Dictionary<string, bool>(config.Enabled),
			PurchasePersistence = FireSupportPurchasePersistenceEditorView.FromConfig(
				config.PurchasePersistence),
			Uav = FireSupportUavEditorView.FromConfig(config.Uav),
			FocusedSweep = FireSupportUavEditorView.FromConfig(config.FocusedSweep),
			Extraction = FireSupportExtractionEditorView.FromConfig(config.Extraction),
			PriorityExfil = FireSupportCargoEditorView.FromConfig(config.PriorityExfil),
			DoublePass = FireSupportDoublePassEditorView.FromConfig(config.DoublePass)
		};
	}

	public RaidOpsFireSupportServerConfig ApplyTo(RaidOpsFireSupportServerConfig config)
	{
		if (Prices is null || Enabled is null || PurchasePersistence is null || Uav is null
			|| FocusedSweep is null || Extraction is null || PriorityExfil is null || DoublePass is null)
		{
			throw new InvalidOperationException(
				"Tactical Services Control settings sections cannot be null. Reload the config and try again.");
		}

		config.PaymentMode = PaymentMode;
		config.PaymentSource = PaymentSource;
		config.PaymentCurrency = PaymentCurrency;
		config.RequestCooldownSeconds = RequestCooldownSeconds;
		config.Prices = new Dictionary<string, int>(Prices);
		config.Enabled = new Dictionary<string, bool>(Enabled);
		PurchasePersistence.ApplyTo(config.PurchasePersistence);
		Uav.ApplyTo(config.Uav);
		FocusedSweep.ApplyTo(config.FocusedSweep);
		Extraction.ApplyTo(config.Extraction);
		PriorityExfil.ApplyTo(config.PriorityExfil);
		DoublePass.ApplyTo(config.DoublePass);
		return config;
	}
}

public sealed class FireSupportPurchasePersistenceEditorView
{
	public bool Enabled { get; set; }
	public int MaxStoredAuthorizationsPerService { get; set; }
	public int PendingUseTimeoutSeconds { get; set; }
	public bool SpendCreditsBeforeCash { get; set; }
	public bool AllowAutoPurchaseOnUse { get; set; }

	public static FireSupportPurchasePersistenceEditorView FromConfig(
		RaidOpsFireSupportServerConfig.PurchasePersistenceSettings config) => new()
	{
		Enabled = config.Enabled,
		MaxStoredAuthorizationsPerService = config.MaxStoredAuthorizationsPerService,
		PendingUseTimeoutSeconds = config.PendingUseTimeoutSeconds,
		SpendCreditsBeforeCash = config.SpendCreditsBeforeCash,
		AllowAutoPurchaseOnUse = config.AllowAutoPurchaseOnUse
	};

	public void ApplyTo(RaidOpsFireSupportServerConfig.PurchasePersistenceSettings config)
	{
		config.Enabled = Enabled;
		config.MaxStoredAuthorizationsPerService = MaxStoredAuthorizationsPerService;
		config.PendingUseTimeoutSeconds = PendingUseTimeoutSeconds;
		config.SpendCreditsBeforeCash = SpendCreditsBeforeCash;
		config.AllowAutoPurchaseOnUse = AllowAutoPurchaseOnUse;
	}
}

public sealed class FireSupportUavEditorView
{
	public int DurationSeconds { get; set; }
	public float RangeMeters { get; set; }
	public float ScanIntervalSeconds { get; set; }

	public static FireSupportUavEditorView FromConfig(
		RaidOpsFireSupportServerConfig.UavSettings config) => new()
	{
		DurationSeconds = config.DurationSeconds,
		RangeMeters = config.RangeMeters,
		ScanIntervalSeconds = config.ScanIntervalSeconds
	};

	public void ApplyTo(RaidOpsFireSupportServerConfig.UavSettings config)
	{
		config.DurationSeconds = DurationSeconds;
		config.RangeMeters = RangeMeters;
		config.ScanIntervalSeconds = ScanIntervalSeconds;
	}
}

public sealed class FireSupportExtractionEditorView
{
	public float DispatchDelaySeconds { get; set; }
	public int WaitTimeSeconds { get; set; }
	public float ExtractTimeSeconds { get; set; }
	public float SpeedMultiplier { get; set; }

	public static FireSupportExtractionEditorView FromConfig(
		RaidOpsFireSupportServerConfig.ExtractionSettings config) => new()
	{
		DispatchDelaySeconds = config.DispatchDelaySeconds,
		WaitTimeSeconds = config.WaitTimeSeconds,
		ExtractTimeSeconds = config.ExtractTimeSeconds,
		SpeedMultiplier = config.SpeedMultiplier
	};

	public void ApplyTo(RaidOpsFireSupportServerConfig.ExtractionSettings config)
	{
		config.DispatchDelaySeconds = DispatchDelaySeconds;
		config.WaitTimeSeconds = WaitTimeSeconds;
		config.ExtractTimeSeconds = ExtractTimeSeconds;
		config.SpeedMultiplier = SpeedMultiplier;
	}
}

public sealed class FireSupportCargoEditorView
{
	public float DispatchDelaySeconds { get; set; }
	public int WaitTimeSeconds { get; set; }
	public float SpeedMultiplier { get; set; }

	public static FireSupportCargoEditorView FromConfig(
		RaidOpsFireSupportServerConfig.CargoSettings config) => new()
	{
		DispatchDelaySeconds = config.DispatchDelaySeconds,
		WaitTimeSeconds = config.WaitTimeSeconds,
		SpeedMultiplier = config.SpeedMultiplier
	};

	public void ApplyTo(RaidOpsFireSupportServerConfig.CargoSettings config)
	{
		config.DispatchDelaySeconds = DispatchDelaySeconds;
		config.WaitTimeSeconds = WaitTimeSeconds;
		config.SpeedMultiplier = SpeedMultiplier;
	}
}

public sealed class FireSupportDoublePassEditorView
{
	public float SecondPassDelaySeconds { get; set; }

	public static FireSupportDoublePassEditorView FromConfig(
		RaidOpsFireSupportServerConfig.A10Settings config) => new()
	{
		SecondPassDelaySeconds = config.SecondPassDelaySeconds
	};

	public void ApplyTo(RaidOpsFireSupportServerConfig.A10Settings config)
	{
		config.SecondPassDelaySeconds = SecondPassDelaySeconds;
	}
}
