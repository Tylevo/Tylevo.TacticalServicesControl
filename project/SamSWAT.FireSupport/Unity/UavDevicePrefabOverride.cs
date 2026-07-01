using EFT;
using EFT.InventoryLogic;
using System;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

internal sealed class UavDevicePrefabOverride : IDisposable
{
	private readonly ItemTemplate _template;
	private readonly ResourceKey _originalPrefab;
	private readonly ResourceKey _originalUsePrefab;
	private bool _disposed;

	private UavDevicePrefabOverride(ItemTemplate template)
	{
		_template = template;
		_originalPrefab = template.Prefab;
		_originalUsePrefab = template.UsePrefab;
	}

	public static UavDevicePrefabOverride Apply(Item item)
	{
		if (!UavDeviceConstants.IsUavDevice(item) || item?.Template == null)
		{
			return null;
		}

		var prefabOverride = new UavDevicePrefabOverride(item.Template);
		try
		{
			ResourceKey containerPrefab = item.Template.UsePrefab;
			if (string.IsNullOrEmpty(containerPrefab.FileName))
			{
				containerPrefab = item.Template.Prefab;
			}

			if (string.IsNullOrEmpty(containerPrefab.FileName) ||
			    containerPrefab.FileName == UavDeviceConstants.UavDeviceLootBundlePath)
			{
				containerPrefab.path = UavDeviceConstants.UavDeviceContainerBundlePath;
				containerPrefab.rcid = string.Empty;
			}

			item.Template.Prefab = containerPrefab;
			item.Template.UsePrefab = containerPrefab;

			TscDiagnostics.LogPhone(
				$"TSC Uplink prefab override applied. tpl={item.StringTemplateId}, prefab={item.Template.Prefab.FileName}, usePrefab={item.Template.UsePrefab.FileName}.");
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource.LogWarning($"TSC Uplink prefab override failed. {ex}");
		}

		return prefabOverride;
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		try
		{
			if (_template == null)
			{
				return;
			}

			_template.Prefab = _originalPrefab;
			_template.UsePrefab = _originalUsePrefab;
			TscDiagnostics.LogPhone(
				$"TSC Uplink prefab override restored. prefab={_template.Prefab.FileName}, usePrefab={_template.UsePrefab.FileName}.");
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource.LogWarning($"TSC Uplink prefab override restore failed. {ex}");
		}
	}
}
