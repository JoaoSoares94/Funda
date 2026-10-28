using Microsoft.Extensions.Time.Testing;

namespace Funda.AgentRankings.Tests.TestUtilities;

internal static class FakeTimeExecution
{
    private static readonly TimeSpan CompletionTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ClockStep = TimeSpan.FromSeconds(1);

    // Only retry integration tests use this bounded driver. Timing assertions advance the clock explicitly.
    public static async Task<T> CompleteAsync<T>(Task<T> operation, FakeTimeProvider timeProvider)
    {
        var completion = operation.WaitAsync(CompletionTimeout);
        while (!completion.IsCompleted)
        {
            // Use the shared queue so asynchronous cancellation also gets a turn.
            await Task.Factory.StartNew(static () => { }, CancellationToken.None,
                TaskCreationOptions.PreferFairness, TaskScheduler.Default);
            if (!completion.IsCompleted)
            {
                timeProvider.Advance(ClockStep);
            }
        }
        return await completion;
    }
}
