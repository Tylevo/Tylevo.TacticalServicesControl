using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Servers;

namespace SamSWAT.FireSupport.ArysReloaded;

/// <summary>
/// Unlocks the formerly messenger-only Pilot in existing PMC profiles. New
/// trader entries continue to use SPT's native UnlockedByDefault initialization.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostLoad + 2)]
public sealed class TscPilotProfileMigrationOnLoad(
	ISptLogger<TscPilotProfileMigrationOnLoad> logger,
	TradersTable tradersTable,
	SaveServer saveServer) : IOnLoad
{
	public async Task OnLoadAsync(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		MongoId pilotId = new(FireSupportUh60DeliveryService.MessengerTraderId);
		Trader? pilot = tradersTable.GetTrader(pilotId);
		// Respect a future quest-gated default and fail closed on an ID collision.
		if (!IsOwnedUnlockedPilot(pilot)) return;

		foreach ((MongoId sessionId, var profile) in saveServer.GetProfiles())
		{
			cancellationToken.ThrowIfCancellationRequested();
			var tradersInfo = profile.CharacterData?.PmcData?.TradersInfo;
			if (tradersInfo == null || !tradersInfo.TryGetValue(pilotId, out TraderInfo? entry) ||
				entry == null || entry.Unlocked == true)
				continue;

			bool? previousUnlocked = entry.Unlocked;
			entry.Unlocked = true;
			try
			{
				await saveServer.SaveProfileAsync(sessionId, cancellationToken);
				logger.Success($"TSC unlocked the UH-60 Pilot shop for profile {sessionId}.");
			}
			catch (OperationCanceledException)
			{
				entry.Unlocked = previousUnlocked;
				throw;
			}
			catch (Exception exception)
			{
				entry.Unlocked = previousUnlocked;
				logger.Error(
					$"TSC could not save the UH-60 Pilot unlock for profile {sessionId}; the previous state was restored.",
					exception);
			}
		}
	}

	private static bool IsOwnedUnlockedPilot(Trader? trader)
	{
		TraderBase? identity = trader?.Base;
		return identity?.UnlockedByDefault == true &&
			string.Equals(identity.Id.ToString(), FireSupportUh60DeliveryService.MessengerTraderId,
				StringComparison.OrdinalIgnoreCase) &&
			string.Equals(identity.Name, "UH-60 Pilot", StringComparison.Ordinal) &&
			string.Equals(identity.Nickname, "UH-60 Pilot", StringComparison.Ordinal) &&
			string.Equals(identity.Location, "Tactical Services Control", StringComparison.Ordinal);
	}
}
