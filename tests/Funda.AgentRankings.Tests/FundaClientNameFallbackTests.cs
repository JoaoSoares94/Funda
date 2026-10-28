using Funda.AgentRankings.Api;
using Funda.AgentRankings.Api.Contracts;
using Funda.AgentRankings.Configuration;
using Funda.AgentRankings.Listings;
using Funda.AgentRankings.Tests.ObjectMothers;
using Funda.AgentRankings.Tests.TestUtilities;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Funda.AgentRankings.Tests;

public sealed class FundaClientNameFallbackTests : IDisposable
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send =
        Substitute.For<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>>();
    private readonly IProgress<ListingSearchProgress> _progress = Substitute.For<IProgress<ListingSearchProgress>>();
    private readonly FundaOptions _options;
    private readonly HttpClient _httpClient;
    private readonly FundaClient _sut;

    public FundaClientNameFallbackTests()
    {
        _options = CreateDefaultOptions();
        var timeProvider = new FakeTimeProvider();
        _httpClient = new HttpClient(new FakeTimeHttpMessageHandler(_send, timeProvider, _options.RequestInterval))
        {
            BaseAddress = _options.BaseUrl,
            Timeout = Timeout.InfiniteTimeSpan
        };
        var policy = new FundaRequestPolicy(_options, timeProvider);
        _sut = new FundaClient(_httpClient, _options, policy, _progress);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetListingsAsync_When_NameMatchingIsEnabled_Then_ResolvesMissingIdFromAnotherPage(bool missingFirst)
    {
        // Arrange
        var page = 0;
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => CreateWithMatchingPages(++page, missingFirst));

        // Act
        var result = await _sut.GetListingsAsync(ListingSearch.Amsterdam, CancellationToken.None).ToListAsync();

        // Assert
        Assert.Equal(CreateWithExpectedResolvedListings(missingFirst), result);
    }

    [Theory]
    [InlineData("example agent")]
    [InlineData("  Example Agent  ")]
    public async Task GetListingsAsync_When_NameDiffersOnlyByCaseOrSurroundingSpaces_Then_ResolvesAgentId(string name)
    {
        // Arrange
        var page = 0;
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => CreateWithMatchingPages(++page, missingName: name));

        // Act
        var result = await _sut.GetListingsAsync(ListingSearch.Amsterdam, CancellationToken.None).ToListAsync();

        // Assert
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetListingsAsync_When_NameMatchingIsDisabled_Then_ExcludesUnidentifiedListing()
    {
        // Arrange
        _options.EnableAgentNameFallback = false;
        var page = 0;
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => CreateWithMatchingPages(++page));

        // Act
        var result = await _sut.GetListingsAsync(ListingSearch.Amsterdam, CancellationToken.None).ToListAsync();

        // Assert
        Assert.Single(result);
    }

    [Fact]
    public async Task GetListingsAsync_When_NameHasNoKnownId_Then_ExcludesUnidentifiedListing()
    {
        // Arrange
        var page = 0;
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => CreateWithMatchingPages(++page, missingName: "Unknown Agent"));

        // Act
        var result = await _sut.GetListingsAsync(ListingSearch.Amsterdam, CancellationToken.None).ToListAsync();

        // Assert
        Assert.Single(result);
    }

    [Fact]
    public async Task GetListingsAsync_When_NameHasMultipleKnownIds_Then_DoesNotInferAnAgent()
    {
        // Arrange
        var page = 0;
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => CreateWithAmbiguousPages(++page));

        // Act
        var result = await _sut.GetListingsAsync(ListingSearch.Amsterdam, CancellationToken.None).ToListAsync();

        // Assert
        Assert.Equal(CreateWithExpectedKnownListingIds(), result.Select(listing => listing.Id));
    }

    [Fact]
    public async Task GetListingsAsync_When_NewSearchStarts_Then_DoesNotReuseNamesFromPreviousSearch()
    {
        // Arrange
        var attempt = 0;
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => CreateWithPage(1, ++attempt == 1 ? 42 : null, "Example Agent", 1));
        await _sut.GetListingsAsync(ListingSearch.Amsterdam, CancellationToken.None).ToListAsync();

        // Act
        var result = await _sut.GetListingsAsync(ListingSearch.AmsterdamWithGarden, CancellationToken.None).ToListAsync();

        // Assert
        Assert.Empty(result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetListingsAsync_When_SearchCompletes_Then_ReportsProgressWithEitherFlag(bool enabled)
    {
        // Arrange
        _options.EnableAgentNameFallback = enabled;
        var page = 0;
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => CreateWithMatchingPages(++page));

        // Act
        await _sut.GetListingsAsync(ListingSearch.Amsterdam, CancellationToken.None).ToListAsync();

        // Assert
        Assert.Equal(CreateWithExpectedProgress(enabled), ReadProgress());
    }

    [Fact]
    public async Task GetListingsAsync_When_SearchHasManyPages_Then_ReportsFractionalProgressForEveryPage()
    {
        // Arrange
        var page = 0;
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => CreateWithPage(++page, 42, "Example Agent", 101));

        // Act
        await _sut.GetListingsAsync(ListingSearch.Amsterdam, CancellationToken.None).ToListAsync();

        // Assert
        Assert.Equal(CreateWithEveryPageProgress(), ReadProgress());
    }

    [Fact]
    public async Task GetListingsAsync_If_LaterPageFails_Then_DoesNotReportCompletion()
    {
        // Arrange
        var page = 0;
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => ++page == 1 ? CreateWithPage(1, 42, "Example Agent", 2)
                : FundaHttpResponseMother.CreateWithStatus(403));

        // Act
        await Record.ExceptionAsync(() => _sut.GetListingsAsync(ListingSearch.Amsterdam, CancellationToken.None).ToListAsync().AsTask());

        // Assert
        Assert.DoesNotContain(ReadProgress(), progress => progress.Percentage == 100);
    }

    [Fact]
    public async Task GetListingsAsync_When_NameFallbackStillYieldsResults_Then_DoesNotReportCompletionYet()
    {
        // Arrange
        var page = 0;
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => CreateWithMatchingPages(++page));
        await using var enumerator = _sut.GetListingsAsync(ListingSearch.Amsterdam, CancellationToken.None).GetAsyncEnumerator();

        // Act
        await enumerator.MoveNextAsync();
        await enumerator.MoveNextAsync();

        // Assert
        Assert.DoesNotContain(ReadProgress(), progress => progress.Percentage == 100);
    }

    [Fact]
    public async Task GetListingsAsync_When_SearchIsEmpty_Then_ReportsCompletion()
    {
        // Arrange
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => FundaHttpResponseMother.CreateWithJson(FundaResponseJsonMother.CreateWithEmptyResults()));

        // Act
        await _sut.GetListingsAsync(ListingSearch.Amsterdam, CancellationToken.None).ToListAsync();

        // Assert
        Assert.Contains(ReadProgress(), progress => progress.Percentage == 100 && progress.Summary?.UniqueProperties == 0);
    }

    public void Dispose() => _httpClient.Dispose();

    private IEnumerable<ListingSearchProgress> ReadProgress() => _progress.ReceivedCalls()
        .Select(call => (ListingSearchProgress)call.GetArguments()[0]!);

    private static ListingSearchProgress[] CreateWithExpectedProgress(bool enabled) =>
        [new(ListingSearch.Amsterdam, 0), new(ListingSearch.Amsterdam, 50),
            new(ListingSearch.Amsterdam, 100, new ListingResolutionSummary(2, enabled ? 2 : 1, 0, enabled ? 1 : 0))];

    private static IEnumerable<ListingSearchProgress> CreateWithEveryPageProgress() =>
        Enumerable.Range(0, 102).Select(page => new ListingSearchProgress(ListingSearch.Amsterdam, page * 100d / 101,
            page == 101 ? new ListingResolutionSummary(101, 101, 0, 0) : null));

    private static FundaOptions CreateDefaultOptions() =>
        FundaConfigurationMother.CreateDefaultOptions(enableAgentNameFallback: true);

    private static HttpResponseMessage CreateWithMatchingPages(
        int page, bool missingFirst = true, string missingName = "Example Agent")
    {
        var isMissing = page == (missingFirst ? 1 : 2);
        return CreateWithPage(page, isMissing ? null : 42, isMissing ? missingName : "Example Agent", 2);
    }

    private static HttpResponseMessage CreateWithAmbiguousPages(int page) =>
        CreateWithPage(page, page == 1 ? null : page == 3 ? 43 : 42, "Example Agent", 4);

    private static HttpResponseMessage CreateWithPage(int page, int? agentId, string name, int totalPages) =>
        FundaHttpResponseMother.CreateWithProperties(page, totalPages, new FundaObject
        {
            Id = $"listing-{page}", AgentId = agentId, AgentName = name, ChildrenObjects = []
        });

    private static Listing[] CreateWithExpectedResolvedListings(bool missingFirst) =>
        missingFirst
            ? [new("listing-2", 42, "Example Agent"), new("listing-1", 42, "Example Agent")]
            : [new("listing-1", 42, "Example Agent"), new("listing-2", 42, "Example Agent")];

    private static string[] CreateWithExpectedKnownListingIds() => ["listing-2", "listing-3", "listing-4"];
}
