using System.Net.Http.Headers;
using Funda.AgentRankings.Api;
using Funda.AgentRankings.Tests.ObjectMothers;
using Funda.AgentRankings.Tests.TestUtilities;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Polly.Timeout;

namespace Funda.AgentRankings.Tests;

public sealed class FundaRequestPolicyTests
{
    private readonly Func<CancellationToken, Task<HttpResponseMessage>> _send =
        Substitute.For<Func<CancellationToken, Task<HttpResponseMessage>>>();
    private readonly FakeTimeProvider _timeProvider;
    private readonly FundaRequestPolicy _sut;

    public FundaRequestPolicyTests()
    {
        _timeProvider = new FakeTimeProvider();
        _sut = new FundaRequestPolicy(
            FundaConfigurationMother.CreateDefaultOptions(), _timeProvider);
    }

    [Fact]
    public async Task ExecuteAsync_When_FirstRequestSucceeds_Then_DoesNotAdvanceTime()
    {
        // Arrange
        var started = _timeProvider.GetUtcNow();
        _send(Arg.Any<CancellationToken>()).Returns(_ => FundaHttpResponseMother.CreateDefault());

        // Act
        using var result = await _sut.ExecuteAsync(_send);

        // Assert
        Assert.Equal(started, _timeProvider.GetUtcNow());
    }

    [Fact]
    public async Task ExecuteAsync_When_TwoSearchesSharePolicy_Then_SpacesRequestsBy650Milliseconds()
    {
        // Arrange
        var times = CreateDefaultAttemptTimes();
        _send(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            times.Add(_timeProvider.GetTimestamp());
            return FundaHttpResponseMother.CreateDefault();
        });

        // Act
        using var first = await _sut.ExecuteAsync(_send);
        var secondOperation = _sut.ExecuteAsync(_send);
        _timeProvider.Advance(TimeSpan.FromMilliseconds(650));
        using var second = await secondOperation.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.Equal(TimeSpan.FromMilliseconds(650), _timeProvider.GetElapsedTime(times[0], times[1]));
    }

    [Fact]
    public async Task ExecuteAsync_When_PreviousAttemptTookLongerThanInterval_Then_DoesNotAddPacingDelay()
    {
        // Arrange
        var started = _timeProvider.GetUtcNow();
        _send(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _timeProvider.Advance(TimeSpan.FromSeconds(2));
            return FundaHttpResponseMother.CreateDefault();
        });

        // Act
        using var first = await _sut.ExecuteAsync(_send);
        using var second = await _sut.ExecuteAsync(_send);

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(4), _timeProvider.GetUtcNow() - started);
    }

    [Theory]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public async Task ExecuteAsync_If_TransientStatusIsFollowedBySuccess_Then_ReturnsSuccess(int status)
    {
        // Arrange
        var attempt = 0;
        _send(Arg.Any<CancellationToken>()).Returns(_ => ++attempt == 1
            ? FundaHttpResponseMother.CreateWithStatus(status)
            : FundaHttpResponseMother.CreateDefault());

        // Act
        using var result = await FakeTimeExecution.CompleteAsync(_sut.ExecuteAsync(_send), _timeProvider);

        // Assert
        Assert.True(result.IsSuccessStatusCode);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(501)]
    public async Task ExecuteAsync_If_StatusIsPermanent_Then_DoesNotRetry(int status)
    {
        // Arrange
        _send(Arg.Any<CancellationToken>()).Returns(_ => FundaHttpResponseMother.CreateWithStatus(status));

        // Act
        using var result = await _sut.ExecuteAsync(_send);

        // Assert
        Assert.Single(_send.ReceivedCalls());
    }

    [Fact]
    public async Task ExecuteAsync_If_TransientFailuresContinue_Then_PacesEveryAttemptAndStopsAfterConfiguredRetries()
    {
        // Arrange
        var times = CreateDefaultAttemptTimes();
        var attemptsStarted = CreateDefaultAttemptSignals();
        var retriedEarly = false;
        _send(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            times.Add(_timeProvider.GetTimestamp());
            attemptsStarted[times.Count - 1].SetResult();
            return CreateWithRetryAfter(false, 0);
        });

        // Act
        var operation = _sut.ExecuteAsync(_send);
        for (var retry = 1; retry < attemptsStarted.Length; retry++)
        {
            _timeProvider.Advance(TimeSpan.FromMilliseconds(649));
            retriedEarly |= times.Count != retry;
            _timeProvider.Advance(TimeSpan.FromMilliseconds(1));
            await attemptsStarted[retry].Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        using var result = await operation.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.False(retriedEarly);
        Assert.Equal(4, times.Count);
        Assert.All(times.Zip(times.Skip(1)), pair =>
            Assert.Equal(TimeSpan.FromMilliseconds(650), _timeProvider.GetElapsedTime(pair.First, pair.Second)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_If_ServerSuppliesRetryAfter_Then_UsesInjectedClockOutsideAttemptTimeout(bool useDate)
    {
        // Arrange
        const int seconds = 60;
        var times = CreateDefaultAttemptTimes();
        _send(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            times.Add(_timeProvider.GetTimestamp());
            return times.Count == 1 ? CreateWithRetryAfter(useDate, seconds)
                : FundaHttpResponseMother.CreateDefault();
        });

        // Act
        var operation = _sut.ExecuteAsync(_send);
        _timeProvider.Advance(TimeSpan.FromSeconds(seconds - 1));
        _timeProvider.Advance(TimeSpan.FromSeconds(1));
        using var result = await operation.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(seconds), _timeProvider.GetElapsedTime(times[0], times[1]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_If_ServerDelayHasElapsed_Then_StillAppliesPacing(bool useDate)
    {
        // Arrange
        var times = CreateDefaultAttemptTimes();
        _send(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            times.Add(_timeProvider.GetTimestamp());
            return times.Count == 1 ? CreateWithRetryAfter(useDate, 0)
                : FundaHttpResponseMother.CreateDefault();
        });

        // Act
        var operation = _sut.ExecuteAsync(_send);
        _timeProvider.Advance(TimeSpan.FromMilliseconds(650));
        using var result = await operation.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.Equal(TimeSpan.FromMilliseconds(650), _timeProvider.GetElapsedTime(times[0], times[1]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_If_TransportFailureIsTransient_Then_Retries(bool timeout)
    {
        // Arrange
        var attempt = 0;
        _send(Arg.Any<CancellationToken>()).Returns<HttpResponseMessage>(_ =>
        {
            if (++attempt == 1)
            {
                throw timeout ? FundaHttpResponseMother.CreateWithTimeout()
                    : FundaHttpResponseMother.CreateWithNetworkFailure();
            }
            return FundaHttpResponseMother.CreateDefault();
        });

        // Act
        using var result = await FakeTimeExecution.CompleteAsync(_sut.ExecuteAsync(_send), _timeProvider);

        // Assert
        Assert.True(result.IsSuccessStatusCode);
    }

    [Fact]
    public async Task ExecuteAsync_If_CallerCancelsDuringPacing_Then_ThrowsCancellation()
    {
        // Arrange
        using var source = CreateDefaultCancellationSource();
        _send(Arg.Any<CancellationToken>()).Returns(_ => FundaHttpResponseMother.CreateDefault());
        using var first = await _sut.ExecuteAsync(_send);

        // Act
        var operation = _sut.ExecuteAsync(_send, source.Token);
        source.Cancel();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
    }

    [Fact]
    public async Task ExecuteAsync_If_CallerCancelsDuringRequest_Then_DoesNotRetry()
    {
        // Arrange
        using var source = CreateDefaultCancellationSource();
        _send(Arg.Any<CancellationToken>()).Returns<HttpResponseMessage>(_ =>
        {
            source.Cancel();
            throw FundaHttpResponseMother.CreateWithTimeout();
        });

        // Act
        await Record.ExceptionAsync(() => _sut.ExecuteAsync(_send, source.Token));

        // Assert
        Assert.Single(_send.ReceivedCalls());
    }

    [Fact]
    public async Task ExecuteAsync_If_CallerCancelsDuringBackoff_Then_StopsRetriesAndAllowsFollowingRequest()
    {
        // Arrange
        using var source = CreateDefaultCancellationSource();
        _send(Arg.Any<CancellationToken>()).Returns(_ => CreateWithRetryAfter(false, 60));

        // Act
        var operation = _sut.ExecuteAsync(_send, source.Token);
        source.Cancel();
        var cancellation = await Record.ExceptionAsync(() => operation.WaitAsync(TimeSpan.FromSeconds(5)));
        var attemptsBeforeFollowingRequest = _send.ReceivedCalls().Count();
        _send(Arg.Any<CancellationToken>()).Returns(_ => FundaHttpResponseMother.CreateDefault());
        var next = _sut.ExecuteAsync(_send);
        _timeProvider.Advance(TimeSpan.FromMilliseconds(650));
        using var result = await next.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.IsAssignableFrom<OperationCanceledException>(cancellation);
        Assert.Equal(1, attemptsBeforeFollowingRequest);
        Assert.True(result.IsSuccessStatusCode);
    }

    [Fact]
    public async Task ExecuteAsync_When_SearchesOverlap_Then_QueuesSecondSearch()
    {
        // Arrange
        var completion = CreateDefaultPendingResponse();
        var attempt = 0;
        _send(Arg.Any<CancellationToken>()).Returns(_ => ++attempt == 1
            ? completion.Task : Task.FromResult(FundaHttpResponseMother.CreateDefault()));

        // Act
        var first = _sut.ExecuteAsync(_send);
        var second = _sut.ExecuteAsync(_send);
        var attemptsBeforeRelease = _send.ReceivedCalls().Count();
        completion.SetResult(FundaHttpResponseMother.CreateDefault());
        using var firstResult = await first;
        _timeProvider.Advance(TimeSpan.FromMilliseconds(650));
        using var secondResult = await second.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.Equal(1, attemptsBeforeRelease);
    }

    [Fact]
    public async Task ExecuteAsync_If_ResponseIsRetried_Then_DisposesResponseBeforeBackoff()
    {
        // Arrange
        var failure = CreateWithRetryAfter(false, 60);
        _send(Arg.Any<CancellationToken>()).Returns(_ => failure);
        using var source = CreateDefaultCancellationSource();

        // Act
        var operation = _sut.ExecuteAsync(_send, source.Token);
        var disposalError = Record.Exception(() => failure.Content.ReadAsStream());
        source.Cancel();
        await Record.ExceptionAsync(() => operation);

        // Assert
        Assert.IsType<ObjectDisposedException>(disposalError);
    }

    [Fact]
    public async Task ExecuteAsync_If_AttemptTimesOut_Then_RetriesAndReturnsSuccess()
    {
        // Arrange
        var attempt = 0;
        _send(Arg.Any<CancellationToken>()).Returns(call => ++attempt == 1
            ? WaitUntilCanceledAsync(call.Arg<CancellationToken>())
            : Task.FromResult(FundaHttpResponseMother.CreateDefault()));

        // Act
        var operation = _sut.ExecuteAsync(_send);
        _timeProvider.Advance(TimeSpan.FromSeconds(30));
        using var result = await FakeTimeExecution.CompleteAsync(operation, _timeProvider);

        // Assert
        Assert.True(result.IsSuccessStatusCode);
    }

    private HttpResponseMessage CreateWithRetryAfter(bool useDate, int seconds)
    {
        var response = FundaHttpResponseMother.CreateWithStatus(429);
        response.Headers.RetryAfter = useDate
            ? new RetryConditionHeaderValue(_timeProvider.GetUtcNow().AddSeconds(seconds))
            : new RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
        return response;
    }

    private static async Task<HttpResponseMessage> WaitUntilCanceledAsync(CancellationToken token)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, token);
        throw new InvalidOperationException("An infinite delay completed without cancellation.");
    }

    private static TaskCompletionSource<HttpResponseMessage> CreateDefaultPendingResponse() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static TaskCompletionSource[] CreateDefaultAttemptSignals() =>
        Enumerable.Range(0, 4).Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
    private static CancellationTokenSource CreateDefaultCancellationSource() => new();
    private static List<long> CreateDefaultAttemptTimes() => [];
}
