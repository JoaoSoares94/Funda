using Funda.AgentRankings.Api;
using Funda.AgentRankings.Configuration;
using Funda.AgentRankings.Listings;
using Funda.AgentRankings.Tests.ObjectMothers;
using NSubstitute;

namespace Funda.AgentRankings.Tests;

public sealed class FundaClientTests : IDisposable
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send =
        Substitute.For<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>>();
    private readonly HttpClient _httpClient;
    private readonly FundaClient _sut;

    public FundaClientTests()
    {
        _httpClient = new HttpClient(new TestHttpMessageHandler(_send));
        _sut = new FundaClient(_httpClient, FundaOptions.Load(FundaEnvironmentMother.CreateDefault()),
            FundaRequestPolicyMother.CreateWithRetriesDisabled());
    }

    [Fact]
    public async Task GetListingsAsync_When_ResponseContainsListing_Then_ReturnsMappedListing()
    {
        // Arrange
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => FundaHttpResponseMother.CreateDefault());

        // Act
        var result = await CollectAsync(_sut.GetListingsAsync(ListingSearch.Amsterdam));

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
        await CollectAsync(_sut.GetListingsAsync(search));

        // Assert
        Assert.Equal($"?type=koop&zo={expectedQuery}&page=1&pagesize=25", requestedQuery);
    }

    [Fact]
    public async Task GetListingsAsync_When_SearchHasMultiplePages_Then_ReturnsListingsInPageOrder()
    {
        // Arrange
        var page = 0;
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => FundaHttpResponseMother.CreateWithPage(++page, 2));

        // Act
        var result = await CollectAsync(_sut.GetListingsAsync(ListingSearch.Amsterdam));

        // Assert
        Assert.Equal(CreateWithExpectedPageIds(), result.Select(listing => listing.Id));
    }

    [Fact]
    public async Task GetListingsAsync_When_SearchHasMultiplePages_Then_RequestsSuccessivePageNumbers()
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
        await CollectAsync(_sut.GetListingsAsync(ListingSearch.Amsterdam));

        // Assert
        Assert.Equal(CreateWithExpectedPageQueries(), queries);
    }

    [Fact]
    public async Task GetListingsAsync_When_SearchIsEmpty_Then_ReturnsNoListings()
    {
        // Arrange
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => FundaHttpResponseMother.CreateWithEmptyResults());

        // Act
        var result = await CollectAsync(_sut.GetListingsAsync(ListingSearch.Amsterdam));

        // Assert
        Assert.Empty(result);
    }

    [Theory]
    [InlineData("Objects", "null")]
    [InlineData("Objects.0", "null")]
    [InlineData("Objects.0.Id", "null")]
    [InlineData("Objects.0.Id", "\"\"")]
    [InlineData("Objects.0.MakelaarId", "0")]
    [InlineData("Objects.0.MakelaarNaam", "null")]
    [InlineData("Objects.0.MakelaarNaam", "\" \"")]
    [InlineData("Paging", "null")]
    [InlineData("Paging.HuidigePagina", "2")]
    [InlineData("Paging.AantalPaginas", "-1")]
    [InlineData("Paging.AantalPaginas", "0")]
    [InlineData("TotaalAantalObjecten", "-1")]
    [InlineData("TotaalAantalObjecten", "0")]
    [InlineData("AccountStatus", "1")]
    [InlineData("EmailNotConfirmed", "true")]
    [InlineData("ValidationFailed", "true")]
    [InlineData("Objects", "[]")]
    public async Task GetListingsAsync_If_ResponseDataIsInvalid_Then_ThrowsFundaApiException(
        string path, string value)
    {
        // Arrange
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => FundaHttpResponseMother.CreateWithInvalidField(path, value));

        // Act
        var act = () => CollectAsync(_sut.GetListingsAsync(ListingSearch.Amsterdam));

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
        var act = () => CollectAsync(_sut.GetListingsAsync(ListingSearch.Amsterdam));

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
        var result = await Record.ExceptionAsync(() => CollectAsync(_sut.GetListingsAsync(ListingSearch.Amsterdam)));

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
        var act = () => CollectAsync(_sut.GetListingsAsync(ListingSearch.Amsterdam));

        // Assert
        await Assert.ThrowsAsync<FundaApiException>(act);
    }

    [Fact]
    public async Task GetListingsAsync_If_PageCountChanges_Then_ThrowsFundaApiException()
    {
        // Arrange
        var page = 0;
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => FundaHttpResponseMother.CreateWithPage(++page, page == 1 ? 2 : 3));

        // Act
        var act = () => CollectAsync(_sut.GetListingsAsync(ListingSearch.Amsterdam));

        // Assert
        await Assert.ThrowsAsync<FundaApiException>(act);
    }

    [Fact]
    public async Task GetListingsAsync_If_PageContainsOnlyPreviouslySeenListings_Then_ThrowsFundaApiException()
    {
        // Arrange
        var page = 0;
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => FundaHttpResponseMother.CreateWithRepeatedListing(++page));

        // Act
        var act = () => CollectAsync(_sut.GetListingsAsync(ListingSearch.Amsterdam));

        // Assert
        await Assert.ThrowsAsync<FundaApiException>(act);
    }

    [Fact]
    public async Task GetListingsAsync_If_NetworkFailureContainsKey_Then_OmitsKeyFromException()
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
    public async Task GetListingsAsync_If_ErrorResponseContainsKey_Then_OmitsKeyFromException()
    {
        // Arrange
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => FundaHttpResponseMother.CreateWithStatus(401));

        // Act
        var result = await Record.ExceptionAsync(() => CollectAsync(_sut.GetListingsAsync(ListingSearch.Amsterdam)));

        // Assert
        Assert.DoesNotContain(FundaEnvironmentMother.TestKey, result!.ToString());
    }

    [Fact]
    public async Task GetListingsAsync_If_RequestTimesOut_Then_ThrowsSafeApiException()
    {
        // Arrange
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns<HttpResponseMessage>(_ => throw FundaHttpResponseMother.CreateWithTimeout());

        // Act
        var result = await Record.ExceptionAsync(() => CollectAsync(_sut.GetListingsAsync(ListingSearch.Amsterdam)));

        // Assert
        Assert.Equal("The API request timed out.", result?.Message);
    }

    [Fact]
    public async Task GetListingsAsync_If_CallerCancels_Then_ThrowsOperationCanceledException()
    {
        // Arrange
        using var source = FundaHttpResponseMother.CreateWithCanceledToken();

        // Act
        var act = () => CollectAsync(_sut.GetListingsAsync(ListingSearch.Amsterdam, source.Token));

        // Assert
        await Assert.ThrowsAsync<OperationCanceledException>(act);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetListingsAsync_When_SecondPageOmitsParentAgentId_Then_ResolvesFromChildrenAndCountsParentOnce(bool explicitNull)
    {
        // Arrange
        var page = 0;
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => ++page == 1 ? FundaHttpResponseMother.CreateWithPage(1, 2)
                : FundaHttpResponseMother.CreateWithGroupedPage(2, explicitNull));

        // Act
        var result = await CollectAsync(_sut.GetListingsAsync(ListingSearch.Amsterdam));

        // Assert
        Assert.Equal(CreateWithResolvedGroupedListings(), result);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("[null]")]
    [InlineData("[{\"MakelaarId\":null,\"MakelaarNaam\":\"Example Agent\"}]")]
    [InlineData("[{\"MakelaarId\":0,\"MakelaarNaam\":\"Example Agent\"}]")]
    [InlineData("[{\"MakelaarId\":42,\"MakelaarNaam\":\"Other Agent\"}]")]
    [InlineData("[{\"MakelaarId\":42,\"MakelaarNaam\":\"Example Agent\"},{\"MakelaarId\":43,\"MakelaarNaam\":\"Example Agent\"}]")]
    public async Task GetListingsAsync_If_ParentAgentCannotBeResolved_Then_ReportsPageAndObject(string children)
    {
        // Arrange
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => FundaHttpResponseMother.CreateWithUnresolvableAgent(children));

        // Act
        var result = await Record.ExceptionAsync(() => CollectAsync(_sut.GetListingsAsync(ListingSearch.Amsterdam)));

        // Assert
        Assert.Equal("The API returned an invalid or ambiguous listing identity on page 1, object 1.", result?.Message);
    }

    private static Listing[] CreateWithResolvedGroupedListings() =>
        [new("listing-1", 42, "Example Agent"), new("listing-2", 42, "Example Agent")];
    public void Dispose() => _httpClient.Dispose();

    private static Listing[] CreateDefaultListings() => [new("listing-1", 42, "Example Agent")];
    private static string[] CreateWithExpectedPageIds() => ["listing-1", "listing-2"];
    private static List<string> CreateDefaultQueryCollection() => [];
    private static string[] CreateWithExpectedPageQueries() =>
        ["?type=koop&zo=/amsterdam/&page=1&pagesize=25", "?type=koop&zo=/amsterdam/&page=2&pagesize=25"];

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
