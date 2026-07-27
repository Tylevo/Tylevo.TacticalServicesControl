using System;

namespace SPTarkov.DI.Annotations
{
	[AttributeUsage(AttributeTargets.Class)]
	internal sealed class InjectableAttribute : Attribute
	{
	}
}

namespace SPTarkov.Server.Core.Models.Utils
{
	public interface ISptLogger<T>
	{
		void Warning(string message);
		void Error(string message);
		void Error(string message, Exception exception);
	}
}
