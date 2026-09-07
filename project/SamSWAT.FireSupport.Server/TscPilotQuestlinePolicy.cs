using SPTarkov.DI.Annotations;
using System.Text.Json;

namespace SamSWAT.FireSupport.ArysReloaded;

/// <summary>The server's installed content selects one immutable progression mode per restart.</summary>
[Injectable(InjectionType.Singleton)]
public sealed class TscPilotQuestlinePolicy
{
	public const string AddonRelativePath = "addons/pilot-questline";
	public const string QuestRelativePath = AddonRelativePath + "/db/CustomQuests";
	public const string AssortRelativePath = AddonRelativePath + "/db/CustomAssortSchemes";
	public const string MechanicId = "5a7c2eca46aef81a7ca2145d";
	public const string PilotId = "66f51f3a0000000000000a60";
	public const string OpenChannelId = "66f51f3a0000000000000b01";
	public const string AssemblyQuestId = "66f51f3a0000000000000b02";
	public const string PhoneOfferId = "66f51f3a0000000000000a02";
	public const string RepeaterOfferId = "66f51f3a0000000000000a03";
	public const string PhoneTemplateId = "66f51f3a0000000000000a01";
	public const string RepeaterTemplateId = "63a0b2eabea67a6d93009e52";

	private bool _initializationAttempted;
	public bool IsInitialized { get; private set; }
	public bool IsActive { get; private set; }
	// Before startup has resolved the installed content, every caller fails closed.
	public bool QuestlineRequired { get; private set; } = true;

	public void Initialize(string pathToMod, string modVersion, string targetSptVersion)
	{
		if (_initializationAttempted)
			throw new InvalidOperationException("TSC progression mode is immutable until the server restarts.");
		_initializationAttempted = true;
		string addonPath = Path.Combine(pathToMod, AddonRelativePath);
		if (File.Exists(addonPath))
			throw InvalidAddon("the add-on path must be a directory");
		if (Directory.Exists(addonPath))
		{
			try
			{
				using JsonDocument manifest = ReadObject(Path.Combine(addonPath, "addon.json"));
				JsonElement metadata = manifest.RootElement;
				if (metadata.GetProperty("schemaVersion").GetInt32() != 1 ||
				    metadata.GetProperty("id").GetString() != "tsc-pilot-questline" ||
				    metadata.GetProperty("version").GetString() != modVersion ||
				    metadata.GetProperty("targetSptVersion").GetString() != targetSptVersion)
					throw InvalidAddon("addon.json does not match this TSC/SPT version");

				ValidateQuests(addonPath, MechanicId, "open_channel.json", [OpenChannelId]);
				ValidateQuests(addonPath, PilotId, "pilot_introduction.json",
					[AssemblyQuestId, TscPilotProgressionService.FinalQuestId]);
				ValidateLocales(addonPath, MechanicId, [OpenChannelId]);
				ValidateLocales(addonPath, PilotId, [AssemblyQuestId, TscPilotProgressionService.FinalQuestId]);
				using JsonDocument gates = ReadObject(Path.Combine(addonPath, "db/CustomQuests", PilotId,
					"QuestAssort/pilot_introduction.json"));
				if (gates.RootElement.GetProperty("started").GetProperty(RepeaterOfferId).GetString() != TscPilotProgressionService.FinalQuestId ||
				    gates.RootElement.GetProperty("success").GetProperty(PhoneOfferId).GetString() != TscPilotProgressionService.FinalQuestId)
					throw InvalidAddon("the replacement purchase gates are missing or incorrect");
				using JsonDocument assort = ReadObject(Path.Combine(addonPath, "db/CustomAssortSchemes/pilot_repeater.json"));
				JsonElement repeater = assort.RootElement.GetProperty(PilotId);
				if (!repeater.GetProperty("items").EnumerateArray().Any(item =>
					item.GetProperty("_id").GetString() == RepeaterOfferId &&
					item.GetProperty("_tpl").GetString() == RepeaterTemplateId) ||
				    !repeater.GetProperty("barter_scheme").TryGetProperty(RepeaterOfferId, out _) ||
				    !repeater.GetProperty("loyal_level_items").TryGetProperty(RepeaterOfferId, out _))
					throw InvalidAddon("the replacement repeater offer is missing");
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
				JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
			{
				throw InvalidAddon("required content could not be validated", exception);
			}
		}
		else
		{
			QuestlineRequired = false;
		}
		IsInitialized = true;
	}

	/// <summary>Only startup may activate permissions after checking WTT's imported database.</summary>
	internal void Activate()
	{
		if (!IsInitialized) throw new InvalidOperationException("TSC progression has not initialized.");
		IsActive = true;
	}

	private static void ValidateQuests(string addonPath, string traderId, string filename, string[] questIds)
	{
		using JsonDocument document = ReadObject(Path.Combine(addonPath, "db/CustomQuests", traderId, "Quests", filename));
		foreach (string questId in questIds)
		{
			JsonElement quest = document.RootElement.GetProperty(questId);
			if (quest.GetProperty("_id").GetString() != questId || quest.GetProperty("traderId").GetString() != traderId ||
			    quest.GetProperty("conditions").GetProperty("AvailableForFinish").GetArrayLength() == 0 ||
			    quest.GetProperty("rewards").GetProperty("Success").GetArrayLength() == 0)
				throw InvalidAddon($"quest {questId} is incomplete or has an incorrect identity");
		}
	}

	private static void ValidateLocales(string addonPath, string traderId, string[] questIds)
	{
		using JsonDocument document = ReadObject(Path.Combine(addonPath, "db/CustomQuests", traderId, "Locales/en.json"));
		foreach (string questId in questIds)
		foreach (string key in new[] { "name", "description", "successMessageText" })
			if (string.IsNullOrWhiteSpace(document.RootElement.GetProperty($"{questId} {key}").GetString()))
				throw InvalidAddon($"quest {questId} has incomplete dialogue");
	}

	private static JsonDocument ReadObject(string path)
	{
		JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
		if (document.RootElement.ValueKind == JsonValueKind.Object) return document;
		document.Dispose();
		throw InvalidAddon($"{Path.GetFileName(path)} must contain an object");
	}

	private static InvalidOperationException InvalidAddon(string reason, Exception? inner = null) =>
		new($"TSC Pilot Questline add-on is incomplete or incompatible: {reason}. Reinstall the matching add-on, or remove its entire addons/pilot-questline directory while the server is stopped.", inner);
}
