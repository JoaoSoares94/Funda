using System.Net.Http.Headers;
using Funda.AgentRankings.Api;
using Funda.AgentRankings.Configuration;
using Funda.AgentRankings.Tests.ObjectMothers;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Polly.Timeout;

namespace Funda.AgentRankings.Tests;

public sealed class FundaRequestPolicyWithoutRetriesTests
{
    private readonly Func<CancellationToken, Task<HttpResponseMessage>> _send =
        Substitute.For<Func<CancellationToken, Task<HttpResponseMessage>>>();
    private readonly FakeTimeProvider _timeProvider;
    private readonly FundaRequestPolicy _sut;

    public FundaRequestPolicyWithoutRetriesTests()
    {
        _timeProvider = new FakeTimeProvider();
        _sut = new FundaRequestPolicy(CreateDefaultOptions(), _timeProvider);
    }

    [Fact]
    public async Task ExecuteAsync_When_RetriesAreDisabled_Then_ReturnsTransientFailureWithoutRetryOrDelay()
    {
        // Arrange
        var started = _timeProvider.GetUtcNow();
        _send(Arg.Any<CancellationToken>()).Returns(_ => CreateWithRetryAfter());

        // Act
        using var result = await _sut.ExecuteAsync(_send);

        // Assert
        Assert.Single(_send.ReceivedCalls());
        Assert.Equal(started, _timeProvider.GetUtcNow());
    }

    [Fact]
    public async Task ExecuteAsync_When_PacingExceedsAttemptTimeout_Then_TimesOnlyTheAttempt()
    {
        // Arrange
        _send(Arg.Any<CancellationToken>()).Returns(_ => FundaHttpResponseMother.CreateDefault());
        using var first = await _sut.ExecuteAsync(_send);

        // Act
        var operation = _sut.ExecuteAsync(_send);
        _timeProvider.Advance(TimeSpan.FromSeconds(2));
        using var result = await operation.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.True(result.IsSuccessStatusCode);
    }

    [Fact]
    public async Task ExecuteAsync_If_AttemptTimesOutWithRetriesDisabled_Then_ThrowsPackageTimeout()
    {
        // Arrange
        _send(Arg.Any<CancellationToken>()).Returns(call => WaitUntilCanceledAsync(call.Arg<CancellationToken>()));

        // Act
        var operation = _sut.ExecuteAsync(_send);
        _timeProvider.Advance(TimeSpan.FromSeconds(1));

        // Assert
        await Assert.ThrowsAsync<TimeoutRejectedException>(() => operation.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    private static FundaOptions CreateDefaultOptions()
    {
        var options = FundaConfigurationMother.CreateDefaultOptions(maxRetries: 0);
        options.TimeoutSeconds = 1;
        options.RequestIntervalMs = 2000;
        return options;
    }

    private static HttpResponseMessage CreateWithRetryAfter()
    {
        var response = FundaHttpResponseMother.CreateWithStatus(429);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(1));
        return response;
    }

    private static async Task<HttpResponseMessage> WaitUntilCanceledAsync(CancellationToken token)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, token);
        throw new InvalidOperationException("An infinite delay completed without cancellation.");
    }
}
