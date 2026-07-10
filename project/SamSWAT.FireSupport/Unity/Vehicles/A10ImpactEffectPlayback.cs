using Comfort.Common;
using System;
using System.Collections;
using Systems.Effects;
using UnityEngine;
using UnityEngine.Rendering;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public static class A10ImpactEffectPlayback
{
	private const string DefaultEffectName = "big_smoky_explosion";
	private const float ImpactRaycastPadding = 2f;
	private const float EndpointProbeHeight = 6f;
	private const float EndpointProbeDistance = 14f;
	private const float EndpointProbeRadius = 1.5f;
	private const float SoloGau8ExplosionVolume = 1f;
	private const string NamedEffectsFieldName = "dictionary_1";
	private static bool s_failureLogged;
	private static bool s_soloImpactPathLogged;
	private static bool s_soloImpactUnavailableLogged;
	private static bool s_fallbackImpactMaterialUnavailableLogged;
	private static Material s_fallbackImpactMaterial;

	public static bool TrySpawn(A10TracerSegment segment, string effectName = DefaultEffectName)
	{
		if (!segment.IsValid)
		{
			return false;
		}

		try
		{
			if (TrySpawnSoloImpact(segment, effectName))
			{
				return true;
			}

			SpawnBuiltInImpact(segment.TracerEnd);
			return true;
		}
		catch (Exception ex)
		{
			LogFailureOnce(effectName, ex);
			return false;
		}
	}

	public static bool TrySpawn(Vector3 position, string effectName = DefaultEffectName)
	{
		try
		{
			SpawnBuiltInImpact(position);
			return true;
		}
		catch (Exception ex)
		{
			LogFailureOnce(effectName, ex);
			return false;
		}
	}

	private static bool TrySpawnSoloImpact(A10TracerSegment segment, string effectName)
	{
		if (!Singleton<Effects>.Instantiated)
		{
			LogSoloImpactUnavailableOnce("effects-singleton-missing");
			return false;
		}

		Effects effects = Singleton<Effects>.Instance;
		if (effects == null)
		{
			LogSoloImpactUnavailableOnce("effects-instance-missing");
			return false;
		}

		if (!HasNamedEffect(effects, effectName))
		{
			LogSoloImpactUnavailableOnce($"named-effect-missing effect={effectName}");
			return false;
		}

		if (!TryResolveImpactHit(segment, out RaycastHit hit))
		{
			LogSoloImpactUnavailableOnce("impact-raycast-missed");
			return false;
		}

		effects.EmitGrenade(effectName, hit.point, hit.normal, SoloGau8ExplosionVolume);
		LogSoloImpactPathOnce(effectName);
		return true;
	}

	private static bool TryResolveImpactHit(A10TracerSegment segment, out RaycastHit hit)
	{
		Vector3 direction = segment.ProjectileDirection.normalized;
		Vector3 endpointProbeStart = segment.TracerEnd + Vector3.up * EndpointProbeHeight;
		if (Physics.Raycast(endpointProbeStart, Vector3.down, out hit, EndpointProbeDistance, ~0, QueryTriggerInteraction.Ignore))
		{
			return true;
		}

		if (Physics.SphereCast(endpointProbeStart, EndpointProbeRadius, Vector3.down, out hit, EndpointProbeDistance, ~0, QueryTriggerInteraction.Ignore))
		{
			return true;
		}

		float distance = Vector3.Distance(segment.ProjectileOrigin, segment.TracerEnd) + ImpactRaycastPadding;
		if (Physics.Raycast(segment.ProjectileOrigin, direction, out hit, distance, ~0, QueryTriggerInteraction.Ignore))
		{
			return true;
		}

		Vector3 probeStart = segment.TracerEnd - direction * ImpactRaycastPadding;
		return Physics.Raycast(probeStart, direction, out hit, ImpactRaycastPadding * 2f, ~0, QueryTriggerInteraction.Ignore);
	}

	private static bool HasNamedEffect(Effects effects, string effectName)
	{
		IDictionary namedEffects = TryGetNamedEffects(effects);
		if (namedEffects != null)
		{
			foreach (object key in namedEffects.Keys)
			{
				if (key is string name &&
				    string.Equals(name, effectName, StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}

			return false;
		}

		if (effects.EffectsArray == null)
		{
			return true;
		}

		foreach (Effects.Effect effect in effects.EffectsArray)
		{
			if (effect != null && string.Equals(effect.Name, effectName, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}

	private static IDictionary TryGetNamedEffects(Effects effects)
	{
		try
		{
			return effects.GetType()
				.GetField(NamedEffectsFieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
				?.GetValue(effects) as IDictionary;
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource?.LogDebug(
				$"TSC A-10 replay impact named effect lookup failed. {ex.GetType().Name}: {ex.Message}");
			return null;
		}
	}

	private static void SpawnBuiltInImpact(Vector3 position)
	{
		Material fallbackMaterial = GetFallbackImpactMaterial();
		if (fallbackMaterial == null)
		{
			return;
		}

		GameObject root = new GameObject("TSC A-10 impact effect");
		root.transform.position = position;

		ParticleSystem smoke = root.AddComponent<ParticleSystem>();
		ParticleSystem.MainModule main = smoke.main;
		main.duration = 1.05f;
		main.loop = false;
		main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 1.05f);
		main.startSpeed = new ParticleSystem.MinMaxCurve(1.8f, 5.5f);
		main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.95f);
		main.startColor = new ParticleSystem.MinMaxGradient(
			new Color(0.22f, 0.22f, 0.21f, 0.46f),
			new Color(0.05f, 0.05f, 0.05f, 0.22f));
		main.gravityModifier = 0.03f;
		main.simulationSpace = ParticleSystemSimulationSpace.World;

		ParticleSystem.EmissionModule emission = smoke.emission;
		emission.rateOverTime = 0f;
		ParticleSystem.ShapeModule shape = smoke.shape;
		shape.shapeType = ParticleSystemShapeType.Hemisphere;
		shape.radius = 0.18f;

		ParticleSystemRenderer renderer = root.GetComponent<ParticleSystemRenderer>();
		if (renderer != null)
		{
			renderer.renderMode = ParticleSystemRenderMode.Billboard;
			renderer.sharedMaterial = fallbackMaterial;
			renderer.shadowCastingMode = ShadowCastingMode.Off;
			renderer.receiveShadows = false;
		}

		smoke.Emit(12);
		UnityEngine.Object.Destroy(root, 2f);
	}

	private static Material GetFallbackImpactMaterial()
	{
		if (s_fallbackImpactMaterial != null)
		{
			return s_fallbackImpactMaterial;
		}

		Shader shader = FindSupportedShader(
			"Particles/Standard Unlit",
			"Legacy Shaders/Particles/Alpha Blended",
			"Sprites/Default",
			"Unlit/Transparent",
			"Unlit/Color",
			"Hidden/Internal-Colored");
		if (shader == null)
		{
			LogFallbackMaterialUnavailableOnce();
			return null;
		}

		s_fallbackImpactMaterial = new Material(shader)
		{
			name = "TSC A-10 fallback impact material",
			color = new Color(0.17f, 0.17f, 0.16f, 0.42f)
		};
		ConfigureTransparentMaterial(s_fallbackImpactMaterial);
		SetMaterialColor(s_fallbackImpactMaterial, new Color(0.17f, 0.17f, 0.16f, 0.42f));
		return s_fallbackImpactMaterial;
	}

	private static Shader FindSupportedShader(params string[] shaderNames)
	{
		foreach (string shaderName in shaderNames)
		{
			Shader shader = Shader.Find(shaderName);
			if (shader != null && shader.isSupported)
			{
				return shader;
			}
		}

		return null;
	}

	private static void ConfigureTransparentMaterial(Material material)
	{
		if (material == null)
		{
			return;
		}

		SetMaterialFloat(material, "_SrcBlend", (float)BlendMode.SrcAlpha);
		SetMaterialFloat(material, "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
		SetMaterialFloat(material, "_Cull", (float)CullMode.Off);
		SetMaterialFloat(material, "_ZWrite", 0f);
		material.renderQueue = 3000;
	}

	private static void SetMaterialFloat(Material material, string propertyName, float value)
	{
		if (material.HasProperty(propertyName))
		{
			material.SetFloat(propertyName, value);
		}
	}

	private static void SetMaterialColor(Material material, Color color)
	{
		if (material.HasProperty("_Color"))
		{
			material.SetColor("_Color", color);
		}

		if (material.HasProperty("_TintColor"))
		{
			material.SetColor("_TintColor", color);
		}
	}

	private static void LogFailureOnce(string effectName, Exception exception)
	{
		if (s_failureLogged)
		{
			return;
		}

		s_failureLogged = true;
		FireSupportPlugin.LogSource?.LogWarning(
			$"TSC A-10 impact effect spawn failed; visual effect skipped. effect={effectName} exception={exception.GetType().Name}: {exception.Message}");
	}

	private static void LogSoloImpactPathOnce(string effectName)
	{
		if (s_soloImpactPathLogged)
		{
			return;
		}

		s_soloImpactPathLogged = true;
		FireSupportPlugin.LogSource?.LogInfo(
			$"TSC A-10 replay impact effects are using solo GAU-8 explosion effect. effect={effectName}");
	}

	private static void LogSoloImpactUnavailableOnce(string reason)
	{
		if (s_soloImpactUnavailableLogged)
		{
			return;
		}

		s_soloImpactUnavailableLogged = true;
		FireSupportPlugin.LogSource?.LogWarning(
			$"TSC A-10 replay impact effects could not use solo GAU-8 explosion effect; using compact gray fallback impact particles. reason={reason}");
	}

	private static void LogFallbackMaterialUnavailableOnce()
	{
		if (s_fallbackImpactMaterialUnavailableLogged)
		{
			return;
		}

		s_fallbackImpactMaterialUnavailableLogged = true;
		FireSupportPlugin.LogSource?.LogWarning(
			"TSC A-10 fallback impact particles skipped because no supported fallback particle shader was available.");
	}
}
