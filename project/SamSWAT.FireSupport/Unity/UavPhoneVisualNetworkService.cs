using Comfort.Common;
using EFT;
using System;
using System.Reflection;
using System.Threading;
using UnityEngine;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public static class UavPhoneVisualNetworkService
{
	public delegate void PhoneVisualRequestedHandler(UavPhoneVisualEvent visualEvent, CancellationToken cancellationToken);

	public static event PhoneVisualRequestedHandler PhoneVisualRequested;

	public static void PublishLocal(
		ESupportType supportType,
		UavPhoneVisualPhase phase,
		float duration = 0f,
		bool success = false,
		CancellationToken cancellationToken = default)
	{
		try
		{
			Player player = Singleton<GameWorld>.Instance?.MainPlayer;
			var visualEvent = new UavPhoneVisualEvent
			{
				ProfileId = player?.ProfileId ?? string.Empty,
				AccountId = GetAccountId(player),
				SupportType = supportType,
				Phase = phase,
				StartTime = Time.time,
				Duration = duration,
				Success = success
			};

			PhoneVisualRequested?.Invoke(visualEvent, cancellationToken);
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource?.LogWarning($"UAV phone visual network publish skipped. {ex}");
		}
	}

	internal static string GetAccountId(Player player)
	{
		if (player == null)
		{
			return string.Empty;
		}

		return GetStringMember(player.Profile, "AccountId", "Aid", "AID", "Id") ??
		       GetStringMember(player.Profile?.Info, "AccountId", "Aid", "AID", "Id") ??
		       string.Empty;
	}

	private static string GetStringMember(object owner, params string[] names)
	{
		if (owner == null)
		{
			return null;
		}

		Type type = owner.GetType();
		const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
		foreach (string name in names)
		{
			PropertyInfo property = type.GetProperty(name, flags);
			object value = property?.GetValue(owner);
			if (!string.IsNullOrWhiteSpace(value?.ToString()))
			{
				return value.ToString();
			}

			FieldInfo field = type.GetField(name, flags);
			value = field?.GetValue(owner);
			if (!string.IsNullOrWhiteSpace(value?.ToString()))
			{
				return value.ToString();
			}
		}

		return null;
	}
}
