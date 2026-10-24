using System.Net.Http.Headers;
using Funda.AgentRankings.Api;
using Funda.AgentRankings.Configuration;
using Funda.AgentRankings.Tests.ObjectMothers;
using Funda.AgentRankings.Tests.TestUtilities;
using NSubstitute;

namespace Funda.AgentRankings.Tests;

public sealed class FundaRequestPolicyTests
{
    private readonly Func<CancellationToken, Task<HttpResponseMessage>> _send =
        Substitute.For<Func<CancellationToken, Task<HttpResponseMessage>>>();
    private readonly Func<double> _nextJitter = Substitute.For<Func<double>>();
    private readonly VirtualRequestTiming _timing;
    private readonly FundaRequestPolicy _sut;

    public FundaRequestPolicyTests()
    {
        _timing = FundaRequestPolicyMother.CreateDefaultTiming();
        _sut = new FundaRequestPolicy(
            FundaOptions.Load(FundaEnvironmentMother.CreateDefault()), _timing, _nextJitter);
    }

    [Fact]
    public async Task ExecuteAsync_When_FirstRequestSucceeds_Then_DoesNotDelay()
    {
        // Arrange
        _send(Arg.Any<CancellationToken>()).Returns(_ => FundaHttpResponseMother.CreateDefault());

        // Act
        using var result = await _sut.ExecuteAsync(_send);

        // Assert
        Assert.Empty(_timing.Delays);
    }

    [Fact]
    public async Task ExecuteAsync_When_TwoSearchesSharePolicy_Then_SpacesRequestsByOneSecond()
    {
        // Arrange
        _send(Arg.Any<CancellationToken>()).Returns(_ => FundaHttpResponseMother.CreateDefault());

        // Act
        using var first = await _sut.ExecuteAsync(_send);
        using var second = await _sut.ExecuteAsync(_send);

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(1), _timing.Delays.Single());
    }

    [Fact]
    public async Task ExecuteAsync_When_PreviousRequestTakesLongerThanInterval_Then_DoesNotAddPacingDelay()
    {
        // Arrange
        _send(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _timing.Advance(TimeSpan.FromSeconds(2));
            return FundaHttpResponseMother.CreateDefault();
        });

        // Act
        using var first = await _sut.ExecuteAsync(_send);
        using var second = await _sut.ExecuteAsync(_send);

        // Assert
        Assert.Empty(_timing.Delays);
    }

    [Theory]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public async Task ExecuteAsync_If_TransientStatusIsFollowedBySuccess_Then_ReturnsSuccessfulResponse(int status)
    {
        // Arrange
        var attempt = 0;
        _send(Arg.Any<CancellationToken>()).Returns(_ => ++attempt == 1
            ? FundaHttpResponseMother.CreateWithStatus(status)
            : FundaHttpResponseMother.CreateDefault());

        // Act
        using var result = await _sut.ExecuteAsync(_send);

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
    public async Task ExecuteAsync_If_TransientFailuresContinue_Then_StopsAfterThreeRetries()
    {
        // Arrange
        _send(Arg.Any<CancellationToken>()).Returns(_ => FundaHttpResponseMother.CreateWithStatus(429));

        // Act
        using var result = await _sut.ExecuteAsync(_send);

        // Assert
        Assert.Equal(4, _send.ReceivedCalls().Count());
    }

    [Fact]
    public async Task ExecuteAsync_If_TransientFailuresContinue_Then_UsesIncreasingBackoffWithoutExtraPacingWait()
    {
        // Arrange
        var times = FundaRequestPolicyMother.CreateDefaultAttemptTimes();
        _send(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            times.Add(_timing.GetTimestamp());
            return FundaHttpResponseMother.CreateWithStatus(503);
        });

        // Act
        using var result = await _sut.ExecuteAsync(_send);

        // Assert
        Assert.Equal(FundaRequestPolicyMother.CreateWithExpectedBackoffTimes(), times);
    }

    [Fact]
    public async Task ExecuteAsync_If_JitterIsEnabled_Then_AddsJitterToBackoff()
    {
        // Arrange
        var attempt = 0;
        _nextJitter().Returns(0.5);
        _send(Arg.Any<CancellationToken>()).Returns(_ => ++attempt == 1
            ? FundaHttpResponseMother.CreateWithStatus(429)
            : FundaHttpResponseMother.CreateDefault());

        // Act
        using var result = await _sut.ExecuteAsync(_send);

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(1.25), _timing.Delays.Single());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_If_ServerSuppliesRetryAfter_Then_WaitsAtLeastServerDelay(bool useDate)
    {
        // Arrange
        var attempt = 0;
        _send(Arg.Any<CancellationToken>()).Returns(_ => ++attempt == 1
            ? CreateWithRetryAfter(useDate, 10)
            : FundaHttpResponseMother.CreateDefault());

        // Act
        using var result = await _sut.ExecuteAsync(_send);

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(10), _timing.Delays.Single());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_If_RetryAfterHasElapsed_Then_StillUsesBackoff(bool useDate)
    {
        // Arrange
        var attempt = 0;
        _send(Arg.Any<CancellationToken>()).Returns(_ => ++attempt == 1
            ? CreateWithRetryAfter(useDate, 0)
            : FundaHttpResponseMother.CreateDefault());

        // Act
        using var result = await _sut.ExecuteAsync(_send);

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(1), _timing.Delays.Single());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_If_TransportFailureIsTransient_Then_RetriesAndReturnsSuccess(bool timeout)
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
        using var result = await _sut.ExecuteAsync(_send);

        // Assert
        Assert.True(result.IsSuccessStatusCode);
    }

    [Fact]
    public async Task ExecuteAsync_If_CallerCancelsDuringBackoff_Then_DoesNotSendAnotherAttempt()
    {
        // Arrange
        using var source = FundaRequestPolicyMother.CreateDefaultCancellationSource();
        _timing.OnDelay = source.Cancel;
        _send(Arg.Any<CancellationToken>()).Returns(_ => FundaHttpResponseMother.CreateWithStatus(429));

        // Act
        await Record.ExceptionAsync(() => _sut.ExecuteAsync(_send, source.Token));

        // Assert
        Assert.Single(_send.ReceivedCalls());
    }

    [Fact]
    public async Task ExecuteAsync_If_CallerCancelsDuringBackoff_Then_ThrowsOperationCanceledException()
    {
        // Arrange
        using var source = FundaRequestPolicyMother.CreateDefaultCancellationSource();
        _timing.OnDelay = source.Cancel;
        _send(Arg.Any<CancellationToken>()).Returns(_ => FundaHttpResponseMother.CreateWithStatus(429));

        // Act
        var act = () => _sut.ExecuteAsync(_send, source.Token);

        // Assert
        await Assert.ThrowsAsync<OperationCanceledException>(act);
    }

    [Fact]
    public async Task ExecuteAsync_If_CallerCancelsDuringRequest_Then_DoesNotRetry()
    {
        // Arrange
        using var source = FundaRequestPolicyMother.CreateDefaultCancellationSource();
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
    public async Task ExecuteAsync_If_CanceledWaitReleasesGate_Then_AllowsFollowingRequest()
    {
        // Arrange
        using var source = FundaRequestPolicyMother.CreateDefaultCancellationSource();
        _timing.OnDelay = source.Cancel;
        _send(Arg.Any<CancellationToken>()).Returns(_ => FundaHttpResponseMother.CreateWithStatus(429));
        await Record.ExceptionAsync(() => _sut.ExecuteAsync(_send, source.Token));
        _timing.OnDelay = null;
        _send(Arg.Any<CancellationToken>()).Returns(_ => FundaHttpResponseMother.CreateDefault());

        // Act
        using var result = await _sut.ExecuteAsync(_send);

        // Assert
        Assert.True(result.IsSuccessStatusCode);
    }

    [Fact]
    public async Task ExecuteAsync_When_SearchesOverlap_Then_QueuesSecondSearchUntilFirstCompletes()
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
        using var secondResult = await second;

        // Assert
        Assert.Equal(1, attemptsBeforeRelease);
    }

    [Fact]
    public async Task ExecuteAsync_If_ResponseIsRetried_Then_DisposesFailedResponseBeforeWaiting()
    {
        // Arrange
        var failure = FundaHttpResponseMother.CreateWithStatus(429);
        var attempt = 0;
        _send(Arg.Any<CancellationToken>()).Returns(_ => ++attempt == 1
            ? failure : FundaHttpResponseMother.CreateDefault());
        Exception? disposalError = null;
        _timing.OnDelay = () => disposalError = Record.Exception(() => failure.Content.ReadAsStream());

        // Act
        using var result = await _sut.ExecuteAsync(_send);

        // Assert
        Assert.IsType<ObjectDisposedException>(disposalError);
    }

    private static TaskCompletionSource<HttpResponseMessage> CreateDefaultPendingResponse() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private HttpResponseMessage CreateWithRetryAfter(bool useDate, int seconds)
    {
        var response = FundaHttpResponseMother.CreateWithStatus(429);
        response.Headers.RetryAfter = useDate
            ? new RetryConditionHeaderValue(_timing.UtcNow.AddSeconds(seconds))
            : new RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
        return response;
    }
}
