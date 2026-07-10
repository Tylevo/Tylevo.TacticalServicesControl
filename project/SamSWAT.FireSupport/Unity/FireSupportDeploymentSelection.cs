using System;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public static class FireSupportDeploymentSelection
{
	private static ESupportType s_selectedA10 = ESupportType.Strafe;
	private static ESupportType s_selectedExfil = ESupportType.Extract;
	private static ESupportType s_selectedRecon = ESupportType.Uav;

	public static void Select(ESupportType supportType)
	{
		ESupportType radialType = GetRadialSupportType(supportType);
		if (radialType == ESupportType.None)
		{
			return;
		}

		switch (radialType)
		{
			case ESupportType.Strafe:
				s_selectedA10 = supportType;
				break;
			case ESupportType.Extract:
				s_selectedExfil = supportType;
				break;
			case ESupportType.Uav:
				s_selectedRecon = supportType;
				break;
		}

		TscDiagnostics.LogPhone($"TSC YY deploy selection updated category={radialType} selected={supportType}.");
	}

	public static ESupportType ResolveRadialRequest(
		ESupportType radialSupportType,
		FireSupportServiceMappings services,
		Func<ESupportType, bool> canDeploy)
	{
		if (!IsRadialSupportType(radialSupportType))
		{
			return radialSupportType;
		}

		ESupportType selected = GetSelectedSupportType(radialSupportType);
		if (CanDeploy(selected, services, canDeploy))
		{
			return selected;
		}

		if (CanDeploy(radialSupportType, services, canDeploy))
		{
			return radialSupportType;
		}

		ESupportType variant = GetVariantSupportType(radialSupportType);
		if (variant != selected && CanDeploy(variant, services, canDeploy))
		{
			return variant;
		}

		return selected != ESupportType.None ? selected : radialSupportType;
	}

	public static bool HasDeployableRadialRequest(
		ESupportType radialSupportType,
		FireSupportServiceMappings services,
		Func<ESupportType, bool> canDeploy)
	{
		if (!IsRadialSupportType(radialSupportType))
		{
			return CanDeploy(radialSupportType, services, canDeploy);
		}

		if (CanDeploy(GetSelectedSupportType(radialSupportType), services, canDeploy))
		{
			return true;
		}

		if (CanDeploy(radialSupportType, services, canDeploy))
		{
			return true;
		}

		return CanDeploy(GetVariantSupportType(radialSupportType), services, canDeploy);
	}

	public static ESupportType GetRadialSupportType(ESupportType supportType)
	{
		return supportType switch
		{
			ESupportType.Strafe or ESupportType.DoubleStrafe => ESupportType.Strafe,
			ESupportType.Extract or ESupportType.PriorityExfil => ESupportType.Extract,
			ESupportType.Uav or ESupportType.FocusedSweep => ESupportType.Uav,
			_ => ESupportType.None
		};
	}

	private static ESupportType GetSelectedSupportType(ESupportType radialSupportType)
	{
		return radialSupportType switch
		{
			ESupportType.Strafe => s_selectedA10,
			ESupportType.Extract => s_selectedExfil,
			ESupportType.Uav => s_selectedRecon,
			_ => radialSupportType
		};
	}

	private static ESupportType GetVariantSupportType(ESupportType radialSupportType)
	{
		return radialSupportType switch
		{
			ESupportType.Strafe => ESupportType.DoubleStrafe,
			ESupportType.Extract => ESupportType.PriorityExfil,
			ESupportType.Uav => ESupportType.FocusedSweep,
			_ => ESupportType.None
		};
	}

	private static bool IsRadialSupportType(ESupportType supportType)
	{
		return supportType is ESupportType.Strafe or ESupportType.Extract or ESupportType.Uav;
	}

	private static bool CanDeploy(
		ESupportType supportType,
		FireSupportServiceMappings services,
		Func<ESupportType, bool> canDeploy)
	{
		return supportType != ESupportType.None &&
		       services != null &&
		       services.TryGetValue(supportType, out IFireSupportService service) &&
		       service.IsRequestAvailable() &&
		       canDeploy(supportType);
	}
}
