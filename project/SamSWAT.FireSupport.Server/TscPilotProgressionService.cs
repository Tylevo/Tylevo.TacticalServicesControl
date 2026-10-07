using SamSWAT.FireSupport.ArysReloaded.Unity;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Enums;
using System.Security.Cryptography;

namespace SamSWAT.FireSupport.ArysReloaded;

/// <summary>
/// Installed server content and native quest state own progression. Permits let a Fika host
/// verify its bound requester without querying another player's profile by ID.
/// They carry no credit balance, payment authority, or quest rewards.
/// </summary>
[Injectable(InjectionType.Singleton)]
public sealed class TscPilotProgressionService(ProfileHelper profileHelper, TscPilotQuestlinePolicy questlinePolicy)
{
	public const string FinalQuestId = "66f51f3a0000000000000b03";
	public const string VerifyRoute = "/tsc/progression/verify";
	public const string LockedReason = "UplinkLocked";
	public const string LockedMessage = "Complete Back on the Air for Pilot.";
	private const int PermitLength = 64;
	private readonly object _gate = new();
	private readonly Dictionary<string, PermitIdentity> _permits = new(StringComparer.Ordinal);
	private readonly Dictionary<string, string> _profilePermits = new(StringComparer.OrdinalIgnoreCase);

	public bool HasUnlockedUplink(PmcData? profile) =>
		questlinePolicy.IsActive && profile?.Id.HasValue == true && !profile.Id.Value.IsEmpty &&
		(!questlinePolicy.QuestlineRequired || profile.Quests?.Any(quest => quest != null &&
			string.Equals(quest.QId.ToString(), FinalQuestId, StringComparison.OrdinalIgnoreCase) &&
			quest.Status == QuestStatusEnum.Success) == true);

	/// <summary>Called only after the HTTP session has resolved this PMC.</summary>
	public string GetPermitForAuthenticatedProfile(PmcData profile, MongoId saveSessionId)
	{
		if (!profile.Id.HasValue || profile.Id.Value.IsEmpty || saveSessionId.IsEmpty)
			return string.Empty;

		string profileId = profile.Id.Value.ToString();
		lock (_gate)
		{
			if (!HasUnlockedUplink(profile))
			{
				RevokeProfilePermit(profileId);
				return string.Empty;
			}

			if (_profilePermits.TryGetValue(profileId, out string? existing) &&
			    _permits.TryGetValue(existing, out PermitIdentity? identity) &&
			    identity.SessionId.Equals(saveSessionId))
				return existing;

			RevokeProfilePermit(profileId);
			string permit = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
			_permits.Add(permit, new PermitIdentity(profileId, saveSessionId));
			_profilePermits[profileId] = permit;
			return permit;
		}
	}

	public FireSupportProgressionVerifyResponse Verify(FireSupportProgressionVerifyRequest? request)
	{
		if (request?.Permit?.Length != PermitLength ||
		    string.IsNullOrWhiteSpace(request.RequesterProfileId))
			return Denied("ProgressionPermitInvalid");

		lock (_gate)
		{
			// The caller never selects a session/profile lookup. A permit issued
			// by an authenticated snapshot is the only way to reach this lookup.
			if (!_permits.TryGetValue(request.Permit, out PermitIdentity? identity))
				return Denied("ProgressionPermitInvalid");
			if (!string.Equals(identity.ProfileId, request.RequesterProfileId, StringComparison.OrdinalIgnoreCase))
				return Denied("ProgressionProfileMismatch");

			PmcData? profile;
			try
			{
				profile = profileHelper.GetPmcProfile(identity.SessionId);
			}
			catch
			{
				profile = null;
			}

			if (profile?.Id == null ||
			    !string.Equals(profile.Id.Value.ToString(), identity.ProfileId, StringComparison.OrdinalIgnoreCase) ||
			    (profile.SessionId.HasValue && !profile.SessionId.Value.Equals(identity.SessionId)))
			{
				RevokeProfilePermit(identity.ProfileId);
				return Denied("ProfileNotFound");
			}

			if (!HasUnlockedUplink(profile))
			{
				RevokeProfilePermit(identity.ProfileId);
				return Denied(LockedReason);
			}

			return new FireSupportProgressionVerifyResponse { Ok = true, Reason = "Verified" };
		}
	}

	private void RevokeProfilePermit(string profileId)
	{
		if (_profilePermits.Remove(profileId, out string? permit)) _permits.Remove(permit);
	}

	private static FireSupportProgressionVerifyResponse Denied(string reason) =>
		new() { Ok = false, Reason = reason };

	private sealed record PermitIdentity(string ProfileId, MongoId SessionId);
}
