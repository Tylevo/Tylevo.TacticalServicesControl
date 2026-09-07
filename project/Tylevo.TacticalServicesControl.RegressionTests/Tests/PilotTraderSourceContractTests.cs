using System.Buffers.Binary;
using System.Text.Json;

internal static class PilotTraderSourceContractTests
{
	private const string ServerRoot = "project/SamSWAT.FireSupport.Server/";
	private const string PilotId = "66f51f3a0000000000000a60";
	private const string UplinkId = "66f51f3a0000000000000a01";
	private const string OfferId = "66f51f3a0000000000000a02";
	private const string RoubleId = "5449016a4bdc2d6f028b456f";
	private const string PortraitPath = "assets/traders/uh60-pilot.png";

	[RegressionTest]
	private static void UplinkOfferMovesToPilotWithoutChangingPurchaseTerms()
	{
		string directory = Resolve(ServerRoot + "CopyToOutput/db/CustomAssortSchemes");
		string[] files = Directory.GetFiles(directory, "*", SearchOption.TopDirectoryOnly)
			.Where(path => Path.GetExtension(path) is ".json" or ".jsonc")
			.ToArray();
		AssertEx.Equal(1, files.Length,
			"The Uplink must have one imported offer; an extra assort file can duplicate it on overlay upgrades.");
		AssertEx.Equal("jaeger_uav_uplink.json", Path.GetFileName(files[0]),
			"Keep the existing installed filename so an overlay replaces the old Jaeger assignment.");

		using JsonDocument document = JsonDocument.Parse(File.ReadAllText(files[0]));
		JsonProperty[] traders = document.RootElement.EnumerateObject().ToArray();
		AssertEx.Equal(1, traders.Length);
		AssertEx.Equal(PilotId, traders[0].Name);
		JsonElement assortment = traders[0].Value;
		JsonElement[] items = assortment.GetProperty("items").EnumerateArray().ToArray();
		AssertEx.Equal(1, items.Length, "The main download stocks the Uplink; the optional add-on supplies its own repeater offer.");
		JsonElement item = items.Single(entry => entry.GetProperty("_id").GetString() == OfferId);
		AssertEx.Equal(OfferId, item.GetProperty("_id").GetString());
		AssertEx.Equal(UplinkId, item.GetProperty("_tpl").GetString());
		AssertEx.Equal("hideout", item.GetProperty("parentId").GetString());
		AssertEx.Equal("hideout", item.GetProperty("slotId").GetString());
		JsonElement update = item.GetProperty("upd");
		AssertEx.True(update.GetProperty("UnlimitedCount").GetBoolean());
		AssertEx.Equal(999999, update.GetProperty("StackObjectsCount").GetInt32());
		AssertEx.Equal(5, update.GetProperty("BuyRestrictionMax").GetInt32());

		JsonProperty[] schemes = assortment.GetProperty("barter_scheme").EnumerateObject().ToArray();
		AssertEx.Equal(1, schemes.Length);
		JsonElement uplinkScheme = assortment.GetProperty("barter_scheme").GetProperty(OfferId);
		AssertEx.Equal(1, uplinkScheme.GetArrayLength());
		AssertEx.Equal(1, uplinkScheme[0].GetArrayLength());
		JsonElement payment = uplinkScheme[0][0];
		AssertEx.Equal(RoubleId, payment.GetProperty("_tpl").GetString());
		AssertEx.Equal(50000, payment.GetProperty("count").GetInt32());
		JsonProperty[] loyalty = assortment.GetProperty("loyal_level_items").EnumerateObject().ToArray();
		AssertEx.Equal(1, loyalty.Length);
		AssertEx.Equal(1, assortment.GetProperty("loyal_level_items").GetProperty(OfferId).GetInt32());

		using JsonDocument templates = JsonDocument.Parse(Read(ServerRoot + "CopyToOutput/db/CustomItems/RaidOpsUavDevice.json"));
		AssertEx.True(templates.RootElement.TryGetProperty(UplinkId, out _),
			"The imported offer must reference the actual registered Uplink template.");
	}

	[RegressionTest]
	private static void PilotExistsBeforeWttImportAndReinitializationPreservesItsShop()
	{
		string startup = Read(ServerRoot + "ServerMod.cs");
		const string initialize = "uh60DeliveryService.Initialize(pathToMod)";
		const string import = "wttCommon.CustomAssortSchemeService.CreateCustomAssortSchemes(assembly)";
		AssertBefore(startup, initialize, import,
			"WTT skips an unknown trader instead of deferring its offer, so the Pilot must exist first.");
		AssertEx.Equal(1, startup.Split(initialize, StringSplitOptions.None).Length - 1,
			"Pilot initialization must not run again after WTT has populated the shop.");
		AssertEx.True(System.Text.RegularExpressions.Regex.IsMatch(startup,
			@"if \(uh60DeliveryService\.IsPilotShopReady\)\s*\{\s*await wttCommon\.CustomAssortSchemeService\.CreateCustomAssortSchemes\(assembly\)"),
			"A rejected Pilot ID collision must also block WTT from adding an offer to the foreign trader.");

		string service = Read(ServerRoot + "FireSupportUh60DeliveryService.cs");
		AssertEx.Contains("public bool IsPilotShopReady => _messengerReady", service);
		string identity = Between(service,
			"private void InitializeMessengerIdentity()", "private static bool IsOwnedMessengerIdentity(");
		AssertBefore(identity, "_messengerReady = false", "tradersTable.GetTrader(BtrTraderId)",
			"A failed reinitialization must not retain a successful shop-ready flag from an earlier call.");
		string clone = Between(identity, "pilot = cloner.Clone(btrDriver)", "traders[MessengerTraderId] = pilot");
		AssertEx.Contains("pilot.Assort = new TraderAssort", clone,
			"A new Pilot must get an independent empty inventory rather than the BTR inventory.");
		AssertEx.Contains("Items = []", clone);
		AssertEx.Contains("BarterScheme = []", clone);
		AssertEx.Contains("LoyalLevelItems = []", clone);
		string reused = Between(identity, "pilot.Base.Id = MessengerTraderId", "AddMessengerLocales()");
		AssertEx.False(reused.Contains("pilot.Assort", StringComparison.Ordinal),
			"Reusing the TSC identity must preserve offers already imported by WTT.");
		AssertEx.False(identity.Contains("btrDriver.Base.", StringComparison.Ordinal) &&
			System.Text.RegularExpressions.Regex.IsMatch(identity, @"btrDriver\.Base\.\w+\s*="),
			"Pilot configuration must not mutate the native BTR trader.");
	}

	[RegressionTest]
	private static void PilotShopUsesTheOptionalIntroductionPolicyAndItsOwnRestockEntry()
	{
		string service = Read(ServerRoot + "FireSupportUh60DeliveryService.cs");
		string identity = Between(service,
			"private void InitializeMessengerIdentity()", "private static bool IsOwnedMessengerIdentity(");
		AssertEx.Contains("pilot.Base.UnlockedByDefault = !questlinePolicy.QuestlineRequired", identity);
		AssertEx.Contains("pilot.Base.IsAvailableInPVE = true", identity);
		AssertEx.Contains("pilot.Base.AvailableInRaid = false", identity);
		AssertEx.Contains("pilot.Base.Currency = CurrencyType.RUB", identity);
		AssertEx.Contains("pilot.Base.LoyaltyLevels =", identity);
		AssertEx.Contains("MinLevel = 1", identity);
		AssertEx.Contains("MinSalesSum = 0", identity);
		AssertEx.Contains("MinStanding = 0", identity);
		AssertEx.Contains("pilot.Base.IsCanTransferItems = false", identity);
		AssertEx.Contains("pilot.Base.IsCanTransferItemsFromPve = false", identity);
		AssertEx.Contains("pilot.Services = []", identity);
		AssertEx.Contains("if (!traderConfig.UpdateTime.Any(entry => entry.TraderId == MessengerTraderId))", identity,
			"The stock purchase limit needs a Pilot restock entry, without duplicate entries on reinitialization.");
		AssertEx.Contains("TraderId = MessengerTraderId", identity);
		AssertEx.Contains("Math.Max(1, traderConfig.UpdateTimeDefault)", identity);
	}

	[RegressionTest]
	private static void PilotPortraitIsPackagedAndServedByTheServerAtAFreshRoute()
	{
		byte[] portrait = File.ReadAllBytes(Resolve(ServerRoot + "CopyToOutput/" + PortraitPath));
		AssertEx.True(portrait.Length >= 33, "The shipped Pilot portrait must contain a PNG header.");
		AssertEx.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, portrait.Take(8));
		AssertEx.Equal("IHDR", System.Text.Encoding.ASCII.GetString(portrait, 12, 4));
		AssertEx.True(BinaryPrimitives.ReadUInt32BigEndian(portrait.AsSpan(16, 4)) > 0);
		AssertEx.True(BinaryPrimitives.ReadUInt32BigEndian(portrait.AsSpan(20, 4)) > 0);

		using JsonDocument package = JsonDocument.Parse(Read("tools/package-layout.allowlist.json"));
		JsonElement serverMirror = package.RootElement.GetProperty("mirrors").EnumerateArray()
			.Single(mirror => mirror.GetProperty("source").GetString() == ServerRoot.TrimEnd('/') + "/CopyToOutput");
		AssertEx.Equal("SPT_Runtime/user/mods/Tylevo.TacticalServicesControl",
			serverMirror.GetProperty("destination").GetString());
		AssertEx.Contains(PortraitPath,
			serverMirror.GetProperty("files").EnumerateArray().Select(file => file.GetString()!));

		string service = Read(ServerRoot + "FireSupportUh60DeliveryService.cs");
		AssertEx.Contains("MessengerAvatarRoute = \"/tsc/assets/uh60-pilot-v2.png\"", service,
			"The dedicated portrait needs a new URL so clients do not reuse the old service-icon cache.");
		AssertEx.Contains($"PilotArtworkPath = \"{PortraitPath}\"", service);
		AssertEx.Contains("IOPath.Combine(pathToMod, PilotArtworkPath)", service,
			"A remote client must receive the server's bundled portrait without requiring a local client art path.");
		AssertEx.False(service.Contains("FindPriorityExfilArtwork", StringComparison.Ordinal));
		string listener = Read(ServerRoot + "FireSupportHttpListener.cs");
		AssertEx.Contains("FireSupportUh60DeliveryService.MessengerAvatarRoute", listener);
		AssertEx.Contains("uh60DeliveryService.TryGetMessengerAvatar(out byte[] avatar)", listener);
		AssertEx.Contains("httpContext.Response.ContentType = \"image/png\"", listener);
	}

	private static string Read(string relativePath) => File.ReadAllText(Resolve(relativePath));

	private static string Resolve(string relativePath)
	{
		foreach (string seed in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
		{
			for (DirectoryInfo? current = new(seed); current != null; current = current.Parent)
			{
				if (File.Exists(Path.Combine(current.FullName, ServerRoot, "ServerMod.cs")))
				{
					return Path.Combine(current.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
				}
			}
		}
		throw new RegressionAssertionException("Could not locate the TacticalServicesControl source root.");
	}

	private static string Between(string source, string start, string end)
	{
		int startIndex = source.IndexOf(start, StringComparison.Ordinal);
		int endIndex = startIndex < 0 ? -1 : source.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
		AssertEx.True(startIndex >= 0 && endIndex > startIndex, $"Could not inspect source between {start} and {end}.");
		return source[startIndex..endIndex];
	}

	private static void AssertBefore(string source, string first, string second, string message)
	{
		int firstIndex = source.IndexOf(first, StringComparison.Ordinal);
		int secondIndex = source.IndexOf(second, StringComparison.Ordinal);
		AssertEx.True(firstIndex >= 0 && secondIndex > firstIndex, message);
	}
}
