using System.Runtime.CompilerServices;
using Funda.AgentRankings.Listings;
using Funda.AgentRankings.Rankings;
using NSubstitute;

namespace Funda.AgentRankings.Tests;

public sealed class AgentRankingServiceTests
{
    private readonly IListingSource _listingSource = Substitute.For<IListingSource>();
    private readonly AgentRankingService _sut;

    public AgentRankingServiceTests()
    {
        _sut = new AgentRankingService(_listingSource);
    }

    [Fact]
    public async Task GetTopAgentsAsync_When_ListingsContainDuplicateIds_Then_ReturnsCountsForUniqueListingsPerAgent()
    {
        // Arrange
        _listingSource.GetListingsAsync(Arg.Any<ListingSearch>(), Arg.Any<CancellationToken>())
            .Returns(CreateWithDuplicateListings());

        // Act
        var result = await _sut.GetTopAgentsAsync(ListingSearch.Amsterdam);

        // Assert
        Assert.Equal(CreateWithExpectedUniqueCounts(), result);
    }

    [Fact]
    public async Task GetTopAgentsAsync_When_CountsAreTied_Then_ReturnsAgentsOrderedByNameAndId()
    {
        // Arrange
        _listingSource.GetListingsAsync(Arg.Any<ListingSearch>(), Arg.Any<CancellationToken>())
            .Returns(CreateWithTiedCounts());

        // Act
        var result = await _sut.GetTopAgentsAsync(ListingSearch.Amsterdam);

        // Assert
        Assert.Equal(CreateWithExpectedTieOrder(), result.Select(agent => agent.AgentId));
    }

    [Fact]
    public async Task GetTopAgentsAsync_When_MoreThanTenAgentsHaveListings_Then_ReturnsFirstTenAgents()
    {
        // Arrange
        _listingSource.GetListingsAsync(Arg.Any<ListingSearch>(), Arg.Any<CancellationToken>())
            .Returns(CreateWithTwelveAgents());

        // Act
        var result = await _sut.GetTopAgentsAsync(ListingSearch.AmsterdamWithGarden);

        // Assert
        Assert.Equal(CreateWithExpectedTopTenIds(), result.Select(agent => agent.AgentId));
    }

    [Fact]
    public async Task GetTopAgentsAsync_When_SearchHasNoListings_Then_ReturnsNoRankings()
    {
        // Arrange
        _listingSource.GetListingsAsync(Arg.Any<ListingSearch>(), Arg.Any<CancellationToken>())
            .Returns(CreateWithNoListings());

        // Act
        var result = await _sut.GetTopAgentsAsync(ListingSearch.Amsterdam);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetTopAgentsAsync_If_ListingIdentityIsInvalid_Then_ThrowsInvalidDataException()
    {
        // Arrange
        _listingSource.GetListingsAsync(Arg.Any<ListingSearch>(), Arg.Any<CancellationToken>())
            .Returns(CreateWithInvalidIdentity());

        // Act
        var act = () => _sut.GetTopAgentsAsync(ListingSearch.Amsterdam);

        // Assert
        await Assert.ThrowsAsync<InvalidDataException>(act);
    }

    [Fact]
    public async Task GetTopAgentsAsync_If_SourceFailsAfterAListing_Then_ThrowsHttpRequestException()
    {
        // Arrange
        _listingSource.GetListingsAsync(Arg.Any<ListingSearch>(), Arg.Any<CancellationToken>())
            .Returns(CreateWithSourceFailure());

        // Act
        var act = () => _sut.GetTopAgentsAsync(ListingSearch.Amsterdam);

        // Assert
        await Assert.ThrowsAsync<HttpRequestException>(act);
    }

    private static IAsyncEnumerable<Listing> CreateWithDuplicateListings() =>
        Enumerate([new("1", 1, "Same"), new("1", 1, "Same"),
            new("2", 1, "Same"), new("3", 2, "Same")]);

    private static AgentRanking[] CreateWithExpectedUniqueCounts() =>
        [new(1, "Same", 2), new(2, "Same", 1)];

    private static IAsyncEnumerable<Listing> CreateWithTiedCounts() =>
        Enumerate([new("1", 3, "Beta"), new("2", 2, "Alpha"), new("3", 1, "Alpha")]);

    private static int[] CreateWithExpectedTieOrder() => [1, 2, 3];

    private static IAsyncEnumerable<Listing> CreateWithTwelveAgents() =>
        Enumerate(Enumerable.Range(1, 12)
            .Select(id => new Listing(id.ToString(), id, $"Agent {id:D2}")));

    private static int[] CreateWithExpectedTopTenIds() => Enumerable.Range(1, 10).ToArray();

    private static IAsyncEnumerable<Listing> CreateWithNoListings() => Enumerate([]);

    private static IAsyncEnumerable<Listing> CreateWithInvalidIdentity() =>
        Enumerate([new("", 1, "Agent")]);

    private static async IAsyncEnumerable<Listing> Enumerate(IEnumerable<Listing> listings,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        foreach (var listing in listings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return listing;
        }
    }

    private static async IAsyncEnumerable<Listing> CreateWithSourceFailure()
    {
        await Task.CompletedTask;
        yield return new Listing("1", 1, "Agent");
        throw new HttpRequestException("Simulated failure on a subsequent page.");
    }
}
