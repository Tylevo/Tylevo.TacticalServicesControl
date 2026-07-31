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
	public sealed class Item
	{
		public string Id { get; set; } = string.Empty;
		public string Template { get; set; } = string.Empty;
		public string? ParentId { get; set; }
		public string? SlotId { get; set; }
		public Upd? Upd { get; set; }
	}

	public sealed class Upd
	{
		public double StackObjectsCount { get; set; } = 1d;
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
	}

	public sealed class BotBaseInventory
	{
		public List<Item>? Items { get; set; } = new();
		public MongoId? Stash { get; set; }
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
	}
}

namespace SPTarkov.Server.Core.Servers
{
	using SPTarkov.Server.Core.Models.Common;

	public class SaveServer
	{
		public Func<MongoId, Task>? SaveProfile { get; set; }

		public virtual Task SaveProfileAsync(MongoId sessionId)
		{
			return SaveProfile?.Invoke(sessionId) ??
			       Task.CompletedTask;
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
