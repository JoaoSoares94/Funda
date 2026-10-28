using System.Text.Json.Nodes;
using Funda.AgentRankings.Api;
using Funda.AgentRankings.Api.Contracts;
using Funda.AgentRankings.Listings;
using Funda.AgentRankings.Tests.ObjectMothers;
using Funda.AgentRankings.Tests.TestUtilities;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Funda.AgentRankings.Tests;

public sealed class FundaClientTests : IDisposable
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send =
        Substitute.For<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>>();
    private readonly IProgress<ListingSearchProgress> _progress = Substitute.For<IProgress<ListingSearchProgress>>();
    private readonly HttpClient _httpClient;
    private readonly FundaClient _sut;

    public FundaClientTests()
    {
        var options = FundaConfigurationMother.CreateDefaultOptions();
        var timeProvider = new FakeTimeProvider();
        _httpClient = new HttpClient(new FakeTimeHttpMessageHandler(_send, timeProvider, options.RequestInterval))
        {
            BaseAddress = options.BaseUrl,
            Timeout = Timeout.InfiniteTimeSpan
        };
        _sut = new FundaClient(_httpClient, options,
            CreateWithRetriesDisabled(timeProvider), _progress);
    }

    [Fact]
    public async Task GetListingsAsync_When_ResponseContainsListing_Then_ReturnsMappedListing()
    {
        // Arrange
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => FundaHttpResponseMother.CreateDefault());

        // Act
        var result = await _sut.GetListingsAsync(ListingSearch.Amsterdam, CancellationToken.None).ToListAsync();

        // Assert
        Assert.Equal(CreateDefaultListings(), result);
    }

    [Theory]
    [InlineData(ListingSearch.Amsterdam, "/amsterdam/")]
    [InlineData(ListingSearch.AmsterdamWithGarden, "/amsterdam/tuin/")]
    public async Task GetListingsAsync_When_SearchIsSelected_Then_RequestsCorrectSaleQuery(
        ListingSearch search, string expectedQuery)
    {
        // Arrange
        string? requestedQuery = null;
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                requestedQuery = Uri.UnescapeDataString(call.Arg<HttpRequestMessage>().RequestUri!.Query);
                return FundaHttpResponseMother.CreateDefault();
            });

        // Act
        await _sut.GetListingsAsync(search, CancellationToken.None).ToListAsync();

        // Assert
        Assert.Equal($"?type=koop&zo={expectedQuery}&page=1&pagesize=25", requestedQuery);
    }

    [Fact]
    public async Task GetListingsAsync_When_SearchHasMultiplePages_Then_RequestsPagesAndReturnsListingsInOrder()
    {
        // Arrange
        var queries = CreateDefaultQueryCollection();
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                queries.Add(Uri.UnescapeDataString(call.Arg<HttpRequestMessage>().RequestUri!.Query));
                return FundaHttpResponseMother.CreateWithPage(queries.Count, 2);
            });

        // Act
        var result = await _sut.GetListingsAsync(ListingSearch.Amsterdam, CancellationToken.None).ToListAsync();

        // Assert
        Assert.Equal(CreateWithExpectedPageQueries(), queries);
        Assert.Equal(CreateWithExpectedPageIds(), result.Select(listing => listing.Id));
    }

    [Fact]
    public async Task GetListingsAsync_When_SearchIsEmpty_Then_ReturnsNoListings()
    {
        // Arrange
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => CreateWithEmptyResults());

        // Act
        var result = await _sut.GetListingsAsync(ListingSearch.Amsterdam, CancellationToken.None).ToListAsync();

        // Assert
        Assert.Empty(result);
    }

    [Theory]
    [InlineData("Objects", "null")]
    [InlineData("Objects.0", "null")]
    [InlineData("Objects.0.Id", "null")]
    [InlineData("Objects.0.Id", "\"\"")]
    [InlineData("Objects.0.MakelaarId", "0")]
    [InlineData("Paging", "null")]
    [InlineData("Paging.AantalPaginas", "-1")]
    [InlineData("Paging.HuidigePagina", "2")]
    [InlineData("AccountStatus", "1")]
    [InlineData("EmailNotConfirmed", "true")]
    [InlineData("ValidationFailed", "true")]
    public async Task GetListingsAsync_If_ResponseDataIsInvalid_Then_ThrowsFundaApiException(
        string path, string value)
    {
        // Arrange
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => CreateWithInvalidField(path, value));

        // Act
        var act = () => _sut.GetListingsAsync(ListingSearch.Amsterdam, CancellationToken.None).ToListAsync().AsTask();

        // Assert
        await Assert.ThrowsAsync<FundaApiException>(act);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("null")]
    public async Task GetListingsAsync_If_ResponseCannotBeDeserialized_Then_ThrowsFundaApiException(string json)
    {
        // Arrange
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => FundaHttpResponseMother.CreateWithJson(json));

        // Act
        var act = () => _sut.GetListingsAsync(ListingSearch.Amsterdam, CancellationToken.None).ToListAsync().AsTask();

        // Assert
        await Assert.ThrowsAsync<FundaApiException>(act);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(429)]
    [InlineData(500)]
    public async Task GetListingsAsync_If_HttpRequestFails_Then_PreservesStatusCode(int status)
    {
        // Arrange
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => FundaHttpResponseMother.CreateWithStatus(status));

        // Act
        var result = await Record.ExceptionAsync(() => _sut.GetListingsAsync(ListingSearch.Amsterdam, CancellationToken.None).ToListAsync().AsTask());

        // Assert
        Assert.Equal(status, (int)Assert.IsType<FundaApiException>(result).StatusCode!);
    }

    [Fact]
    public async Task GetListingsAsync_If_LaterPageFails_Then_ThrowsInsteadOfCompleting()
    {
        // Arrange
        var page = 0;
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => ++page == 1
                ? FundaHttpResponseMother.CreateWithPage(1, 2)
                : FundaHttpResponseMother.CreateWithStatus(500));

        // Act
        var act = () => _sut.GetListingsAsync(ListingSearch.Amsterdam, CancellationToken.None).ToListAsync().AsTask();

        // Assert
        await Assert.ThrowsAsync<FundaApiException>(act);
    }

    [Fact]
    public async Task GetListingsAsync_When_PageCountChanges_Then_UsesFirstPageTotal()
    {
        // Arrange
        var page = 0;
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => FundaHttpResponseMother.CreateWithPage(++page, page == 1 ? 2 : 3));

        // Act
        var result = await _sut.GetListingsAsync(ListingSearch.Amsterdam, CancellationToken.None).ToListAsync();

        // Assert
        Assert.Equal(CreateWithExpectedPageIds(), result.Select(listing => listing.Id));
    }

    [Fact]
    public async Task GetListingsAsync_When_PagesContainRepeatedListings_Then_MergesThemBeforeReturningListings()
    {
        // Arrange
        var page = 0;
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => CreateWithRepeatedListing(++page));

        // Act
        var result = await _sut.GetListingsAsync(ListingSearch.Amsterdam, CancellationToken.None).ToListAsync();

        // Assert
        Assert.Equal(CreateWithExpectedRepeatedIds(), result.Select(listing => listing.Id));
    }

    [Fact]
    public async Task GetListingsAsync_When_MiddlePageIsEmpty_Then_ContinuesToFirstPageTotal()
    {
        // Arrange
        var page = 0;
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => ++page == 2 ? CreateWithEmptyMiddlePage()
                : FundaHttpResponseMother.CreateWithPage(page, 3));

        // Act
        var result = await _sut.GetListingsAsync(ListingSearch.Amsterdam, CancellationToken.None).ToListAsync();

        // Assert
        Assert.Equal(CreateWithExpectedNonemptyPageIds(), result.Select(listing => listing.Id));
    }

    [Fact]
    public async Task GetListingsAsync_If_ErrorResponseContainsKey_Then_OmitsKeyFromException()
    {
        // Arrange
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => FundaHttpResponseMother.CreateWithStatus(401));

        // Act
        var result = await Record.ExceptionAsync(() => _sut.GetListingsAsync(ListingSearch.Amsterdam, CancellationToken.None).ToListAsync().AsTask());

        // Assert
        Assert.DoesNotContain(FundaConfigurationMother.TestKey, result!.ToString());
    }

    [Fact]
    public async Task GetListingsAsync_If_RequestTimesOut_Then_ThrowsSafeApiException()
    {
        // Arrange
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns<HttpResponseMessage>(_ => throw FundaHttpResponseMother.CreateWithTimeout());

        // Act
        var result = await Record.ExceptionAsync(() => _sut.GetListingsAsync(ListingSearch.Amsterdam, CancellationToken.None).ToListAsync().AsTask());

        // Assert
        Assert.Equal("The API request timed out.", result?.Message);
    }

    [Fact]
    public async Task GetListingsAsync_If_CallerCancels_Then_ThrowsOperationCanceledException()
    {
        // Arrange
        using var source = FundaHttpResponseMother.CreateWithCanceledToken();

        // Act
        var act = () => _sut.GetListingsAsync(ListingSearch.Amsterdam, source.Token).ToListAsync().AsTask();

        // Assert
        await Assert.ThrowsAsync<OperationCanceledException>(act);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetListingsAsync_When_SecondPageOmitsParentAgentId_Then_ResolvesParentAndCountsChildSeparately(bool explicitNull)
    {
        // Arrange
        var page = 0;
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => ++page == 1 ? FundaHttpResponseMother.CreateWithPage(1, 2)
                : CreateWithGroupedPage(2, explicitNull));

        // Act
        var result = await _sut.GetListingsAsync(ListingSearch.Amsterdam, CancellationToken.None).ToListAsync();

        // Assert
        Assert.Equal(CreateWithExpectedFallbackListings(), result);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("[null]")]
    [InlineData("""[{"Id":"child-1","MakelaarId":null,"MakelaarNaam":"Example Agent"}]""")]
    public async Task GetListingsAsync_When_ParentAndChildrenHaveNoAgentId_Then_ExcludesListing(string children)
    {
        // Arrange
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => CreateWithUnresolvableAgent(children));

        // Act
        var result = await _sut.GetListingsAsync(ListingSearch.Amsterdam, CancellationToken.None).ToListAsync();

        // Assert
        Assert.Empty(result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetListingsAsync_When_AgentIdIsMissing_Then_ReportsSearchProgress(bool explicitNull)
    {
        // Arrange
        var page = 0;
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => ++page == 1 ? FundaHttpResponseMother.CreateWithPage(1, 2)
                : CreateWithGroupedPage(2, explicitNull, includeChild: false));

        // Act
        await _sut.GetListingsAsync(ListingSearch.Amsterdam, CancellationToken.None).ToListAsync();

        // Assert
        Assert.Equal(CreateWithExpectedProgress(), _progress.ReceivedCalls()
            .Select(call => (ListingSearchProgress)call.GetArguments()[0]!));
    }

    [Fact]
    public async Task GetListingsAsync_When_EntirePageHasMissingAgentIds_Then_ContinuesToNextPage()
    {
        // Arrange
        var page = 0;
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => ++page == 1 ? CreateWithGroupedPage(1, false, includeChild: false)
                : FundaHttpResponseMother.CreateWithPage(2, 2));

        // Act
        var result = await _sut.GetListingsAsync(ListingSearch.Amsterdam, CancellationToken.None).ToListAsync();

        // Assert
        Assert.Equal("listing-2", Assert.Single(result).Id);
    }

    [Theory]
    [InlineData("42",
        """[{"Id":"child-1","MakelaarId":43,"MakelaarNaam":"Example Agent"}]""", 42)]
    [InlineData("null",
        """[null,{"Id":"child-2","MakelaarNaam":"Example Agent"},{"Id":"child-3","MakelaarId":null,"MakelaarNaam":"Example Agent"},{"Id":"child-4","MakelaarId":43,"MakelaarNaam":"Example Agent"}]""", 43)]
    [InlineData("null",
        """[{"Id":"child-1","MakelaarId":42,"MakelaarNaam":"Example Agent"},{"Id":"child-2","MakelaarId":43,"MakelaarNaam":"Example Agent"}]""", null)]
    [InlineData("null",
        """[{"Id":"child-1","MakelaarId":42,"MakelaarNaam":"Example Agent"},{"Id":"child-2","MakelaarId":42,"MakelaarNaam":"Example Agent"}]""", 42)]
    public async Task GetListingsAsync_When_AgentIdExistsInHierarchy_Then_PrefersParentOtherwiseRequiresChildAgreement(
        string parentId, string children, int? expectedAgentId)
    {
        // Arrange
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => CreateWithAgentHierarchy(parentId, children));

        // Act
        var result = await _sut.GetListingsAsync(ListingSearch.Amsterdam, CancellationToken.None).ToListAsync();

        // Assert
        Assert.Equal(expectedAgentId, result.SingleOrDefault(listing => listing.Id == "listing-1")?.AgentId);
    }

    public void Dispose() => _httpClient.Dispose();

    private static string[] CreateWithExpectedRepeatedIds() => ["listing-1"];
    private static string[] CreateWithExpectedNonemptyPageIds() => ["listing-1", "listing-3"];

    private static Listing[] CreateWithExpectedFallbackListings() =>
        [new("listing-1", 42, "Example Agent"), new("child-2", 42, "Example Agent"), new("listing-2", 42, "Example Agent")];

    private static HttpResponseMessage CreateWithAgentHierarchy(string parentId, string children)
    {
        var json = JsonNode.Parse(FundaResponseJsonMother.CreateDefault())!;
        json["Paging"]!["AantalPaginas"] = 1;
        json["Objects"]![0]!["MakelaarId"] = JsonNode.Parse(parentId);
        json["Objects"]![0]!["ChildrenObjects"] = JsonNode.Parse(children);
        return FundaHttpResponseMother.CreateWithJson(json.ToJsonString());
    }

    private static Listing[] CreateDefaultListings() => [new("listing-1", 42, "Example Agent")];
    private static string[] CreateWithExpectedPageIds() => ["listing-1", "listing-2"];
    private static List<string> CreateDefaultQueryCollection() => [];
    private static string[] CreateWithExpectedPageQueries() =>
        ["?type=koop&zo=/amsterdam/&page=1&pagesize=25", "?type=koop&zo=/amsterdam/&page=2&pagesize=25"];

    private static FundaRequestPolicy CreateWithRetriesDisabled(TimeProvider timeProvider) =>
        new(FundaConfigurationMother.CreateDefaultOptions(maxRetries: 0),
            timeProvider);

    private static HttpResponseMessage CreateWithInvalidField(string path, string value)
    {
        var json = JsonNode.Parse(FundaResponseJsonMother.CreateDefault())!;
        json["Paging"]!["AantalPaginas"] = 1;
        var segments = path.Split('.');
        var parent = json;
        foreach (var segment in segments[..^1])
        {
            parent = int.TryParse(segment, out var index) ? parent![index] : parent![segment];
        }
        if (int.TryParse(segments[^1], out var itemIndex))
        {
            parent![itemIndex] = JsonNode.Parse(value);
        }
        else
        {
            parent![segments[^1]] = JsonNode.Parse(value);
        }
        return FundaHttpResponseMother.CreateWithJson(json.ToJsonString());
    }

    private static HttpResponseMessage CreateWithRepeatedListing(int page) =>
        FundaHttpResponseMother.CreateWithProperties(page, 2, new FundaObject
        {
            Id = "listing-1", AgentId = 42, AgentName = "Example Agent", ChildrenObjects = []
        });

    private static HttpResponseMessage CreateWithGroupedPage(int page, bool explicitNull, bool includeChild = true)
    {
        var json = JsonNode.Parse(FundaResponseJsonMother.CreateDefault())!;
        json["Paging"]!["HuidigePagina"] = page;
        json["Paging"]!["AantalPaginas"] = 2;
        json["Objects"]![0]!["Id"] = $"listing-{page}";
        var listing = json["Objects"]![0]!.AsObject();
        listing["ChildrenObjects"] = includeChild
            ? JsonNode.Parse($$"""[{"Id":"child-{{page}}","MakelaarId":42,"MakelaarNaam":"Example Agent"}]""")
            : new JsonArray();
        if (explicitNull)
        {
            listing["MakelaarId"] = null;
        }
        else
        {
            listing.Remove("MakelaarId");
        }
        return FundaHttpResponseMother.CreateWithJson(json.ToJsonString());
    }

    private static HttpResponseMessage CreateWithUnresolvableAgent(string children)
    {
        var json = JsonNode.Parse(FundaResponseJsonMother.CreateDefault())!;
        json["Paging"]!["AantalPaginas"] = 1;
        json["Objects"]![0]!.AsObject().Remove("MakelaarId");
        json["Objects"]![0]!["ChildrenObjects"] = JsonNode.Parse(children);
        return FundaHttpResponseMother.CreateWithJson(json.ToJsonString());
    }

    private static HttpResponseMessage CreateWithEmptyMiddlePage() =>
        FundaHttpResponseMother.CreateWithProperties(2, 3);

    private static HttpResponseMessage CreateWithEmptyResults() =>
        FundaHttpResponseMother.CreateWithJson(FundaResponseJsonMother.CreateWithEmptyResults());

    private static ListingSearchProgress[] CreateWithExpectedProgress() =>
        [new(ListingSearch.Amsterdam, 0), new(ListingSearch.Amsterdam, 50),
            new(ListingSearch.Amsterdam, 100, new ListingResolutionSummary(2, 1, 0, 0))];
}
