using System.Runtime.CompilerServices;
using Funda.AgentRankings.Api;
using Funda.AgentRankings.Listings;
using Funda.AgentRankings.Output;
using Funda.AgentRankings.Tests.ObjectMothers;
using NSubstitute;

namespace Funda.AgentRankings.Tests;

public sealed class RankingApplicationTests : IDisposable
{
    private readonly IListingSource _source = Substitute.For<IListingSource>();
    private readonly StringWriter _output;
    private readonly StringWriter _error;
    private readonly ConsoleSearchProgress _progress;
    private readonly RankingApplication _sut;

    public RankingApplicationTests()
    {
        _output = CreateDefaultWriter();
        _error = CreateDefaultWriter();
        _progress = new ConsoleSearchProgress(_error, interactive: false);
        _sut = new RankingApplication(_source, _output, _error, _progress);
    }

    [Fact]
    public async Task RunAsync_When_BothSearchesSucceed_Then_PrintsBothTablesAndReturnsSuccess()
    {
        // Arrange
        _source.GetListingsAsync(ListingSearch.Amsterdam, Arg.Any<CancellationToken>())
            .Returns(CreateDefaultListings());
        _source.GetListingsAsync(ListingSearch.AmsterdamWithGarden, Arg.Any<CancellationToken>())
            .Returns(CreateWithGardenListings());

        // Act
        var exitCode = await _sut.RunAsync();

        // Assert
        Assert.Equal(0, exitCode);
        Assert.All(CreateWithExpectedReportLabels(), label => Assert.Contains(label, _output.ToString()));
        Assert.Matches(@"\|\s*1\s*\|\s*42\s*\|\s*1\s*\|\s*Example Agent\s*\|", _output.ToString());
        Assert.Matches(@"\|\s*1\s*\|\s*43\s*\|\s*1\s*\|\s*Garden Agent\s*\|", _output.ToString());
    }

    [Fact]
    public async Task RunAsync_When_SearchesAreEmpty_Then_PrintsEmptyReportsAndReturnsSuccess()
    {
        // Arrange
        _source.GetListingsAsync(Arg.Any<ListingSearch>(), Arg.Any<CancellationToken>())
            .Returns(_ => CreateWithEmptyListings());

        // Act
        var exitCode = await _sut.RunAsync();

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Equal(CreateWithEmptyReport(), _output.ToString().ReplaceLineEndings("\n"));
    }

    [Theory]
    [InlineData("[red]Agent[/]", "[red]Agent[/]")]
    [InlineData("Agent\nOffice", "Agent Office")]
    [InlineData("Agent\u001b[31m", "Agent [31m")]
    public async Task RunAsync_When_AgentNameContainsFormatting_Then_PrintsLiteralSanitizedName(
        string name, string expected)
    {
        // Arrange
        _source.GetListingsAsync(Arg.Any<ListingSearch>(), Arg.Any<CancellationToken>())
            .Returns(_ => CreateWithAgentName(name));

        // Act
        var exitCode = await _sut.RunAsync();

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Contains(expected, _output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("\u001b", _output.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RunAsync_If_EitherSearchFails_Then_ReturnsFailureWithSafeErrorAndNoPartialReport(bool firstSearchFails)
    {
        // Arrange
        _source.GetListingsAsync(ListingSearch.Amsterdam, Arg.Any<CancellationToken>())
            .Returns(firstSearchFails ? CreateWithApiFailure() : CreateDefaultListings());
        _source.GetListingsAsync(ListingSearch.AmsterdamWithGarden, Arg.Any<CancellationToken>())
            .Returns(CreateWithApiFailure());

        // Act
        var exitCode = await _sut.RunAsync();

        // Assert
        Assert.Equal(1, exitCode);
        Assert.Empty(_output.ToString());
        Assert.Contains("Search failed: The API returned HTTP 403.", _error.ToString());
    }

    [Fact]
    public async Task RunAsync_If_UnexpectedFailureContainsKey_Then_ReturnsFailureWithoutExposingKeyOrReport()
    {
        // Arrange
        _source.GetListingsAsync(Arg.Any<ListingSearch>(), Arg.Any<CancellationToken>())
            .Returns(CreateWithUnexpectedFailure());

        // Act
        var exitCode = await _sut.RunAsync();

        // Assert
        Assert.Equal(1, exitCode);
        Assert.Empty(_output.ToString());
        Assert.DoesNotContain(FundaConfigurationMother.TestKey, _error.ToString());
    }

    [Fact]
    public async Task RunAsync_If_CallerCancels_Then_ReturnsCancellationWithoutReport()
    {
        // Arrange
        using var source = CreateWithCanceledToken();
        _source.GetListingsAsync(Arg.Any<ListingSearch>(), Arg.Any<CancellationToken>())
            .Returns(_ => CreateWithEmptyListings());

        // Act
        var exitCode = await _sut.RunAsync(source.Token);

        // Assert
        Assert.Equal(130, exitCode);
        Assert.Empty(_output.ToString());
    }

    [Fact]
    public async Task RunAsync_When_SearchesReportProgress_Then_WritesProgressToErrorAndTablesToOutput()
    {
        // Arrange
        _source.GetListingsAsync(Arg.Any<ListingSearch>(), Arg.Any<CancellationToken>())
            .Returns(call => CreateWithProgress(call.Arg<ListingSearch>(), _progress));

        // Act
        var exitCode = await _sut.RunAsync();

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Contains("Funda Agent Rankings", _error.ToString());
        Assert.EndsWith(CreateWithCompletedProgressOutput(), _error.ToString().ReplaceLineEndings("\n"));
        Assert.Contains("Top agents:", _output.ToString());
    }

    [Fact]
    public async Task RunAsync_If_SearchFailsAfterReportingProgress_Then_StopsBelowCompletionWithoutReport()
    {
        // Arrange
        _source.GetListingsAsync(Arg.Any<ListingSearch>(), Arg.Any<CancellationToken>())
            .Returns(call => CreateWithProgress(call.Arg<ListingSearch>(), _progress, fail: true));

        // Act
        var exitCode = await _sut.RunAsync();

        // Assert
        Assert.Equal(1, exitCode);
        Assert.DoesNotContain("100%", _error.ToString());
        Assert.Contains("Search failed:", _error.ToString());
        Assert.Empty(_output.ToString());
    }

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
    }

    private static StringWriter CreateDefaultWriter() => new() { NewLine = "\n" };

    private static string CreateWithCompletedProgressOutput() =>
        "Amsterdam: 100%\nAmsterdam with a garden: 100%\n" +
        "Amsterdam: 1/1 properties ranked; 0 unresolved; 0 inferred.\n\n" +
        "Amsterdam with a garden: 1/1 properties ranked; 0 unresolved; 0 inferred.\n\n";

    private static IAsyncEnumerable<Listing> CreateDefaultListings() =>
        Enumerate([new("listing-1", 42, "Example Agent")]);

    private static IAsyncEnumerable<Listing> CreateWithGardenListings() =>
        Enumerate([new("garden-1", 43, "Garden Agent")]);

    private static IAsyncEnumerable<Listing> CreateWithAgentName(string name) =>
        Enumerate([new("listing-1", 42, name)]);

    private static IAsyncEnumerable<Listing> CreateWithEmptyListings() => Enumerate([]);

    private static async IAsyncEnumerable<Listing> CreateWithProgress(
        ListingSearch search, IProgress<ListingSearchProgress> progress, bool fail = false)
    {
        progress.Report(new ListingSearchProgress(search, 0));
        progress.Report(new ListingSearchProgress(search, 37.5));
        yield return new Listing("listing-1", 42, "Example Agent");
        if (fail)
        {
            throw new FundaApiException("The API returned HTTP 403.");
        }
        progress.Report(new ListingSearchProgress(search, 100, new ListingResolutionSummary(1, 1, 0, 0)));
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<Listing> CreateWithApiFailure()
    {
        await Task.CompletedTask;
        yield return new Listing("listing-1", 42, "Example Agent");
        throw new FundaApiException("The API returned HTTP 403.");
    }

    private static async IAsyncEnumerable<Listing> CreateWithUnexpectedFailure()
    {
        await Task.CompletedTask;
        yield return new Listing("listing-1", 42, "Example Agent");
        throw new InvalidOperationException(FundaConfigurationMother.TestKey);
    }

    private static CancellationTokenSource CreateWithCanceledToken() =>
        FundaHttpResponseMother.CreateWithCanceledToken();

    private static string[] CreateWithExpectedReportLabels() =>
        ["Top agents: Amsterdam sale listings", "Top agents: Amsterdam sale listings with a garden",
            "Rank", "Agent ID", "Listings", "Agent"];

    private static string CreateWithEmptyReport() =>
        "Top agents: Amsterdam sale listings\nNo rankable listings found.\n\n" +
        "Top agents: Amsterdam sale listings with a garden\nNo rankable listings found.\n\n";

    private static async IAsyncEnumerable<Listing> Enumerate(IEnumerable<Listing> listings,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var listing in listings)
        {
            yield return listing;
        }
    }
}
