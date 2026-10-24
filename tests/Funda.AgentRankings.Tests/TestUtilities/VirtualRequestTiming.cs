using Funda.AgentRankings.Api;

namespace Funda.AgentRankings.Tests.TestUtilities;

internal sealed class VirtualRequestTiming : IRequestTiming
{
    private long _ticks;
    public List<TimeSpan> Delays { get; } = [];
    public Action? OnDelay { get; set; }
    public DateTimeOffset UtcNow => new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(_ticks);
    public long GetTimestamp() => _ticks;
    public TimeSpan GetElapsedTime(long startingTimestamp) => TimeSpan.FromTicks(_ticks - startingTimestamp);
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        OnDelay?.Invoke();
        cancellationToken.ThrowIfCancellationRequested();
        Delays.Add(delay);
        _ticks += delay.Ticks;
        return Task.CompletedTask;
    }
    public void Advance(TimeSpan elapsed) => _ticks += elapsed.Ticks;
}
