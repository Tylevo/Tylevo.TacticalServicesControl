internal static class SeasonalIntegrationSourceContractTests
{
	private const string BridgePath =
		"project/SamSWAT.FireSupport/Integration/SeasonalModifiersBridge.cs";
	private const string AvailabilityPath =
		"project/SamSWAT.FireSupport/Unity/FireSupportServiceAvailability.cs";
	private const string FikaIntegrationPath =
		"project/SamSWAT.FireSupport.Fika.Interop/FikaIntegration.cs";
	private const string WarningNetworkingPath =
		"project/SamSWAT.FireSupport/Integration/DangerCloseWarningNetworking.cs";
	private const string UplinkInventoryPath =
		"project/SamSWAT.FireSupport/Unity/UavDeviceInventory.cs";
	private const string PhoneHotkeyPath =
		"project/SamSWAT.FireSupport/Unity/UavPhoneHotkeyController.cs";
	private const string MainMenuPath =
		"project/SamSWAT.FireSupport/Unity/MainMenuPurchaseController.cs";
	private const string MainMenuViewPath =
		"project/SamSWAT.FireSupport/Unity/MainMenuPurchaseController.View.cs";
	private const string MainMenuPatchPath =
		"project/SamSWAT.FireSupport/Patches/MainMenuPurchasePatch.cs";
	private const string GameWorldStartPatchPath =
		"project/SamSWAT.FireSupport/Patches/GameWorldStartPatch.cs";
	private const string GameWorldDisposePatchPath =
		"project/SamSWAT.FireSupport/Patches/GameWorldDisposePatch.cs";
	private const string PhoneControllerPath =
		"project/SamSWAT.FireSupport/Unity/UavDeviceController.cs";
	private const string PhoneLaunchModePath =
		"project/SamSWAT.FireSupport/Unity/UavPhoneLaunchMode.cs";
	private const string PhoneScreenPath =
		"project/SamSWAT.FireSupport/Unity/UavPhoneScreenRenderer.cs";
	private const string ClientProjectPath =
		"project/SamSWAT.FireSupport/SamSWAT.FireSupport.Core.csproj";
	private const string PackageAllowlistPath =
		"tools/package-layout.allowlist.json";
	private const string VisualExecutorPath =
		"project/SamSWAT.FireSupport/Unity/Vehicles/A10VisualRuntimeExecutor.cs";
	private const string RuntimeContextPath =
		"project/SamSWAT.FireSupport/Unity/Vehicles/A10RuntimeRequestContext.cs";
	private const string DamageOnlyPassPath =
		"project/SamSWAT.FireSupport/Unity/Vehicles/A10DamageOnlyPass.cs";
	private const string VehicleWeaponPath =
		"project/SamSWAT.FireSupport/Unity/Vehicles/VehicleWeapon.cs";
	private const string IntegrationDocumentationPath =
		"docs/seasonal-modifiers-integration.md";

	[RegressionTest]
	private static void PublicBridgeKeepsTheVersionThreeReflectionContract()
	{
		string bridge = ReadProductionSource(BridgePath);

		AssertEx.Contains("public static class SeasonalModifiersBridge", bridge);
		AssertEx.Contains("private const int CurrentApiVersion = 3;", bridge);
		AssertEx.Contains("public static int ApiVersion => CurrentApiVersion;", bridge);
		AssertEx.Contains("public static bool IsDangerCloseAuthority", bridge);
		AssertEx.Contains("public static bool TrySetDangerCloseActive(", bridge);
		AssertEx.Contains("bool active,", bridge);
		AssertEx.Contains("string sourceId,", bridge);
		AssertEx.Contains("out string reason)", bridge);
		AssertEx.Contains("public static bool TryDispatchDangerCloseA10(", bridge);
		AssertEx.Contains("Vector3 target,", bridge);
		AssertEx.Contains("Vector3 direction,", bridge);
		AssertEx.Contains("string requestId,", bridge);
		AssertEx.Contains("Action<bool, string> onProcessed,", bridge);
		AssertEx.Contains("NotifyDispatchProcessed(onProcessed, accepted, processedReason);", bridge);
		AssertEx.Contains("processedReason = accepted ? \"Accepted\" : \"RuntimeRejected\";", bridge);
		AssertEx.Contains("FireSupportRequestOrigin.SeasonalAmbient", bridge);
		AssertEx.Contains("ProjectileOwnerModeOverride = A10ProjectileOwnerMode.RequesterProfile", bridge);
		AssertEx.Contains("gameWorld.GetEverExistedBridgeByProfileID(ballisticOwnerProfileId)", bridge);
		AssertEx.Contains("BallisticOwnerUnavailable", bridge);
		AssertEx.Contains("public static bool TryPublishDangerCloseAdvanceWarning(", bridge);
		AssertEx.Contains("string opportunityId,", bridge);
		AssertEx.Contains("int secondsRemaining,", bridge);
		AssertEx.Contains("public static bool TryCancelDangerCloseAdvanceWarning(", bridge);
		AssertEx.Contains("public static bool TryPublishDangerCloseInboundWarning(", bridge);
		AssertEx.Contains("string requestId,", bridge);
		AssertEx.Contains("requireActiveLease: false", bridge);
		AssertEx.Contains("reason = \"NotAuthority\";", bridge);
	}

	[RegressionTest]
	private static void WarningPresentationUsesDedicatedSlotAndUniversalInbound()
	{
		string warning = ReadProductionSource(WarningNetworkingPath);
		AssertEx.Contains("UavDeviceInventory.HasUplinkInDedicatedWarningSlot", warning);
		AssertEx.Contains("publication.Kind == DangerCloseWarningKind.Advance", warning);
		AssertEx.Contains("TryPresentDangerCloseAdvance(publication)", warning);
		AssertEx.Contains("ApplyDangerCloseTerminal(publication)", warning);
		AssertEx.Contains("ResetDangerClosePresentation(", warning);
		AssertEx.Contains("incomingCallStarted", warning);
		AssertEx.Contains("TryStartDangerCloseIncomingCall(publication)", warning);
		AssertEx.Contains("TryApplyDangerCloseTerminal(publication)", warning);
		AssertEx.Contains("TryResetDangerClosePresentation(", warning);
		AssertEx.Contains("lock (s_presentationGate)", warning);
		AssertEx.Contains("using the stock warning", warning);
		AssertEx.Contains(
			"$\"TSC UPLINK: PHONE RINGING. Press [{GetAnswerKeyLabel()}] to answer.\"",
			warning);
		AssertEx.Contains("DangerCloseWarningKind.Inbound =>", warning);
		AssertEx.Contains("A-10 STRAFE INBOUND", warning);
		AssertEx.Contains("SEEK COVER NOW", warning);
		AssertEx.Contains("AuthorityPublished", warning);
		int presentationGateIndex = warning.IndexOf(
			"if (!shouldPresent)",
			StringComparison.Ordinal);
		int terminalIndex = warning.IndexOf(
			"TryApplyDangerCloseTerminal(publication)",
			StringComparison.Ordinal);
		AssertEx.True(
			presentationGateIndex >= 0 && terminalIndex > presentationGateIndex,
			"Duplicate or late terminal packets must not mutate local phone state.");

		string inventory = ReadProductionSource(UplinkInventoryPath);
		AssertEx.Contains("DedicatedWarningSlotName = \"SpecialSlot4\"", inventory);
		AssertEx.Contains("address?.IsSpecialSlotAddress() == true", inventory);
		AssertEx.Contains("address.Container?.ID", inventory);
		AssertEx.True(
			inventory.IndexOf("address.ContainerName", StringComparison.Ordinal) < 0,
			"Dedicated-slot matching must use the slot container ID, not the parent item's display name.");
		AssertEx.Contains("FindUplinkInDedicatedWarningSlot", inventory);
	}

	[RegressionTest]
	private static void IncomingCallReusesTheUprightRadarPhonePresentation()
	{
		string launchMode = ReadProductionSource(PhoneLaunchModePath);
		AssertEx.Contains("DangerCloseIncomingCall", launchMode);

		string hotkey = ReadProductionSource(PhoneHotkeyPath);
		int answerIndex = hotkey.IndexOf(
			"HandleDangerCloseAnswer(pluginEnabled)",
			StringComparison.Ordinal);
		int radarIndex = hotkey.IndexOf(
			"HandleRadarHold(pluginEnabled)",
			StringComparison.Ordinal);
		AssertEx.True(
			answerIndex >= 0 && radarIndex > answerIndex,
			"A fresh configured radar-key press must answer before normal J/radar handling.");
		AssertEx.Contains(
			"TryOpenUplink(UavPhoneLaunchMode.DangerCloseIncomingCall)",
			hotkey);
		AssertEx.Contains("FindUplinkInDedicatedWarningSlot(player)", hotkey);
		AssertEx.Contains("DangerCloseRingDurationSeconds = 15f", hotkey);
		AssertEx.Contains("DangerCloseAnswerEquipTimeoutSeconds = 8f", hotkey);
		AssertEx.Contains("IsDangerCloseAnswerShortcutBound()", hotkey);
		AssertEx.Contains("IsLocalPlayerAlive()", hotkey);
		AssertEx.Contains("_dangerCloseRingtoneSource.loop = true;", hotkey);
		AssertEx.Contains("StopDangerCloseRingtone();", hotkey);
		AssertEx.Contains("UnityWebRequestMultimedia.GetAudioClip", hotkey);
		AssertEx.False(
			hotkey.Contains("_dangerCloseRingtoneLoadCoroutine", StringComparison.Ordinal),
			"Stopping a call must let the generation-invalidated request finish and dispose.");

		string controller = ReadProductionSource(PhoneControllerPath);
		AssertEx.Contains("IsUprightPhoneMode(LaunchMode)", controller);
		AssertEx.Contains(
			"launchMode == UavPhoneLaunchMode.DangerCloseIncomingCall;",
			controller);
		AssertEx.Contains(
			"StartUprightPhoneSessionWhenEquipped(weaponPrefab, LaunchMode)",
			controller);
		AssertEx.Contains("PhoneAnimator.Play(\"Outro Success\"", controller);
		AssertEx.Contains("GetDeployPoseNormalizedTime()", controller);
		AssertEx.Contains(
			"ShowPhoneState(TerraGroupPhoneState.DangerCloseWarning)",
			controller);
		AssertEx.Contains("NotifyDangerClosePhonePresented()", controller);
		AssertEx.Contains("FinishDangerCloseWarningSession", controller);

		int uprightStart = controller.IndexOf(
			"private IEnumerator StartUprightPhoneSessionWhenEquipped(",
			StringComparison.Ordinal);
		int uprightEnd = controller.IndexOf(
			"private void HandleRadarMonitorInput()",
			uprightStart,
			StringComparison.Ordinal);
		AssertEx.True(uprightStart >= 0 && uprightEnd > uprightStart);
		string uprightSession = controller[uprightStart..uprightEnd];
		AssertEx.Contains("if (!radarMonitor && !dangerCloseWarning)", uprightSession);

		int localFinishStart = controller.IndexOf(
			"private void FinishLocalUprightPresentation(",
			StringComparison.Ordinal);
		int localFinishEnd = controller.IndexOf(
			"private bool HandleServiceSelectionShortcuts()",
			localFinishStart,
			StringComparison.Ordinal);
		AssertEx.True(localFinishStart >= 0 && localFinishEnd > localFinishStart);
		AssertEx.False(
			controller[localFinishStart..localFinishEnd].Contains(
				"PublishPhoneVisualPhase",
				StringComparison.Ordinal),
			"The local warning phone must not emit purchase/cancel visual packets.");

		string screen = ReadProductionSource(PhoneScreenPath);
		AssertEx.Contains("TerraGroupPhoneState.DangerCloseWarning", screen);
		AssertEx.Contains("A-10 STRAFE TASKED", screen);
		AssertEx.Contains("COVER NOW", screen);
		AssertEx.Contains("INBOUND SOON", screen);
	}

	[RegressionTest]
	private static void AnsweredIncomingCallCanBeStowedAndReopenedUntilAdvanceExpiry()
	{
		string hotkey = ReadProductionSource(PhoneHotkeyPath);
		AssertEx.False(
			hotkey.Contains("DangerCloseAnsweredDisplaySeconds", StringComparison.Ordinal),
			"An answered warning must not retain the former five-second display lifetime.");
		AssertEx.False(
			hotkey.Contains("ResumeRingingAfterFailedPresentation", StringComparison.Ordinal),
			"Initial-answer and reopen failures must use their distinct recovery paths.");
		AssertEx.Contains("s_instance?._dangerCloseCall.IsAnswerActive == true", hotkey);
		AssertEx.Contains("_dangerCloseCall.TryMarkAnswerPresented(", hotkey);
		AssertEx.Contains("_dangerCloseCall.GetSecondsRemaining(Time.unscaledTime)", hotkey);

		int handlerStart = hotkey.IndexOf(
			"private bool HandleDangerCloseAnswer(bool allowOpen)",
			StringComparison.Ordinal);
		int handlerEnd = hotkey.IndexOf(
			"private void TryReopenDangerClosePhone()",
			handlerStart,
			StringComparison.Ordinal);
		AssertEx.True(handlerStart >= 0 && handlerEnd > handlerStart);
		string answerHandler = hotkey[handlerStart..handlerEnd];
		int stowIndex = answerHandler.IndexOf(
			"if (_dangerCloseCall.IsAnswered)",
			StringComparison.Ordinal);
		int reopenIndex = answerHandler.IndexOf(
			"if (_dangerCloseCall.IsAnsweredStowed)",
			StringComparison.Ordinal);
		int initialAnswerIndex = answerHandler.IndexOf(
			"if (!_dangerCloseCall.IsRinging)",
			StringComparison.Ordinal);
		AssertEx.True(
			stowIndex >= 0 && reopenIndex > stowIndex && initialAnswerIndex > reopenIndex,
			"J must stow a visible answer, reopen a stowed answer, then fall back to the initial ring path.");
		AssertEx.Contains("_dangerCloseCall.MarkAnswerStowed();", answerHandler);
		AssertEx.Contains("TryReopenDangerClosePhone();", answerHandler);
		AssertEx.Contains(
			"_dangerCloseCall.ResumeRingingAfterFailedAnswer(Time.unscaledTime);",
			answerHandler);
		int openIndex = answerHandler.IndexOf(
			"if (!TryOpenUplink(UavPhoneLaunchMode.DangerCloseIncomingCall))",
			StringComparison.Ordinal);
		int failedAnswerIndex = answerHandler.IndexOf(
			"_dangerCloseCall.ResumeRingingAfterFailedAnswer(Time.unscaledTime);",
			openIndex,
			StringComparison.Ordinal);
		int failedReturnIndex = answerHandler.IndexOf(
			"return true;",
			failedAnswerIndex,
			StringComparison.Ordinal);
		int stopAfterAnswerIndex = answerHandler.IndexOf(
			"StopDangerCloseRingtone();",
			openIndex,
			StringComparison.Ordinal);
		AssertEx.True(
			openIndex >= 0 && failedAnswerIndex > openIndex &&
			failedReturnIndex > failedAnswerIndex && stopAfterAnswerIndex > failedReturnIndex,
			"A synchronous answer failure must return after restoring Ringing, before ringtone shutdown.");

		int tryOpenStart = hotkey.IndexOf(
			"private bool TryOpenUplink(UavPhoneLaunchMode launchMode)",
			StringComparison.Ordinal);
		int tryOpenEnd = hotkey.IndexOf(
			"private void OnManualPhoneSpawned(",
			tryOpenStart,
			StringComparison.Ordinal);
		AssertEx.True(tryOpenStart >= 0 && tryOpenEnd > tryOpenStart);
		string tryOpen = hotkey[tryOpenStart..tryOpenEnd];
		AssertEx.Contains("bool launchStarted =", tryOpen);
		AssertEx.Contains("return launchStarted &&", tryOpen);
		AssertEx.Contains(
			"launchMode != UavPhoneLaunchMode.DangerCloseIncomingCall ||",
			tryOpen);
		AssertEx.Contains("_dangerCloseCall.IsAnswerActive", tryOpen);

		AssertEx.Contains("_dangerCloseCall.TryBeginReopen(", hotkey);
		AssertEx.Contains(
			"_dangerCloseCall.ResumeStowedAfterFailedReopen(Time.unscaledTime);",
			hotkey);
		AssertEx.Contains("DangerCloseIncomingCallTickResult.ReopenEquipTimedOut", hotkey);
		int advanceStart = hotkey.IndexOf(
			"private void AdvanceDangerCloseIncomingCall()",
			StringComparison.Ordinal);
		int advanceEnd = hotkey.IndexOf(
			"private void RequestDangerClosePhoneRestore(string reason)",
			advanceStart,
			StringComparison.Ordinal);
		AssertEx.True(advanceStart >= 0 && advanceEnd > advanceStart);
		string advance = hotkey[advanceStart..advanceEnd];
		int expiryIndex = advance.IndexOf(
			"DangerCloseIncomingCallTickResult.AdvanceExpired",
			StringComparison.Ordinal);
		int stopRingtoneIndex = advance.IndexOf(
			"StopDangerCloseRingtone();",
			expiryIndex,
			StringComparison.Ordinal);
		int restoreIndex = advance.IndexOf(
			"RequestDangerClosePhoneRestore(\"advance forecast elapsed\")",
			expiryIndex,
			StringComparison.Ordinal);
		AssertEx.True(
			expiryIndex >= 0 && stopRingtoneIndex > expiryIndex && restoreIndex > stopRingtoneIndex,
			"Advance expiry must stop a still-ringing call before beginning phone restore.");

		string controller = ReadProductionSource(PhoneControllerPath);
		AssertEx.Contains("!UavPhoneHotkeyController.IsDangerCloseAnswerActive", controller);
		AssertEx.Contains(
			"durationSeconds: UavPhoneHotkeyController.DangerCloseSecondsRemaining",
			controller);
	}

	[RegressionTest]
	private static void PhoneRestoreWaitsForTheHandsCallbackBeforeAllowingReopen()
	{
		string hotkey = ReadProductionSource(PhoneHotkeyPath);
		int transitionStart = hotkey.IndexOf(
			"private bool IsDangerClosePhoneTransitionActive()",
			StringComparison.Ordinal);
		int transitionEnd = hotkey.IndexOf(
			"private static bool IsRadarShortcutDown()",
			transitionStart,
			StringComparison.Ordinal);
		AssertEx.True(transitionStart >= 0 && transitionEnd > transitionStart);
		string transition = hotkey[transitionStart..transitionEnd];
		AssertEx.Contains("if (_restoreCoroutine != null)", transition);
		AssertEx.Contains(
			"(_dangerCloseCall.IsRinging || _dangerCloseCall.IsAnsweredStowed) &&",
			transition);
		AssertEx.Contains("player?.HandsController == null", transition);

		int normalRestoreStart = hotkey.IndexOf(
			"private IEnumerator RestoreManualPhoneAfterOutro(",
			StringComparison.Ordinal);
		int normalRestoreEnd = hotkey.IndexOf(
			"private static bool TryRemoveOwnedPhoneController(",
			normalRestoreStart,
			StringComparison.Ordinal);
		AssertEx.True(normalRestoreStart >= 0 && normalRestoreEnd > normalRestoreStart);
		string normalRestore = hotkey[normalRestoreStart..normalRestoreEnd];
		int normalWaitIndex = normalRestore.IndexOf(
			"yield return RestoreLastEquippedWeaponAndWait(",
			StringComparison.Ordinal);
		int normalReleaseIndex = normalRestore.IndexOf(
			"_restoreCoroutine = null;",
			normalWaitIndex,
			StringComparison.Ordinal);
		AssertEx.True(
			normalWaitIndex >= 0 && normalReleaseIndex > normalWaitIndex,
			"A normal outro must retain restore ownership until the last-weapon callback completes.");

		int cleanupStart = hotkey.IndexOf(
			"private void CleanupFailedManualEquip(",
			StringComparison.Ordinal);
		int failedRestoreStart = hotkey.IndexOf(
			"private IEnumerator RestoreLastEquippedWeaponAfterFailedEquip(",
			StringComparison.Ordinal);
		AssertEx.True(cleanupStart >= 0 && failedRestoreStart > cleanupStart);
		string failedCleanup = hotkey[cleanupStart..failedRestoreStart];
		AssertEx.Contains("_restoreCoroutine = StartCoroutine(", failedCleanup);
		AssertEx.Contains("RestoreLastEquippedWeaponAfterFailedEquip(", failedCleanup);
		int sharedRestoreStart = hotkey.IndexOf(
			"private IEnumerator RestoreLastEquippedWeaponAndWait(",
			failedRestoreStart,
			StringComparison.Ordinal);
		AssertEx.True(failedRestoreStart >= 0 && sharedRestoreStart > failedRestoreStart);
		string failedRestore = hotkey[failedRestoreStart..sharedRestoreStart];
		int failedWaitIndex = failedRestore.IndexOf(
			"yield return RestoreLastEquippedWeaponAndWait(",
			StringComparison.Ordinal);
		int failedReleaseIndex = failedRestore.IndexOf(
			"_restoreCoroutine = null;",
			failedWaitIndex,
			StringComparison.Ordinal);
		AssertEx.True(
			failedWaitIndex >= 0 && failedReleaseIndex > failedWaitIndex,
			"A failed equip must also retain restore ownership until the last-weapon callback completes.");

		int sharedRestoreEnd = hotkey.IndexOf(
			"private void ResetLifecycleState(",
			sharedRestoreStart,
			StringComparison.Ordinal);
		AssertEx.True(sharedRestoreEnd > sharedRestoreStart);
		string sharedRestore = hotkey[sharedRestoreStart..sharedRestoreEnd];
		AssertEx.Contains("Callback callback = result =>", sharedRestore);
		AssertEx.Contains("player.TrySetLastEquippedWeapon(true, callback);", sharedRestore);
		AssertEx.Contains("while (!completed &&", sharedRestore);
		AssertEx.Contains("yield return null;", sharedRestore);
		AssertEx.Contains("LastEquippedWeaponRestoreTimeoutSeconds", sharedRestore);
	}

	[RegressionTest]
	private static void IncomingCallAudioLoaderKeepsTheUserClipOutOfReleases()
	{
		string allowlist = ReadProductionSource(PackageAllowlistPath);
		AssertEx.False(
			allowlist.Contains(
				"danger-close-ringtone.mp3",
				StringComparison.Ordinal),
			"The user-supplied clip must stay out of public release archives until its redistribution rights are known.");

		string clientProject = ReadProductionSource(ClientProjectPath);
		AssertEx.Contains("UnityEngine.UnityWebRequestModule", clientProject);
		AssertEx.Contains("UnityEngine.UnityWebRequestAudioModule", clientProject);
		AssertEx.Contains(
			"LocalOnly\\danger-close-ringtone.mp3",
			clientProject);
		AssertEx.Contains(
			"Condition=\"Exists('$(DangerCloseLocalRingtone)')\"",
			clientProject);
	}

	[RegressionTest]
	private static void IncomingCallRingtoneIsFreshAndRaidScoped()
	{
		string hotkey = ReadProductionSource(PhoneHotkeyPath);
		int startStart = hotkey.IndexOf(
			"private void StartDangerCloseRingtone()",
			StringComparison.Ordinal);
		int loadStart = hotkey.IndexOf(
			"private IEnumerator LoadDangerCloseRingtone(",
			startStart,
			StringComparison.Ordinal);
		AssertEx.True(startStart >= 0 && loadStart > startStart);
		string start = hotkey[startStart..loadStart];
		int stopBeforeLoadIndex = start.IndexOf(
			"StopDangerCloseRingtone();",
			StringComparison.Ordinal);
		int generationCaptureIndex = start.IndexOf(
			"int audioGeneration = _dangerCloseAudioGeneration;",
			StringComparison.Ordinal);
		int loadStartedLogIndex = start.IndexOf(
			"TSC Danger Close ringtone load started generation={audioGeneration}",
			StringComparison.Ordinal);
		int freshLoadIndex = start.IndexOf(
			"StartCoroutine(LoadDangerCloseRingtone(audioGeneration));",
			StringComparison.Ordinal);
		AssertEx.True(
			stopBeforeLoadIndex >= 0 &&
			generationCaptureIndex > stopBeforeLoadIndex &&
			loadStartedLogIndex > generationCaptureIndex &&
			freshLoadIndex > loadStartedLogIndex,
			"Every ring must dispose the prior clip, capture the new generation, and decode the local MP3 again.");
		AssertEx.False(
			start.Contains("_dangerCloseRingtoneClip != null", StringComparison.Ordinal),
			"A later raid must not reuse an AudioClip owned by an already-disposed download handler.");
		AssertEx.False(
			start.Contains("PlayDangerCloseRingtone(_dangerCloseRingtoneClip", StringComparison.Ordinal),
			"The ringtone start path must never bypass a fresh decode with a cached clip.");

		int stopStart = hotkey.IndexOf(
			"private void StopDangerCloseRingtone()",
			loadStart,
			StringComparison.Ordinal);
		int pathStart = hotkey.IndexOf(
			"private static string GetDangerCloseRingtonePath()",
			stopStart,
			StringComparison.Ordinal);
		AssertEx.True(stopStart >= 0 && pathStart > stopStart);
		string stop = hotkey[stopStart..pathStart];
		int invalidateIndex = stop.IndexOf(
			"_dangerCloseAudioGeneration++;",
			StringComparison.Ordinal);
		int stopSourceIndex = stop.IndexOf(
			"_dangerCloseRingtoneSource.Stop();",
			StringComparison.Ordinal);
		int detachClipIndex = stop.IndexOf(
			"_dangerCloseRingtoneSource.clip = null;",
			StringComparison.Ordinal);
		int snapshotClipIndex = stop.IndexOf(
			"AudioClip ringtoneClip = _dangerCloseRingtoneClip;",
			StringComparison.Ordinal);
		int clearClipIndex = stop.IndexOf(
			"_dangerCloseRingtoneClip = null;",
			snapshotClipIndex,
			StringComparison.Ordinal);
		int destroyClipIndex = stop.IndexOf(
			"Destroy(ringtoneClip);",
			clearClipIndex,
			StringComparison.Ordinal);
		AssertEx.True(
			invalidateIndex >= 0 &&
			stopSourceIndex > invalidateIndex &&
			detachClipIndex > stopSourceIndex &&
			snapshotClipIndex > detachClipIndex &&
			clearClipIndex > snapshotClipIndex &&
			destroyClipIndex > clearClipIndex,
			"Stopping must invalidate stale loads, detach the source, clear the owned field, then destroy its snapshot.");
		AssertEx.Contains("if (ringtoneClip != null)", stop);

		int playStart = hotkey.IndexOf(
			"private void PlayDangerCloseRingtone(",
			loadStart,
			StringComparison.Ordinal);
		AssertEx.True(playStart >= 0 && stopStart > playStart);
		string load = hotkey[loadStart..playStart];
		AssertEx.Contains("TSC Danger Close ringtone load discarded generation=", load);
		AssertEx.Contains("currentGeneration={_dangerCloseAudioGeneration}", load);
		AssertEx.Contains("ringing={_dangerCloseCall.IsRinging}", load);
		string play = hotkey[playStart..stopStart];
		AssertEx.Contains("TSC Danger Close ringtone started generation={audioGeneration}", play);
		AssertEx.Contains("loadState={clip.loadState}", play);
		AssertEx.Contains("length={clip.length:0.000}s", play);
		AssertEx.Contains("sourceEnabled={_dangerCloseRingtoneSource.enabled}", play);
		AssertEx.Contains("active={_dangerCloseRingtoneSource.gameObject.activeInHierarchy}", play);
		AssertEx.Contains("volume={_dangerCloseRingtoneSource.volume:0.00}", play);
		AssertEx.Contains("isPlaying={_dangerCloseRingtoneSource.isPlaying}", play);

		int resetStart = hotkey.IndexOf(
			"private void ResetLifecycleState(",
			StringComparison.Ordinal);
		int onDestroyStart = hotkey.IndexOf(
			"private void OnDestroy()",
			resetStart,
			StringComparison.Ordinal);
		AssertEx.True(resetStart >= 0 && onDestroyStart > resetStart);
		string reset = hotkey[resetStart..onDestroyStart];
		int resetStopIndex = reset.IndexOf(
			"StopDangerCloseRingtone();",
			StringComparison.Ordinal);
		int callResetIndex = reset.IndexOf(
			"_dangerCloseCall.Reset();",
			StringComparison.Ordinal);
		AssertEx.True(
			resetStopIndex >= 0 && callResetIndex > resetStopIndex,
			"A raid reset must release ringtone resources before clearing the call state.");

		string onDestroy = hotkey[onDestroyStart..];
		AssertEx.Contains(
			"ResetLifecycleState(\"hotkey component destroyed\", destroyOwnedController: true);",
			onDestroy);
		AssertEx.False(
			onDestroy.Contains("Destroy(ringtoneClip);", StringComparison.Ordinal),
			"OnDestroy must use ResetLifecycleState instead of taking duplicate ownership of clip disposal.");
		AssertEx.False(
			onDestroy.Contains("_dangerCloseRingtoneClip = null", StringComparison.Ordinal),
			"OnDestroy must not duplicate StopDangerCloseRingtone's field cleanup.");
		AssertEx.Equal(
			-1,
			hotkey.IndexOf(
				"Destroy(ringtoneClip);",
				stopStart + destroyClipIndex + 1,
				StringComparison.Ordinal));

		string raidStart = ReadProductionSource(GameWorldStartPatchPath);
		string raidDispose = ReadProductionSource(GameWorldDisposePatchPath);
		AssertEx.Contains(
			"UavPhoneHotkeyController.ResetForRaidBoundary(\"raid started\")",
			raidStart);
		AssertEx.Contains(
			"UavPhoneHotkeyController.ResetForRaidBoundary(\"raid disposed\")",
			raidDispose);
	}

	[RegressionTest]
	private static void FikaWarningTransportIsReliableAndHostToClientOnly()
	{
		string integration = ReadProductionSource(FikaIntegrationPath);
		AssertEx.Contains(
			"client.RegisterPacket<DangerCloseWarningPacket>",
			integration);
		AssertEx.Contains(
			"DangerCloseWarningNetworking.AuthorityPublished +=",
			integration);
		AssertEx.Contains(
			"DangerCloseWarningNetworking.ApplyRemote(",
			integration);
		int publishIndex = integration.IndexOf(
			"private static void OnAuthorityDangerCloseWarningPublished(",
			StringComparison.Ordinal);
		int receiveIndex = integration.IndexOf(
			"private static void OnClientDangerCloseWarning(",
			publishIndex,
			StringComparison.Ordinal);
		AssertEx.True(publishIndex >= 0 && receiveIndex > publishIndex);
		string dangerCloseTransport = integration[publishIndex..receiveIndex];
		AssertEx.Contains("new DangerCloseWarningPacket(publication)", dangerCloseTransport);
		AssertEx.Contains("DeliveryMethod.ReliableOrdered", dangerCloseTransport);
		AssertEx.Contains("broadcast: true", dangerCloseTransport);
		AssertEx.False(
			integration.Contains(
				"server.RegisterPacket<DangerCloseWarningPacket",
				StringComparison.Ordinal),
			"Fika clients must have no server-side route for originating warning packets.");
		AssertEx.False(
			integration.Contains("DangerCloseAnswer", StringComparison.Ordinal),
			"Answering the warning phone must remain local presentation state.");
	}

	[RegressionTest]
	private static void ManualDangerCloseLockTargetsOnlyBothA10Services()
	{
		string availability = ReadProductionSource(AvailabilityPath);

		AssertEx.Contains("supportType == ESupportType.Strafe ||", availability);
		AssertEx.Contains("supportType == ESupportType.DoubleStrafe", availability);
		AssertEx.Contains("IsA10Type(supportType) && SeasonalModifiersBridge.IsDangerCloseActive", availability);
		AssertEx.Contains("_ => true", availability);
		AssertEx.False(
			availability.Contains("supportType == ESupportType.Uav", StringComparison.Ordinal),
			"Danger Close must not add a local restriction for UAV.");
		AssertEx.False(
			availability.Contains("supportType == ESupportType.Extract ||", StringComparison.Ordinal),
			"Danger Close must not add a local restriction for UH-60 extraction.");
	}

	[RegressionTest]
	private static void MainMenuStorefrontIsSuppressedOnlyWhenSeasonalClientIsLoaded()
	{
		string patch = ReadProductionSource(MainMenuPatchPath);
		AssertEx.Contains("typeof(MenuScreen)", patch);
		AssertEx.Contains("nameof(MenuScreen.Show)", patch);
		AssertEx.Contains("typeof(Profile)", patch);
		AssertEx.Contains("typeof(MatchmakerPlayersController)", patch);
		AssertEx.Contains("typeof(ESessionMode)", patch);
		AssertEx.Contains("[PatchPostfix]", patch);
		AssertEx.Contains(
			"MainMenuPurchaseController.Attach(__instance, __0);",
			patch);

		string mainMenu = ReadProductionSource(MainMenuPath);
		int catalogStart = mainMenu.IndexOf(
			"private static readonly ServiceDescriptor[] s_services =",
			StringComparison.Ordinal);
		int catalogEnd = mainMenu.IndexOf(
			"private static readonly string[] s_stackButtonNames =",
			catalogStart,
			StringComparison.Ordinal);
		AssertEx.True(catalogStart >= 0 && catalogEnd > catalogStart);
		string catalog = mainMenu[catalogStart..catalogEnd];
		AssertEx.Equal(
			6,
			catalog.Split("new(ESupportType.", StringSplitOptions.None).Length - 1);
		AssertEx.Contains("new(ESupportType.Strafe, \"A10\", \"A-10 STRAFE\")", catalog);
		AssertEx.Contains("new(ESupportType.DoubleStrafe, \"DoublePass\", \"A-10 DOUBLE PASS\")", catalog);
		AssertEx.Contains("new(ESupportType.Extract, \"Extraction\", \"UH-60 EXTRACTION\")", catalog);
		AssertEx.Contains("new(ESupportType.PriorityExfil, \"PriorityExfil\", \"UH-60 CARGO TRANSFER\")", catalog);
		AssertEx.Contains("new(ESupportType.Uav, \"Uav\", \"UAV RECON\")", catalog);
		AssertEx.Contains("new(ESupportType.FocusedSweep, \"FocusedSweep\", \"UAV FOCUSED SWEEP\")", catalog);
		AssertEx.False(
			catalog.Contains("Seasonal", StringComparison.OrdinalIgnoreCase),
			"The pre-raid catalog must contain only TSC-owned products.");

		int updateStart = mainMenu.IndexOf(
			"private void Update()",
			StringComparison.Ordinal);
		int ensureButtonStart = mainMenu.IndexOf(
			"private void EnsureMenuButton()",
			updateStart,
			StringComparison.Ordinal);
		AssertEx.True(updateStart >= 0 && ensureButtonStart > updateStart);
		string update = mainMenu[updateStart..ensureButtonStart];
		AssertEx.Contains(
			"bool shouldShow = ShouldShowMenuButton;",
			update);
		AssertEx.Contains("_menuButton.gameObject.SetActive(shouldShow);", update);
		AssertEx.Contains("if (_seasonalClientActive)", update);
		AssertEx.Contains("SuppressMenuForSeasonal();", update);

		AssertEx.Contains(
			"private const string SeasonalModifiersPluginGuid = \"com.tylevo.seasonalmodifiers\";",
			mainMenu);
		AssertEx.Contains(
			"return Chainloader.PluginInfos.ContainsKey(SeasonalModifiersPluginGuid);",
			mainMenu);
		AssertEx.Contains("_seasonalClientActive = IsSeasonalModifiersClientActive();", mainMenu);
		AssertEx.Contains("PluginSettings.Enabled?.Value == true &&", mainMenu);
		AssertEx.Contains("!_seasonalClientActive;", mainMenu);
		AssertEx.Contains("ClosePage();", mainMenu);
		AssertEx.Contains("RestoreMenuStackPositions();", mainMenu);
		AssertEx.Contains("RetireAllMenuButtons();", mainMenu);
		AssertEx.Contains("ClearMenuStackTracking();", mainMenu);
		AssertEx.Contains("_nextLayoutScanAt = float.PositiveInfinity;", mainMenu);
		AssertEx.Contains("enabled = false;", mainMenu);

		int positionButtonStart = mainMenu.IndexOf(
			"private void PositionMenuButton(",
			ensureButtonStart,
			StringComparison.Ordinal);
		AssertEx.True(positionButtonStart > ensureButtonStart);
		string ensureButton = mainMenu[ensureButtonStart..positionButtonStart];
		AssertEx.Contains(
			"_menuButton.gameObject.SetActive(ShouldShowMenuButton);",
			ensureButton);
		AssertEx.Contains("_menuButton.SetRawText(\"TSC UPLINK\"", ensureButton);
		AssertEx.Contains("_menuButton.OnClick.AddListener(OpenPage);", ensureButton);
		AssertEx.False(
			mainMenu.Contains("IsDangerCloseActive", StringComparison.Ordinal),
			"Main-menu suppression must depend on Seasonal client presence, not a raid lease.");
		AssertEx.False(
			mainMenu.Contains("SeasonalModifiersBridge", StringComparison.Ordinal) ||
			mainMenu.Contains("SeasonalAmbient", StringComparison.Ordinal) ||
			mainMenu.Contains("DangerCloseWarning", StringComparison.Ordinal),
			"Seasonal-owned raid presentation must not be added to the TSC pre-raid storefront.");
	}

	[RegressionTest]
	private static void DangerCloseLocksOnlyTheTwoA10Services()
	{
		string availability = ReadProductionSource(AvailabilityPath);
		int a10HelperStart = availability.IndexOf(
			"private static bool IsA10Type(",
			StringComparison.Ordinal);
		AssertEx.True(a10HelperStart >= 0);
		string a10Helper = availability[a10HelperStart..];
		AssertEx.Equal(
			2,
			a10Helper.Split("ESupportType.", StringSplitOptions.None).Length - 1);
		AssertEx.Contains("supportType == ESupportType.Strafe", a10Helper);
		AssertEx.Contains("supportType == ESupportType.DoubleStrafe", a10Helper);
		AssertEx.False(a10Helper.Contains("ESupportType.Extract", StringComparison.Ordinal));
		AssertEx.False(a10Helper.Contains("ESupportType.PriorityExfil", StringComparison.Ordinal));
		AssertEx.False(a10Helper.Contains("ESupportType.Uav", StringComparison.Ordinal));
		AssertEx.False(a10Helper.Contains("ESupportType.FocusedSweep", StringComparison.Ordinal));
		AssertEx.Contains(
			"IsA10Type(supportType) && SeasonalModifiersBridge.IsDangerCloseActive",
			availability);
		AssertEx.Contains("return \"AUTONOMOUS OPS\";", availability);

		string mainMenu = ReadProductionSource(MainMenuPath);
		string view = ReadProductionSource(MainMenuViewPath);
		int rowsStart = view.IndexOf("_rows.Clear();", StringComparison.Ordinal);
		AssertEx.True(rowsStart >= 0);
		int rowAssignment = view.IndexOf(
			"_rows[service.Type] = new RowView",
			rowsStart,
			StringComparison.Ordinal);
		AssertEx.True(rowsStart >= 0 && rowAssignment > rowsStart);
		int rowsEnd = view.IndexOf(';', rowAssignment) + 1;
		AssertEx.True(rowsStart >= 0 && rowsEnd > rowsStart);
		string rowConstruction = view[rowsStart..rowsEnd];
		AssertEx.Contains("for (int index = 0; index < s_services.Length; index++)", rowConstruction);
		AssertEx.Contains("_rows[service.Type] = new RowView", rowConstruction);
		AssertEx.Contains("SelectStoreService(service.Type)", rowConstruction);
		AssertEx.False(
			rowConstruction.Contains("ShowPurchaseConfirmation(", StringComparison.Ordinal) ||
			rowConstruction.Contains("BeginPurchase(", StringComparison.Ordinal),
			"Selecting a service card must only inspect the service, without purchasing it.");
		AssertEx.Contains("() => ShowPurchaseConfirmation(_selectedService)", view);
		AssertEx.Contains("_detailReview.interactable = row.CanPurchase;", view);
		AssertEx.False(
			rowConstruction.Contains("IsLocalUseAllowed", StringComparison.Ordinal) ||
			rowConstruction.Contains("IsDangerCloseActive", StringComparison.Ordinal),
			"Danger Close must not remove its locked A-10 rows from the TSC storefront.");

		int redrawStart = mainMenu.IndexOf("private void Redraw()", StringComparison.Ordinal);
		int redrawEnd = mainMenu.IndexOf(
			"private static bool ValidateSnapshot(",
			redrawStart,
			StringComparison.Ordinal);
		AssertEx.True(redrawStart >= 0 && redrawEnd > redrawStart);
		string redraw = mainMenu[redrawStart..redrawEnd];
		AssertEx.Contains("foreach (ServiceDescriptor service in s_services)", redraw);
		AssertEx.Contains(
			"FireSupportServiceAvailability.IsLocalUseAllowed(service.Type)",
			redraw);
		AssertEx.Contains(
			"FireSupportServiceAvailability.GetLocalRestrictionStatus(service.Type)",
			redraw);
		AssertEx.Contains("!locallyAvailable", redraw);
		AssertEx.Contains("? localRestrictionStatus", redraw);
		AssertEx.Contains("row.CanPurchase =", redraw);
		AssertEx.Contains("(!hasAmbiguousPurchase && enabled && !atLimit)", redraw);
		AssertEx.False(
			redraw.Contains("SetActive(false)", StringComparison.Ordinal) ||
			redraw.Contains("Destroy(row", StringComparison.Ordinal),
			"A locally restricted A-10 row must remain visible and report AUTONOMOUS OPS.");

		int purchaseStart = mainMenu.IndexOf(
			"private bool TryGetPurchaseContext(",
			StringComparison.Ordinal);
		int purchaseEnd = mainMenu.IndexOf(
			"private async UniTaskVoid PurchaseAsync(",
			purchaseStart,
			StringComparison.Ordinal);
		AssertEx.True(purchaseStart >= 0 && purchaseEnd > purchaseStart);
		string purchase = mainMenu[purchaseStart..purchaseEnd];
		AssertEx.Contains(
			"if (!FireSupportServiceAvailability.IsLocalUseAllowed(supportType))",
			purchase);
		AssertEx.Contains(
			"FireSupportServiceAvailability.GetLocalRestrictionReason(supportType)",
			purchase);
		AssertEx.Contains("return false;", purchase);
	}

	[RegressionTest]
	private static void FikaAuthorityRejectsPeerAmbientAndBypassesOnlyTheManualLock()
	{
		string integration = ReadProductionSource(FikaIntegrationPath);

		AssertEx.Contains("request.RequestOrigin == FireSupportRequestOrigin.SeasonalAmbient &&", integration);
		AssertEx.Contains("entry.OriginPeer != null", integration);
		AssertEx.Contains("AmbientAuthorityOnly", integration);
		AssertEx.Contains("!SeasonalModifiersBridge.IsDangerCloseActive", integration);
		AssertEx.Contains("ModifierInactive", integration);
		AssertEx.Contains("request.RequestOrigin == FireSupportRequestOrigin.Manual &&", integration);
		AssertEx.Contains("!FireSupportServiceAvailability.IsServiceEnabled(request.SupportType)", integration);
		AssertEx.Contains("packet.RequestOrigin == FireSupportRequestOrigin.SeasonalAmbient", integration);
		AssertEx.Contains("? A10ProjectileOwnerMode.RequesterProfile", integration);
	}

	[RegressionTest]
	private static void SeasonalAmbientUsesARealRequesterBallisticOwner()
	{
		string executor = ReadProductionSource(VisualExecutorPath);

		AssertEx.Contains("string projectileOwnerProfileId = request.RequesterProfileId;", executor);
		AssertEx.Contains("new A10RuntimeRequestContext", executor);
		AssertEx.Contains("new A10RuntimeRequestContext(", executor);
		AssertEx.False(
			executor.Contains("PushSupportRequestContext", StringComparison.Ordinal),
			"A-10 ownership must not use mutable global context across an await.");
		string context = ReadProductionSource(RuntimeContextPath);
		AssertEx.Contains("public sealed class A10RuntimeRequestContext", context);

		string damageOnlyPass = ReadProductionSource(DamageOnlyPassPath);
		AssertEx.Contains("A10ProjectileOwnerMode.NeutralSupport => owner.OwnerProfileId", damageOnlyPass);
		AssertEx.False(
			damageOnlyPass.Contains("TSC_A10_SUPPORT", StringComparison.Ordinal),
			"Headless ballistics must not use a synthetic owner profile.");

		string vehicleWeapon = ReadProductionSource(VehicleWeaponPath);
		AssertEx.Contains("gameWorld.GetEverExistedBridgeByProfileID(_playerProfileId)", vehicleWeapon);
		AssertEx.False(
			vehicleWeapon.Contains("TSC_A10_SUPPORT", StringComparison.Ordinal),
			"VehicleWeapon must receive only a real EFT ballistic owner profile.");
	}

	[RegressionTest]
	private static void DocumentationDefinesV3WarningAndReplayContract()
	{
		string documentation = ReadProductionSource(IntegrationDocumentationPath);

		AssertEx.Contains("ApiVersion == 3", documentation);
		AssertEx.Contains("System.Action<bool, string> onProcessed", documentation);
		AssertEx.Contains("globally unique for the", documentation);
		AssertEx.Contains("The synchronous return value describes queueing only.", documentation);
		AssertEx.Contains("reports the later authority or local", documentation);
		AssertEx.Contains("synchronous validation failures return `false`", documentation);
		AssertEx.Contains("TryPublishDangerCloseAdvanceWarning", documentation);
		AssertEx.Contains("TryCancelDangerCloseAdvanceWarning", documentation);
		AssertEx.Contains("TryPublishDangerCloseInboundWarning", documentation);
		AssertEx.Contains("SpecialSlot4", documentation);
		AssertEx.Contains("ReliableOrdered", documentation);
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
		foreach (string seed in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
		{
			DirectoryInfo? directory = new(seed);
			while (directory != null)
			{
				if (File.Exists(Path.Combine(directory.FullName, "SamSWAT.FireSupport.ArysReloaded.sln")))
				{
					return directory.FullName;
				}

				directory = directory.Parent;
			}
		}

		throw new RegressionAssertionException(
			"Could not locate the TacticalServicesControl source root.");
	}
}
