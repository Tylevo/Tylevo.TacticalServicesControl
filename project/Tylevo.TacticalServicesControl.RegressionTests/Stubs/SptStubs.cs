using System;

namespace SPTarkov.DI.Annotations
{
	internal enum InjectionType
	{
		Singleton
	}

	[AttributeUsage(AttributeTargets.Class)]
	internal sealed class InjectableAttribute : Attribute
	{
		public int TypePriority { get; set; }

		public InjectableAttribute()
		{
		}

		public InjectableAttribute(InjectionType injectionType)
		{
		}
	}
}

namespace SPTarkov.Server.Core.Models.Utils
{
	public interface ISptLogger<T>
	{
		void Success(string message)
		{
		}

		void Warning(string message);
		void Error(string message);
		void Error(string message, Exception exception);
	}
}

namespace SPTarkov.Server.Core.DI
{
	public static class OnLoadOrder
	{
		public const int SaveCallbacks = 600000;
		public const int PostLoad = 1000000;
	}

	public interface IOnLoad
	{
		Task OnLoadAsync(CancellationToken cancellationToken);
	}
}

namespace SPTarkov.Common.Models.Logging
{
	public interface ISptLogger<T>
	{
		void Success(string message);
		void Warning(string message);
		void Error(string message);
		void Error(string message, Exception exception);
	}
}

namespace SPTarkov.Server.Core.Models.Common
{
	public readonly struct MongoId : IEquatable<MongoId>
	{
		private readonly string? _value;

		public MongoId(string value)
		{
			_value = value;
		}

		public bool IsEmpty => string.IsNullOrWhiteSpace(_value);

		public override string ToString()
		{
			return _value ?? string.Empty;
		}

		public bool Equals(MongoId other)
		{
			return string.Equals(
				_value,
				other._value,
				StringComparison.OrdinalIgnoreCase);
		}

		public override bool Equals(object? obj)
		{
			return obj is MongoId other && Equals(other);
		}

		public override int GetHashCode()
		{
			return StringComparer.OrdinalIgnoreCase.GetHashCode(
				_value ?? string.Empty);
		}
	}
}

namespace SPTarkov.Server.Core.Models.Eft.Common.Tables
{
	using SPTarkov.Server.Core.Models.Common;

	public sealed class Item
	{
		public string Id { get; set; } = string.Empty;
		public string Template { get; set; } = string.Empty;
		public string? ParentId { get; set; }
		public string? SlotId { get; set; }
		public object? Location { get; set; }
		public Upd? Upd { get; set; }
	}

	public sealed class Upd
	{
		public double? StackObjectsCount { get; set; } = 1d;
		public bool? SpawnedInSession { get; set; }
	}
}

namespace SPTarkov.Server.Core.Models.Eft.Common
{
	using SPTarkov.Server.Core.Models.Common;
	using SPTarkov.Server.Core.Models.Eft.Common.Tables;

	public sealed class PmcData
	{
		public MongoId? Id { get; set; }
		public MongoId? SessionId { get; set; }
		public BotBaseInventory? Inventory { get; set; }
		public Dictionary<MongoId, TraderInfo>? TradersInfo { get; set; }
		public List<QuestStatus>? Quests { get; set; }
	}
}

namespace SPTarkov.Server.Core.Models.Enums
{
	public enum QuestStatusEnum
	{
		Locked = 0, AvailableForStart = 1, Started = 2, AvailableForFinish = 3,
		Success = 4, Fail = 5, FailRestartable = 6, MarkedAsFailed = 7, Expired = 8, AvailableAfter = 9
	}
}

namespace SPTarkov.Server.Core.Models.Eft.Common.Tables
{
	using SPTarkov.Server.Core.Models.Common;
	using SPTarkov.Server.Core.Models.Enums;

	public sealed class QuestStatus
	{
		public required MongoId QId { get; set; }
		public required double StartTime { get; set; }
		public required QuestStatusEnum Status { get; set; }
		public required Dictionary<QuestStatusEnum, double> StatusTimers { get; set; }
	}

	public sealed class BotBaseInventory
	{
		public List<Item>? Items { get; set; } = new();
		public MongoId? Equipment { get; set; }
		public MongoId? Stash { get; set; }
	}

	public sealed class TemplateItem
	{
		public MongoId Id { get; set; }
		public TemplateItemProperties? Properties { get; set; }
	}

	public sealed class TemplateItemProperties
	{
		public IEnumerable<Slot>? Slots { get; set; }
	}

	public sealed class Slot
	{
		public string? Name { get; set; }
		public MongoId? Id { get; set; }
		public MongoId? Parent { get; set; }
		public SlotProperties? Properties { get; set; }
		public bool? Required { get; set; }
		public bool? MergeSlotWithChildren { get; set; }
		public string? Prototype { get; set; }
	}

	public sealed class SlotProperties
	{
		public IEnumerable<SlotFilter>? Filters { get; set; }
	}

	public sealed class SlotFilter
	{
		public bool? Locked { get; set; }
		public HashSet<MongoId>? Filter { get; set; }
	}
}

namespace SPTarkov.Server.Core.Models.Spt.Tables
{
	using SPTarkov.Server.Core.Models.Common;
	using SPTarkov.Server.Core.Models.Eft.Common.Tables;

	public sealed class TemplateTable
	{
		public Dictionary<MongoId, TemplateItem> Items { get; set; } = new();
	}
}

namespace SPTarkov.Server.Core.Models.Eft.Profile
{
	using SPTarkov.Server.Core.Models.Eft.Common;

	public sealed class SptProfile
	{
		public Characters? CharacterData { get; set; }
	}

	public sealed class Characters
	{
		public PmcData? PmcData { get; set; }
	}
}

namespace SPTarkov.Server.Core.Helpers
{
	using SPTarkov.Server.Core.Models.Common;
	using SPTarkov.Server.Core.Models.Eft.Common;

	public class ProfileHelper
	{
		public Func<MongoId, PmcData?>? ResolvePmcProfile { get; set; }

		public virtual PmcData? GetPmcProfile(MongoId sessionId)
		{
			return ResolvePmcProfile?.Invoke(sessionId);
		}

		public virtual PmcData? GetProfileByPmcId(MongoId profileId)
		{
			return ResolvePmcProfile?.Invoke(profileId);
		}
	}
}

namespace SPTarkov.Server.Core.Servers
{
	using SPTarkov.Server.Core.Models.Common;

	public class SaveServer
	{
		public Func<MongoId, Task>? SaveProfile { get; set; }
		public Dictionary<MongoId, SPTarkov.Server.Core.Models.Eft.Profile.SptProfile> Profiles { get; } = new();

		public virtual Dictionary<MongoId, SPTarkov.Server.Core.Models.Eft.Profile.SptProfile> GetProfiles()
		{
			return Profiles;
		}

		public virtual Task SaveProfileAsync(MongoId sessionId)
		{
			return SaveProfile?.Invoke(sessionId) ??
			       Task.CompletedTask;
		}

		public virtual Task<long> SaveProfileAsync(
			MongoId sessionId,
			CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			return SaveAndReturnAsync(sessionId);
		}

		private async Task<long> SaveAndReturnAsync(MongoId sessionId)
		{
			await SaveProfileAsync(sessionId);
			return 0;
		}
	}
}

namespace SPTarkov.Server.Core.Utils.Cloners
{
	public interface ICloner
	{
		T? Clone<T>(T? value);
	}
}
