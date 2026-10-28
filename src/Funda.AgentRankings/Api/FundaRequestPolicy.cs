using System.Net;
using Funda.AgentRankings.Configuration;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.Timeout;

namespace Funda.AgentRankings.Api;

/// <summary>Share one instance across searches using the same API request budget.</summary>
public sealed class FundaRequestPolicy
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _requestInterval;
    private readonly ResiliencePipeline<HttpResponseMessage> _retryPipeline;
    private readonly ResiliencePipeline<HttpResponseMessage> _attemptPipeline;
    private long? _lastAttemptStarted;

    public FundaRequestPolicy(FundaOptions options, TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        _requestInterval = options.RequestInterval;
        _attemptPipeline = new ResiliencePipelineBuilder<HttpResponseMessage> { TimeProvider = timeProvider }
            .AddTimeout(options.RequestTimeout)
            .Build();

        var retryBuilder = new ResiliencePipelineBuilder<HttpResponseMessage> { TimeProvider = timeProvider };
        if (options.MaxRetries > 0)
        {
            retryBuilder.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = options.MaxRetries,
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = args => ValueTask.FromResult(
                    !args.Context.CancellationToken.IsCancellationRequested &&
                    (IsTransient(args.Outcome.Result?.StatusCode) ||
                     args.Outcome.Exception is HttpRequestException or IOException or TimeoutRejectedException or OperationCanceledException)),
                // The package's HTTP-date parser uses system time; use our injected clock instead.
                DelayGenerator = args => ValueTask.FromResult(GetServerDelay(args.Outcome.Result)),
                OnRetry = args =>
                {
                    args.Outcome.Result?.Dispose();
                    return default;
                }
            });
        }
        _retryPipeline = retryBuilder.Build();
    }

    public async Task<HttpResponseMessage> ExecuteAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> sendAttempt,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await _retryPipeline.ExecuteAsync(async token =>
            {
                token.ThrowIfCancellationRequested();
                if (_lastAttemptStarted is { } previous)
                {
                    var delay = _requestInterval - _timeProvider.GetElapsedTime(previous);
                    if (delay > TimeSpan.Zero)
                    {
                        await Task.Delay(delay, _timeProvider, token);
                    }
                }

                _lastAttemptStarted = _timeProvider.GetTimestamp();
                // Only the buffered HTTP attempt is timed, excluding pacing and retry backoff.
                return await _attemptPipeline.ExecuteAsync(
                    attemptToken => new ValueTask<HttpResponseMessage>(sendAttempt(attemptToken)), token);
            }, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private TimeSpan? GetServerDelay(HttpResponseMessage? response)
    {
        var header = response?.Headers.RetryAfter;
        if (header?.Delta is { } delta)
        {
            return delta;
        }
        if (header?.Date is { } date)
        {
            var delay = date - _timeProvider.GetUtcNow();
            return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
        }
        return null; // Let the package calculate exponential backoff and jitter.
    }

    private static bool IsTransient(HttpStatusCode? status) =>
        status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests or
            HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or
            HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
}
