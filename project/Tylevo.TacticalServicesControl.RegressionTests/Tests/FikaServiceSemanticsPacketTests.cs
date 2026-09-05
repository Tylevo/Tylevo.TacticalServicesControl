using Fika.Core.Networking.LiteNetLib.Utils;
using SamSWAT.FireSupport.ArysReloaded.Fika;
using SamSWAT.FireSupport.ArysReloaded.Unity;
using UnityEngine;

internal static class FikaServiceSemanticsPacketTests
{
	[RegressionTest]
	private static void RequestPacketAppendsSemanticsAndConsumesLegacyFields()
	{
		FireSupportRequestPacket expected = CreateRequest();
		AssertEx.Near(
			8.25f,
			expected.HelicopterExtractTimeSeconds,
			0.0001f,
			"The positional legacy extraction-time field must remain writable for mixed-version packet compatibility.");
		byte[] currentBytes = Serialize(expected);

		var current = new FireSupportRequestPacket();
		var currentReader = new NetDataReader(currentBytes);
		current.Deserialize(currentReader);
		AssertRequestEqual(expected, current);
		AssertEx.Near(
			0f,
			current.HelicopterExtractTimeSeconds,
			0.0001f,
			"Cargo readers must consume but never activate a legacy extraction-time payload.");
		AssertEx.Equal(
			FireSupportServiceSemantics.CurrentVersion,
			current.ServiceSemanticsVersion);
		AssertEx.Equal(0, currentReader.AvailableBytes);

		byte[] withoutOriginBytes = currentBytes.ToArray();
		Array.Resize(ref withoutOriginBytes, withoutOriginBytes.Length - sizeof(int));
		var withoutOrigin = new FireSupportRequestPacket();
		var withoutOriginReader = new NetDataReader(withoutOriginBytes);
		withoutOrigin.Deserialize(withoutOriginReader);
		AssertRequestEqual(expected, withoutOrigin, compareOrigin: false);
		AssertEx.Equal(
			FireSupportServiceSemantics.CurrentVersion,
			withoutOrigin.ServiceSemanticsVersion);
		AssertEx.Equal(FireSupportRequestOrigin.Manual, withoutOrigin.RequestOrigin);
		AssertEx.Equal(0, withoutOriginReader.AvailableBytes);

		Array.Resize(ref currentBytes, currentBytes.Length - sizeof(int) * 2);
		var legacy = new FireSupportRequestPacket();
		var legacyReader = new NetDataReader(currentBytes);
		legacy.Deserialize(legacyReader);
		AssertRequestEqual(
			expected,
			legacy,
			compareSemantics: false,
			compareOrigin: false);
		AssertEx.Equal(
			FireSupportServiceSemantics.LegacyVersion,
			legacy.ServiceSemanticsVersion);
		AssertEx.Equal(FireSupportRequestOrigin.Manual, legacy.RequestOrigin);
		AssertEx.Equal(0, legacyReader.AvailableBytes);
	}

	[RegressionTest]
	private static void CargoRuntimePacketsAlwaysWriteZeroExtractionTime()
	{
		HelicopterTimingSnapshot cargoTiming =
			CargoTimingPolicy.CreateRuntimeSnapshot(
				new ExtractionTimingValues(
					dispatchDelaySeconds: 3.5f,
					waitTimeSeconds: 37,
					extractTimeSeconds: 987.25f,
					speedMultiplier: 1.5f));
		AssertEx.Near(0f, cargoTiming.ExtractTimeSeconds, 0.0001f);

		FireSupportRequestPacket request = CreateRequest();
		request.SetHelicopterTiming(cargoTiming, revision: 10);
		AssertEx.Near(
			0f,
			request.HelicopterExtractTimeSeconds,
			0.0001f,
			"Cargo runtime requests must populate the retained legacy wire slot with zero.");

		var rehydratedRequest = new FireSupportRequestPacket();
		rehydratedRequest.Deserialize(
			new NetDataReader(Serialize(request)));
		AssertEx.Near(
			0f,
			rehydratedRequest.HelicopterExtractTimeSeconds,
			0.0001f);

		var result = new FireSupportAuthorityResultPacket(
			request,
			accepted: true,
			reason: "Accepted");
		var rehydratedResult = new FireSupportAuthorityResultPacket();
		rehydratedResult.Deserialize(
			new NetDataReader(Serialize(result)));
		AssertEx.Near(
			0f,
			rehydratedResult.HelicopterExtractTimeSeconds,
			0.0001f,
			"Cargo authority results must preserve the runtime zero in the legacy wire slot.");
	}

	[RegressionTest]
	private static void AuthorityResultAppendsItsSemanticsWithoutChangingLegacyFields()
	{
		FireSupportRequestPacket request = CreateRequest();
		request.ServiceSemanticsVersion = FireSupportServiceSemantics.LegacyVersion;
		var expected = new FireSupportAuthorityResultPacket(
			request,
			accepted: true,
			reason: "Accepted");

		AssertEx.Equal(
			FireSupportServiceSemantics.CurrentVersion,
			expected.ServiceSemanticsVersion,
			"A new authority must advertise its own service semantics, not echo a legacy request.");

		byte[] currentBytes = Serialize(expected);
		var current = new FireSupportAuthorityResultPacket();
		var currentReader = new NetDataReader(currentBytes);
		current.Deserialize(currentReader);
		AssertResultEqual(expected, current);
		AssertEx.Equal(0, currentReader.AvailableBytes);

		FireSupportRequestPacket rehydrated = current.ToSupportRequest();
		AssertEx.Equal(
			FireSupportServiceSemantics.CurrentVersion,
			rehydrated.ServiceSemanticsVersion);

		byte[] withoutOriginBytes = currentBytes.ToArray();
		Array.Resize(ref withoutOriginBytes, withoutOriginBytes.Length - sizeof(int));
		var withoutOrigin = new FireSupportAuthorityResultPacket();
		var withoutOriginReader = new NetDataReader(withoutOriginBytes);
		withoutOrigin.Deserialize(withoutOriginReader);
		AssertResultEqual(expected, withoutOrigin, compareOrigin: false);
		AssertEx.Equal(
			FireSupportServiceSemantics.CurrentVersion,
			withoutOrigin.ServiceSemanticsVersion);
		AssertEx.Equal(FireSupportRequestOrigin.Manual, withoutOrigin.RequestOrigin);
		AssertEx.Equal(0, withoutOriginReader.AvailableBytes);

		Array.Resize(ref currentBytes, currentBytes.Length - sizeof(int) * 2);
		var legacy = new FireSupportAuthorityResultPacket();
		var legacyReader = new NetDataReader(currentBytes);
		legacy.Deserialize(legacyReader);
		AssertResultEqual(
			expected,
			legacy,
			compareSemantics: false,
			compareOrigin: false);
		AssertEx.Equal(
			FireSupportServiceSemantics.LegacyVersion,
			legacy.ServiceSemanticsVersion);
		AssertEx.Equal(
			FireSupportServiceSemantics.LegacyVersion,
			legacy.ToSupportRequest().ServiceSemanticsVersion);
		AssertEx.Equal(FireSupportRequestOrigin.Manual, legacy.RequestOrigin);
		AssertEx.Equal(0, legacyReader.AvailableBytes);
	}

	[RegressionTest]
	private static void CancelPacketCarriesOriginAndDefaultsLegacyToManual()
	{
		FireSupportRequestPacket request = CreateRequest();
		var expected = new FireSupportCancelPacket(request);
		var writer = new NetDataWriter();
		expected.Serialize(writer);
		byte[] bytes = writer.ToArray();

		var current = new FireSupportCancelPacket();
		var currentReader = new NetDataReader(bytes);
		current.Deserialize(currentReader);
		AssertEx.Equal(expected.SupportRequestId, current.SupportRequestId);
		AssertEx.Equal(expected.SupportType, current.SupportType);
		AssertEx.Equal(expected.PassIndex, current.PassIndex);
		AssertEx.Equal(expected.RequesterProfileId, current.RequesterProfileId);
		AssertEx.Equal(FireSupportRequestOrigin.SeasonalAmbient, current.RequestOrigin);
		AssertEx.Equal(0, currentReader.AvailableBytes);

		Array.Resize(ref bytes, bytes.Length - sizeof(int));
		var legacy = new FireSupportCancelPacket();
		var legacyReader = new NetDataReader(bytes);
		legacy.Deserialize(legacyReader);
		AssertEx.Equal(FireSupportRequestOrigin.Manual, legacy.RequestOrigin);
		AssertEx.Equal(0, legacyReader.AvailableBytes);
	}

	[RegressionTest]
	private static void CargoGateFailsClosedForLegacyTypeTenOnly()
	{
		AssertEx.False(
			FireSupportServiceSemantics.CanExecute(
				ESupportType.PriorityExfil,
				FireSupportServiceSemantics.LegacyVersion));
		AssertEx.True(
			FireSupportServiceSemantics.CanExecute(
				ESupportType.PriorityExfil,
				FireSupportServiceSemantics.CurrentVersion));
		AssertEx.True(
			FireSupportServiceSemantics.CanExecute(
				ESupportType.PriorityExfil,
				FireSupportServiceSemantics.CurrentVersion + 1));

		foreach (ESupportType supportType in new[]
		         {
			         ESupportType.Strafe,
			         ESupportType.Extract,
			         ESupportType.Uav,
			         ESupportType.DoubleStrafe,
			         ESupportType.FocusedSweep
		         })
		{
			AssertEx.True(
				FireSupportServiceSemantics.CanExecute(
					supportType,
					FireSupportServiceSemantics.LegacyVersion),
				$"Legacy {supportType} requests must remain compatible.");
		}

		AssertEx.False(
			FireSupportServiceSemantics.IsCargoAvailable(
				advertised: true,
				FireSupportServiceSemantics.LegacyVersion));
		AssertEx.True(
			FireSupportServiceSemantics.IsCargoAvailable(
				advertised: true,
				FireSupportServiceSemantics.CurrentVersion));
		AssertEx.False(
			FireSupportServiceSemantics.IsCargoAvailable(
				advertised: false,
				FireSupportServiceSemantics.CurrentVersion));
	}

	private static FireSupportRequestPacket CreateRequest()
	{
		var packet = new FireSupportRequestPacket(
			ESupportType.PriorityExfil,
			new Vector3(1.25f, 2.5f, 3.75f),
			new Vector3(4.25f, 5.5f, 6.75f),
			new Vector3(7.25f, 8.5f, 9.75f),
			visualSeed: 4711,
			durationSeconds: 21.5f,
			passIndex: 2,
			requesterProfileId: "profile-1",
			supportRequestId: "request-1",
			scanIntervalSeconds: 1.75f,
			rangeMeters: 250.5f,
			helicopterTimingSnapshot: new HelicopterTimingSnapshot(
				ESupportType.PriorityExfil,
				dispatchDelaySeconds: 3.5f,
				waitTimeSeconds: 37,
				extractTimeSeconds: 8.25f,
				speedMultiplier: 1.5f),
			helicopterTimingRevision: 9);
		// Simulate a legacy type-10 sender. New runtime constructors zero this
		// field for Cargo, but readers/writers must retain its positional slot.
		packet.HelicopterExtractTimeSeconds = 8.25f;
		packet.RequestOrigin = FireSupportRequestOrigin.SeasonalAmbient;
		return packet;
	}

	private static byte[] Serialize(FireSupportRequestPacket packet)
	{
		var writer = new NetDataWriter();
		packet.Serialize(writer);
		return writer.ToArray();
	}

	private static byte[] Serialize(FireSupportAuthorityResultPacket packet)
	{
		var writer = new NetDataWriter();
		packet.Serialize(writer);
		return writer.ToArray();
	}

	private static void AssertRequestEqual(
		FireSupportRequestPacket expected,
		FireSupportRequestPacket actual,
		bool compareSemantics = true,
		bool compareOrigin = true)
	{
		AssertEx.Equal(expected.SupportType, actual.SupportType);
		AssertEx.Equal(expected.Position, actual.Position);
		AssertEx.Equal(expected.Direction, actual.Direction);
		AssertEx.Equal(expected.Rotation, actual.Rotation);
		AssertEx.Equal(expected.VisualSeed, actual.VisualSeed);
		AssertEx.Near(expected.DurationSeconds, actual.DurationSeconds, 0.0001f);
		AssertEx.Near(expected.ScanIntervalSeconds, actual.ScanIntervalSeconds, 0.0001f);
		AssertEx.Near(expected.RangeMeters, actual.RangeMeters, 0.0001f);
		AssertEx.Equal(expected.PassIndex, actual.PassIndex);
		AssertEx.Equal(expected.SupportRequestId, actual.SupportRequestId);
		AssertEx.Equal(expected.RequesterProfileId, actual.RequesterProfileId);
		AssertEx.Equal(expected.HelicopterTimingRevision, actual.HelicopterTimingRevision);
		AssertEx.Near(
			expected.HelicopterDispatchDelaySeconds,
			actual.HelicopterDispatchDelaySeconds,
			0.0001f);
		AssertEx.Equal(expected.HelicopterWaitTimeSeconds, actual.HelicopterWaitTimeSeconds);
		AssertEx.Near(
			expected.SupportType == ESupportType.PriorityExfil
				? 0f
				: expected.HelicopterExtractTimeSeconds,
			actual.HelicopterExtractTimeSeconds,
			0.0001f);
		AssertEx.Near(
			expected.HelicopterSpeedMultiplier,
			actual.HelicopterSpeedMultiplier,
			0.0001f);
		if (compareSemantics)
		{
			AssertEx.Equal(
				expected.ServiceSemanticsVersion,
				actual.ServiceSemanticsVersion);
		}
		if (compareOrigin)
		{
			AssertEx.Equal(expected.RequestOrigin, actual.RequestOrigin);
		}
	}

	private static void AssertResultEqual(
		FireSupportAuthorityResultPacket expected,
		FireSupportAuthorityResultPacket actual,
		bool compareSemantics = true,
		bool compareOrigin = true)
	{
		AssertEx.Equal(expected.Accepted, actual.Accepted);
		AssertEx.Equal(expected.Reason, actual.Reason);
		AssertRequestEqual(
			expected.ToSupportRequest(),
			actual.ToSupportRequest(),
			compareSemantics,
			compareOrigin);
	}
}
