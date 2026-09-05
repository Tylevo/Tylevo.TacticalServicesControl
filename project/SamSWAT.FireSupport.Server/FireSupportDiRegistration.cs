using Microsoft.Extensions.DependencyInjection;
using SPTarkov.Server.Core.Callbacks;
using SPTarkov.Server.Core.DI;

namespace SamSWAT.FireSupport.ArysReloaded;

/// <summary>
/// Replaces SPT's stock BTR update registration with TSC's partition-aware
/// callback. SPT 4.1 removed Injectable.TypeOverride, so this must happen while
/// the service collection is still mutable.
/// </summary>
public sealed class FireSupportDiRegistration : IOnDIConstruct
{
	public static Task OnDIConstructAsync(
		IServiceCollection serviceCollection,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		ServiceDescriptor[] stockDescriptors = serviceCollection
			.Where(ReferencesStockBtrCallbacks)
			.ToArray();
		if (stockDescriptors.Length != 2)
		{
			throw new InvalidOperationException(
				$"Expected exactly two SPT BTR delivery callback registrations, but found {stockDescriptors.Length}. Refusing to start because duplicate delivery processing could corrupt profiles.");
		}

		foreach (ServiceDescriptor descriptor in stockDescriptors)
		{
			serviceCollection.Remove(descriptor);
		}

		return Task.CompletedTask;
	}

	private static bool ReferencesStockBtrCallbacks(
		ServiceDescriptor descriptor)
	{
		if (descriptor.ServiceType == typeof(BtrDeliveryCallbacks) ||
		    descriptor.ImplementationType == typeof(BtrDeliveryCallbacks) ||
		    descriptor.ImplementationInstance is BtrDeliveryCallbacks)
		{
			return true;
		}

		object? closure = descriptor.ImplementationFactory?.Target;
		return closure != null && closure.GetType()
			.GetFields(
				System.Reflection.BindingFlags.Instance |
				System.Reflection.BindingFlags.Public |
				System.Reflection.BindingFlags.NonPublic)
			.Any(field =>
				field.FieldType == typeof(Type) &&
				field.GetValue(closure) is Type implementationType &&
				implementationType == typeof(BtrDeliveryCallbacks));
	}
}
