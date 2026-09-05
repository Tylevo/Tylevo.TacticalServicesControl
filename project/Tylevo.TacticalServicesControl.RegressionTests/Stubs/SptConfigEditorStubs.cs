// Public registration surface verified against SPTarkov.Server.Web 4.1.4.
// The production provider is compiled separately against the installed assembly.
namespace SPTarkov.Server.Web.Models.Configs
{
	public sealed record ConfigEditorConfigRegistration
	{
		public required string Id { get; init; }
		public required string DisplayName { get; init; }
		public required object RuntimeConfig { get; init; }
		public Type? RuntimeType { get; init; }
		public string? FilePath { get; init; }
		public string? FileName { get; init; }
		public IReadOnlySet<string> IgnoredSectionPaths { get; init; } = new HashSet<string>();
		public Func<CancellationToken, ValueTask<object?>>? LoadFromDiskAsync { get; init; }
		public Func<object, CancellationToken, ValueTask>? SaveToDiskAsync { get; init; }
		public Func<object, CancellationToken, ValueTask>? ApplyToRuntimeAsync { get; init; }
		public Func<object, CancellationToken, ValueTask>? OnAppliedToRuntimeAsync { get; init; }
	}
}

namespace SPTarkov.Server.Web.Services
{
	using SPTarkov.Server.Web.Models.Configs;

	public interface IConfigEditorConfigProvider
	{
		IEnumerable<ConfigEditorConfigRegistration> GetConfigs();
	}
}
