using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;

namespace SamSWAT.FireSupport.ArysReloaded;

/// <summary>
/// Runs after SPT's callbacks and normal mod item registration have completed.
/// The late template reconciliation removes any item filters that WTT-based
/// mods may have appended after TSC's initial database patch; migration then
/// sees both loaded profiles and the final, validated slot contract.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostLoad + 1)]
public sealed class TscUplinkProfileMigrationOnLoad(
	TscUplinkSpecialSlotService uplinkSpecialSlotService) : IOnLoad
{
	public Task OnLoadAsync(CancellationToken cancellationToken)
	{
		uplinkSpecialSlotService.ConfigurePocketTemplates();
		return uplinkSpecialSlotService.MigrateLoadedProfilesAsync(cancellationToken);
	}
}
