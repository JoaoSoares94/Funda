using Funda.AgentRankings.Tests.TestUtilities;
using Microsoft.Extensions.Time.Testing;
using Funda.AgentRankings.Api;
using Funda.AgentRankings.Listings;
using Funda.AgentRankings.Tests.ObjectMothers;
using NSubstitute;
using System.Net.Http.Headers;

namespace Funda.AgentRankings.Tests;

public sealed class FundaClientRetryTests : IDisposable
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send =
        Substitute.For<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>>();
    private readonly FakeTimeProvider _timeProvider;
    private readonly HttpClient _httpClient;
    private readonly FundaClient _sut;

    public FundaClientRetryTests()
    {
        var options = FundaConfigurationMother.CreateDefaultOptions();
        _timeProvider = new FakeTimeProvider();
        var policy = new FundaRequestPolicy(options, _timeProvider);
        _httpClient = new HttpClient(new FakeTimeHttpMessageHandler(_send, _timeProvider, options.RequestInterval)) { BaseAddress = options.BaseUrl, Timeout = Timeout.InfiniteTimeSpan };
        var progress = Substitute.For<IProgress<ListingSearchProgress>>();
        _sut = new FundaClient(_httpClient, options, policy, progress);
    }

    [Fact]
    public async Task GetListingsAsync_If_FirstRequestIsThrottled_Then_ReturnsListingsUsingAFreshRetryRequest()
    {
        // Arrange
        var messages = CreateDefaultRequestCollection();
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                messages.Add(call.Arg<HttpRequestMessage>());
                return messages.Count == 1 ? CreateWithRetryAfter()
                    : FundaHttpResponseMother.CreateDefault();
            });

        // Act
        var operation = _sut.GetListingsAsync(ListingSearch.Amsterdam, CancellationToken.None).ToListAsync().AsTask();
        _timeProvider.Advance(TimeSpan.FromSeconds(1));
        var result = await operation.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.Single(result);
        Assert.Equal(2, messages.Count);
        Assert.NotSame(messages[0], messages[1]);
    }

    [Fact]
    public async Task GetListingsAsync_If_RetriesExhaustWithNetworkFailures_Then_OmitsKeyFromFinalException()
    {
        // Arrange
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns<HttpResponseMessage>(_ => throw FundaHttpResponseMother.CreateWithNetworkFailure());

        // Act
        var operation = _sut.GetListingsAsync(ListingSearch.Amsterdam, CancellationToken.None).ToListAsync().AsTask();
        var result = await Record.ExceptionAsync(() => FakeTimeExecution.CompleteAsync(operation, _timeProvider));

        // Assert
        Assert.IsType<FundaApiException>(result);
        Assert.DoesNotContain(FundaConfigurationMother.TestKey, result!.ToString());
    }

    [Fact]
    public async Task GetListingsAsync_If_JsonIsInvalid_Then_DoesNotRetry()
    {
        // Arrange
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => FundaHttpResponseMother.CreateWithJson("not-json"));

        // Act
        await Record.ExceptionAsync(() => _sut.GetListingsAsync(ListingSearch.Amsterdam, CancellationToken.None).ToListAsync().AsTask());

        // Assert
        Assert.Single(_send.ReceivedCalls());
    }

    public void Dispose() => _httpClient.Dispose();

    private static List<HttpRequestMessage> CreateDefaultRequestCollection() => [];

    private static HttpResponseMessage CreateWithRetryAfter()
    {
        var response = FundaHttpResponseMother.CreateWithStatus(429);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
        return response;
    }

}
