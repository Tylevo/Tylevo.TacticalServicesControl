using Fika.Core.Networking.LiteNetLib.Utils;
using SamSWAT.FireSupport.ArysReloaded;
using SamSWAT.FireSupport.ArysReloaded.Fika;
using SamSWAT.FireSupport.ArysReloaded.Unity;
using System.Reflection;

internal static class ExtractionSettingsRegressionTests
{
	[RegressionTest]
	private static void FikaSettingsPacketRoundTripsDistinctExtractionContracts()
	{
		FireSupportSettingsPacket expected = CreateSettingsPacket();
		var writer = new NetDataWriter();
		expected.Serialize(writer);

		var actual = new FireSupportSettingsPacket();
		var reader = new NetDataReader(writer.ToArray());
		actual.Deserialize(reader);

		AssertPacketEqual(expected, actual);
		AssertEx.Equal(0, reader.AvailableBytes);
	}

	[RegressionTest]
	private static void FikaSettingsPacketAcceptsLegacyCurrencyTail()
	{
		FireSupportSettingsPacket packet = CreateSettingsPacket();
		packet.PaymentCurrency = PaymentCurrency.USD;
		var writer = new NetDataWriter();
		packet.Serialize(writer);
		byte[] legacyBytes = writer.ToArray();
		Array.Resize(ref legacyBytes, legacyBytes.Length - 2 * sizeof(int));

		var legacy = new FireSupportSettingsPacket();
		var legacyReader = new NetDataReader(legacyBytes);
		legacy.Deserialize(legacyReader);

		AssertEx.Equal(PaymentCurrency.RUB, legacy.PaymentCurrency);
		AssertEx.Equal(
			FireSupportServiceSemantics.LegacyVersion,
			legacy.ServiceSemanticsVersion);
		AssertEx.Equal(0, legacyReader.AvailableBytes);
		AssertEx.Near(
			packet.ExtractionDispatchDelaySeconds,
			legacy.ExtractionDispatchDelaySeconds,
			0.0001f);
		AssertEx.Equal(
			packet.PriorityExfilHelicopterWaitTimeSeconds,
			legacy.PriorityExfilHelicopterWaitTimeSeconds);
		AssertEx.Near(
			packet.PriorityExfilExtractTimeSeconds,
			legacy.PriorityExfilExtractTimeSeconds,
			0.0001f);
	}

	[RegressionTest]
	private static void FikaSettingsPacketAcceptsLegacyServiceSemanticsTail()
	{
		FireSupportSettingsPacket packet = CreateSettingsPacket();
		packet.PaymentCurrency = PaymentCurrency.USD;
		var writer = new NetDataWriter();
		packet.Serialize(writer);
		byte[] legacyBytes = writer.ToArray();
		Array.Resize(ref legacyBytes, legacyBytes.Length - sizeof(int));

		var legacy = new FireSupportSettingsPacket();
		var legacyReader = new NetDataReader(legacyBytes);
		legacy.Deserialize(legacyReader);

		AssertEx.Equal(PaymentCurrency.USD, legacy.PaymentCurrency);
		AssertEx.Equal(
			FireSupportServiceSemantics.LegacyVersion,
			legacy.ServiceSemanticsVersion);
		AssertEx.Equal(0, legacyReader.AvailableBytes);
	}

	[RegressionTest]
	private static void FikaSettingsPacketNormalizesInvalidCurrency()
	{
		FireSupportSettingsPacket packet = CreateSettingsPacket();
		packet.PaymentCurrency = (PaymentCurrency)999;
		var writer = new NetDataWriter();
		packet.Serialize(writer);

		var actual = new FireSupportSettingsPacket();
		actual.Deserialize(new NetDataReader(writer.ToArray()));

		AssertEx.Equal(PaymentCurrency.RUB, actual.PaymentCurrency);
	}

	[RegressionTest]
	private static void TuningPrecedenceKeepsStandardAndCargoDistinct()
	{
		FireSupportTuningSettings.ClearSyncedTuning();
		FireSupportTuningSettings.ClearServerConfigTuning();
		try
		{
			SetLocalTuning(
				standard: new ExtractionTimingValues(9f, 45, 14f, 0.8f),
				priority: new ExtractionTimingValues(2f, 5, 60f, 1.8f));

			AssertTiming(
				new ExtractionTimingValues(9f, 45, 14f, 0.8f),
				FireSupportTuningSettings.CaptureHelicopterTiming(ESupportType.Extract));
			AssertTiming(
				new ExtractionTimingValues(2f, 5, 0f, 1.8f),
				FireSupportTuningSettings.CaptureHelicopterTiming(ESupportType.PriorityExfil));

			FireSupportTuningSettings.SetServerConfigTuning(
				doubleStrafeSecondPassDelay: 7f,
				helicopterDispatchDelay: 11f,
				helicopterWaitTime: 46,
				helicopterExtractTime: 15f,
				helicopterSpeedMultiplier: 0.9f,
				priorityExfilDispatchDelay: 3f,
				priorityExfilHelicopterWaitTime: 21,
				priorityExfilHelicopterSpeedMultiplier: 1.7f,
				requestCooldown: 222,
				revision: 10);
			AssertTiming(
				new ExtractionTimingValues(11f, 46, 15f, 0.9f),
				FireSupportTuningSettings.CaptureHelicopterTiming(ESupportType.Extract));
			AssertTiming(
				new ExtractionTimingValues(3f, 21, 0f, 1.7f),
				FireSupportTuningSettings.CaptureHelicopterTiming(ESupportType.PriorityExfil));

			FireSupportTuningSettings.SetSyncedTuning(
				doubleStrafeSecondPassDelay: 6f,
				helicopterDispatchDelay: 13f,
				helicopterWaitTime: 47,
				helicopterExtractTime: 16f,
				helicopterSpeedMultiplier: 1.1f,
				priorityExfilDispatchDelay: 4f,
				priorityExfilHelicopterWaitTime: 22,
				priorityExfilHelicopterSpeedMultiplier: 1.6f,
				requestCooldown: 111);
			HelicopterTimingSnapshot captured =
				FireSupportTuningSettings.CaptureHelicopterTiming(ESupportType.Extract);
			AssertTiming(
				new ExtractionTimingValues(13f, 47, 16f, 1.1f),
				captured);
			AssertTiming(
				new ExtractionTimingValues(4f, 22, 0f, 1.6f),
				FireSupportTuningSettings.CaptureHelicopterTiming(ESupportType.PriorityExfil));
			AssertEx.Equal(111, FireSupportTuningSettings.GetRequestCooldown());

			FireSupportTuningSettings.ClearSyncedTuning();
			AssertTiming(
				new ExtractionTimingValues(11f, 46, 15f, 0.9f),
				FireSupportTuningSettings.CaptureHelicopterTiming(ESupportType.Extract));
			AssertTiming(
				new ExtractionTimingValues(3f, 21, 0f, 1.7f),
				FireSupportTuningSettings.CaptureHelicopterTiming(ESupportType.PriorityExfil));
			AssertEx.Equal(222, FireSupportTuningSettings.GetRequestCooldown());

			FireSupportTuningSettings.ClearServerConfigTuning();
			AssertTiming(
				new ExtractionTimingValues(9f, 45, 14f, 0.8f),
				FireSupportTuningSettings.CaptureHelicopterTiming(ESupportType.Extract));
			AssertTiming(
				new ExtractionTimingValues(2f, 5, 0f, 1.8f),
				FireSupportTuningSettings.CaptureHelicopterTiming(ESupportType.PriorityExfil));

			// A request snapshot must remain immutable when later authority
			// layers are cleared or replaced.
			AssertTiming(
				new ExtractionTimingValues(13f, 47, 16f, 1.1f),
				captured);
		}
		finally
		{
			FireSupportTuningSettings.ClearSyncedTuning();
			FireSupportTuningSettings.ClearServerConfigTuning();
		}
	}

	[RegressionTest]
	private static void RuntimeTuningHasNoPriorityExtractionStateOrArguments()
	{
		Type tuningType = typeof(FireSupportTuningSettings);
		string[] forbiddenState =
		[
			"_syncedPriorityExfilHelicopterExtractTime",
			"_serverPriorityExfilHelicopterExtractTime"
		];
		FieldInfo[] fields = tuningType.GetFields(
			BindingFlags.Static |
			BindingFlags.NonPublic |
			BindingFlags.Public);
		foreach (string fieldName in forbiddenState)
		{
			AssertEx.False(
				fields.Any(field => field.Name == fieldName),
				$"Cargo runtime tuning must not retain extraction state '{fieldName}'.");
		}

		foreach (string methodName in new[]
		         {
			         nameof(FireSupportTuningSettings.SetSyncedTuning),
			         nameof(FireSupportTuningSettings.SetServerConfigTuning)
		         })
		{
			MethodInfo method = tuningType.GetMethod(
				methodName,
				BindingFlags.Static |
				BindingFlags.Public) ??
				throw new RegressionAssertionException(
					$"Required tuning method was not found: {methodName}");
			AssertEx.False(
				method.GetParameters().Any(
					parameter =>
						string.Equals(
							parameter.Name,
							"priorityExfilHelicopterExtractTime",
							StringComparison.Ordinal)),
				$"{methodName} must not accept the dormant PriorityExfil extraction value.");
		}
	}

	[RegressionTest]
	private static void TimingPolicyEnforcesBoundsAndExactSafetyMargin()
	{
		AssertEx.True(ExtractionTimingPolicy.TryValidate(
			new ExtractionTimingValues(0f, 5, 1f, 0.5f),
			"extraction",
			out string minimumError),
			minimumError);
		AssertEx.True(ExtractionTimingPolicy.TryValidate(
			new ExtractionTimingValues(120f, 300, 60f, 3f),
			"extraction",
			out string maximumError),
			maximumError);

		AssertEx.False(ExtractionTimingPolicy.TryValidate(
			new ExtractionTimingValues(9f, 10, 10f, 1f),
			"extraction",
			out string equalWaitError));
		AssertEx.Contains("extraction.waitTimeSeconds", equalWaitError);
		AssertEx.True(ExtractionTimingPolicy.TryValidate(
			new ExtractionTimingValues(9f, 11, 10f, 1f),
			"extraction",
			out _));
		AssertEx.False(ExtractionTimingPolicy.TryValidate(
			new ExtractionTimingValues(2f, 11, 10.5f, 1.5f),
			"extraction",
			out string fractionalError));
		AssertEx.Contains("extraction.extractTimeSeconds (10.5)", fractionalError);
		AssertEx.True(ExtractionTimingPolicy.TryValidate(
			new ExtractionTimingValues(2f, 12, 10.5f, 1.5f),
			"extraction",
			out _));

		AssertEx.False(ExtractionTimingPolicy.TryValidate(
			new ExtractionTimingValues(-0.01f, 30, 10f, 1f),
			"extraction",
			out _));
		AssertEx.False(ExtractionTimingPolicy.TryValidate(
			new ExtractionTimingValues(float.NaN, 30, 10f, 1f),
			"extraction",
			out _));
		AssertEx.False(ExtractionTimingPolicy.TryValidate(
			new ExtractionTimingValues(0f, 4, 1f, 0.5f),
			"extraction",
			out _));
		AssertEx.False(ExtractionTimingPolicy.TryValidate(
			new ExtractionTimingValues(0f, 300, 60.01f, 3f),
			"extraction",
			out _));
		AssertEx.False(ExtractionTimingPolicy.TryValidate(
			new ExtractionTimingValues(0f, 30, 10f, float.PositiveInfinity),
			"extraction",
			out _));

		AssertEx.Equal(11, ExtractionTimingPolicy.GetMinimumSafeWaitTimeSeconds(10f));
		AssertEx.Equal(12, ExtractionTimingPolicy.GetMinimumSafeWaitTimeSeconds(10.5f));
		AssertEx.Equal(155, ExtractionTimingPolicy.GetRequiredPendingUseTimeoutSeconds());
	}

	[RegressionTest]
	private static void CargoTimingPolicyIgnoresLegacyExtractCountdown()
	{
		var independentCargoTiming =
			new ExtractionTimingValues(
				dispatchDelaySeconds: 120f,
				waitTimeSeconds: 5,
				extractTimeSeconds: 987.25f,
				speedMultiplier: 3f);
		AssertEx.True(
			CargoTimingPolicy.TryValidate(
				independentCargoTiming,
				"priorityExfil",
				out string independentError),
			independentError);

		HelicopterTimingSnapshot directRuntime =
			CargoTimingPolicy.CreateRuntimeSnapshot(independentCargoTiming);
		AssertEx.Equal(ESupportType.PriorityExfil, directRuntime.SupportType);
		AssertTiming(
			new ExtractionTimingValues(120f, 5, 0f, 3f),
			directRuntime);

		HelicopterTimingSnapshot clampedActiveFields =
			CargoTimingPolicy.CreateRuntimeSnapshot(
				new ExtractionTimingValues(-2f, 0, -77f, -3f));
		AssertEx.Near(0f, clampedActiveFields.DispatchDelaySeconds, 0.0001f);
		AssertEx.Equal(1, clampedActiveFields.WaitTimeSeconds);
		AssertEx.Near(0f, clampedActiveFields.ExtractTimeSeconds, 0.0001f);
		AssertEx.Near(
			ExtractionTimingPolicy.RuntimeMinSpeedMultiplier,
			clampedActiveFields.SpeedMultiplier,
			0.0001f);

		AssertEx.False(
			CargoTimingPolicy.TryValidate(
				new ExtractionTimingValues(float.NaN, 20, 10f, 1.35f),
				"priorityExfil",
				out string dispatchError));
		AssertEx.Contains("priorityExfil.dispatchDelaySeconds", dispatchError);

		AssertEx.False(
			CargoTimingPolicy.TryValidate(
				new ExtractionTimingValues(3f, 4, 10f, 1.35f),
				"priorityExfil",
				out string waitError));
		AssertEx.Contains("priorityExfil.waitTimeSeconds", waitError);

		AssertEx.False(
			CargoTimingPolicy.TryValidate(
				new ExtractionTimingValues(
					3f,
					20,
					10f,
					float.PositiveInfinity),
				"priorityExfil",
				out string speedError));
		AssertEx.Contains("priorityExfil.speedMultiplier", speedError);
	}

	[RegressionTest]
	private static void ExtractionTimingPolicyNeverProducesCargoSnapshots()
	{
		HelicopterTimingSnapshot runtime =
			ExtractionTimingPolicy.CreateRuntimeSnapshot(
				ESupportType.PriorityExfil,
				new ExtractionTimingValues(2f, 20, 5f, 1.8f));

		AssertEx.Equal(
			ESupportType.Extract,
			runtime.SupportType,
			"ExtractionTimingPolicy must remain standard-Extraction-only even when a legacy caller passes PriorityExfil.");
		AssertEx.Near(5f, runtime.ExtractTimeSeconds, 0.0001f);
	}

	[RegressionTest]
	private static void CargoTimingRepairPreservesLegacyExtractValue()
	{
		var defaults = new ExtractionTimingValues(3f, 20, 10f, 1.35f);
		ExtractionTimingValues repaired = CargoTimingPolicy.Repair(
			new ExtractionTimingValues(-1f, 301, 987.25f, 4f),
			defaults);

		AssertEx.Near(defaults.DispatchDelaySeconds, repaired.DispatchDelaySeconds, 0.0001f);
		AssertEx.Equal(defaults.WaitTimeSeconds, repaired.WaitTimeSeconds);
		AssertEx.Near(987.25f, repaired.ExtractTimeSeconds, 0.0001f);
		AssertEx.Near(defaults.SpeedMultiplier, repaired.SpeedMultiplier, 0.0001f);

		ExtractionTimingValues independent = CargoTimingPolicy.Repair(
			new ExtractionTimingValues(2f, 5, 60f, 1.5f),
			defaults);
		AssertEx.Near(2f, independent.DispatchDelaySeconds, 0.0001f);
		AssertEx.Equal(5, independent.WaitTimeSeconds);
		AssertEx.Near(60f, independent.ExtractTimeSeconds, 0.0001f);
		AssertEx.Near(1.5f, independent.SpeedMultiplier, 0.0001f);
	}

	[RegressionTest]
	private static void TimingPolicyRepairsUnsafeValuesWithoutChangingValidFields()
	{
		var defaults = new ExtractionTimingValues(8f, 30, 10f, 1f);
		ExtractionTimingValues relationshipRepair = ExtractionTimingPolicy.Repair(
			new ExtractionTimingValues(9f, 11, 10.5f, 1.25f),
			defaults);
		AssertEx.Near(9f, relationshipRepair.DispatchDelaySeconds, 0.0001f);
		AssertEx.Equal(12, relationshipRepair.WaitTimeSeconds);
		AssertEx.Near(10.5f, relationshipRepair.ExtractTimeSeconds, 0.0001f);
		AssertEx.Near(1.25f, relationshipRepair.SpeedMultiplier, 0.0001f);

		ExtractionTimingValues rangeRepair = ExtractionTimingPolicy.Repair(
			new ExtractionTimingValues(-1f, 301, float.NaN, 4f),
			defaults);
		AssertEx.Near(defaults.DispatchDelaySeconds, rangeRepair.DispatchDelaySeconds, 0.0001f);
		AssertEx.Equal(defaults.WaitTimeSeconds, rangeRepair.WaitTimeSeconds);
		AssertEx.Near(defaults.ExtractTimeSeconds, rangeRepair.ExtractTimeSeconds, 0.0001f);
		AssertEx.Near(defaults.SpeedMultiplier, rangeRepair.SpeedMultiplier, 0.0001f);

		HelicopterTimingSnapshot runtime = ExtractionTimingPolicy.CreateRuntimeSnapshot(
			ESupportType.Extract,
			new ExtractionTimingValues(-5f, 1, -2f, -3f));
		AssertEx.Equal(ESupportType.Extract, runtime.SupportType);
		AssertEx.Near(0f, runtime.DispatchDelaySeconds, 0.0001f);
		AssertEx.Equal(2, runtime.WaitTimeSeconds);
		AssertEx.Near(0.1f, runtime.ExtractTimeSeconds, 0.0001f);
		AssertEx.Near(0.01f, runtime.SpeedMultiplier, 0.0001f);
	}

	[RegressionTest]
	private static void CountdownInitializesResetsAndCompletesOnlyOnce()
	{
		var clock = new ExtractionCountdownClock();
		clock.Initialize(14.5f);
		AssertEx.Near(14.5f, clock.DurationSeconds, 0.0001f);
		AssertEx.Near(14.5f, clock.RemainingSeconds, 0.0001f);
		AssertEx.False(clock.IsComplete);

		AssertEx.False(clock.Advance(8f));
		AssertEx.Near(6.5f, clock.RemainingSeconds, 0.0001f);
		clock.Reset();
		AssertEx.Near(14.5f, clock.RemainingSeconds, 0.0001f);
		AssertEx.False(clock.IsComplete);

		AssertEx.True(clock.Advance(20f));
		AssertEx.Near(0f, clock.RemainingSeconds, 0.0001f);
		AssertEx.True(clock.IsComplete);
		AssertEx.False(clock.Advance(1f));

		clock.Reset();
		AssertEx.Near(14.5f, clock.RemainingSeconds, 0.0001f);
		AssertEx.False(clock.IsComplete);
	}

	[RegressionTest]
	private static void CountdownClampsInvalidDurationForStandardExtraction()
	{
		var standard = new ExtractionCountdownClock();
		standard.Initialize(14f);
		AssertEx.Near(14f, standard.RemainingSeconds, 0.0001f);

		standard.Advance(4f);
		standard.Reset();
		AssertEx.Near(14f, standard.RemainingSeconds, 0.0001f);

		var clamped = new ExtractionCountdownClock();
		clamped.Initialize(-10f);
		AssertEx.Near(
			ExtractionCountdownClock.MinimumDurationSeconds,
			clamped.DurationSeconds,
			0.0001f);
		AssertEx.False(clamped.Advance(-1f));
		AssertEx.Near(
			ExtractionCountdownClock.MinimumDurationSeconds,
			clamped.RemainingSeconds,
			0.0001f);
	}

	private static FireSupportSettingsPacket CreateSettingsPacket()
	{
		return new FireSupportSettingsPacket
		{
			IsRequest = false,
			Revision = 4711,
			StrafeCostRoubles = 101,
			DoubleStrafeCostRoubles = 202,
			ExtractionCostRoubles = 303,
			PriorityExfilCostRoubles = 404,
			UavCostRoubles = 505,
			FocusedSweepCostRoubles = 606,
			EnablePriorityExfil = true,
			EnableDoublePass = false,
			EnableFocusedSweep = true,
			UavDurationSeconds = 71,
			UavScanIntervalSeconds = 1.25f,
			UavRangeMeters = 207.5f,
			FocusedSweepDurationSeconds = 43,
			FocusedSweepScanIntervalSeconds = 0.75f,
			FocusedSweepRangeMeters = 88.5f,
			DoubleStrafeSecondPassDelaySeconds = 6.5f,
			ExtractionDispatchDelaySeconds = 9.25f,
			HelicopterWaitTimeSeconds = 47,
			ExtractionExtractTimeSeconds = 14.5f,
			HelicopterSpeedMultiplier = 0.85f,
			PriorityExfilDispatchDelaySeconds = 2.75f,
			PriorityExfilHelicopterWaitTimeSeconds = 23,
			PriorityExfilExtractTimeSeconds = 5.5f,
			PriorityExfilHelicopterSpeedMultiplier = 1.75f,
			RequestCooldownSeconds = 217,
			PaymentMode = PaymentMode.Hybrid,
			PaymentSource = PaymentSource.PreferStashThenCarried,
			ServerConfigUrl = "http://127.0.0.1:6969/tsc/config",
			PaymentCurrency = PaymentCurrency.EUR,
			ServiceSemanticsVersion = FireSupportServiceSemantics.CurrentVersion
		};
	}

	private static void AssertPacketEqual(
		FireSupportSettingsPacket expected,
		FireSupportSettingsPacket actual)
	{
		AssertEx.Equal(expected.IsRequest, actual.IsRequest);
		AssertEx.Equal(expected.Revision, actual.Revision);
		AssertEx.Equal(expected.StrafeCostRoubles, actual.StrafeCostRoubles);
		AssertEx.Equal(expected.DoubleStrafeCostRoubles, actual.DoubleStrafeCostRoubles);
		AssertEx.Equal(expected.ExtractionCostRoubles, actual.ExtractionCostRoubles);
		AssertEx.Equal(expected.PriorityExfilCostRoubles, actual.PriorityExfilCostRoubles);
		AssertEx.Equal(expected.UavCostRoubles, actual.UavCostRoubles);
		AssertEx.Equal(expected.FocusedSweepCostRoubles, actual.FocusedSweepCostRoubles);
		AssertEx.Equal(expected.EnablePriorityExfil, actual.EnablePriorityExfil);
		AssertEx.Equal(expected.EnableDoublePass, actual.EnableDoublePass);
		AssertEx.Equal(expected.EnableFocusedSweep, actual.EnableFocusedSweep);
		AssertEx.Equal(expected.UavDurationSeconds, actual.UavDurationSeconds);
		AssertEx.Near(expected.UavScanIntervalSeconds, actual.UavScanIntervalSeconds, 0.0001f);
		AssertEx.Near(expected.UavRangeMeters, actual.UavRangeMeters, 0.0001f);
		AssertEx.Equal(expected.FocusedSweepDurationSeconds, actual.FocusedSweepDurationSeconds);
		AssertEx.Near(
			expected.FocusedSweepScanIntervalSeconds,
			actual.FocusedSweepScanIntervalSeconds,
			0.0001f);
		AssertEx.Near(expected.FocusedSweepRangeMeters, actual.FocusedSweepRangeMeters, 0.0001f);
		AssertEx.Near(
			expected.DoubleStrafeSecondPassDelaySeconds,
			actual.DoubleStrafeSecondPassDelaySeconds,
			0.0001f);
		AssertEx.Near(
			expected.ExtractionDispatchDelaySeconds,
			actual.ExtractionDispatchDelaySeconds,
			0.0001f);
		AssertEx.Equal(expected.HelicopterWaitTimeSeconds, actual.HelicopterWaitTimeSeconds);
		AssertEx.Near(
			expected.ExtractionExtractTimeSeconds,
			actual.ExtractionExtractTimeSeconds,
			0.0001f);
		AssertEx.Near(
			expected.HelicopterSpeedMultiplier,
			actual.HelicopterSpeedMultiplier,
			0.0001f);
		AssertEx.Near(
			expected.PriorityExfilDispatchDelaySeconds,
			actual.PriorityExfilDispatchDelaySeconds,
			0.0001f);
		AssertEx.Equal(
			expected.PriorityExfilHelicopterWaitTimeSeconds,
			actual.PriorityExfilHelicopterWaitTimeSeconds);
		AssertEx.Near(
			expected.PriorityExfilExtractTimeSeconds,
			actual.PriorityExfilExtractTimeSeconds,
			0.0001f);
		AssertEx.Near(
			expected.PriorityExfilHelicopterSpeedMultiplier,
			actual.PriorityExfilHelicopterSpeedMultiplier,
			0.0001f);
		AssertEx.Equal(expected.RequestCooldownSeconds, actual.RequestCooldownSeconds);
		AssertEx.Equal(expected.PaymentMode, actual.PaymentMode);
		AssertEx.Equal(expected.PaymentSource, actual.PaymentSource);
		AssertEx.Equal(expected.ServerConfigUrl, actual.ServerConfigUrl);
		AssertEx.Equal(expected.PaymentCurrency, actual.PaymentCurrency);
		AssertEx.Equal(expected.ServiceSemanticsVersion, actual.ServiceSemanticsVersion);
	}

	private static void SetLocalTuning(
		ExtractionTimingValues standard,
		ExtractionTimingValues priority)
	{
		PluginSettings.HelicopterDispatchDelay.Value = standard.DispatchDelaySeconds;
		PluginSettings.HelicopterWaitTime.Value = standard.WaitTimeSeconds;
		PluginSettings.HelicopterExtractTime.Value = standard.ExtractTimeSeconds;
		PluginSettings.HelicopterSpeedMultiplier.Value = standard.SpeedMultiplier;
		PluginSettings.PriorityExfilDispatchDelay.Value = priority.DispatchDelaySeconds;
		PluginSettings.PriorityExfilHelicopterWaitTime.Value = priority.WaitTimeSeconds;
		PluginSettings.PriorityExfilHelicopterExtractTime.Value = priority.ExtractTimeSeconds;
		PluginSettings.PriorityExfilHelicopterSpeedMultiplier.Value = priority.SpeedMultiplier;
		PluginSettings.RequestCooldown.Value = 333;
	}

	private static void AssertTiming(
		ExtractionTimingValues expected,
		HelicopterTimingSnapshot actual)
	{
		AssertEx.Near(expected.DispatchDelaySeconds, actual.DispatchDelaySeconds, 0.0001f);
		AssertEx.Equal(expected.WaitTimeSeconds, actual.WaitTimeSeconds);
		AssertEx.Near(expected.ExtractTimeSeconds, actual.ExtractTimeSeconds, 0.0001f);
		AssertEx.Near(expected.SpeedMultiplier, actual.SpeedMultiplier, 0.0001f);
	}
}
