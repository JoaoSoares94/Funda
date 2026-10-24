using Funda.AgentRankings.Api;
using Funda.AgentRankings.Configuration;
using Funda.AgentRankings.Listings;
using Funda.AgentRankings.Tests.ObjectMothers;
using Funda.AgentRankings.Tests.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Funda.AgentRankings.Tests;

public sealed class FundaTypedClientTests : IDisposable
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send =
        Substitute.For<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>>();
    private readonly VirtualRequestTiming _timing;
    private readonly ServiceProvider _provider;
    private readonly IListingSource _sut;

    public FundaTypedClientTests()
    {
        var options = FundaOptions.Load(FundaEnvironmentMother.CreateDefault());
        _timing = FundaRequestPolicyMother.CreateDefaultTiming();
        var services = new ServiceCollection();
        services.AddFundaApi(options)
            .ConfigurePrimaryHttpMessageHandler(() => new TestHttpMessageHandler(_send));
        services.AddSingleton(new FundaRequestPolicy(options, _timing, () => 0));
        _provider = services.BuildServiceProvider();
        _sut = _provider.GetRequiredService<IListingSource>();
    }

    [Fact]
    public async Task GetListingsAsync_When_ResolvedAsTypedClient_Then_FetchesThroughConfiguredTransport()
    {
        // Arrange
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => FundaHttpResponseMother.CreateDefault());

        // Act
        var result = await CollectAsync(_sut.GetListingsAsync(ListingSearch.Amsterdam));

        // Assert
        Assert.Equal("listing-1", Assert.Single(result).Id);
    }

    [Fact]
    public async Task GetListingsAsync_When_SeparateTypedClientsRunSearches_Then_SharesRequestPacing()
    {
        // Arrange
        var secondClient = _provider.GetRequiredService<IListingSource>();
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => FundaHttpResponseMother.CreateDefault());

        // Act
        await CollectAsync(_sut.GetListingsAsync(ListingSearch.Amsterdam));
        await CollectAsync(secondClient.GetListingsAsync(ListingSearch.AmsterdamWithGarden));

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(1), _timing.Delays.Single());
    }

    public void Dispose() => _provider.Dispose();

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
