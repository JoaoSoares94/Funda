namespace Funda.AgentRankings.Api;

public sealed class SystemRequestTiming : IRequestTiming
{
    public static SystemRequestTiming Instance { get; } = new();
    private SystemRequestTiming() { }

    public DateTimeOffset UtcNow => TimeProvider.System.GetUtcNow();
    public long GetTimestamp() => TimeProvider.System.GetTimestamp();
    public TimeSpan GetElapsedTime(long startingTimestamp) =>
        TimeProvider.System.GetElapsedTime(startingTimestamp);
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}
