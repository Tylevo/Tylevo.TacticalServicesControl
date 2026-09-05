using System.Collections.Generic;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

/// <summary>
/// Defines the compatibility contract for authorization snapshots returned by
/// purchase and lifecycle mutations.
/// </summary>
public static class AuthorizationSnapshotPresence
{
	/// <summary>
	/// An explicit inclusion flag makes an empty dictionary authoritative.
	/// Pre-flag servers remain compatible when they return a non-empty ledger.
	/// A null or unflagged empty dictionary means the ledger was omitted.
	/// </summary>
	public static bool ShouldApply(
		bool authorizationsIncluded,
		IReadOnlyDictionary<string, int> authorizations)
	{
		return authorizations != null &&
		       (authorizationsIncluded || authorizations.Count > 0);
	}
}
