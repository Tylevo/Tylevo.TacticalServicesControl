using SamSWAT.FireSupport.ArysReloaded.Unity;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SamSWAT.FireSupport.ArysReloaded;

/// <summary>A bounded administrator preset library, separate from runtime configuration and player state.</summary>
public sealed class FireSupportPresetStore
{
	public const int MaxPresets = 30;
	public const int MaxPresetBytes = 32 * 1024;
	private static readonly JsonSerializerOptions s_json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
	private static readonly Regex s_idPattern = new("\\Acustom-[a-f0-9]{32}\\z", RegexOptions.CultureInvariant);
	private readonly object _gate = new();
	private readonly string _directory;
	private readonly Dictionary<string, JsonElement> _fields;
	private readonly HashSet<string> _integerFields;

	public FireSupportPresetStore(string directory, object dashboardSchema, RaidOpsFireSupportServerConfig defaults)
	{
		_directory = Path.GetFullPath(directory);
		Dictionary<string, object> portable = FireSupportPresetCatalog.ProjectSettings(defaults);
		JsonElement schema = JsonSerializer.SerializeToElement(dashboardSchema, s_json);
		_fields = schema.GetProperty("sections").EnumerateArray()
			.SelectMany(section => section.GetProperty("fields").EnumerateArray())
			.Where(field => portable.ContainsKey(field.GetProperty("path").GetString()!))
			.ToDictionary(field => field.GetProperty("path").GetString()!, field => field.Clone(), StringComparer.Ordinal);
		_integerFields = portable.Where(entry => entry.Value is int).Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);
	}

	internal static async Task<JsonElement> ReadRequestAsync(Stream stream, CancellationToken cancellationToken)
	{
		using var body = new MemoryStream();
		byte[] buffer = new byte[4096];
		while (true)
		{
			int count = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, MaxPresetBytes - (int)body.Length + 1)), cancellationToken);
			if (count == 0) break;
			body.Write(buffer, 0, count);
			if (body.Length > MaxPresetBytes) throw new InvalidDataException("The preset body exceeds 32 KiB.");
		}
		using JsonDocument document = JsonDocument.Parse(body.ToArray(), new JsonDocumentOptions { MaxDepth = 8 });
		return document.RootElement.Clone();
	}

	public FireSupportSavedPresetCollection Read()
	{
		lock (_gate)
		{
			var presets = new List<FireSupportPreset>();
			bool skipped = false;
			try
			{
				if (!Directory.Exists(_directory)) return new() { Presets = presets };
				RejectReparsePoint(_directory);
				string[] paths = Directory.EnumerateFiles(_directory, "*.json", SearchOption.TopDirectoryOnly).Take(MaxPresets + 1).ToArray();
				skipped = paths.Length > MaxPresets;
				foreach (string path in paths.Take(MaxPresets))
				{
					try
					{
						string id = Path.GetFileNameWithoutExtension(path);
						if (!IsValidId(id)) { skipped = true; continue; }
						RejectReparsePoint(path);
						using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
						if (stream.Length > MaxPresetBytes) { skipped = true; continue; }
						byte[] bytes = new byte[MaxPresetBytes + 1];
						int count = stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
						if (count > MaxPresetBytes) { skipped = true; continue; }
						using JsonDocument document = JsonDocument.Parse(bytes.AsMemory(0, count), new JsonDocumentOptions { MaxDepth = 8 });
						if (!TryValidate(document.RootElement, out FireSupportPreset? preset, out _) || preset!.Id != id)
						{ skipped = true; continue; }
						presets.Add(preset);
					}
					catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
					{
						skipped = true;
					}
				}
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				skipped = true;
			}
			return new()
			{
				Presets = presets.OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase).ThenBy(preset => preset.Id, StringComparer.Ordinal).ToArray(),
				Warning = skipped ? "Some saved presets could not be loaded. Invalid, oversized, unreadable or excess files were left unchanged." : null
			};
		}
	}

	public bool TrySave(JsonElement payload, out FireSupportPreset? saved, out string error, out bool storageFailure)
	{
		saved = null;
		storageFailure = false;
		if (!TryValidate(payload, out FireSupportPreset? validated, out error)) return false;
		FireSupportPreset candidate = new()
		{
			Id = string.IsNullOrEmpty(validated!.Id) ? $"custom-{Guid.NewGuid():N}" : validated.Id,
			Name = validated.Name, Description = validated.Description, Settings = validated.Settings
		};
		byte[] body = JsonSerializer.SerializeToUtf8Bytes(candidate, s_json);
		if (body.Length > MaxPresetBytes) { error = "The saved preset exceeds 32 KiB."; return false; }
		lock (_gate)
		{
			string? temporary = null;
			try
			{
				Directory.CreateDirectory(_directory);
				RejectReparsePoint(_directory);
				string target = Path.Combine(_directory, candidate.Id + ".json");
				if (File.Exists(target)) RejectReparsePoint(target);
				else if (Directory.EnumerateFiles(_directory, "*.json", SearchOption.TopDirectoryOnly).Take(MaxPresets).Count() >= MaxPresets)
				{ error = "The saved preset library is full (30 presets). Remove a preset before saving another."; return false; }
				temporary = Path.Combine(_directory, $".{candidate.Id}-{Guid.NewGuid():N}.tmp");
				using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
				{
					stream.Write(body);
					stream.Flush(flushToDisk: true);
				}
				File.Move(temporary, target, overwrite: true);
				temporary = null;
				saved = candidate;
				error = string.Empty;
				return true;
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				storageFailure = true;
				error = "The preset could not be saved. Check that the server can write to its preset folder and that the file is not locked.";
				return false;
			}
			finally
			{
				if (temporary != null)
				{
					try { File.Delete(temporary); }
					catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
				}
			}
		}
	}

	public bool TryRemove(JsonElement payload, out string error, out bool storageFailure)
	{
		storageFailure = false;
		if (payload.ValueKind != JsonValueKind.Object || payload.EnumerateObject().Count() != 1 ||
		    !payload.TryGetProperty("id", out JsonElement idValue) || idValue.ValueKind != JsonValueKind.String || !IsValidId(idValue.GetString()))
		{ error = "A valid saved preset ID is required."; return false; }
		lock (_gate)
		{
			try
			{
				if (Directory.Exists(_directory)) RejectReparsePoint(_directory);
				string target = Path.Combine(_directory, idValue.GetString() + ".json");
				if (!File.Exists(target)) { error = "The saved preset no longer exists."; return false; }
				RejectReparsePoint(target);
				File.Delete(target);
				error = string.Empty;
				return true;
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				storageFailure = true;
				error = "The preset could not be removed. Check that the server can write to its preset folder and that the file is not locked.";
				return false;
			}
		}
	}

	private bool TryValidate(JsonElement payload, out FireSupportPreset? preset, out string error)
	{
		preset = null;
		error = "Invalid preset JSON.";
		if (payload.ValueKind != JsonValueKind.Object || Encoding.UTF8.GetByteCount(payload.GetRawText()) > MaxPresetBytes) return false;
		var metadata = new HashSet<string>(StringComparer.Ordinal);
		foreach (JsonProperty property in payload.EnumerateObject())
		{
			if (!metadata.Add(property.Name) || property.Name is not ("id" or "name" or "description" or "format" or "formatVersion" or "settings"))
			{ error = "Preset metadata contains an unknown or duplicate field."; return false; }
		}
		if (!payload.TryGetProperty("format", out JsonElement format) || format.ValueKind != JsonValueKind.String || format.GetString() != FireSupportPresetCatalog.Format ||
		    !payload.TryGetProperty("formatVersion", out JsonElement version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int number) || number != FireSupportPresetCatalog.FormatVersion)
		{ error = "The preset must use tsc-preset format version 1."; return false; }
		string id = string.Empty;
		if (payload.TryGetProperty("id", out JsonElement idValue))
		{
			if (idValue.ValueKind != JsonValueKind.String || !IsValidId(idValue.GetString()))
			{ error = "The saved preset ID is invalid. Omit the ID to save a new preset."; return false; }
			id = idValue.GetString()!;
		}
		if (!payload.TryGetProperty("name", out JsonElement nameValue) || nameValue.ValueKind != JsonValueKind.String)
		{ error = "A preset name is required."; return false; }
		string name = nameValue.GetString()!.Trim();
		if (name.Length is < 1 or > 80 || name.Any(char.IsControl))
		{ error = "The preset name must contain 1 to 80 characters and no control characters."; return false; }
		string description = string.Empty;
		if (payload.TryGetProperty("description", out JsonElement descriptionValue))
		{
			if (descriptionValue.ValueKind != JsonValueKind.String)
			{ error = "The preset description must be text of at most 500 characters."; return false; }
			description = descriptionValue.GetString()!.Trim();
			if (description.Length > 500)
			{ error = "The preset description must be text of at most 500 characters."; return false; }
		}
		if (!payload.TryGetProperty("settings", out JsonElement settingsValue) || settingsValue.ValueKind != JsonValueKind.Object)
		{ error = "Preset settings must be a flat object of editable gameplay fields."; return false; }
		var settings = new Dictionary<string, object>(StringComparer.Ordinal);
		var seenSettings = new HashSet<string>(StringComparer.Ordinal);
		foreach (JsonProperty setting in settingsValue.EnumerateObject())
		{
			if (!seenSettings.Add(setting.Name))
			{ error = $"The preset contains an unknown or duplicate gameplay setting: {setting.Name}."; return false; }
			JsonElement value = setting.Value;
			if (setting.Name == "paymentSource")
			{
				// This removed setting is accepted only at the legacy import boundary.
				// It never becomes an editable or newly exported gameplay field.
				if (value.ValueKind != JsonValueKind.String || value.GetString() is not
				    ("CarriedRoubles" or "StashRoubles" or "PreferCarriedThenStash" or "PreferStashThenCarried"))
				{ error = "The preset setting has an invalid type or value: paymentSource."; return false; }
				continue;
			}
			if (!_fields.TryGetValue(setting.Name, out JsonElement field))
			{ error = $"The preset contains an unknown or duplicate gameplay setting: {setting.Name}."; return false; }
			bool valid = field.GetProperty("type").GetString() switch
			{
				"toggle" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
				"select" => value.ValueKind == JsonValueKind.String && field.GetProperty("options").EnumerateArray().Any(option => option.GetString() == value.GetString()),
				"number" => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double amount) && double.IsFinite(amount) &&
				            amount >= field.GetProperty("min").GetDouble() && amount <= field.GetProperty("max").GetDouble() &&
				            (!_integerFields.Contains(setting.Name) || value.TryGetInt32(out _)),
				_ => false
			};
			if (!valid) { error = $"The preset setting has an invalid type or value: {setting.Name}."; return false; }
			settings.Add(setting.Name, value.Clone());
		}
		if (settings.Count == 0) { error = "The preset must contain at least one gameplay setting."; return false; }
		if (settingsValue.TryGetProperty("extraction.waitTimeSeconds", out JsonElement wait) &&
		    settingsValue.TryGetProperty("extraction.extractTimeSeconds", out JsonElement countdown) &&
		    wait.GetDouble() < countdown.GetDouble() + 1)
		{ error = "Extraction wait time must exceed its countdown by at least one second."; return false; }
		preset = new() { Id = id, Name = name, Description = description, Settings = settings };
		error = string.Empty;
		return true;
	}

	private static bool IsValidId(string? id) => id != null && s_idPattern.IsMatch(id);

	private static void RejectReparsePoint(string path)
	{
		if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
			throw new IOException("The preset library does not follow filesystem links.");
	}
}

public sealed class FireSupportSavedPresetCollection
{
	public IReadOnlyList<FireSupportPreset> Presets { get; init; } = Array.Empty<FireSupportPreset>();
	public string? Warning { get; init; }
}
