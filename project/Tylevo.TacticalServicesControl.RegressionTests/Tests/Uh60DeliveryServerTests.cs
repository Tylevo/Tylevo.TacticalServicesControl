using SamSWAT.FireSupport.ArysReloaded;

internal static class Uh60DeliveryServerTests
{
	private const string SessionId = "66f51f3a0000000000000101";
	private const string ProfileId = "66f51f3a0000000000000102";
	private const string OtherProfileId = "66f51f3a0000000000000103";
	private const string FirstItemId = "66f51f3a0000000000000201";
	private const string SecondItemId = "66f51f3a0000000000000202";

	private const string CallbackPath =
		"project/SamSWAT.FireSupport.Server/FireSupportUh60DeliveryCallbacks.cs";
	private const string DiRegistrationPath =
		"project/SamSWAT.FireSupport.Server/FireSupportDiRegistration.cs";
	private const string DeliveryServicePath =
		"project/SamSWAT.FireSupport.Server/FireSupportUh60DeliveryService.cs";
	private const string MarkerStorePath =
		"project/SamSWAT.FireSupport.Server/FireSupportUh60TransferMarkerStore.cs";
	private const string HttpListenerPath =
		"project/SamSWAT.FireSupport.Server/FireSupportHttpListener.cs";
	private const string ServerModPath =
		"project/SamSWAT.FireSupport.Server/ServerMod.cs";

	[RegressionTest]
	private static void MarkersSurviveRestartAndAcknowledgementIsDurable()
	{
		string directory = CreateTemporaryDirectory();
		try
		{
			var firstProcess = new FireSupportUh60TransferMarkerStore();
			firstProcess.Initialize(directory);
			AssertEx.True(
				firstProcess.TryMark(
					SessionId,
					ProfileId,
					[FirstItemId, SecondItemId, FirstItemId],
					out int accepted,
					out string markReason),
				markReason);
			AssertEx.Equal(2, accepted);

			var restartedProcess = new FireSupportUh60TransferMarkerStore();
			restartedProcess.Initialize(directory);
			HashSet<string> restored =
				restartedProcess.GetMarkedItemIds(SessionId, ProfileId);
			AssertEx.True(restored.SetEquals([FirstItemId, SecondItemId]));
			AssertEx.Equal(
				0,
				restartedProcess.GetMarkedItemIds(
					SessionId,
					OtherProfileId).Count,
				"A marker must remain bound to the authenticated PMC profile.");

			AssertEx.True(
				restartedProcess.TryAcknowledge(
					SessionId,
					ProfileId,
					[FirstItemId, SecondItemId],
					out string acknowledgeReason),
				acknowledgeReason);

			var afterAcknowledgementRestart =
				new FireSupportUh60TransferMarkerStore();
			afterAcknowledgementRestart.Initialize(directory);
			AssertEx.Equal(
				0,
				afterAcknowledgementRestart.GetMarkedItemIds(
					SessionId,
					ProfileId).Count);
			AssertEx.False(
				File.Exists(
					Path.Combine(
						directory,
						"tsc-uh60-transfer-markers.json.tmp")),
				"Atomic writes must not leave a temporary sidecar behind.");
		}
		finally
		{
			DeleteTemporaryDirectory(directory);
		}
	}

	[RegressionTest]
	private static void CorruptPrimaryRecoversBackupThenFailsSafeToNoMarkers()
	{
		string directory = CreateTemporaryDirectory();
		string statePath =
			Path.Combine(directory, "tsc-uh60-transfer-markers.json");
		string backupPath = statePath + ".bak";
		try
		{
			var writer = new FireSupportUh60TransferMarkerStore();
			writer.Initialize(directory);
			AssertEx.True(
				writer.TryMark(
					SessionId,
					ProfileId,
					[FirstItemId],
					out _,
					out string firstReason),
				firstReason);
			AssertEx.True(
				writer.TryMark(
					SessionId,
					ProfileId,
					[SecondItemId],
					out _,
					out string secondReason),
				secondReason);

			File.WriteAllText(statePath, "{not valid json");
			var recovered = new FireSupportUh60TransferMarkerStore();
			recovered.Initialize(directory);
			HashSet<string> recoveredIds =
				recovered.GetMarkedItemIds(SessionId, ProfileId);
			AssertEx.True(recoveredIds.Contains(FirstItemId));
			AssertEx.False(recoveredIds.Contains(SecondItemId));
			AssertEx.Contains("backup was recovered", recovered.LastLoadWarning);

			File.Delete(backupPath);
			File.WriteAllText(statePath, "{still not valid json");
			var failSafe = new FireSupportUh60TransferMarkerStore();
			failSafe.Initialize(directory);
			AssertEx.Equal(
				0,
				failSafe.GetMarkedItemIds(SessionId, ProfileId).Count,
				"Unreadable state without a backup must produce no custom-routing markers.");
		}
		finally
		{
			DeleteTemporaryDirectory(directory);
		}
	}

	[RegressionTest]
	private static void DeliveryReceiptPhasesAreDurableAndIdempotent()
	{
		string directory = CreateTemporaryDirectory();
		const string packageId = "66f51f3a0000000000000301";
		try
		{
			var writer = new FireSupportUh60TransferMarkerStore();
			writer.Initialize(directory);
			AssertEx.True(
				writer.TryMark(
					SessionId,
					ProfileId,
					[FirstItemId, SecondItemId],
					out _,
					out string markReason),
				markReason);
			AssertEx.True(
				writer.TryPrepareDelivery(
					SessionId,
					ProfileId,
					packageId,
					[FirstItemId, SecondItemId],
					out FireSupportUh60DeliveryReceipt prepared,
					out string prepareReason),
				prepareReason);
			AssertEx.Equal(12, prepared.ReceiptToken.Length);
			AssertEx.Equal("Prepared", prepared.State);

			var afterPrepareRestart =
				new FireSupportUh60TransferMarkerStore();
			afterPrepareRestart.Initialize(directory);
			AssertEx.True(
				afterPrepareRestart.TryPrepareDelivery(
					SessionId,
					ProfileId,
					packageId,
					[FirstItemId, SecondItemId],
					out FireSupportUh60DeliveryReceipt replay,
					out string replayReason),
				replayReason);
			AssertEx.Equal(prepared.ReceiptToken, replay.ReceiptToken);
			AssertEx.False(
				afterPrepareRestart.TryPrepareDelivery(
					SessionId,
					ProfileId,
					packageId,
					[FirstItemId],
					out _,
					out string mismatchReason));
			AssertEx.Equal("DeliveryReceiptMismatch", mismatchReason);

			AssertEx.True(
				afterPrepareRestart.TryRecordMailObserved(
					SessionId,
					ProfileId,
					packageId,
					out string observedReason),
				observedReason);

			var afterMailRestart =
				new FireSupportUh60TransferMarkerStore();
			afterMailRestart.Initialize(directory);
			AssertEx.True(
				afterMailRestart.TryPrepareDelivery(
					SessionId,
					ProfileId,
					packageId,
					[FirstItemId, SecondItemId],
					out FireSupportUh60DeliveryReceipt observed,
					out string observedReplayReason),
				observedReplayReason);
			AssertEx.Equal("MailObserved", observed.State);
			AssertEx.Equal(prepared.ReceiptToken, observed.ReceiptToken);

			AssertEx.True(
				afterMailRestart.TryCompleteDelivery(
					SessionId,
					ProfileId,
					packageId,
					[FirstItemId, SecondItemId],
					out string completeReason),
				completeReason);

			var afterCompleteRestart =
				new FireSupportUh60TransferMarkerStore();
			afterCompleteRestart.Initialize(directory);
			AssertEx.Equal(
				0,
				afterCompleteRestart.GetMarkedItemIds(
					SessionId,
					ProfileId).Count);
			AssertEx.True(
				afterCompleteRestart.TryPrepareDelivery(
					SessionId,
					ProfileId,
					packageId,
					[FirstItemId, SecondItemId],
					out FireSupportUh60DeliveryReceipt completed,
					out string completedReplayReason),
				completedReplayReason);
			AssertEx.Equal("Completed", completed.State);
			AssertEx.Equal(prepared.ReceiptToken, completed.ReceiptToken);
		}
		finally
		{
			DeleteTemporaryDirectory(directory);
		}
	}

	[RegressionTest]
	private static void SchemaOneMarkersMigrateWithoutLosingPendingCargo()
	{
		string directory = CreateTemporaryDirectory();
		string statePath =
			Path.Combine(directory, "tsc-uh60-transfer-markers.json");
		try
		{
			File.WriteAllText(
				statePath,
				$$"""
				{
				  "schemaVersion": 1,
				  "profiles": {
				    "{{SessionId}}": {
				      "profileId": "{{ProfileId}}",
				      "updatedUtc": "{{DateTimeOffset.UtcNow:O}}",
				      "itemIds": [ "{{FirstItemId}}" ]
				    }
				  }
				}
				""");

			var migrated = new FireSupportUh60TransferMarkerStore();
			migrated.Initialize(directory);
			AssertEx.True(
				migrated.GetMarkedItemIds(
					SessionId,
					ProfileId).SetEquals([FirstItemId]));
			AssertEx.Contains(
				"\"schemaVersion\": 2",
				File.ReadAllText(statePath));
		}
		finally
		{
			DeleteTemporaryDirectory(directory);
		}
	}

	[RegressionTest]
	private static void ServerRoutingPreservesStockFallbackAndIsolatedSender()
	{
		string callback = ReadProductionSource(CallbackPath);
		string diRegistration = ReadProductionSource(DiRegistrationPath);
		string service = ReadProductionSource(DeliveryServicePath);
		string markerStore = ReadProductionSource(MarkerStorePath);
		string listener = ReadProductionSource(HttpListenerPath);
		string serverMod = ReadProductionSource(ServerModPath);

		AssertEx.Contains(": IOnDIConstruct", diRegistration);
		AssertEx.Contains("ReferencesStockBtrCallbacks", diRegistration);
		AssertEx.Contains("stockDescriptors.Length != 2", diRegistration);
		AssertEx.Contains("typeof(BtrDeliveryCallbacks)", diRegistration);
		AssertEx.Contains("serviceCollection.Remove(descriptor)", diRegistration);
		AssertEx.False(
			callback.Contains("TypeOverride", StringComparison.Ordinal),
			"SPT 4.1 removed Injectable.TypeOverride; the DI construction hook must own stock callback replacement.");
		AssertEx.Contains("if (!hasMessengerItems)", callback);
		AssertEx.Contains(
			"btrDeliveryService.SendBTRDelivery",
			callback);
		AssertEx.Contains("package.Items = stockItems", callback);
		AssertEx.Contains(
			"await saveServer.SaveProfileAsync(sessionId)",
			callback);
		AssertEx.Contains(
			"TryValidateItemTemplates(",
			callback);
		AssertEx.Contains(
			"the complete original package and marker remain queued for recovery",
			callback);
		AssertEx.Contains(
			"TryPrepareDelivery(",
			callback);
		AssertEx.Contains(
			"InspectMessengerDeliveryReceipt(",
			callback);
		AssertEx.Contains(
			"TryRecordMailObserved(",
			callback);
		AssertEx.Contains(
			"TryCompleteDelivery(",
			callback);
		AssertEx.Contains(
			"TryCaptureDeliveryMessageIds(",
			callback);
		AssertEx.Contains(
			"InspectNewDeliveryMessage(",
			callback);
		AssertEx.Contains(
			"bool stockFallbackCompleted",
			callback);
		AssertBefore(
			callback,
			"if (stockFallbackCompleted)",
			"uh60DeliveryService.TryCompleteDelivery",
			"A prepared Pilot receipt may be retired only after confirmed stock fallback delivery and save.");
		AssertBefore(
			callback,
			"uh60DeliveryService.TryPrepareDelivery",
			"uh60DeliveryService.SendMessengerDelivery",
			"A durable prepared receipt must exist before pilot mail is attempted.");
		AssertEx.Equal(
			2,
			callback.Split(
				"InspectMessengerDeliveryReceipt",
				StringSplitOptions.None).Length - 1,
			"Pilot mail must be inspected both before and after its send attempt.");
		AssertBefore(
			callback,
			"uh60DeliveryService.TryRecordMailObserved",
			"package.Items = stockItems",
			"A verified pilot receipt must be recorded before the authoritative package is narrowed.");
		AssertBeforeFollowing(
			callback,
			"package.Items = stockItems",
			"if (!await TrySaveProfileAsync(",
			"The package must be narrowed before profile mail/package state is saved.");
		AssertBeforeFollowing(
			callback,
			"if (!await TrySaveProfileAsync(",
			"uh60DeliveryService.TryCompleteDelivery",
			"Profile mail/package state must be durable before the routing sidecar is completed.");

		AssertEx.Contains(
			"MessengerTraderId = \"66f51f3a0000000000000a60\"",
			service);
		AssertEx.Contains(
			"cloner.Clone(btrDriver)",
			service);
		AssertEx.Contains(
			"string inheritedAvatar = btrDriver.Base.Avatar",
			service);
		AssertEx.Contains(
			": inheritedAvatar",
			service);
		AssertEx.False(
			service.Contains(
				"/files/trader/avatar/5935c25fb3acc3127c3d8cd9.png",
				StringComparison.Ordinal),
			"The missing-artwork fallback must preserve the native BTR Driver avatar rather than guessing a stale route.");
		AssertEx.Contains(
			"traders[MessengerTraderId] = pilot",
			service);
		AssertEx.Contains(
			"if (!IsOwnedMessengerIdentity(existing))",
			service);
		AssertBefore(
			service,
			"if (!IsOwnedMessengerIdentity(existing))",
			"pilot = existing",
			"A pre-existing trader ID must be proven to belong to TSC before it can be reused or mutated.");
		AssertEx.Contains(
			"pilot.Base.UnlockedByDefault = !questlinePolicy.QuestlineRequired",
			service);
		AssertEx.Contains(
			"mailSendService.SendDirectNpcMessageToPlayer",
			service);
		AssertEx.Contains(
			"MessageType.BtrItemsDelivery",
			service);
		AssertEx.Contains(
			"Manifest {normalizedToken}",
			service);
		AssertEx.Contains(
			"templateTable.Items",
			service);
		AssertEx.Contains(
			"MessageContainsExpectedItems",
			service);
		AssertEx.Contains(
			"assets/traders/uh60-pilot.png",
			service);
		AssertEx.False(
			service.Contains(
				"/files/trader/avatar/656f0f98d80a697f855d34b1.png",
				StringComparison.Ordinal),
			"The isolated pilot must not reuse the BTR Driver portrait.");

		AssertEx.Contains(
			"tsc-uh60-transfer-markers.json",
			markerStore);
		AssertEx.Contains(
			"CurrentSchemaVersion = 2",
			markerStore);
		AssertEx.Contains(
			"TryPrepareDelivery(",
			markerStore);
		AssertEx.Contains(
			"TryRecordMailObserved(",
			markerStore);
		AssertEx.Contains(
			"TryCompleteDelivery(",
			markerStore);
		AssertEx.Contains("File.Replace(", markerStore);
		AssertEx.Contains("MarkerLifetime = TimeSpan.FromDays(30)", markerStore);
		AssertEx.Contains(
			"\"/tsc/uh60-transfer/mark\"",
			listener);
		AssertEx.Contains(
			"FireSupportUh60TransferMarkerRequest",
			listener);
		AssertEx.Contains(
			"uh60DeliveryService.Initialize(pathToMod)",
			serverMod);
	}

	private static string CreateTemporaryDirectory()
	{
		string path = Path.Combine(
			Path.GetTempPath(),
			$"tsc-uh60-markers-{Guid.NewGuid():N}");
		Directory.CreateDirectory(path);
		return path;
	}

	private static void DeleteTemporaryDirectory(string path)
	{
		if (Directory.Exists(path))
		{
			Directory.Delete(path, recursive: true);
		}
	}

	private static string ReadProductionSource(string relativePath)
	{
		string fullPath = Path.Combine(
			FindRepositoryRoot(),
			relativePath.Replace('/', Path.DirectorySeparatorChar));
		AssertEx.True(
			File.Exists(fullPath),
			$"Production source was not found: {fullPath}");
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
						    "SamSWAT.FireSupport.Server",
						    "ServerMod.cs")))
				{
					return current.FullName;
				}

				current = current.Parent;
			}
		}

		throw new RegressionAssertionException(
			"Could not locate the TacticalServicesControl source root.");
	}

	private static void AssertBefore(
		string source,
		string first,
		string second,
		string message)
	{
		int firstIndex = source.IndexOf(first, StringComparison.Ordinal);
		int secondIndex = source.IndexOf(second, StringComparison.Ordinal);
		AssertEx.True(
			firstIndex >= 0 && secondIndex > firstIndex,
			message);
	}

	private static void AssertBeforeFollowing(
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
