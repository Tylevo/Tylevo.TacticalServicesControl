namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public static class FireSupportProgression
{
	public const string LockedMessage = "Complete Back on the Air for Pilot";
	public const string HostUpgradeMessage = "The Fika host must update TSC to support Pilot progression";
	private static readonly FireSupportProgressionState s_state = new();
	private static bool s_hostSupportsProgression;

	public static bool UplinkUnlocked => s_state.IsUnlocked(FireSupportServerConfigClient.GetAuthenticatedSessionKey());
	public static string Permit => s_state.GetPermit(FireSupportServerConfigClient.GetAuthenticatedSessionKey());
	public static string RestrictionReason => !UplinkUnlocked
		? LockedMessage
		: FireSupportServerConfigClient.IsFikaClientHostAuthorityActive && !s_hostSupportsProgression
			? HostUpgradeMessage : string.Empty;

	public static void Clear() => s_state.Clear();
	public static void SetHostSupportsProgression(bool supported) => s_hostSupportsProgression = supported;
	public static void ApplySnapshot(string sessionKey, RaidOpsFireSupportServerConfig snapshot) =>
		s_state.Apply(sessionKey, snapshot?.PlayerStateIncluded == true, snapshot?.UplinkUnlocked, snapshot?.ProgressionPermit);
}
