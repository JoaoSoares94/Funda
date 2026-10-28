using System.Text.Json.Nodes;
using Funda.AgentRankings.Api;
using Funda.AgentRankings.Api.Contracts;
using Funda.AgentRankings.Configuration;
using Funda.AgentRankings.Listings;
using Funda.AgentRankings.Tests.ObjectMothers;
using Funda.AgentRankings.Tests.TestUtilities;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Funda.AgentRankings.Tests;

public sealed class FundaClientPropertyResolutionTests : IDisposable
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send =
        Substitute.For<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>>();
    private readonly IProgress<ListingSearchProgress> _progress = Substitute.For<IProgress<ListingSearchProgress>>();
    private readonly FundaOptions _options;
    private readonly HttpClient _httpClient;
    private readonly FundaClient _sut;

    public FundaClientPropertyResolutionTests()
    {
        _options = FundaConfigurationMother.CreateDefaultOptions(maxRetries: 0);
        var clock = new FakeTimeProvider();
        _httpClient = new HttpClient(new FakeTimeHttpMessageHandler(_send, clock, _options.RequestInterval))
        {
            BaseAddress = _options.BaseUrl,
            Timeout = Timeout.InfiniteTimeSpan
        };
        _sut = new FundaClient(_httpClient, _options, new FundaRequestPolicy(_options, clock), _progress);
    }

    [Fact]
    public async Task GetListingsAsync_When_PropertiesAreNested_Then_CountsEveryPropertyWithItsOwnAgent()
    {
        // Arrange
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => CreateWithPage(1, 1, CreateWithNestedProperties()));

        // Act
        var result = await _sut.GetListingsAsync(ListingSearch.Amsterdam, CancellationToken.None).ToListAsync();

        // Assert
        Assert.Equal(CreateWithExpectedNestedListings(), result);
        Assert.Equal(CreateSummary(4, 4), ReadSummary());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetListingsAsync_When_DuplicateIdentityIsCompletedAcrossPagesAndHierarchy_Then_MergesBeforeResolving(bool knownFirst)
    {
        // Arrange
        var page = 0;
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => CreateWithDuplicateIdentityPage(++page, knownFirst));

        // Act
        var result = await _sut.GetListingsAsync(ListingSearch.Amsterdam, CancellationToken.None).ToListAsync();

        // Assert
        Assert.Equal(CreateWithExpectedMergedListings(), result.OrderBy(listing => listing.Id));
        Assert.Equal(CreateSummary(2, 2), ReadSummary());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetListingsAsync_If_DirectAgentIdsConflict_Then_LeavesThatPropertyUnresolvedRegardlessOfOrder(bool reverse)
    {
        // Arrange
        _options.EnableAgentNameFallback = true;
        var page = 0;
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => CreateWithConflictingIdentityPage(++page, reverse));

        // Act
        var result = await _sut.GetListingsAsync(ListingSearch.Amsterdam, CancellationToken.None).ToListAsync();

        // Assert
        Assert.Equal("valid", Assert.Single(result).Id);
        Assert.Equal(CreateSummary(2, 1, conflicts: 1), ReadSummary());
    }

    [Fact]
    public async Task GetListingsAsync_If_ChildHasConflictingAgentIds_Then_LeavesParentUnresolvedAndCountsConsistentChild()
    {
        // Arrange
        var page = 0;
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => CreateWithConflictedChildPage(++page));

        // Act
        var result = await _sut.GetListingsAsync(ListingSearch.Amsterdam, CancellationToken.None).ToListAsync();

        // Assert
        var child = Assert.Single(result);
        Assert.Equal(("consistent-child", 42), (child.Id, child.AgentId));
        Assert.Equal(CreateSummary(3, 1, conflicts: 1), ReadSummary());
    }

    [Fact]
    public async Task GetListingsAsync_When_DuplicateParentHasAdditionalChildren_Then_PreservesBothSubtrees()
    {
        // Arrange
        var page = 0;
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => CreateWithAdditionalChildrenPage(++page));

        // Act
        var result = await _sut.GetListingsAsync(ListingSearch.Amsterdam, CancellationToken.None).ToListAsync();

        // Assert
        Assert.Equal(CreateWithExpectedSubtreeIds(), result.Select(listing => listing.Id));
        Assert.Equal(CreateSummary(3, 3), ReadSummary());
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, 42)]
    public async Task GetListingsAsync_When_ChildHasNoAgentId_Then_UsesOptionalNameMatchingWithoutInheritingParentsAgent(
        bool enabled, int? expectedChildAgentId)
    {
        // Arrange
        _options.EnableAgentNameFallback = enabled;
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => CreateWithPage(1, 1, CreateWithUnidentifiedChild()));

        // Act
        var result = await _sut.GetListingsAsync(ListingSearch.Amsterdam, CancellationToken.None).ToListAsync();

        // Assert
        Assert.Equal(expectedChildAgentId, result.SingleOrDefault(listing => listing.Id == "child")?.AgentId);
        Assert.Equal(CreateSummary(3, enabled ? 3 : 2, inferred: enabled ? 1 : 0), ReadSummary());
    }

    [Fact]
    public async Task GetListingsAsync_When_ParentIdentityIsInferred_Then_DoesNotLearnItsUnverifiedName()
    {
        // Arrange
        _options.EnableAgentNameFallback = true;
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => CreateWithPage(1, 1, CreateWithUnverifiedParentName()));

        // Act
        var result = await _sut.GetListingsAsync(ListingSearch.Amsterdam, CancellationToken.None).ToListAsync();

        // Assert
        Assert.DoesNotContain(result, listing => listing.Id == "unresolved");
        Assert.Equal(CreateSummary(3, 2, inferred: 1), ReadSummary());
    }

    [Fact]
    public async Task GetListingsAsync_When_AgentNameIsMissing_Then_UsesAKnownNameForTheSameAgentId()
    {
        // Arrange
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => CreateWithPage(1, 1, CreateWithMissingAgentName()));

        // Act
        var result = await _sut.GetListingsAsync(ListingSearch.Amsterdam, CancellationToken.None).ToListAsync();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, listing => Assert.Equal("Agent 42", listing.AgentName));
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(false, null)]
    [InlineData(false, " ")]
    public async Task GetListingsAsync_When_NoAgentNameIsAvailable_Then_CountsThePropertyWithAFallbackName(bool omitted, string? name)
    {
        // Arrange
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => CreateWithUnavailableAgentName(omitted, name));

        // Act
        var result = await _sut.GetListingsAsync(ListingSearch.Amsterdam, CancellationToken.None).ToListAsync();

        // Assert
        Assert.Equal("Agent 42", Assert.Single(result).AgentName);
        Assert.Equal(CreateSummary(1, 1), ReadSummary());
    }

    [Theory]
    [InlineData("")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task GetListingsAsync_If_ChildHasNoUsablePropertyId_Then_FailsInsteadOfSilentlyDiscardingIt(string id)
    {
        // Arrange
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => CreateWithPage(1, 1, CreateWithInvalidChild(id)));

        // Act
        var error = await Record.ExceptionAsync(() => _sut.GetListingsAsync(ListingSearch.Amsterdam, CancellationToken.None).ToListAsync().AsTask());

        // Assert
        Assert.IsType<FundaApiException>(error);
        Assert.DoesNotContain(ReadProgress(), update => update.Percentage == 100);
    }

    [Fact]
    public async Task GetListingsAsync_When_PropertyGuidCasingDiffers_Then_CountsOneProperty()
    {
        // Arrange
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => CreateWithPage(1, 1, CreateWithGuidVariants()));

        // Act
        var result = await _sut.GetListingsAsync(ListingSearch.Amsterdam, CancellationToken.None).ToListAsync();

        // Assert
        Assert.Single(result);
        Assert.Equal(CreateSummary(1, 1), ReadSummary());
    }

    [Fact]
    public async Task GetListingsAsync_When_LaterDirectIdentityDisagreesWithChildInference_Then_PrefersDirectIdentity()
    {
        // Arrange
        var page = 0;
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => CreateWithLateDirectIdentityPage(++page));

        // Act
        var result = await _sut.GetListingsAsync(ListingSearch.Amsterdam, CancellationToken.None).ToListAsync();

        // Assert
        Assert.Equal(43, result.Single(listing => listing.Id == "parent").AgentId);
        Assert.Equal(CreateSummary(2, 2), ReadSummary());
    }

    public void Dispose() => _httpClient.Dispose();

    private IEnumerable<ListingSearchProgress> ReadProgress() => _progress.ReceivedCalls()
        .Select(call => (ListingSearchProgress)call.GetArguments()[0]!);

    private ListingResolutionSummary ReadSummary() => ReadProgress().Last().Summary!;

    private static FundaObject CreateProperty(string id, int? agentId = 42, string? name = "Agent 42",
        params FundaObject[] children) =>
        new() { Id = id, AgentId = agentId, AgentName = name, ChildrenObjects = children };

    private static HttpResponseMessage CreateWithPage(int page, int totalPages, params FundaObject[] properties) =>
        FundaHttpResponseMother.CreateWithProperties(page, totalPages, properties);

    private static FundaObject[] CreateWithNestedProperties() =>
        [CreateProperty("parent", children: [CreateProperty("child", 43, "Agent 43",
            CreateProperty("grandchild", 44, "Agent 44")), CreateProperty("sibling")])];

    private static Listing[] CreateWithExpectedNestedListings() =>
        [new("parent", 42, "Agent 42"), new("child", 43, "Agent 43"),
            new("grandchild", 44, "Agent 44"), new("sibling", 42, "Agent 42")];

    private static HttpResponseMessage CreateWithDuplicateIdentityPage(int page, bool knownFirst) =>
        (page == 1) == knownFirst
            ? CreateWithPage(page, 2, CreateProperty("shared"))
            : CreateWithPage(page, 2, CreateProperty("parent", children: [CreateProperty("shared", null)]));

    private static Listing[] CreateWithExpectedMergedListings() =>
        [new("parent", 42, "Agent 42"), new("shared", 42, "Agent 42")];

    private static HttpResponseMessage CreateWithConflictingIdentityPage(int page, bool reverse)
    {
        var id = (page == 1) == reverse ? 43 : 42;
        return page == 1
            ? CreateWithPage(page, 2, CreateProperty("shared", id), CreateProperty("valid"))
            : CreateWithPage(page, 2, CreateProperty("shared", id));
    }

    private static HttpResponseMessage CreateWithAdditionalChildrenPage(int page) =>
        CreateWithPage(page, 2, CreateProperty("parent", children: [CreateProperty($"child-{page}", 42 + page, $"Agent {42 + page}")]));

    private static HttpResponseMessage CreateWithConflictedChildPage(int page) =>
        page == 1
            ? CreateWithPage(page, 2, CreateProperty("parent", null,
                children: [CreateProperty("consistent-child"), CreateProperty("conflicted-child")]))
            : CreateWithPage(page, 2, CreateProperty("conflicted-child", 43, "Agent 43"));

    private static string[] CreateWithExpectedSubtreeIds() => ["parent", "child-1", "child-2"];

    private static FundaObject[] CreateWithUnidentifiedChild() =>
        [CreateProperty("parent", 43, "Agent 43", CreateProperty("child", null)), CreateProperty("known")];

    private static FundaObject[] CreateWithUnverifiedParentName() =>
        [CreateProperty("parent", null, "Unverified alias", CreateProperty("child")),
            CreateProperty("unresolved", null, "Unverified alias")];

    private static FundaObject[] CreateWithMissingAgentName() =>
        [CreateProperty("unnamed", name: null), CreateProperty("known")];

    private static FundaObject[] CreateWithInvalidChild(string id) =>
        [CreateProperty("parent", children: [CreateProperty(id)])];

    private static FundaObject[] CreateWithGuidVariants() =>
        [CreateProperty("91de1f4e-cfe5-44e4-ac43-7559e0a177ca"),
            CreateProperty("91DE1F4E-CFE5-44E4-AC43-7559E0A177CA")];

    private static HttpResponseMessage CreateWithUnavailableAgentName(bool omitted, string? name)
    {
        var json = JsonNode.Parse(FundaResponseJsonMother.CreateDefault())!;
        json["Paging"]!["AantalPaginas"] = 1;
        var item = json["Objects"]![0]!.AsObject();
        if (omitted) { item.Remove("MakelaarNaam"); }
        else { item["MakelaarNaam"] = name; }
        return FundaHttpResponseMother.CreateWithJson(json.ToJsonString());
    }

    private static HttpResponseMessage CreateWithLateDirectIdentityPage(int page) =>
        page == 1 ? CreateWithPage(page, 2, CreateProperty("parent", null, children: [CreateProperty("child")]))
            : CreateWithPage(page, 2, CreateProperty("parent", 43, "Agent 43"));

    private static ListingResolutionSummary CreateSummary(int unique, int ranked, int conflicts = 0, int inferred = 0) =>
        new(unique, ranked, conflicts, inferred);
}
