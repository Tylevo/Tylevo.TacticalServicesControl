internal static class ProgressionDispatchSourceContractTests
{
	[RegressionTest]
	private static void ManualPaymentsVerifyBeforeAnyLocalOrPersistentCharge()
	{
		string payment = Read("project/SamSWAT.FireSupport/Unity/FireSupportPayment.cs");
		string dispatch = Member(payment, "public static async UniTask<FireSupportAuthorizationUse> TryPayForDeploymentAsync(");
		int verify = dispatch.IndexOf("EnsureLocalProgressionVerifiedAsync()", StringComparison.Ordinal);
		int nonPersistent = dispatch.IndexOf("if (!_serverPurchasePersistenceEnabled)", StringComparison.Ordinal);
		AssertEx.True(verify >= 0 && nonPersistent > verify,
			"The progression check must precede the local cash/authorization fast path.");
		string purchase = Member(payment, "private static async UniTask<FireSupportPurchaseResponse> PurchaseAuthorizationAsync(");
		AssertEx.True(purchase.IndexOf("EnsureLocalProgressionVerifiedAsync()", StringComparison.Ordinal) <
		              purchase.IndexOf("if (result.Cost <= 0)", StringComparison.Ordinal),
			"Even free authorizations require progression verification.");
		AssertEx.False(Member(payment, "public static async UniTask<bool> RefundConsumedAuthorizationAsync(")
			.Contains("EnsureLocalProgressionVerifiedAsync", StringComparison.Ordinal));
		AssertEx.False(Member(payment, "public static async UniTask<bool> CommitConsumedAuthorizationAsync(")
			.Contains("EnsureLocalProgressionVerifiedAsync", StringComparison.Ordinal));
	}

	[RegressionTest]
	private static void FikaVerifiesTheBoundRequesterBeforeAuthorityExecution()
	{
		string source = Read("project/SamSWAT.FireSupport.Fika.Interop/FikaIntegration.cs");
		string execute = Member(source, "private static async UniTask<AuthorityOutcome> ExecuteAuthorityRequestAsync(");
		int peer = execute.IndexOf("TryValidateRequesterPeer(", StringComparison.Ordinal);
		int permit = execute.IndexOf("VerifyProgressionPermitAsync(", StringComparison.Ordinal);
		int authority = execute.IndexOf("TryApplyHostAuthority(", StringComparison.Ordinal);
		AssertEx.True(peer >= 0 && permit > peer && authority > permit,
			"Peer binding and server permission must both precede dispatch authority side effects.");
		AssertEx.Contains("request.ProgressionPermit, request.RequesterProfileId, entry.CancellationToken", execute);
		AssertEx.Contains("request.RequestOrigin == FireSupportRequestOrigin.Manual)", execute);
		AssertEx.Contains("SupportsProgression(request.ServiceSemanticsVersion)", execute);
		AssertEx.Contains("IsServiceEnabledForAuthority(request.SupportType)", execute);
		AssertEx.False(execute.Contains("FireSupportProgression.UplinkUnlocked", StringComparison.Ordinal),
			"A locked host cannot deny an independently verified requester.");
		string settings = Member(source, "private static FireSupportSettingsPacket BuildHostSettingsPacket(");
		AssertEx.False(settings.Contains("FireSupportProgression", StringComparison.Ordinal));
		AssertEx.False(settings.Contains(".IsServiceEnabled(", StringComparison.Ordinal));
	}

	[RegressionTest]
	private static void CapabilitiesSurviveAuthorityCloningButNeverVisualBroadcasts()
	{
		string source = Read("project/SamSWAT.FireSupport.Fika.Interop/FikaIntegration.cs");
		AssertEx.Contains("clone.ProgressionPermit = packet.ProgressionPermit;",
			Member(source, "private static FireSupportRequestPacket CloneSupportRequest("));
		AssertEx.Contains("acceptedRequest.ProgressionPermit = string.Empty;",
			Member(source, "public static AuthorityOutcome Accepted("));
		AssertEx.Contains("snapshot.ProgressionPermit = string.Empty;",
			Member(source, "public static AuthorityOutcome FromResult("));
		AssertEx.False(Read("project/SamSWAT.FireSupport.Fika.Interop/FireSupportAuthorityResultPacket.cs")
			.Contains("ProgressionPermit", StringComparison.Ordinal));
	}

	private static string Member(string source, string marker)
	{
		int start = source.IndexOf(marker, StringComparison.Ordinal);
		AssertEx.True(start >= 0, $"Missing member {marker}");
		int brace = source.IndexOf('{', start);
		int depth = 1;
		for (int index = brace + 1; index < source.Length; index++)
		{
			if (source[index] == '{') depth++;
			if (source[index] == '}' && --depth == 0) return source[start..(index + 1)];
		}
		throw new RegressionAssertionException($"Unterminated member {marker}");
	}

	private static string Read(string relative)
	{
		foreach (string seed in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
		{
			for (DirectoryInfo? directory = new(seed); directory != null; directory = directory.Parent)
			{
				if (File.Exists(Path.Combine(directory.FullName, "SamSWAT.FireSupport.ArysReloaded.sln")))
					return File.ReadAllText(Path.Combine(directory.FullName, relative));
			}
		}
		throw new RegressionAssertionException("Repository root unavailable");
	}
}
