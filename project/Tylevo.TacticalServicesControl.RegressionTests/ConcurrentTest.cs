internal static class ConcurrentTest
{
	private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);

	public static async Task<TResult[]> RunTogether<TResult>(
		params Func<TResult>[] actions)
	{
		if (actions == null || actions.Length < 2)
		{
			throw new ArgumentException(
				"At least two concurrent actions are required.",
				nameof(actions));
		}

		int readyCount = 0;
		var allReady = new TaskCompletionSource<bool>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource<bool>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		Task<TResult>[] tasks = actions
			.Select(action => Task.Run(
				async () =>
				{
					if (Interlocked.Increment(ref readyCount) == actions.Length)
					{
						allReady.TrySetResult(true);
					}

					await release.Task;
					return action();
				}))
			.ToArray();

		try
		{
			await allReady.Task.WaitAsync(s_timeout);
		}
		catch
		{
			release.TrySetResult(true);
			throw;
		}

		release.TrySetResult(true);
		return await Task.WhenAll(tasks).WaitAsync(s_timeout);
	}
}
