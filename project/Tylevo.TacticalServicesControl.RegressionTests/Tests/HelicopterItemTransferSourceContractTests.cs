using System.Text.RegularExpressions;

internal static class HelicopterItemTransferSourceContractTests
{
	private const string AdapterPath =
		"project/SamSWAT.FireSupport/Unity/FireSupportItemTransfer.cs";
	private const string ExtractionPointPath =
		"project/SamSWAT.FireSupport/Unity/HeliExfiltrationPoint.cs";
	private const string CargoPointPath =
		"project/SamSWAT.FireSupport/Unity/HeliCargoTransferPoint.cs";
	private const string InteractionPatchesPath =
		"project/SamSWAT.FireSupport/Patches/HelicopterItemTransferInteractionPatches.cs";
	private const string Uh60Path =
		"project/SamSWAT.FireSupport/Unity/Vehicles/UH60Behaviour.cs";
	private const string RuntimePath =
		"project/SamSWAT.FireSupport/Unity/FireSupportRuntime.cs";
	private const string DispatchPath =
		"project/SamSWAT.FireSupport/Unity/Vehicles/HelicopterDispatchService.cs";
	private const string CargoDepartureNetworkingPath =
		"project/SamSWAT.FireSupport/Unity/Uh60CargoDepartureNetworking.cs";
	private const string FikaIntegrationPath =
		"project/SamSWAT.FireSupport.Fika.Interop/FikaIntegration.cs";
	private const string CargoDeparturePacketPath =
		"project/SamSWAT.FireSupport.Fika.Interop/Uh60CargoDeparturePacket.cs";
	private const string StartPatchPath =
		"project/SamSWAT.FireSupport/Patches/GameWorldStartPatch.cs";
	private const string DisposePatchPath =
		"project/SamSWAT.FireSupport/Patches/GameWorldDisposePatch.cs";
	private const string SettingsPath =
		"project/SamSWAT.FireSupport/PluginSettings.cs";
	private const string AvailabilityPath =
		"project/SamSWAT.FireSupport/Unity/FireSupportServiceAvailability.cs";
	private const string AuthorizationsPath =
		"project/SamSWAT.FireSupport/Unity/FireSupportAuthorizations.cs";
	private const string PaymentPath =
		"project/SamSWAT.FireSupport/Unity/FireSupportPayment.cs";
	private const string ServerConfigClientPath =
		"project/SamSWAT.FireSupport/Unity/FireSupportServerConfigClient.cs";
	private const string DeployMenuPath =
		"project/SamSWAT.FireSupport/Unity/FireSupportDeployMenu.cs";
	private const string MainMenuPath =
		"project/SamSWAT.FireSupport/Unity/MainMenuPurchaseController.cs";
	private const string MainMenuViewPath =
		"project/SamSWAT.FireSupport/Unity/MainMenuPurchaseController.View.cs";
	private const string PhoneRendererPath =
		"project/SamSWAT.FireSupport/Unity/UavPhoneScreenRenderer.cs";
	private const string SupportTypePath =
		"project/SamSWAT.FireSupport/Unity/ESupportType.cs";

	[RegressionTest]
	private static void CanonicalControllerResolutionPrefersTransitThenExistingBtr()
	{
		string source = ReadProductionSource(AdapterPath);
		string resolver = ExtractMember(source, "TryResolveCanonicalController");

		int transit = resolver.IndexOf(
			"gameWorld?.TransitController?.TransferItemsController",
			StringComparison.Ordinal);
		int btr = resolver.IndexOf(
			"gameWorld?.BtrController?.TransferItemsController",
			StringComparison.Ordinal);

		AssertEx.True(
			transit >= 0,
			"The transfer bridge must first reuse GameWorld.TransitController.TransferItemsController.");
		AssertEx.True(
			btr > transit,
			"The BTR-owned transfer controller must be the fallback after the transit controller.");
		AssertEx.Contains(
			"ETraderServiceType.TransitItemsDelivery",
			source);
		AssertEx.Contains(
			"ETraderServiceType.BtrItemsDelivery",
			source);
		AssertEx.False(
			Regex.IsMatch(
				source,
				@"\bnew\s+(?:global::)?BTRTransferItemsControllerClass\b",
				RegexOptions.CultureInvariant),
			"The bridge must not construct a detached BTR transfer controller.");
	}

	[RegressionTest]
	private static void NativeScreenOpensOnlyForTheRequesterLocalPlayer()
	{
		string source = ReadProductionSource(AdapterPath);
		string availability = ExtractMember(source, "IsInteractionAvailable");
		string open = ExtractMember(source, "TryOpen");

		AssertEx.Contains("point == s_activePoint", availability);
		AssertEx.Contains("player == s_activePlayer", availability);
		AssertEx.Contains("player.IsYourPlayer", availability);
		AssertEx.Contains("s_screenController == null", availability);
		AssertEx.Contains(
			"!FireSupportServerConfigClient.IsFikaClientHostAuthorityActive",
			availability);
		AssertEx.Contains("player is not LocalPlayer", open);
		AssertEx.Contains(
			"FireSupportServerConfigClient.IsFikaClientHostAuthorityActive",
			open);
		AssertEx.Contains(
			"await RefreshTraderServiceData(localPlayer)",
			open);
		AssertEx.Contains("TryResolveCanonicalController", open);
		AssertEx.Contains("TryEnsurePlayerTransferGrid", open);
		AssertEx.Contains("TryBeginItemTransfer", open);
		AssertEx.Contains(
			"new TransferItemsInRaidScreen.TransferItemsInRaidScreenController",
			open);
		AssertEx.Contains("screenController.OnClose +=", open);
		AssertEx.Contains(
			"screenController.ShowScreen(EScreenState.Queued)",
			open);

		AssertBefore(
			open,
			"await RefreshTraderServiceData(localPlayer)",
			"TryEnableService",
			"The native trader response must complete before TSC applies its temporary availability gate.");
		AssertBefore(
			open,
			"TryEnsurePlayerTransferGrid",
			"TryBeginItemTransfer",
			"The native controller/grid must be validated before pausing extraction.");
		AssertBefore(
			open,
			"TryBeginItemTransfer",
			"new TransferItemsInRaidScreen.TransferItemsInRaidScreenController",
			"The point must enter its paused state before the native screen is shown.");
		AssertEx.False(
			open.Contains(
				"GetTraderServicesDataFromServer",
				StringComparison.Ordinal),
			"Opening the screen must not race a native asynchronous trader-data refresh against the temporary availability gate.");
	}

	[RegressionTest]
	private static void FirstUseInitializesTheNativeGridWithoutClearingExistingCargo()
	{
		string source = ReadProductionSource(AdapterPath);
		string ensure = ExtractMember(
			source,
			"TryEnsurePlayerTransferGrid");

		AssertEx.Contains(
			"if (HasPlayerTransferGrid(controller, player.ProfileId))",
			ensure);
		AssertEx.Contains("controller.InitPlayerStash(player)", ensure);
		AssertBefore(
			ensure,
			"if (HasPlayerTransferGrid(controller, player.ProfileId))",
			"controller.InitPlayerStash(player)",
			"An existing native grid must be returned unchanged before initialization is attempted.");
		AssertEx.True(
			Regex.Matches(
				ensure,
				@"HasPlayerTransferGrid\s*\(\s*controller\s*,\s*player\.ProfileId\s*\)",
				RegexOptions.CultureInvariant).Count >= 2,
			"The grid must be revalidated after one-time initialization.");
	}

	[RegressionTest]
	private static void PilotMarkerUsesOnlyItemsVerifiedInThePersistentNativeGrid()
	{
		string adapter = ReadProductionSource(AdapterPath);
		string serverClient = ReadProductionSource(ServerConfigClientPath);
		string notify = ExtractMember(adapter, "NotifyServicePurchased");
		string marker = ExtractMember(
			adapter,
			"MarkVerifiedUh60TransferAsync");
		string request = ExtractMember(
			serverClient,
			"TryMarkUh60TransferAsync");

		AssertEx.Contains(
			"CollectTemporaryTransferItemIds(controller, profileId)",
			notify);
		AssertEx.Contains(
			"CollectPersistentTransferItemIds",
			marker);
		AssertEx.Contains(
			"stagedItemIds",
			marker);
		AssertEx.Contains(
			".Where(persistentItemIds.Contains)",
			marker);
		AssertEx.Contains(
			"if (!point.IsSuccessfulTransferPending)",
			marker);
		AssertBefore(
			marker,
			"if (!point.IsSuccessfulTransferPending)",
			"verificationFrame++",
			"The finite persistent-grid verification budget must not begin before the successful native screen-close boundary.");
		AssertEx.Contains(
			"TryMarkUh60TransferAsync",
			marker);
		AssertEx.Contains(
			"generation != s_sessionGeneration",
			marker);
		AssertEx.Contains(
			"native delivery remains active",
			marker);
		AssertEx.Contains(
			"IsAuthenticatedProfile(normalizedProfileId)",
			request);
		AssertEx.Contains(
			"\"uh60-transfer/mark\"",
			request);
		AssertEx.Contains(
			".Distinct(StringComparer.Ordinal)",
			request);
		AssertEx.Contains(
			".Take(4096)",
			request);
		AssertEx.False(
			Regex.IsMatch(
				notify + marker + request,
				@"\b(?:AddItem|RemoveItem|MoveItem)\s*\(",
				RegexOptions.CultureInvariant),
			"Pilot tagging must observe EFT's native move and never mutate inventory itself.");
	}

	[RegressionTest]
	private static void F12ToggleRefreshesTheActiveInteractionPrompt()
	{
		string settings = ReadProductionSource(SettingsPath);
		string adapter = ReadProductionSource(AdapterPath);
		string refresh = ExtractMember(
			adapter,
			"RefreshInteractionAvailability");

		AssertEx.Contains(
			"EnableHelicopterItemTransfer.SettingChanged +=",
			settings);
		AssertEx.Contains(
			"FireSupportItemTransfer.RefreshInteractionAvailability()",
			settings);
		AssertEx.Contains(
			"FireSupportPayment.NotifySettingsChanged(sender)",
			settings);
		AssertEx.Contains(
			"s_activePlayer?.SearchForInteractions()",
			refresh);
	}

	[RegressionTest]
	private static void CargoAvailabilityFailsClosedBeforePurchaseConsumptionAndDispatch()
	{
		string availability = ReadProductionSource(AvailabilityPath);
		string authorizations = ReadProductionSource(AuthorizationsPath);
		string payment = ReadProductionSource(PaymentPath);
		string deployMenu = ReadProductionSource(DeployMenuPath);
		string mainMenu = ReadProductionSource(MainMenuPath);
		string enabled = ExtractMember(availability, "IsServiceEnabled");
		string restriction = ExtractMember(
			availability,
			"GetLocalRestrictionReason");
		string persistentPurchase = ExtractMember(
			payment,
			"PurchasePersistentAuthorizationAsync");
		string deploymentPayment = ExtractMember(
			payment,
			"TryPayForDeploymentAsync");
		string purchaseContext = ExtractMember(
			mainMenu,
			"TryGetPurchaseContext");
		string redraw = ExtractMember(mainMenu, "Redraw");

		AssertEx.Contains("IsLocalUseAllowed(supportType)", enabled);
		AssertEx.Contains(
			"GetOperationalRestrictionReason(supportType)",
			restriction);
		AssertEx.Contains(
			"PluginSettings.EnableHelicopterItemTransfer?.Value",
			availability);
		AssertEx.Contains(
			"FireSupportServiceAvailability.IsServiceEnabled(type)",
			authorizations);
		AssertEx.Contains(
			"FireSupportServerConfigClient.PurchasePersistentAuthorizationAsync(",
			persistentPurchase);
		AssertEx.Contains(
			"FireSupportServiceAvailability.IsServiceEnabled(supportType)",
			deploymentPayment);
		AssertEx.Contains(
			"FireSupportServiceAvailability.IsLocalUseAllowed(supportType)",
			purchaseContext);
		AssertEx.Contains(
			"FireSupportServiceAvailability.GetLocalRestrictionStatus(service.Type)",
			redraw);
		AssertEx.Contains(
			"FireSupportAuthorizations.GetDeployableCount(type)",
			deployMenu);
	}

	[RegressionTest]
	private static void InteractionPatchesOnlyAdvertiseAndOpenTheNativeBridge()
	{
		string source = ReadProductionSource(InteractionPatchesPath);

		AssertEx.Contains("HeliCargoTransferPoint", source);
		AssertEx.False(
			source.Contains("HeliExfiltrationPoint", StringComparison.Ordinal),
			"Standard extraction points must never advertise the cargo-transfer interaction.");
		AssertEx.Contains(
			"FireSupportItemTransfer.IsInteractionAvailable",
			source);
		AssertEx.Contains(
			"FireSupportItemTransfer.TryOpen",
			source);
		AssertEx.Contains(
			"nameof(LocalPlayer.ProcessTraderServicePurchase)",
			source);
		AssertEx.Contains("[PatchPostfix]", source);
		AssertEx.Contains(
			"__instance?.AvailableInteractionState?.Value != null",
			source);
		AssertEx.False(
			Regex.IsMatch(
				source,
				@"\b(?:AddItem|RemoveItem|MoveItem)\s*\(",
				RegexOptions.CultureInvariant),
			"The interaction patches must delegate to EFT's native transfer flow instead of mutating inventory.");
	}

	[RegressionTest]
	private static void HelicopterWaitWindowPausesWhileTransferScreenIsOpen()
	{
		string source = ReadProductionSource(Uh60Path);
		string arrival = ExtractMember(source, "OnHelicopterArrive");
		string wait = ExtractMember(
			source,
			"WaitForAvailableHelicopterWindow");

		AssertEx.True(
			Regex.Matches(
				arrival,
				@"await\s+WaitForAvailableHelicopterWindow\s*\(",
				RegexOptions.CultureInvariant).Count == 2,
			"Both helicopter wait-window phases must use the transfer-aware clock.");
		AssertEx.True(
			Regex.IsMatch(
				wait,
				@"\b\w+\.IsItemTransferOpen\b",
				RegexOptions.CultureInvariant),
			"The helicopter wait clock must pause while the cargo-transfer screen is open.");
		AssertEx.Contains("elapsedSeconds += Time.deltaTime", wait);
		AssertEx.Contains(
			"TryBeginImmediateCargoDeparture(cargoTransferPoint)",
			wait);
		AssertEx.Contains("return true", wait);
		AssertEx.Contains(
			"EVoiceoverType.SupportHeliLeavingAfterPickup",
			arrival);
		AssertEx.Contains(
			"EVoiceoverType.SupportHeliLeavingNoPickup",
			arrival);
		AssertBefore(
			arrival,
			"await WaitForAvailableHelicopterWindow(",
			"helicopterAnimator.SetTrigger(s_flyAway)",
			"Successful cargo must short-circuit the remaining landed wait before the existing fly-away animation is triggered.");
	}

	[RegressionTest]
	private static void SuccessfulNativeMoveAloneTriggersCargoDeparture()
	{
		string adapter = ReadProductionSource(AdapterPath);
		string pointSource = ReadProductionSource(CargoPointPath);
		string notify = ExtractMember(adapter, "NotifyServicePurchased");
		string marker = ExtractMember(
			adapter,
			"MarkVerifiedUh60TransferAsync");
		string close = ExtractMember(adapter, "OnScreenClosed");
		string complete = ExtractMember(
			pointSource,
			"CompleteSuccessfulTransfer");

		AssertEx.False(
			notify.Contains(
				"CompleteSuccessfulTransfer",
				StringComparison.Ordinal),
			"The purchase callback precedes EFT's item move and must never start departure itself.");
		AssertEx.Contains("BeginSuccessfulTransfer", close);
		AssertEx.False(
			close.Contains(
				"CompleteSuccessfulTransfer",
				StringComparison.Ordinal),
			"Closing a successful screen may block reopening, but persistent-grid verification must remain the departure commit point.");
		AssertBefore(
			marker,
			"CollectPersistentTransferItemIds",
			"point.CompleteSuccessfulTransfer(player)",
			"Departure must wait until EFT's persistent delivery grid contains the staged cargo.");
		AssertBefore(
			marker,
			"point.CompleteSuccessfulTransfer(player)",
			"TryMarkUh60TransferAsync",
			"Pilot-messenger HTTP latency must not hold a verified helicopter on the ground.");
		AssertEx.True(
			Regex.Matches(
				adapter,
				@"point\.CompleteSuccessfulTransfer\s*\(",
				RegexOptions.CultureInvariant).Count == 1,
			"Only the verified persistent-grid path may commit immediate departure.");
		AssertEx.Contains(
			"if (!_successfulTransferPending ||",
			complete);
		AssertEx.Contains(
			"_successfulTransferCompleted = true",
			complete);
	}

	[RegressionTest]
	private static void FikaCargoDepartureTargetsTheAcceptedRequestOnce()
	{
		string runtime = ReadProductionSource(RuntimePath);
		string dispatch = ReadProductionSource(DispatchPath);
		string networking =
			ReadProductionSource(CargoDepartureNetworkingPath);
		string integration = ReadProductionSource(FikaIntegrationPath);
		string packet = ReadProductionSource(CargoDeparturePacketPath);
		string localPublish = ExtractMember(
			integration,
			"OnLocalUh60CargoDeparture");
		string remoteReceive = ExtractMember(
			integration,
			"OnClientUh60CargoDeparture");

		AssertEx.Contains("string supportRequestId = \"\"", runtime);
		AssertEx.Contains(
			"uh60Behaviour.SetRequestTiming(",
			runtime);
		AssertEx.Contains(
			"string supportRequestId =",
			dispatch);
		AssertEx.Contains(
			"supportRequestId: supportRequestId",
			dispatch);
		AssertEx.Contains(
			"DeparturePublished",
			networking);
		AssertEx.Contains(
			"TryPublishDeparture",
			networking);
		AssertEx.Contains(
			"RemoteDepartureReceived",
			networking);
		AssertEx.Contains(
			"s_remoteDepartures[supportRequestId]",
			networking);
		AssertEx.Contains(
			"TryGetRemoteDeparture",
			runtime + ReadProductionSource(Uh60Path));
		AssertEx.Contains(
			"class Uh60CargoDeparturePacket",
			packet);
		AssertEx.Contains(
			"client.RegisterPacket<Uh60CargoDeparturePacket>",
			integration);
		AssertEx.Contains(
			"FikaBackendUtils.IsServer",
			localPublish);
		AssertEx.Contains(
			"s_authorityRequests.TryGetValue",
			localPublish);
		AssertEx.Contains(
			"outcome.Result.Accepted",
			localPublish);
		AssertEx.Contains(
			"DeliveryMethod.ReliableOrdered",
			localPublish);
		AssertEx.Contains(
			"return false",
			localPublish);
		AssertEx.Contains(
			"RetryPendingCargoDeparturePublication",
			ReadProductionSource(Uh60Path));
		AssertEx.Contains(
			"CargoDeparturePublishMaxAttempts",
			ReadProductionSource(Uh60Path));
		AssertEx.Contains(
			"s_acceptedClientEvents.TryGetValue",
			remoteReceive);
		AssertEx.Contains(
			"MatchesCargoDeparture",
			remoteReceive);
		AssertEx.Contains(
			"s_completedClientCargoDepartures.Add(requestId)",
			remoteReceive);
		AssertEx.Contains(
			"Uh60CargoDepartureNetworking.ApplyRemoteDeparture(",
			remoteReceive);
		AssertEx.Contains(
			"s_completedClientCargoDepartures.Clear()",
			integration);
	}

	[RegressionTest]
	private static void CargoTransferPointHasNoExtractionCapability()
	{
		string source = ReadProductionSource(CargoPointPath);
		string enter = ExtractMember(source, "OnTriggerEnter");
		string exit = ExtractMember(source, "OnTriggerExit");
		string destroy = ExtractMember(source, "OnDestroy");
		string begin = ExtractMember(source, "TryBeginItemTransfer");
		string end = ExtractMember(source, "EndItemTransfer");
		_ = ExtractMember(source, "CanOpenItemTransfer");

		AssertEx.Contains("class HeliCargoTransferPoint", source);
		AssertEx.Contains("FireSupportItemTransfer.EnterZone", enter);
		AssertEx.Contains("FireSupportItemTransfer.LeaveZone", exit);
		AssertEx.Contains("FireSupportItemTransfer.PointDestroyed", destroy);
		AssertEx.Contains("_itemTransferOpen = true", begin);
		AssertEx.Contains("_itemTransferOpen = false", end);
		AssertEx.False(
			source.Contains("HeliExfiltrationPoint", StringComparison.Ordinal),
			"The cargo component must not inherit from or delegate to the extraction component.");
		AssertEx.False(
			Regex.IsMatch(
				source,
				@"ExtractionCountdownClock|" +
				@"BattleUIPanelExitTrigger|" +
				@"StartCoroutine|" +
				@"StopCoroutine|" +
				@"\bIEnumerator\b|" +
				@"\bTimer\s*\(|" +
				@"ResetTimer|" +
				@"CanCompleteExtraction|" +
				@"FireSupportExtraction|" +
				@"TryOverrideExtract|" +
				@"ISessionStopper|" +
				@"\bStopSession\s*\(|" +
				@"ExitStatus|" +
				@"extractTime",
				RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),
			"The PriorityExfil cargo component must contain no extraction countdown or raid-ending capability.");
	}

	[RegressionTest]
	private static void ExtractionPointIsStandardOnlyAndRetainsNativeFlow()
	{
		string source = ReadProductionSource(ExtractionPointPath);
		string enter = ExtractMember(source, "OnTriggerEnter");
		string timer = ExtractMember(source, "Timer");

		AssertEx.False(
			Regex.IsMatch(
				source,
				@"ESupportType\.PriorityExfil|" +
				@"FireSupportItemTransfer|" +
				@"HeliCargoTransferPoint|" +
				@"GInterface177|" +
				@"_allowsItemTransfer|" +
				@"IsItemTransferOpen|" +
				@"TryBeginItemTransfer",
				RegexOptions.CultureInvariant),
			"The standard extraction component must not retain any PriorityExfil cargo wiring.");
		AssertEx.True(
			Regex.IsMatch(
				source,
				@"void\s+Initialize\s*\(\s*" +
				@"float\s+extractTimeSeconds\s*,\s*" +
				@"CancellationToken\s+\w+\s*\)",
				RegexOptions.CultureInvariant),
			"The standard extraction point must initialize only from an extraction duration and cancellation token.");
		AssertBefore(
			enter,
			"ResetTimer()",
			"Show(_countdown.RemainingSeconds)",
			"The standard extraction countdown must reset before it is shown.");
		AssertBefore(
			enter,
			"Show(_countdown.RemainingSeconds)",
			"StartCoroutine(Timer(player))",
			"The standard extraction countdown must be shown before its timer starts.");
		AssertEx.True(
			Regex.Matches(
				timer,
				@"CanCompleteExtraction\s*\(\s*player\s*\)",
				RegexOptions.CultureInvariant).Count >= 2,
			"Extraction eligibility must be checked both during and after the countdown.");
		AssertEx.Contains("_countdown.Advance(Time.deltaTime)", timer);
		AssertBefore(
			timer,
			"FireSupportExtraction.TryOverrideExtract",
			"sessionStopper.StopSession",
			"Fika extraction must remain the preferred path before the solo session-stop fallback.");
		AssertEx.Contains("ExitStatus.Survived", timer);
		AssertEx.Equal(
			1,
			Regex.Matches(
				source,
				@"\bFireSupportExtraction\.TryOverrideExtract\s*\(",
				RegexOptions.CultureInvariant).Count,
			"The standard extraction point must own exactly one Fika extraction handoff.");
		AssertEx.Equal(
			1,
			Regex.Matches(
				source,
				@"\bStopSession\s*\(",
				RegexOptions.CultureInvariant).Count,
			"The standard extraction point must retain exactly one solo session-stop fallback.");
	}

	[RegressionTest]
	private static void Uh60RoutesPriorityToCargoAndExtractToExtraction()
	{
		string source = ReadProductionSource(Uh60Path);
		string route = ExtractMember(source, "CreateLandingPoint");
		string createCargo = ExtractMember(source, "CreateCargoTransferPoint");
		string createExtraction = ExtractMember(source, "CreateExtractionPoint");

		bool ternaryRoute = Regex.IsMatch(
			route,
			@"requestSupportType\s*==\s*ESupportType\.PriorityExfil\s*" +
			@"\?\s*CreateCargoTransferPoint\s*\([^)]*\)\s*" +
			@":\s*CreateExtractionPoint\s*\(",
			RegexOptions.CultureInvariant | RegexOptions.Singleline);
		bool branchRoute = Regex.IsMatch(
			route,
			@"if\s*\(\s*requestSupportType\s*==\s*" +
			@"ESupportType\.PriorityExfil\s*\)\s*\{?\s*" +
			@"return\s+CreateCargoTransferPoint\s*\([^)]*\)\s*;\s*\}?" +
			@"[\s\S]*?return\s+CreateExtractionPoint\s*\(",
			RegexOptions.CultureInvariant);

		AssertEx.True(
			ternaryRoute || branchRoute,
			"PriorityExfil must route exclusively to Cargo Transfer while every other UH-60 request uses the standard extraction point.");
		AssertEx.Contains("AddComponent<HeliCargoTransferPoint>()", createCargo);
		AssertEx.False(
			Regex.IsMatch(
				createCargo,
				@"HeliExfiltrationPoint|ExtractTimeSeconds|" +
				@"\bStopSession\s*\(|\bTimer\s*\(",
				RegexOptions.CultureInvariant),
			"The cargo-point factory must not receive or instantiate extraction behavior.");
		AssertEx.Contains("AddComponent<HeliExfiltrationPoint>()", createExtraction);
		AssertEx.Contains("timingSnapshot.ExtractTimeSeconds", createExtraction);
		AssertEx.False(
			Regex.IsMatch(
				createExtraction,
				@"HeliCargoTransferPoint|ESupportType\.PriorityExfil",
				RegexOptions.CultureInvariant),
			"The standard extraction-point factory must not contain Cargo Transfer routing.");
	}

	[RegressionTest]
	private static void TransferBridgeTargetsCargoPointOnly()
	{
		string adapter = ReadProductionSource(AdapterPath);
		string patches = ReadProductionSource(InteractionPatchesPath);
		string cargo = ReadProductionSource(CargoPointPath);
		string extraction = ReadProductionSource(ExtractionPointPath);

		AssertEx.Contains("HeliCargoTransferPoint", adapter);
		AssertEx.Contains("HeliCargoTransferPoint", patches);
		AssertEx.False(
			adapter.Contains("HeliExfiltrationPoint", StringComparison.Ordinal) ||
			patches.Contains("HeliExfiltrationPoint", StringComparison.Ordinal),
			"The item-transfer bridge must never bind to a standard extraction point.");
		AssertEx.False(
			Regex.IsMatch(
				cargo + adapter + patches,
				@"TryOverrideExtract|" +
				@"ISessionStopper|" +
				@"\bStopSession\s*\(|" +
				@"ExitStatus|" +
				@"ExtractionCountdownClock",
				RegexOptions.CultureInvariant),
			"No code reachable only through the Cargo Transfer bridge may own extraction capability.");
		AssertEx.Contains(
			"FireSupportExtraction.TryOverrideExtract",
			extraction);
		AssertEx.Contains("sessionStopper.StopSession", extraction);
	}

	[RegressionTest]
	private static void CargoUsesTheReleasedPrioritySlotAndExistingArtwork()
	{
		string supportTypes = ReadProductionSource(SupportTypePath);
		string mainMenu = ReadProductionSource(MainMenuPath);
		string mainMenuView = ReadProductionSource(MainMenuViewPath);
		string phone = ReadProductionSource(PhoneRendererPath);

		AssertEx.Contains("PriorityExfil = 10", supportTypes);
		AssertEx.Contains(
			"ESupportType.PriorityExfil, \"PriorityExfil\", \"UH-60 CARGO TRANSFER\"",
			mainMenu);
		AssertEx.Contains(
			"ESupportType.PriorityExfil => \"UH-60 CARGO TRANSFER\"",
			phone);
		AssertEx.Contains(
			"ESupportType.PriorityExfil => \"CARGO ONLY\"",
			phone);
		AssertEx.Contains(
			"TG_03_PriorityExfil_Review_Rotate.png",
			phone);
		AssertEx.Contains(
			"TG_04_PriorityExfil_ConfirmSwipe.png",
			phone);
		AssertEx.Contains(
			"TG_06_PriorityExfil_Authorized.png",
			phone);
		AssertEx.False(
			mainMenu.Contains("UH-60 PRIORITY EXFIL", StringComparison.Ordinal),
			"The storefront must expose the replacement Cargo Transfer product.");
		AssertEx.Contains(
			"SetConfirmationPresentation(",
			ExtractMember(mainMenu, "ShowPurchaseConfirmation"));
		string confirmation = ExtractMember(mainMenuView, "SetConfirmationPresentation");
		AssertEx.Contains("service.Type == ESupportType.PriorityExfil", confirmation);
		AssertEx.Contains("This service does not extract your PMC.", confirmation);
		AssertEx.Contains("A separate RUB handling fee", confirmation);
		AssertEx.Contains("when cargo is loaded.", confirmation);
	}

	[RegressionTest]
	private static void TriggerAndDepartureLifecycleOwnTheTransferSession()
	{
		string pointSource = ReadProductionSource(CargoPointPath);
		string adapterSource = ReadProductionSource(AdapterPath);
		string enter = ExtractMember(pointSource, "OnTriggerEnter");
		string exit = ExtractMember(pointSource, "OnTriggerExit");
		string destroy = ExtractMember(pointSource, "OnDestroy");
		string pointDestroyed = ExtractMember(adapterSource, "PointDestroyed");
		string cleanup = ExtractMember(adapterSource, "CleanupSession");

		AssertEx.Contains("FireSupportItemTransfer.EnterZone", enter);
		AssertEx.Contains("FireSupportItemTransfer.LeaveZone", exit);
		AssertEx.Contains("FireSupportItemTransfer.PointDestroyed", destroy);
		AssertEx.Contains("ForceClose", pointDestroyed);
		AssertEx.Contains("point == s_sessionPoint", pointDestroyed);
		AssertEx.Contains("HeliCargoTransferPoint point = s_sessionPoint", cleanup);
		AssertEx.Contains("Player player = s_sessionPlayer", cleanup);
		AssertEx.Contains("point.EndItemTransfer(player)", cleanup);

		int clearActivePoint = pointDestroyed.IndexOf(
			"s_activePoint = null",
			StringComparison.Ordinal);
		int forceClose = pointDestroyed.IndexOf(
			"ForceClose(",
			StringComparison.Ordinal);
		bool capturedActiveState = Regex.IsMatch(
			pointDestroyed,
			@"bool\s+\w+\s*=\s*point\s*==\s*s_activePoint\s*;",
			RegexOptions.CultureInvariant);

		AssertEx.True(
			forceClose >= 0 &&
			(clearActivePoint < 0 ||
			 forceClose < clearActivePoint ||
			 capturedActiveState),
			"Helicopter departure must preserve enough state to close an active transfer screen.");
	}

	[RegressionTest]
	private static void RaidBoundariesResetAndCloseTransferState()
	{
		string start = ReadProductionSource(StartPatchPath);
		string dispose = ReadProductionSource(DisposePatchPath);
		string adapter = ReadProductionSource(AdapterPath);
		string reset = ExtractMember(adapter, "ResetForRaidBoundary");
		string restore = ExtractMember(
			adapter,
			"RestoreServiceAvailability");

		AssertEx.Contains(
			"FireSupportItemTransfer.ResetForRaidBoundary(\"raid started\")",
			start);
		AssertEx.Contains(
			"FireSupportItemTransfer.ResetForRaidBoundary(\"raid disposed\")",
			dispose);
		AssertEx.Contains("s_activePoint = null", reset);
		AssertEx.Contains("s_activePlayer = null", reset);
		AssertEx.Contains("ForceClose(reason)", reset);
		AssertEx.Contains("RestoreServiceAvailability()", reset);
		AssertEx.Contains("s_sessionGeneration++", reset);
		AssertEx.Contains("s_servicePurchaseObserved", restore);
		AssertEx.Contains("if (purchaseObserved)", restore);
		AssertEx.Contains("previousLocalServiceAvailability", restore);
		AssertEx.Contains("player?.SetTraderServiceAvailability", restore);
	}

	private static string ReadProductionSource(string relativePath)
	{
		string root = FindRepositoryRoot();
		string fullPath = Path.Combine(
			root,
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
		string[] seeds =
		[
			Environment.CurrentDirectory,
			AppContext.BaseDirectory
		];

		foreach (string seed in seeds)
		{
			DirectoryInfo? directory = new(seed);
			while (directory != null)
			{
				string marker = Path.Combine(
					directory.FullName,
					"project",
					"SamSWAT.FireSupport",
					"Unity",
					"HeliExfiltrationPoint.cs");
				if (File.Exists(marker))
				{
					return directory.FullName;
				}

				directory = directory.Parent;
			}
		}

		throw new RegressionAssertionException(
			"Could not locate the TacticalServicesControl source root.");
	}

	private static string ExtractMember(string source, string memberName)
	{
		Match declaration = Regex.Match(
			source,
			@"(?m)^[ \t]*(?:public|private|internal|protected)\s+" +
			@"(?:(?:static|virtual|override|sealed|async|new)\s+)*" +
			@"[\w<>,?.\[\]]+\s+" +
			Regex.Escape(memberName) +
			@"\s*\(",
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
		bool inString = false;
		bool inCharacter = false;
		bool inLineComment = false;
		bool inBlockComment = false;
		bool inVerbatimString = false;
		bool escaped = false;
		for (int index = openBrace; index < source.Length; index++)
		{
			char current = source[index];
			char next = index + 1 < source.Length
				? source[index + 1]
				: '\0';

			if (inLineComment)
			{
				if (current == '\n')
				{
					inLineComment = false;
				}

				continue;
			}

			if (inBlockComment)
			{
				if (current == '*' && next == '/')
				{
					inBlockComment = false;
					index++;
				}

				continue;
			}

			if (inVerbatimString)
			{
				if (current == '"' && next == '"')
				{
					index++;
				}
				else if (current == '"')
				{
					inVerbatimString = false;
				}

				continue;
			}

			if (escaped)
			{
				escaped = false;
				continue;
			}

			if ((inString || inCharacter) && current == '\\')
			{
				escaped = true;
				continue;
			}

			if (!inString &&
			    !inCharacter &&
			    current == '/' &&
			    next == '/')
			{
				inLineComment = true;
				index++;
				continue;
			}

			if (!inString &&
			    !inCharacter &&
			    current == '/' &&
			    next == '*')
			{
				inBlockComment = true;
				index++;
				continue;
			}

			if (!inString &&
			    !inCharacter &&
			    current == '@' &&
			    next == '"')
			{
				inVerbatimString = true;
				index++;
				continue;
			}

			if (!inCharacter && current == '"')
			{
				inString = !inString;
				continue;
			}

			if (!inString && current == '\'')
			{
				inCharacter = !inCharacter;
				continue;
			}

			if (inString || inCharacter)
			{
				continue;
			}

			if (current == '{')
			{
				depth++;
			}
			else if (current == '}')
			{
				depth--;
				if (depth == 0)
				{
					return source[start..(index + 1)];
				}
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
		int secondIndex = source.IndexOf(second, StringComparison.Ordinal);
		AssertEx.True(
			firstIndex >= 0 && secondIndex > firstIndex,
			message);
	}
}
