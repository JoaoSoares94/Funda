using Funda.AgentRankings.Api;
using Funda.AgentRankings.Configuration;
using Funda.AgentRankings.Listings;
using Funda.AgentRankings.Tests.ObjectMothers;
using NSubstitute;

namespace Funda.AgentRankings.Tests;

public sealed class FundaClientRetryTests : IDisposable
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send =
        Substitute.For<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>>();
    private readonly HttpClient _httpClient;
    private readonly FundaClient _sut;

    public FundaClientRetryTests()
    {
        var options = FundaOptions.Load(FundaEnvironmentMother.CreateDefault());
        var policy = new FundaRequestPolicy(options, FundaRequestPolicyMother.CreateDefaultTiming(), () => 0);
        _httpClient = new HttpClient(new TestHttpMessageHandler(_send)) { Timeout = Timeout.InfiniteTimeSpan };
        _sut = new FundaClient(_httpClient, options, policy);
    }

    [Fact]
    public async Task GetListingsAsync_If_FirstRequestIsThrottled_Then_ReturnsListingsAfterRetry()
    {
        // Arrange
        var attempt = 0;
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => ++attempt == 1 ? FundaHttpResponseMother.CreateWithStatus(429)
                : FundaHttpResponseMother.CreateDefault());

        // Act
        var result = await CollectAsync(_sut.GetListingsAsync(ListingSearch.Amsterdam));

        // Assert
        Assert.Single(result);
    }

    [Fact]
    public async Task GetListingsAsync_If_RequestIsRetried_Then_UsesFreshRequestMessages()
    {
        // Arrange
        var messages = CreateDefaultRequestCollection();
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                messages.Add(call.Arg<HttpRequestMessage>());
                return messages.Count == 1 ? FundaHttpResponseMother.CreateWithStatus(429)
                    : FundaHttpResponseMother.CreateDefault();
            });

        // Act
        await CollectAsync(_sut.GetListingsAsync(ListingSearch.Amsterdam));

        // Assert
        Assert.Equal(2, messages.Distinct(ReferenceEqualityComparer.Instance).Count());
    }

    [Fact]
    public async Task GetListingsAsync_If_RetriesExhaustWithNetworkFailures_Then_OmitsKeyFromFinalException()
    {
        // Arrange
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns<HttpResponseMessage>(_ => throw FundaHttpResponseMother.CreateWithNetworkFailure());

        // Act
        var result = await Record.ExceptionAsync(() => CollectAsync(_sut.GetListingsAsync(ListingSearch.Amsterdam)));

        // Assert
        Assert.DoesNotContain(FundaEnvironmentMother.TestKey, result!.ToString());
    }

    [Fact]
    public async Task GetListingsAsync_If_JsonIsInvalid_Then_DoesNotRetry()
    {
        // Arrange
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => FundaHttpResponseMother.CreateWithJson("not-json"));

        // Act
        await Record.ExceptionAsync(() => CollectAsync(_sut.GetListingsAsync(ListingSearch.Amsterdam)));

        // Assert
        Assert.Single(_send.ReceivedCalls());
    }

    public void Dispose() => _httpClient.Dispose();

    private static List<HttpRequestMessage> CreateDefaultRequestCollection() => [];

    private static async Task<List<Listing>> CollectAsync(IAsyncEnumerable<Listing> listings)
    {
        var result = new List<Listing>();
        await foreach (var listing in listings)
        {
            result.Add(listing);
        }
        return result;
    }

    private sealed class TestHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
