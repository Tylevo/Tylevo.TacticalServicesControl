using SamSWAT.FireSupport.ArysReloaded;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Servers;

internal static class TscPilotProfileMigrationTests
{
	private static readonly MongoId PilotId = new(FireSupportUh60DeliveryService.MessengerTraderId);
	private static readonly MongoId BtrId = new("656f0f98d80a697f855d34b1");

	[RegressionTest]
	private static void RunsAfterProfilesAndExistingPocketMigration()
	{
		var attribute = (SPTarkov.DI.Annotations.InjectableAttribute?)Attribute.GetCustomAttribute(
			typeof(TscPilotProfileMigrationOnLoad), typeof(SPTarkov.DI.Annotations.InjectableAttribute));
		AssertEx.True(AssertEx.NotNull(attribute).TypePriority > SPTarkov.Server.Core.DI.OnLoadOrder.PostLoad + 1);
	}

	[RegressionTest]
	private static async Task UnlocksOnlyPilotAndPreservesProgressAndOtherTraders()
	{
		Rig rig = new();
		TraderInfo pilot = new() { Unlocked = false, Disabled = false, LoyaltyLevel = 1,
			Standing = 0.42, SalesSum = 76543, NextResupply = 123456789 };
		TraderInfo btr = new() { Unlocked = false, LoyaltyLevel = 1, Standing = 0.17 };
		PmcData pmc = rig.AddProfile("profile-one", pilot);
		pmc.TradersInfo![BtrId] = btr;

		await rig.Hook.OnLoadAsync(CancellationToken.None);
		await rig.Hook.OnLoadAsync(CancellationToken.None);

		AssertEx.Equal((bool?)true, pilot.Unlocked);
		AssertEx.Equal((bool?)false, pilot.Disabled);
		AssertEx.Equal((int?)1, pilot.LoyaltyLevel);
		AssertEx.Equal((double?)0.42, pilot.Standing);
		AssertEx.Equal((double?)76543, pilot.SalesSum);
		AssertEx.Equal((long?)123456789, pilot.NextResupply);
		AssertEx.Equal((bool?)false, btr.Unlocked);
		AssertEx.Equal((double?)0.17, btr.Standing);
		AssertEx.True(ReferenceEquals(pilot, pmc.TradersInfo[PilotId]));
		AssertEx.True(ReferenceEquals(btr, pmc.TradersInfo[BtrId]));
		AssertEx.Equal(2, pmc.TradersInfo.Count);
		AssertEx.Equal(1, rig.Saves);
	}

	[RegressionTest]
	private static async Task MissingEntriesAndAlreadyUnlockedProfilesNeedNoSave()
	{
		Rig rig = new();
		PmcData missing = rig.AddProfile("missing", null);
		rig.AddProfile("unlocked", new TraderInfo { Unlocked = true });
		rig.SavesServer.Profiles[new MongoId("no-character")] = new SptProfile();
		rig.SavesServer.Profiles[new MongoId("no-trader-info")] = new SptProfile
			{ CharacterData = new Characters { PmcData = new PmcData() } };

		await rig.Hook.OnLoadAsync(CancellationToken.None);

		AssertEx.Equal(0, rig.Saves);
		AssertEx.False(missing.TradersInfo!.ContainsKey(PilotId));
	}

	[RegressionTest]
	private static async Task FutureQuestGateAndForeignIdentityPreventMigration()
	{
		Action<TraderBase>[] changes =
		[
			identity => identity.UnlockedByDefault = false,
			identity => identity.UnlockedByDefault = null,
			identity => identity.Id = BtrId,
			identity => identity.Name = "Another trader",
			identity => identity.Nickname = "Another trader",
			identity => identity.Location = "Another shop"
		];
		foreach (Action<TraderBase> change in changes)
		{
			Rig rig = new();
			TraderInfo entry = new() { Unlocked = false };
			rig.AddProfile("guarded", entry);
			change(rig.Traders[PilotId].Base!);
			await rig.Hook.OnLoadAsync(CancellationToken.None);
			AssertEx.Equal((bool?)false, entry.Unlocked);
			AssertEx.Equal(0, rig.Saves);
		}
		Rig missing = new();
		TraderInfo untouched = new() { Unlocked = false };
		missing.AddProfile("missing-identity", untouched);
		missing.Traders.Remove(PilotId);
		await missing.Hook.OnLoadAsync(CancellationToken.None);
		AssertEx.Equal((bool?)false, untouched.Unlocked);
		AssertEx.Equal(0, missing.Saves);
	}

	[RegressionTest]
	private static async Task FailedSaveRestoresFalseAndNullAndContinuesOtherProfiles()
	{
		Rig rig = new();
		TraderInfo locked = new() { Unlocked = false };
		TraderInfo unknown = new() { Unlocked = null };
		TraderInfo succeeds = new() { Unlocked = false };
		rig.AddProfile("fail-false", locked);
		rig.AddProfile("fail-null", unknown);
		rig.AddProfile("success", succeeds);
		rig.SavesServer.SaveProfile = id =>
		{
			rig.Saves++;
			AssertEx.Equal((bool?)true, rig.SavesServer.Profiles[id].CharacterData!.PmcData!.TradersInfo![PilotId].Unlocked);
			return id.ToString().StartsWith("fail", StringComparison.Ordinal)
				? Task.FromException(new IOException("synthetic save failure")) : Task.CompletedTask;
		};

		await rig.Hook.OnLoadAsync(CancellationToken.None);

		AssertEx.Equal((bool?)false, locked.Unlocked);
		AssertEx.Null(unknown.Unlocked);
		AssertEx.Equal((bool?)true, succeeds.Unlocked);
		AssertEx.Equal(3, rig.Saves);
		AssertEx.Equal(2, rig.Logger.Errors);
	}

	[RegressionTest]
	private static async Task CancellationBeforeMigrationLeavesProfilesUntouched()
	{
		Rig rig = new();
		TraderInfo entry = new() { Unlocked = false };
		rig.AddProfile("cancelled", entry);
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();

		await AssertEx.ThrowsAsync<OperationCanceledException>(() => rig.Hook.OnLoadAsync(cancellation.Token));

		AssertEx.Equal((bool?)false, entry.Unlocked);
		AssertEx.Equal(0, rig.Saves);
	}

	[RegressionTest]
	private static async Task CancellationDuringSaveRollsBackAndDoesNotTouchNextProfile()
	{
		Rig rig = new();
		TraderInfo first = new() { Unlocked = null };
		TraderInfo second = new() { Unlocked = false };
		rig.AddProfile("first", first);
		rig.AddProfile("second", second);
		using CancellationTokenSource cancellation = new();
		rig.SavesServer.SaveProfile = _ =>
		{
			rig.Saves++;
			cancellation.Cancel();
			return Task.FromCanceled(cancellation.Token);
		};

		await AssertEx.ThrowsAsync<OperationCanceledException>(() => rig.Hook.OnLoadAsync(cancellation.Token));

		AssertEx.Null(first.Unlocked);
		AssertEx.Equal((bool?)false, second.Unlocked);
		AssertEx.Equal(1, rig.Saves);
	}

	private sealed class Rig
	{
		public TradersTable Traders { get; } = new();
		public SaveServer SavesServer { get; } = new();
		public RecordingLogger Logger { get; } = new();
		public TscPilotProfileMigrationOnLoad Hook { get; }
		public int Saves;

		public Rig()
		{
			Traders[PilotId] = new Trader { Base = new TraderBase
			{
				Id = PilotId, Name = "UH-60 Pilot", Nickname = "UH-60 Pilot",
				Location = "Tactical Services Control", UnlockedByDefault = true
			} };
			SavesServer.SaveProfile = _ => { Saves++; return Task.CompletedTask; };
			Hook = new TscPilotProfileMigrationOnLoad(Logger, Traders, SavesServer);
		}

		public PmcData AddProfile(string session, TraderInfo? pilot)
		{
			PmcData pmc = new() { TradersInfo = new() };
			if (pilot != null) pmc.TradersInfo[PilotId] = pilot;
			SavesServer.Profiles[new MongoId(session)] = new SptProfile
				{ CharacterData = new Characters { PmcData = pmc } };
			return pmc;
		}
	}

	private sealed class RecordingLogger : ISptLogger<TscPilotProfileMigrationOnLoad>
	{
		public int Errors;
		public void Success(string message) { }
		public void Warning(string message) { }
		public void Error(string message) => Errors++;
		public void Error(string message, Exception exception) => Errors++;
	}
}
