using System.Text.RegularExpressions;

internal static class HelicopterTransferFeeSourceContractTests
{
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
	private static void ObsoleteFeeSelectorIsRemovedWhileRecoverySettingsRemain()
	{
		string settings = ReadProductionSource(SettingsPath);
		string adapter = ReadProductionSource(AdapterPath);
		AssertEx.False(settings.Contains("HelicopterTransferFeeSource", StringComparison.Ordinal));
		AssertEx.False(settings.Contains("\"Transfer fee source\"", StringComparison.Ordinal));
		AssertEx.Contains("Uh60TransferFeeRecoveryJournal = config.Bind(", settings);
		AssertEx.Contains("Uh60TransferFeeRecoveryQuarantine = config.Bind(", settings);
		AssertEx.False(adapter.Contains("PluginSettings.PaymentSource", StringComparison.Ordinal));
		AssertEx.False(adapter.Contains("PluginSettings.PaymentCurrency", StringComparison.Ordinal));
	}

	[RegressionTest]
	private static void NativePurchasePatchChangesOnlyTheServiceDataArgument()
	{
		string patches = ReadProductionSource(InteractionPatchesPath);
		string purchasePatch = SliceAround(patches,
			"internal sealed class HelicopterItemTransferIncludedFeePurchasePatch", 2400);
		AssertEx.Contains("typeof(ItemManipulator)", purchasePatch);
		AssertEx.Contains("nameof(ItemManipulator.PurchaseTraderService)", purchasePatch);
		AssertEx.Contains("typeof(GlobalConfiguration.ServiceData)", purchasePatch);
		AssertEx.Contains("typeof(EFT.Quests.QuestController)", purchasePatch);
		AssertEx.Contains("typeof(InventoryController)", purchasePatch);
		AssertEx.Contains("typeof(bool)", purchasePatch);
		string prefix = ExtractMember(purchasePatch, "Prefix");
		AssertEx.Contains("ref GlobalConfiguration.ServiceData __0", prefix);
		AssertEx.Contains("IncludeCargoHandlingInServicePurchase", prefix);
		AssertEx.False(prefix.Contains("__result", StringComparison.Ordinal),
			"The native transaction must produce its own success/failure result.");
		AssertEx.False(prefix.Contains("return false", StringComparison.Ordinal));
		AssertEx.False(prefix.Contains("simulate", StringComparison.Ordinal),
			"Both native simulation and execution must receive the included-fee data.");
	}

	[RegressionTest]
	private static void IncludedHandlingRequiresExactRequesterControllerAndCargoSession()
	{
		string adapter = ReadProductionSource(AdapterPath);
		string gate = ExtractMember(adapter, "IsExactActiveCargoPurchase");
		foreach (string boundary in new[]
		{
			"!FireSupportServerConfigClient.IsFikaClientHostAuthorityActive",
			"player != null", "player.IsYourPlayer", "player == s_servicePlayer",
			"player.InventoryController == inventoryController", "s_sessionPoint != null",
			"s_screenController != null", "s_transferController != null",
			"serviceType == ETraderServiceType.TransitItemsDelivery",
			"serviceType == ETraderServiceType.BtrItemsDelivery",
			"s_transferController.ServiceType == serviceType", "s_serviceType == serviceType"
		})
		{
			AssertEx.Contains(boundary, gate);
		}

		string include = ExtractMember(adapter, "IncludeCargoHandlingInServicePurchase");
		AssertEx.Contains("!string.IsNullOrEmpty(subServiceId)", include);
		AssertEx.Contains("questController != (s_sessionPlayer as LocalPlayer)?.QuestController", include);
		AssertEx.Contains("IsExactActiveCargoPurchase(inventoryController, serviceData.ServiceType)", include);
		AssertEx.Contains("OwnsTemporaryCargoStash(s_transferController, stash)", include);
		AssertEx.Contains("temporaryStash?.Grids?.Any(grid => grid?.Items?.Any() == true) != true", include);
		AssertBefore(include, "IsExactActiveCargoPurchase(", "CopyWithIncludedHandling(",
			"A native service-data copy must only be supplied after the exact session and ownership checks.");
	}

	[RegressionTest]
	private static void NativeZeroQuoteIsScopedToTheRequesterTemporaryGrid()
	{
		string adapter = ReadProductionSource(AdapterPath);
		string quote = ExtractMember(adapter, "TryOverrideCargoTransferPrice");
		string owns = ExtractMember(adapter, "OwnsTemporaryCargoStash");
		AssertEx.Contains("price = 0", quote);
		AssertEx.Contains("controller == s_transferController", quote);
		AssertEx.Contains("IsExactActiveCargoPurchase(", quote);
		AssertEx.Contains("OwnsTemporaryCargoStash(controller, temporaryStash)", quote);
		AssertEx.Contains("string.Equals(temporaryStash.Id, s_sessionPlayer.ProfileId, StringComparison.Ordinal)", owns);
		AssertEx.Contains("controller._transferContainers?.Contains(temporaryStash) == true", owns);

		string patches = ReadProductionSource(InteractionPatchesPath);
		string quotePatch = SliceAround(patches,
			"internal sealed class HelicopterItemTransferIncludedFeeQuotePatch", 1200);
		AssertEx.Contains("typeof(TransferItemsController)", quotePatch);
		AssertEx.Contains("typeof(Stash), typeof(ETraderServiceType), typeof(float), typeof(float)", quotePatch);
		AssertEx.Contains("return true", quotePatch);
		AssertEx.Contains("__result = price", quotePatch);
		AssertEx.False(patches.Contains("nameof(TransferItemsPanel.UpdateCounters)", StringComparison.Ordinal),
			"Native counters retain their empty-grid and affordability rules; the fee quote alone changes.");
	}

	[RegressionTest]
	private static void IncludedHandlingDoesNotMutateGlobalServiceCosts()
	{
		string adapter = ReadProductionSource(AdapterPath);
		string include = ExtractMember(adapter, "IncludeCargoHandlingInServicePurchase");
		AssertEx.Contains("serviceData = CargoTransferServiceData.CopyWithIncludedHandling(serviceData)", include);
		AssertEx.False(adapter.Contains("ServiceItemCost", StringComparison.Ordinal),
			"The adapter must not clear, restore or retain a reference to the global payment dictionary.");
		AssertEx.False(adapter.Contains("s_nativePurchaseBypass", StringComparison.Ordinal));
		AssertEx.False(adapter.Contains("StartNativePurchaseWithZeroRubCost", StringComparison.Ordinal));
	}

	[RegressionTest]
	private static void NewCargoTransfersNeverStartSeparateFeeTransactions()
	{
		string adapter = ReadProductionSource(AdapterPath);
		foreach (string retiredCall in new[]
		{
			"PrepareUh60TransferFeeAsync", "PersistCommitIntent", "PersistRefundIntent",
			"PurchaseCargoTransferWithStashFeeAsync", "Uh60TransferFeeRecoveryStore"
		})
		{
			AssertEx.False(adapter.Contains(retiredCall, StringComparison.Ordinal),
				"Sending newly purchased cargo must not start or await an additional fee transaction.");
		}
		string client = ReadProductionSource(ServerClientPath);
		AssertEx.Contains("RetryMatchingProfileAsync", client,
			"Startup/profile recovery must still reconcile payments from older clients.");
	}

	[RegressionTest]
	private static void IncludedHandlingPreservesNativePurchaseAndDeliveryObservation()
	{
		string adapter = ReadProductionSource(AdapterPath);
		string observed = ExtractMember(adapter, "NotifyServicePurchased");
		AssertEx.Contains("s_servicePurchaseObserved = true", observed);
		AssertEx.Contains("CollectTemporaryTransferItemIds(controller, profileId)", observed);
		AssertEx.Contains("MarkVerifiedUh60TransferAsync(", observed);
		string close = ExtractMember(adapter, "OnScreenClosed");
		AssertEx.Contains("if (purchaseObserved)", close);
		AssertEx.Contains("point?.BeginSuccessfulTransfer(player)", close);
		AssertEx.Contains("CleanupSession(generation, endPointSession: true)", close);
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
	private static void ServerFeeEndpointRetiresDebitsAndPreservesLegacyTerminalRecovery()
	{
		string service = ReadProductionSource(ServerServicePath);
		string prepare = ExtractMember(service, "Prepare");
		string recoverDebit = ExtractMember(service, "RecoverLegacyDebit");
		string finalizePrepared = ExtractMember(service, "FinalizePrepared");
		string commit = ExtractMember(service, "CommitAsync");
		string refund = ExtractMember(service, "RefundAsync");
		string finalizeRefunded = ExtractMember(service, "FinalizeRefunded");

		AssertEx.Contains("\"AlreadyPrepared\"", prepare);
		AssertEx.Contains("\"AlreadyCommitted\"", prepare);
		AssertEx.Contains("\"FeeTransactionRefunded\"", prepare);
		AssertEx.Contains("IncludedInServiceReason", prepare);
		AssertEx.False(service.Contains("journal.TryCreate(", StringComparison.Ordinal),
			"The retired endpoint must never create a new handling-fee transaction.");
		AssertEx.False(service.Contains("ApplyDebitPlan(", StringComparison.Ordinal),
			"Legacy recovery must never apply an unpaid debit after handling fees are removed.");
		AssertEx.Contains("record.ExpectedPostDebitFingerprint", recoverDebit);
		AssertEx.Contains("\"RecoveredPrepared\"", recoverDebit);
		AssertEx.Contains("record.PreDebitFingerprint", recoverDebit);
		AssertEx.Contains("CancelUndebitedLegacyFee(", recoverDebit);
		AssertEx.Contains("\"FeePaymentStateAmbiguous\"", recoverDebit);
		AssertEx.Contains("FireSupportUh60TransferFeeJournal.PreparedState", finalizePrepared);
		AssertEx.Contains("journal.TrySave(", finalizePrepared);

		AssertEx.Contains("FireSupportUh60TransferFeeJournal.PreparedState", commit);
		AssertEx.Contains("FireSupportUh60TransferFeeJournal.CommittedState", commit);
		AssertEx.Contains("\"AlreadyCommitted\"", commit);
		AssertEx.Contains("journal.TrySave(", commit);
		AssertEx.Contains("\"FeeTransactionCommitted\"", refund);
		AssertBefore(refund, "\"FeeTransactionCommitted\"", "TryBuildRefundPlan(",
			"A committed native transfer must be rejected before any refund plan is created.");
		AssertEx.Contains("FireSupportUh60TransferFeeJournal.RefundPendingState", refund);
		AssertEx.Contains("FireSupportUh60TransferFeeJournal.RefundedState", finalizeRefunded);
		AssertEx.Contains("journal.TrySave(", finalizeRefunded);
	}

	[RegressionTest]
	private static void LegacyFeeRefundsUseOnlyNestedRoubleStacksAndAdditivePlans()
	{
		string service = ReadProductionSource(ServerServicePath);
		string stacks = ExtractMember(service, "GetStashRoubleStacks");
		string descendant = ExtractMember(service, "IsDescendantOfStash");
		string refundPlan = ExtractMember(service, "TryBuildRefundPlan");
		string applyRefund = ExtractMember(service, "TryApplyRefundPlan");
		AssertEx.Contains("PaymentCurrencyInfo.RoubleTemplateId", stacks);
		AssertEx.Contains("inventory.Stash", stacks);
		AssertEx.Contains("IsDescendantOfStash(", stacks);
		AssertEx.Contains("parentId = parent.ParentId", descendant);
		AssertEx.Contains("stashId", descendant);
		AssertEx.Contains("record.Debits", refundPlan);
		AssertEx.Contains("BeforeCount", refundPlan);
		AssertEx.Contains("RestoredItem", refundPlan);
		AssertEx.Contains("checked(", applyRefund);
		AssertEx.False(Regex.IsMatch(refundPlan + applyRefund,
			@"Inventory\.Items\s*=\s*record\.", RegexOptions.CultureInvariant),
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
