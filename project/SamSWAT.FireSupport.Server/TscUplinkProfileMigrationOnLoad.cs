using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;

namespace SamSWAT.FireSupport.ArysReloaded;

/// <summary>
/// Runs after SPT's callbacks and normal mod item registration have completed.
/// The late template reconciliation includes custom pocket layouts registered
/// after TSC. Existing profile item placements are left untouched.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostLoad + 1)]
public sealed class TscUplinkProfileMigrationOnLoad(
	TscUplinkSpecialSlotService uplinkSpecialSlotService) : IOnLoad
{
	public Task OnLoadAsync(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		uplinkSpecialSlotService.ConfigurePocketTemplates();
		return Task.CompletedTask;
	}
}
