using System.Text.RegularExpressions;

internal static class HelicopterTransferFeeSourceContractTests
{
	private const string FeeSourcePath =
		"project/SamSWAT.FireSupport/Unity/HelicopterTransferFeeSource.cs";
	private const string SettingsPath =
		"project/SamSWAT.FireSupport/PluginSettings.cs";
	private const string AdapterPath =
		"project/SamSWAT.FireSupport/Unity/FireSupportItemTransfer.cs";
	private const string InteractionPatchesPath =
		"project/SamSWAT.FireSupport/Patches/HelicopterItemTransferInteractionPatches.cs";
	private const string ServerClientPath =
		"project/SamSWAT.FireSupport/Unity/FireSupportServerConfigClient.cs";
	private const string SharedContractsPath =
		"project/SamSWAT.FireSupport/Unity/RaidOpsFireSupportServerConfig.cs";
	private const string HttpListenerPath =
		"project/SamSWAT.FireSupport.Server/FireSupportHttpListener.cs";
	private const string ServerServicePath =
		"project/SamSWAT.FireSupport.Server/FireSupportUh60TransferFeeService.cs";
	private const string JournalPath =
		"project/SamSWAT.FireSupport.Server/FireSupportUh60TransferFeeJournal.cs";
	private const string ProfileMutationGatePath =
		"project/SamSWAT.FireSupport.Server/FireSupportProfileMutationGate.cs";
	private const string ServerConfigServicePath =
		"project/SamSWAT.FireSupport.Server/FireSupportServerConfigService.cs";
	private const string DeliveryCallbacksPath =
		"project/SamSWAT.FireSupport.Server/FireSupportUh60DeliveryCallbacks.cs";

	[RegressionTest]
	private static void FeeSourceIsDedicatedAndDefaultsToNativeCarriedRoubles()
	{
		string settings = ReadProductionSource(SettingsPath);
		string feeSource = ReadProductionSource(FeeSourcePath);
		string adapter = ReadProductionSource(AdapterPath);

		AssertEx.Contains("enum HelicopterTransferFeeSource", feeSource);
		AssertEx.Contains("Carried", feeSource);
		AssertEx.Contains("Stash", feeSource);
		AssertEx.Equal(
			2,
			Regex.Matches(
				feeSource,
				@"^\s*(?:Carried|Stash)\s*(?:=\s*\d+)?\s*,?\s*$",
				RegexOptions.Multiline | RegexOptions.CultureInvariant).Count,
			"The cargo fee selector must remain a two-way choice between EFT-native carried RUB and TSC-server stash RUB.");

		AssertEx.Contains(
			"ConfigEntry<HelicopterTransferFeeSource> HelicopterTransferFeeSource",
			settings);
		string binding = SliceAround(
			settings,
			"HelicopterTransferFeeSource = config.Bind(",
			900);
		AssertEx.Contains("\"Helicopter Cargo\"", binding);
		AssertEx.Contains("\"Transfer fee source\"", binding);
		AssertEx.Contains(
			"HelicopterTransferFeeSource.Carried",
			binding);
		AssertEx.False(
			binding.Contains("PluginSettings.PaymentSource", StringComparison.Ordinal) ||
			binding.Contains("PluginSettings.PaymentCurrency", StringComparison.Ordinal),
			"The cargo handling-fee selector must not alias the general TSC authorization wallet or currency settings.");

		AssertEx.Contains(
			"PluginSettings.HelicopterTransferFeeSource",
			adapter);
		AssertEx.False(
			adapter.Contains("PluginSettings.PaymentSource", StringComparison.Ordinal),
			"Cargo handling-fee interception must be independent from the general authorization payment source.");
		AssertEx.False(
			adapter.Contains("PluginSettings.PaymentCurrency", StringComparison.Ordinal),
			"The native cargo handling fee remains RUB-only and must be independent from the authorization currency.");
	}

	[RegressionTest]
	private static void NativeCarriedModeBypassesTheInterceptorWithoutHttp()
	{
		string adapter = ReadProductionSource(AdapterPath);
		string patches = ReadProductionSource(InteractionPatchesPath);
		string intercept = ExtractMember(
			adapter,
			"TryInterceptTraderServicePurchase");

		AssertEx.Contains("s_nativePurchaseBypass", intercept);
		AssertEx.Contains(
			"HelicopterTransferFeeSource.Stash",
			intercept);
		AssertEx.Contains("return false", intercept);
		AssertBefore(
			intercept,
			"HelicopterTransferFeeSource.Stash",
			"PurchaseCargoTransferWithStashFeeAsync(",
			"The default carried-RUB mode must exit before the stash transaction task is created.");
		AssertEx.False(
			intercept.Contains(
				"PrepareUh60TransferFeeAsync",
				StringComparison.Ordinal),
			"The interception gate itself must perform no HTTP work for bypassed native purchases.");

		string stashPurchasePatch = SliceAround(
			patches,
			"internal sealed class HelicopterItemTransferStashFeePurchasePatch",
			2600);
		string patchPrefix = ExtractMember(
			stashPurchasePatch,
			"Prefix");
		AssertEx.Contains(
			"TryInterceptTraderServicePurchase",
			patchPrefix);
		AssertEx.Contains("return true", patchPrefix);
		AssertEx.Contains("__result = stashPurchaseTask", patchPrefix);
		AssertEx.Contains("return false", patchPrefix);
	}

	[RegressionTest]
	private static void StashInterceptionRequiresTheExactActiveCargoSession()
	{
		string adapter = ReadProductionSource(AdapterPath);
		string gate = ExtractMember(
			adapter,
			"IsExactActiveCargoPurchase");
		string quote = ExtractMember(
			adapter,
			"TryGetExactNativeFee");

		AssertEx.Contains(
			"!FireSupportServerConfigClient.IsFikaClientHostAuthorityActive",
			gate);
		AssertEx.Contains("player != null", gate);
		AssertEx.Contains("player.IsYourPlayer", gate);
		AssertEx.Contains("player == s_servicePlayer", gate);
		AssertEx.Contains(
			"player.InventoryController == inventoryController",
			gate);
		AssertEx.Contains("s_sessionPoint != null", gate);
		AssertEx.Contains("s_screenController != null", gate);
		AssertEx.Contains("s_transferController != null", gate);
		AssertEx.Contains(
			"s_transferController.ServiceType == serviceType",
			gate);
		AssertEx.Contains("s_serviceType == serviceType", gate);

		AssertEx.Contains(
			"generation != s_sessionGeneration",
			quote);
		AssertEx.Contains(
			"IsExactActiveCargoPurchase(",
			quote);
		AssertEx.Contains(
			"serviceData.ServiceItemCost.Count != 1",
			quote);
		AssertEx.Contains(
			"PaymentCurrencyInfo.RoubleTemplateId",
			quote);
		AssertEx.Contains(
			"s_transferController.GetGridItemsPrice(temporaryStash)",
			quote);
		AssertEx.Contains(
			"calculatedFee != quotedFee",
			quote);
	}

	[RegressionTest]
	private static void NativeZeroCostBypassIsThreadScopedAndRestoredBeforeAwait()
	{
		string adapter = ReadProductionSource(AdapterPath);
		string native = ExtractMember(
			adapter,
			"StartNativePurchaseWithZeroRubCost");

		AssertEx.True(
			Regex.IsMatch(
				adapter,
				@"\[ThreadStatic\]\s*" +
				@"private\s+static\s+bool\s+s_nativePurchaseBypass\s*;",
				RegexOptions.CultureInvariant),
			"The recursive native purchase guard must be scoped to the invoking thread.");
		AssertEx.Contains(
			"var serviceItemCost = serviceData.ServiceItemCost",
			native);
		AssertEx.Contains(
			"KeyValuePair<string, int>[] originalCosts",
			native);
		AssertEx.Contains("s_nativePurchaseBypass = true", native);
		AssertEx.Contains("serviceItemCost.Clear()", native);
		AssertEx.Contains(
			"inventoryController.TryPurchaseTraderService(",
			native);
		AssertEx.Contains("finally", native);
		AssertEx.Contains(
			"foreach (KeyValuePair<string, int> cost in originalCosts)",
			native);
		AssertEx.Contains(
			"serviceItemCost.Add(cost.Key, cost.Value)",
			native);
		AssertEx.Contains("s_nativePurchaseBypass = false", native);
		AssertEx.False(
			native.Contains("await ", StringComparison.Ordinal),
			"The full ServiceItemCost dictionary and recursion guard must be restored synchronously before the native task is awaited.");
		AssertBefore(
			native,
			"serviceItemCost.Add(cost.Key, cost.Value)",
			"return nativePurchaseTask",
			"The exact dynamic cost dictionary must be restored before the native task is returned to an awaiting caller.");
	}

	[RegressionTest]
	private static void ClientLifecycleRefundsOnlyBeforeNativeSuccess()
	{
		string adapter = ReadProductionSource(AdapterPath);
		string purchase = ExtractMember(
			adapter,
			"PurchaseCargoTransferWithStashFeeAsync");

		AssertBefore(
			purchase,
			"PrepareUh60TransferFeeAsync(",
			"StartNativePurchaseWithZeroRubCost(",
			"The stash debit must be prepared before EFT can run its zero-carried-cost native transaction.");
		AssertEx.Contains(
			"if (!IsPreparedFeeResponse(prepareResponse))",
			purchase);
		AssertEx.Contains(
			"revalidatedFee != nativeFeeRoubles",
			purchase);
		AssertEx.Contains(
			"if (!nativePurchaseSucceeded)",
			purchase);
		AssertEx.Contains(
			"RefundPreparedStashFeeAsync(",
			purchase);
		AssertEx.Contains(
			"No refund was attempted.",
			purchase);

		int nativeSuccess = purchase.IndexOf(
			"bool nativePurchaseSucceeded = await nativePurchaseTask",
			StringComparison.Ordinal);
		int catchIndex = purchase.IndexOf(
			"catch (Exception ex)",
			nativeSuccess,
			StringComparison.Ordinal);
		int commit = purchase.IndexOf(
			"PersistCommitIntent(",
			catchIndex,
			StringComparison.Ordinal);
		AssertEx.True(
			nativeSuccess >= 0 &&
			catchIndex > nativeSuccess &&
			commit > catchIndex,
			"The refundable exception boundary must end after native execution and before persisting commit intent.");
		string preCommit =
			purchase[catchIndex..commit];
		AssertEx.Contains(
			"prepared = false",
			preCommit,
			"Once EFT reports native success, the catch path must be disarmed before the durable idempotent commit intent.");
		string commitAcknowledgement = purchase[commit..];
		AssertEx.False(
			commitAcknowledgement.Contains(
				"RefundPreparedStashFeeAsync(",
				StringComparison.Ordinal),
			"A failed commit acknowledgement must never refund a completed native transfer.");
		string catchBlock = purchase[catchIndex..commit];
		AssertEx.Contains("if (prepared)", catchBlock);
	}

	[RegressionTest]
	private static void LegacyOrUnavailableServerFailsClosedBeforeNativePurchase()
	{
		string adapter = ReadProductionSource(AdapterPath);
		string client = ReadProductionSource(ServerClientPath);
		string purchase = ExtractMember(
			adapter,
			"PurchaseCargoTransferWithStashFeeAsync");
		string send = ExtractMember(
			client,
			"SendUh60TransferFeeActionAsync");

		AssertEx.Contains(
			"Reason = \"ServerConfigUnavailable\"",
			send);
		AssertEx.Contains("\"uh60-transfer/fee\"", send);
		AssertEx.Contains("catch (Exception ex)", send);
		AssertEx.Contains(
			"fallback.Reason = \"RequestFailed\"",
			send);
		AssertEx.Contains(
			"fails Prepare before EFT's",
			send);
		AssertBefore(
			purchase,
			"if (!IsPreparedFeeResponse(prepareResponse))",
			"StartNativePurchaseWithZeroRubCost(",
			"A missing legacy route must be rejected before the native purchase can consume or transfer anything.");
	}

	[RegressionTest]
	private static void AmbiguousPrepareFailureReconcilesTheSameTransactionBeforeReturning()
	{
		string adapter = ReadProductionSource(AdapterPath);
		string purchase = ExtractMember(
			adapter,
			"PurchaseCargoTransferWithStashFeeAsync");
		string reconcile = ExtractMember(
			adapter,
			"ReconcileAmbiguousPrepareFailureAsync");

		AssertBefore(
			purchase,
			"ReconcileAmbiguousPrepareFailureAsync(",
			"return false",
			"An ambiguous Prepare response must be reconciled before the native purchase is rejected.");
		AssertEx.Contains(
			"Reconcile every rejected",
			reconcile);
		AssertEx.Contains(
			"RefundPreparedStashFeeAsync(",
			reconcile);
		AssertEx.Contains("profileId", reconcile);
		AssertEx.Contains("transactionId", reconcile);
		AssertEx.Contains("amountRoubles", reconcile);
		AssertEx.Contains(
			"notFoundIsSuccess: true",
			reconcile);
		AssertEx.Contains(
			"Native EFT purchase has not started here.",
			reconcile);
	}

	[RegressionTest]
	private static void FeeProtocolIsAnExplicitAuthenticatedTransactionLifecycle()
	{
		string contracts = ReadProductionSource(SharedContractsPath);
		string listener = ReadProductionSource(HttpListenerPath);
		string client = ReadProductionSource(ServerClientPath);
		string service = ReadProductionSource(ServerServicePath);

		AssertEx.Contains(
			"class FireSupportUh60TransferFeeRequest",
			contracts);
		AssertEx.Contains("string Action", contracts);
		AssertEx.Contains("string ProfileId", contracts);
		AssertEx.Contains("string TransactionId", contracts);
		AssertEx.Contains("int AmountRoubles", contracts);
		AssertEx.Contains(
			"class FireSupportUh60TransferFeeResponse",
			contracts);
		AssertEx.Contains("int StashRoubleBalance", contracts);

		AssertEx.Contains(
			"Route = \"/tsc/uh60-transfer/fee\"",
			service);
		AssertEx.Contains(
			"FireSupportUh60TransferFeeService.Route",
			listener);
		AssertEx.Contains("HttpMethods.Post", listener);
		AssertEx.Contains(
			"FireSupportUh60TransferFeeRequest",
			listener);
		AssertEx.Contains("sessionId", listener);

		AssertEx.Contains("\"Prepare\"", client);
		AssertEx.Contains("\"Commit\"", client);
		AssertEx.Contains("\"Refund\"", client);
		AssertEx.Contains("\"uh60-transfer/fee\"", client);
		AssertEx.Contains("IsAuthenticatedProfile", client);
	}

	[RegressionTest]
	private static void ProfileMutationsShareOneServerSerializationGate()
	{
		string gate = ReadProductionSource(ProfileMutationGatePath);
		string purchases = ReadProductionSource(ServerConfigServicePath);
		string fees = ReadProductionSource(ServerServicePath);

		AssertEx.Contains("[Injectable(InjectionType.Singleton)]", gate);
		AssertEx.Contains("SemaphoreSlim", gate);
		string run = ExtractMember(gate, "RunAsync");
		AssertEx.Contains("await _gate.WaitAsync()", run);
		AssertEx.Contains("finally", run);
		AssertEx.Contains("_gate.Release()", run);

		string purchase = ExtractMember(purchases, "TryPurchaseAsync");
		AssertEx.Contains("profileMutationGate.RunAsync", purchase);
		AssertEx.Contains("FireSupportProfileMutationGate profileMutationGate", fees);
		AssertEx.Contains("profileMutationGate.RunAsync", fees);
	}

	[RegressionTest]
	private static void FeeServerAuthenticatesTheHttpSessionAndIdempotencyTuple()
	{
		string service = ReadProductionSource(ServerServicePath);
		string handle = ExtractMember(service, "TryHandleSerializedAsync");
		string resolve = ExtractMember(
			service,
			"TryResolveAuthenticatedProfile");

		AssertEx.Contains(
			"TryResolveAuthenticatedProfile(",
			handle);
		AssertEx.Contains("sessionId", handle);
		AssertEx.Contains("request.ProfileId", handle);
		AssertEx.Contains("record!.ProfileId", handle);
		AssertEx.Contains("record.AmountRoubles", handle);
		AssertEx.Contains("\"FeeTransactionConflict\"", handle);
		AssertEx.Contains("\"Status\"", handle);
		AssertEx.Contains("\"Prepare\"", handle);
		AssertEx.Contains("\"Commit\"", handle);
		AssertEx.Contains("\"Refund\"", handle);

		AssertEx.Contains("profileHelper.GetPmcProfile(sessionId)", resolve);
		AssertEx.Contains("pmc.Id.Value.ToString()", resolve);
		AssertEx.Contains("requestedProfileId.Trim()", resolve);
		AssertEx.Contains("\"AuthenticatedSessionRequired\"", resolve);
		AssertEx.Contains("\"ProfileNotFound\"", resolve);
		AssertEx.Contains("\"ProfileMismatch\"", resolve);
		AssertEx.False(
			resolve.Contains("GetProfileByPmcId", StringComparison.Ordinal),
			"The request profile hint must never select a profile independently of the authenticated HTTP session.");
	}

	[RegressionTest]
	private static void ServerPrepareCommitRefundLifecycleIsWriteAheadAndTerminal()
	{
		string service = ReadProductionSource(ServerServicePath);
		string prepare = ExtractMember(service, "PrepareAsync");
		string resumeDebit = ExtractMember(service, "ResumeDebitAsync");
		string finalizePrepared = ExtractMember(
			service,
			"FinalizePrepared");
		string commit = ExtractMember(service, "CommitAsync");
		string refund = ExtractMember(service, "RefundAsync");
		string finalizeRefunded = ExtractMember(
			service,
			"FinalizeRefunded");

		AssertEx.Contains("\"AlreadyPrepared\"", prepare);
		AssertEx.Contains("\"AlreadyCommitted\"", prepare);
		AssertEx.Contains("\"FeeTransactionRefunded\"", prepare);
		AssertEx.Contains(
			"FireSupportUh60TransferFeeJournal.DebitPendingState",
			prepare);
		AssertBefore(
			prepare,
			"journal.TryCreate(",
			"ResumeDebitAsync(",
			"The DebitPending journal entry must be durable before any stash debit is resumed.");

		AssertBefore(
			resumeDebit,
			"ApplyDebitPlan(",
			"saveServer.SaveProfileAsync(",
			"The exact debit plan must be applied before the authoritative profile is saved.");
		AssertBefore(
			resumeDebit,
			"saveServer.SaveProfileAsync(",
			"FinalizePrepared(",
			"The fee may enter Prepared only after the debited profile has been saved.");
		AssertEx.Contains(
			"FireSupportUh60TransferFeeJournal.PreparedState",
			finalizePrepared);
		AssertEx.Contains("journal.TrySave(", finalizePrepared);

		AssertEx.Contains(
			"FireSupportUh60TransferFeeJournal.PreparedState",
			commit);
		AssertEx.Contains(
			"FireSupportUh60TransferFeeJournal.CommittedState",
			commit);
		AssertEx.Contains("\"AlreadyCommitted\"", commit);
		AssertEx.Contains("journal.TrySave(", commit);

		AssertEx.Contains(
			"FireSupportUh60TransferFeeJournal.CommittedState",
			refund);
		AssertEx.Contains("\"FeeTransactionCommitted\"", refund);
		AssertBefore(
			refund,
			"\"FeeTransactionCommitted\"",
			"TryBuildRefundPlan(",
			"A committed native transfer must be rejected before any refund plan is created.");
		AssertEx.Contains(
			"FireSupportUh60TransferFeeJournal.RefundPendingState",
			refund);
		AssertEx.Contains(
			"FireSupportUh60TransferFeeJournal.RefundedState",
			finalizeRefunded);
		AssertEx.Contains("journal.TrySave(", finalizeRefunded);
	}

	[RegressionTest]
	private static void StashFeesUseOnlyNestedRoubleStacksAndAdditiveRefundPlans()
	{
		string service = ReadProductionSource(ServerServicePath);
		string stacks = ExtractMember(
			service,
			"GetStashRoubleStacks");
		string descendant = ExtractMember(
			service,
			"IsDescendantOfStash");
		string debitPlan = ExtractMember(
			service,
			"TryBuildDebitPlan");
		string applyDebit = ExtractMember(
			service,
			"ApplyDebitPlan");
		string refundPlan = ExtractMember(
			service,
			"TryBuildRefundPlan");
		string applyRefund = ExtractMember(
			service,
			"TryApplyRefundPlan");

		AssertEx.Contains(
			"PaymentCurrencyInfo.RoubleTemplateId",
			stacks);
		AssertEx.Contains("inventory.Stash", stacks);
		AssertEx.Contains("IsDescendantOfStash(", stacks);
		AssertEx.Contains("parentId = parent.ParentId", descendant);
		AssertEx.Contains("stashId", descendant);
		AssertEx.Contains("GetStashRoubleStacks(pmc)", debitPlan);
		AssertEx.Contains("IsStashRouble(pmc, current)", applyDebit);
		AssertEx.Contains("record.Debits", refundPlan);
		AssertEx.Contains("BeforeCount", refundPlan);
		AssertEx.Contains("RestoredItem", refundPlan);
		AssertEx.Contains("checked(", applyRefund);
		AssertEx.False(
			Regex.IsMatch(
				refundPlan + applyRefund,
				@"Inventory\.Items\s*=\s*record\.",
				RegexOptions.CultureInvariant),
			"Refund must apply captured RUB-stack credits, never replace the live inventory with a stale journal snapshot.");
	}

	[RegressionTest]
	private static void StockBtrDeliveryRoutingDoesNotDependOnTheTscFeeJournal()
	{
		string patches = ReadProductionSource(InteractionPatchesPath);
		string adapter = ReadProductionSource(AdapterPath);
		string callbacks = ReadProductionSource(DeliveryCallbacksPath);

		AssertEx.Contains(
			"nameof(LocalPlayer.ProcessTraderServicePurchase)",
			patches);
		AssertEx.Contains("FireSupportItemTransfer", patches);
		AssertEx.Contains("BtrItemsDelivery", adapter);
		AssertEx.Contains("TransitItemsDelivery", adapter);
		AssertEx.Contains("btrDeliveryService.SendBTRDelivery", callbacks);
		AssertEx.False(
			callbacks.Contains("TransferFee", StringComparison.Ordinal) ||
			callbacks.Contains("uh60-transfer/fee", StringComparison.Ordinal),
			"Stock BTR package delivery must stay independent from TSC's UH-60 fee transaction.");
	}

	private static string ReadProductionSource(string relativePath)
	{
		string fullPath = Path.Combine(
			FindRepositoryRoot(),
			relativePath.Replace('/', Path.DirectorySeparatorChar));
		if (!File.Exists(fullPath))
		{
			throw new RegressionAssertionException(
				$"Required production source was not found: {fullPath}");
		}

		return File.ReadAllText(fullPath);
	}

	private static string FindRepositoryRoot()
	{
		foreach (string seed in new[]
		         {
			         Environment.CurrentDirectory,
			         AppContext.BaseDirectory
		         })
		{
			DirectoryInfo? current = new(seed);
			while (current != null)
			{
				if (File.Exists(
					    Path.Combine(
						    current.FullName,
						    "project",
						    "SamSWAT.FireSupport",
						    "Unity",
						    "FireSupportItemTransfer.cs")))
				{
					return current.FullName;
				}

				current = current.Parent;
			}
		}

		throw new RegressionAssertionException(
			"Could not locate the TacticalServicesControl source root.");
	}

	private static string SliceAround(
		string source,
		string marker,
		int length)
	{
		int start = source.IndexOf(marker, StringComparison.Ordinal);
		if (start < 0)
		{
			throw new RegressionAssertionException(
				$"Could not find required source marker <{marker}>.");
		}

		return source.Substring(start, Math.Min(length, source.Length - start));
	}

	private static string ExtractMember(string source, string memberName)
	{
		Match declaration = Regex.Match(
			source,
			@"(?m)^[ \t]*(?:public|private|internal|protected)\s+" +
			@"(?:(?:static|virtual|override|sealed|async|new)\s+)*" +
			@"[\w<>,?.\[\]]+\s+" +
			Regex.Escape(memberName) +
			@"(?:<[^>]+>)?\s*\(",
			RegexOptions.CultureInvariant);
		if (!declaration.Success)
		{
			throw new RegressionAssertionException(
				$"Could not find required member <{memberName}>.");
		}

		int start = declaration.Index;
		int openBrace = source.IndexOf('{', start);
		if (openBrace < 0)
		{
			throw new RegressionAssertionException(
				$"Could not find body for required member <{memberName}>.");
		}

		int depth = 0;
		for (int index = openBrace; index < source.Length; index++)
		{
			if (source[index] == '{')
			{
				depth++;
			}
			else if (source[index] == '}' && --depth == 0)
			{
				return source[start..(index + 1)];
			}
		}

		throw new RegressionAssertionException(
			$"Could not find closing brace for required member <{memberName}>.");
	}

	private static void AssertBefore(
		string source,
		string first,
		string second,
		string message)
	{
		int firstIndex = source.IndexOf(first, StringComparison.Ordinal);
		int secondIndex = firstIndex < 0
			? -1
			: source.IndexOf(
				second,
				firstIndex + first.Length,
				StringComparison.Ordinal);
		AssertEx.True(
			firstIndex >= 0 && secondIndex > firstIndex,
			message);
	}
}
