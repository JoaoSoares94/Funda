using System.Net;
using Funda.AgentRankings.Configuration;

namespace Funda.AgentRankings.Api;

/// <summary>Share one instance across searches using the same API request budget.</summary>
public sealed class FundaRequestPolicy(
    FundaOptions options, IRequestTiming timing, Func<double> nextJitter)
{
    // Async waits never create the semaphore's optional kernel WaitHandle.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long? _lastAttemptStarted;

    public async Task<HttpResponseMessage> ExecuteAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> sendAttempt,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            for (var retry = 0; ; retry++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_lastAttemptStarted is { } previous)
                {
                    await DelayAsync(options.RequestInterval - timing.GetElapsedTime(previous), cancellationToken);
                }

                _lastAttemptStarted = timing.GetTimestamp();
                TimeSpan retryDelay;
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeout.CancelAfter(options.RequestTimeout);
                    try
                    {
                        var response = await sendAttempt(timeout.Token);
                        if (!IsTransient(response.StatusCode) || retry >= options.MaxRetries)
                        {
                            return response;
                        }

                        // Dispose failed responses before waiting; do not retain bodies across retries.
                        using (response)
                        {
                            retryDelay = GetRetryDelay(retry, response);
                        }
                    }
                    catch (Exception exception) when (!cancellationToken.IsCancellationRequested &&
                        retry < options.MaxRetries && IsTransient(exception))
                    {
                        retryDelay = GetRetryDelay(retry);
                    }
                }

                // The shared gate also prevents another search bypassing a server cooldown.
                await DelayAsync(retryDelay, cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private TimeSpan GetRetryDelay(int retry, HttpResponseMessage? response = null)
    {
        var jitter = Math.Clamp(nextJitter(), 0, 1);
        var backoff = TimeSpan.FromSeconds(Math.Pow(2, retry) + jitter * 0.5);
        var header = response?.Headers.RetryAfter;
        var serverDelay = header?.Delta ??
            (header?.Date is { } date ? date - timing.UtcNow : TimeSpan.Zero);

        return serverDelay > backoff ? serverDelay : backoff;
    }

    private async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        // Chunk very long Retry-After values to stay within Task.Delay's supported range.
        while (delay > TimeSpan.Zero)
        {
            var chunk = delay > TimeSpan.FromDays(1) ? TimeSpan.FromDays(1) : delay;
            await timing.DelayAsync(chunk, cancellationToken);
            delay -= chunk;
        }
    }

    private static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests or
            HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or
            HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;

    private static bool IsTransient(Exception exception) =>
        exception is HttpRequestException or IOException or OperationCanceledException;
}
