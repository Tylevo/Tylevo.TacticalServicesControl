using System.Reflection;

internal static class Program
{
	private static async Task<int> Main()
	{
		MethodInfo[] tests = Assembly.GetExecutingAssembly()
			.GetTypes()
			.SelectMany(type => type.GetMethods(
				BindingFlags.Static |
				BindingFlags.Public |
				BindingFlags.NonPublic))
			.Where(method => method.GetCustomAttribute<RegressionTestAttribute>() != null)
			.OrderBy(method => method.DeclaringType?.FullName, StringComparer.Ordinal)
			.ThenBy(method => method.Name, StringComparer.Ordinal)
			.ToArray();

		if (tests.Length == 0)
		{
			Console.Error.WriteLine("No regression tests were discovered.");
			return 2;
		}

		int failed = 0;
		foreach (MethodInfo test in tests)
		{
			string name = $"{test.DeclaringType?.Name}.{test.Name}";
			try
			{
				ValidateSignature(test);
				object? result = test.Invoke(null, null);
				switch (result)
				{
					case Task task:
						await task;
						break;
					case ValueTask valueTask:
						await valueTask;
						break;
				}

				Console.WriteLine($"PASS {name}");
			}
			catch (Exception exception)
			{
				failed++;
				Exception failure = Unwrap(exception);
				Console.Error.WriteLine($"FAIL {name}");
				Console.Error.WriteLine($"     {failure.GetType().Name}: {failure.Message}");
				if (failure is not RegressionAssertionException)
				{
					Console.Error.WriteLine(failure.StackTrace);
				}
			}
		}

		Console.WriteLine();
		Console.WriteLine(
			$"Regression tests: {tests.Length - failed} passed, {failed} failed, {tests.Length} total.");
		return failed == 0 ? 0 : 1;
	}

	private static void ValidateSignature(MethodInfo method)
	{
		if (!method.IsStatic || method.GetParameters().Length != 0)
		{
			throw new InvalidOperationException(
				"[RegressionTest] methods must be static and parameterless.");
		}

		Type returnType = method.ReturnType;
		if (returnType != typeof(void) &&
		    returnType != typeof(Task) &&
		    returnType != typeof(ValueTask))
		{
			throw new InvalidOperationException(
				"[RegressionTest] methods must return void, Task, or ValueTask.");
		}
	}

	private static Exception Unwrap(Exception exception)
	{
		while (exception is TargetInvocationException { InnerException: not null } ||
		       exception is AggregateException { InnerExceptions.Count: 1 })
		{
			exception = exception.InnerException!;
		}

		return exception;
	}
}
