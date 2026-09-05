using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

internal static class AssertEx
{
	public static void True(bool condition, string? message = null)
	{
		if (!condition)
		{
			throw new RegressionAssertionException(message ?? "Expected true, but was false.");
		}
	}

	public static void False(bool condition, string? message = null)
	{
		if (condition)
		{
			throw new RegressionAssertionException(message ?? "Expected false, but was true.");
		}
	}

	public static void Equal<T>(T expected, T actual, string? message = null)
	{
		if (!EqualityComparer<T>.Default.Equals(expected, actual))
		{
			throw new RegressionAssertionException(
				message ?? $"Expected <{expected}>, but was <{actual}>.");
		}
	}

	public static void NotEqual<T>(T unexpected, T actual, string? message = null)
	{
		if (EqualityComparer<T>.Default.Equals(unexpected, actual))
		{
			throw new RegressionAssertionException(
				message ?? $"Did not expect <{actual}>.");
		}
	}

	public static void Null(object? value, string? message = null)
	{
		if (value != null)
		{
			throw new RegressionAssertionException(message ?? $"Expected null, but was <{value}>.");
		}
	}

	public static T NotNull<T>(T? value, string? message = null)
		where T : class
	{
		if (value == null)
		{
			throw new RegressionAssertionException(message ?? "Expected a non-null value.");
		}

		return value;
	}

	public static void Near(double expected, double actual, double tolerance, string? message = null)
	{
		if (double.IsNaN(expected) != double.IsNaN(actual) ||
		    !double.IsNaN(expected) && Math.Abs(expected - actual) > Math.Abs(tolerance))
		{
			throw new RegressionAssertionException(
				message ??
				$"Expected <{expected}> +/- <{Math.Abs(tolerance)}>, but was <{actual}>.");
		}
	}

	public static void Near(float expected, float actual, float tolerance, string? message = null)
	{
		Near((double)expected, actual, tolerance, message);
	}

	public static TException Throws<TException>(Action action, string? message = null)
		where TException : Exception
	{
		try
		{
			action();
		}
		catch (TException exception)
		{
			return exception;
		}
		catch (Exception exception)
		{
			throw new RegressionAssertionException(
				message ??
				$"Expected {typeof(TException).Name}, but caught {exception.GetType().Name}.",
				exception);
		}

		throw new RegressionAssertionException(
			message ?? $"Expected {typeof(TException).Name}, but no exception was thrown.");
	}

	public static async Task<TException> ThrowsAsync<TException>(
		Func<Task> action,
		string? message = null)
		where TException : Exception
	{
		try
		{
			await action();
		}
		catch (TException exception)
		{
			return exception;
		}
		catch (Exception exception)
		{
			throw new RegressionAssertionException(
				message ??
				$"Expected {typeof(TException).Name}, but caught {exception.GetType().Name}.",
				exception);
		}

		throw new RegressionAssertionException(
			message ?? $"Expected {typeof(TException).Name}, but no exception was thrown.");
	}

	public static void Contains(string expectedSubstring, string? actual, string? message = null)
	{
		if (actual == null ||
		    actual.IndexOf(expectedSubstring, StringComparison.Ordinal) < 0)
		{
			throw new RegressionAssertionException(
				message ?? $"Expected <{actual ?? "<null>"}> to contain <{expectedSubstring}>.");
		}
	}

	public static void Contains<T>(T expected, IEnumerable<T> actual, string? message = null)
	{
		if (actual == null || !actual.Contains(expected))
		{
			throw new RegressionAssertionException(
				message ?? $"Expected sequence to contain <{expected}>.");
		}
	}

	public static void SequenceEqual<T>(
		IEnumerable<T> expected,
		IEnumerable<T> actual,
		string? message = null)
	{
		if (expected == null || actual == null || !expected.SequenceEqual(actual))
		{
			throw new RegressionAssertionException(message ?? "Sequences were not equal.");
		}
	}
}

internal sealed class RegressionAssertionException : Exception
{
	public RegressionAssertionException(string message)
		: base(message)
	{
	}

	public RegressionAssertionException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
