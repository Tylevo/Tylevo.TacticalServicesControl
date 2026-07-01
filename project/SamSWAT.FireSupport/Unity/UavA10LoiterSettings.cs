using UnityEngine;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public static class UavA10LoiterSettings
{
	public static bool IsEnabled()
	{
		return PluginSettings.UavA10LoiterEnabled.Value;
	}

	public static UavA10LoiterRequest CreateConfiguredRequest(Vector3 center, float durationSeconds)
	{
		UavLoiterAircraftType aircraftType = UavLoiterAircraftType.A10;
		float radius = PluginSettings.UavA10LoiterRadius.Value * Random.Range(0.9f, 1.1f);
		float altitude = PluginSettings.UavA10LoiterAltitude.Value * Random.Range(0.92f, 1.08f);
		float orbitPeriod = PluginSettings.UavA10LoiterOrbitPeriod.Value * Random.Range(0.9f, 1.1f);
		float ingressDistance = PluginSettings.UavA10LoiterIngressDistance.Value * Random.Range(0.85f, 1.15f);

		return new UavA10LoiterRequest(
			aircraftType,
			center,
			durationSeconds,
			radius,
			altitude,
			orbitPeriod,
			PluginSettings.UavA10LoiterIngressDuration.Value,
			ingressDistance,
			PluginSettings.UavA10LoiterEngineVolume.Value,
			new Vector3(
				PluginSettings.UavA10LoiterModelPitchOffset.Value,
				PluginSettings.UavA10LoiterModelYawOffset.Value,
				PluginSettings.UavA10LoiterModelRollOffset.Value),
			Random.Range(0f, Mathf.PI * 2f),
			Random.value < 0.5f ? -1 : 1,
			Time.time);
	}

	public static UavA10LoiterRequest ApplyHostAuthority(UavA10LoiterRequest request)
	{
		UavA10LoiterRequest configured = CreateConfiguredRequest(request.Center, Mathf.Max(1f, request.DurationSeconds));
		configured.AircraftType = UavLoiterAircraftType.A10;
		configured.StartAngle = request.StartAngle;
		configured.Direction = request.Direction >= 0 ? 1 : -1;
		configured.StartTime = Time.time;
		return configured;
	}
}
