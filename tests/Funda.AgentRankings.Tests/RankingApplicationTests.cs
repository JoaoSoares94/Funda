using Funda.AgentRankings.Listings;
using Funda.AgentRankings.Tests.ObjectMothers;
using NSubstitute;

namespace Funda.AgentRankings.Tests;

public sealed class RankingApplicationTests : IDisposable
{
    private readonly IListingSource _source = Substitute.For<IListingSource>();
    private readonly StringWriter _output;
    private readonly StringWriter _error;
    private readonly RankingApplication _sut;

    public RankingApplicationTests()
    {
        _output = RankingApplicationMother.CreateDefaultWriter();
        _error = RankingApplicationMother.CreateDefaultWriter();
        _sut = new RankingApplication(_source, _output, _error);
    }

    [Fact]
    public async Task RunAsync_When_BothSearchesSucceed_Then_PrintsBothRankingTables()
    {
        // Arrange
        _source.GetListingsAsync(ListingSearch.Amsterdam, Arg.Any<CancellationToken>())
            .Returns(RankingApplicationMother.CreateDefaultListings());
        _source.GetListingsAsync(ListingSearch.AmsterdamWithGarden, Arg.Any<CancellationToken>())
            .Returns(RankingApplicationMother.CreateWithGardenListings());

        // Act
        await _sut.RunAsync();

        // Assert
        Assert.Equal(RankingApplicationMother.CreateDefaultReport(), _output.ToString());
    }

    [Fact]
    public async Task RunAsync_When_SearchesAreEmpty_Then_PrintsEmptyResultMessages()
    {
        // Arrange
        _source.GetListingsAsync(Arg.Any<ListingSearch>(), Arg.Any<CancellationToken>())
            .Returns(_ => RankingApplicationMother.CreateWithEmptyListings());

        // Act
        await _sut.RunAsync();

        // Assert
        Assert.Equal(RankingApplicationMother.CreateWithEmptyReport(), _output.ToString());
    }

    [Fact]
    public async Task RunAsync_When_BothSearchesSucceed_Then_ReturnsSuccessCode()
    {
        // Arrange
        _source.GetListingsAsync(Arg.Any<ListingSearch>(), Arg.Any<CancellationToken>())
            .Returns(_ => RankingApplicationMother.CreateWithEmptyListings());

        // Act
        var result = await _sut.RunAsync();

        // Assert
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task RunAsync_If_SecondSearchFails_Then_DoesNotPrintPartialReport()
    {
        // Arrange
        _source.GetListingsAsync(ListingSearch.Amsterdam, Arg.Any<CancellationToken>())
            .Returns(RankingApplicationMother.CreateDefaultListings());
        _source.GetListingsAsync(ListingSearch.AmsterdamWithGarden, Arg.Any<CancellationToken>())
            .Returns(RankingApplicationMother.CreateWithApiFailure());

        // Act
        await _sut.RunAsync();

        // Assert
        Assert.Equal(string.Empty, _output.ToString());
    }

    [Fact]
    public async Task RunAsync_If_ApiFails_Then_ReturnsFailureCode()
    {
        // Arrange
        _source.GetListingsAsync(Arg.Any<ListingSearch>(), Arg.Any<CancellationToken>())
            .Returns(RankingApplicationMother.CreateWithApiFailure());

        // Act
        var result = await _sut.RunAsync();

        // Assert
        Assert.Equal(1, result);
    }

    [Fact]
    public async Task RunAsync_If_ApiFails_Then_PrintsSafeFailureMessage()
    {
        // Arrange
        _source.GetListingsAsync(Arg.Any<ListingSearch>(), Arg.Any<CancellationToken>())
            .Returns(RankingApplicationMother.CreateWithApiFailure());

        // Act
        await _sut.RunAsync();

        // Assert
        Assert.Contains("Search failed: The API returned HTTP 403.", _error.ToString());
    }

    [Fact]
    public async Task RunAsync_If_UnexpectedFailureContainsKey_Then_OmitsKeyFromErrorOutput()
    {
        // Arrange
        _source.GetListingsAsync(Arg.Any<ListingSearch>(), Arg.Any<CancellationToken>())
            .Returns(RankingApplicationMother.CreateWithUnexpectedFailure());

        // Act
        await _sut.RunAsync();

        // Assert
        Assert.DoesNotContain(FundaEnvironmentMother.TestKey, _error.ToString());
    }

    [Fact]
    public async Task RunAsync_If_CallerCancels_Then_ReturnsCancellationCode()
    {
        // Arrange
        using var source = RankingApplicationMother.CreateWithCanceledToken();
        _source.GetListingsAsync(Arg.Any<ListingSearch>(), Arg.Any<CancellationToken>())
            .Returns(_ => RankingApplicationMother.CreateWithEmptyListings());

        // Act
        var result = await _sut.RunAsync(source.Token);

        // Assert
        Assert.Equal(130, result);
    }

    [Fact]
    public async Task RunAsync_If_CallerCancels_Then_DoesNotPrintReport()
    {
        // Arrange
        using var source = RankingApplicationMother.CreateWithCanceledToken();
        _source.GetListingsAsync(Arg.Any<ListingSearch>(), Arg.Any<CancellationToken>())
            .Returns(_ => RankingApplicationMother.CreateWithEmptyListings());

        // Act
        await _sut.RunAsync(source.Token);

        // Assert
        Assert.Equal(string.Empty, _output.ToString());
    }

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
    }
}
