namespace SamSWAT.FireSupport.ArysReloaded
{
	internal static class FireSupportUh60DeliveryService
	{
		public const string MessengerTraderId = "66f51f3a0000000000000a60";
	}
}

namespace SPTarkov.Server.Core.Models.Eft.Common.Tables
{
	using SPTarkov.Server.Core.Models.Common;

	public sealed class Trader
	{
		public TraderBase? Base { get; set; }
	}

	public sealed class TraderBase
	{
		public MongoId Id { get; set; }
		public string? Name { get; set; }
		public string? Nickname { get; set; }
		public string? Location { get; set; }
		public bool? UnlockedByDefault { get; set; }
	}

	public sealed class TraderInfo
	{
		public bool? Unlocked { get; set; }
		public bool? Disabled { get; set; }
		public int? LoyaltyLevel { get; set; }
		public double? Standing { get; set; }
		public double? SalesSum { get; set; }
		public long? NextResupply { get; set; }
	}
}

namespace SPTarkov.Server.Core.Models.Spt.Tables
{
	using SPTarkov.Server.Core.Models.Common;
	using SPTarkov.Server.Core.Models.Eft.Common.Tables;

	public sealed class TradersTable : Dictionary<MongoId, Trader>
	{
		public Trader? GetTrader(MongoId traderId) => this.GetValueOrDefault(traderId);
	}
}
