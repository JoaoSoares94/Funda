namespace Funda.AgentRankings.Api;

/// <summary>Monotonic pacing and cancellable delays, with wall time only for HTTP dates.</summary>
public interface IRequestTiming
{
    DateTimeOffset UtcNow { get; }
    long GetTimestamp();
    TimeSpan GetElapsedTime(long startingTimestamp);
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}
