using System.Runtime.CompilerServices;
using Funda.AgentRankings.Api;
using Funda.AgentRankings.Listings;

namespace Funda.AgentRankings.Tests.ObjectMothers;

internal static class RankingApplicationMother
{
    public static StringWriter CreateDefaultWriter() => new() { NewLine = "\n" };

    public static IAsyncEnumerable<Listing> CreateDefaultListings() =>
        Enumerate([new("listing-1", 42, "Example Agent")]);

    public static IAsyncEnumerable<Listing> CreateWithGardenListings() =>
        Enumerate([new("garden-1", 43, "Garden Agent")]);

    public static IAsyncEnumerable<Listing> CreateWithEmptyListings() => Enumerate([]);

    public static async IAsyncEnumerable<Listing> CreateWithApiFailure()
    {
        await Task.CompletedTask;
        yield return new Listing("listing-1", 42, "Example Agent");
        throw new FundaApiException("The API returned HTTP 403.");
    }

    public static async IAsyncEnumerable<Listing> CreateWithUnexpectedFailure()
    {
        await Task.CompletedTask;
        yield return new Listing("listing-1", 42, "Example Agent");
        throw new InvalidOperationException(FundaEnvironmentMother.TestKey);
    }

    public static CancellationTokenSource CreateWithCanceledToken() =>
        FundaHttpResponseMother.CreateWithCanceledToken();

    public static string CreateDefaultReport() =>
        "Top agents: Amsterdam sale listings\n" +
        "Rank    Agent ID  Listings  Agent\n" +
        "   1          42         1  Example Agent\n\n" +
        "Top agents: Amsterdam sale listings with a garden\n" +
        "Rank    Agent ID  Listings  Agent\n" +
        "   1          43         1  Garden Agent\n\n";

    public static string CreateWithEmptyReport() =>
        "Top agents: Amsterdam sale listings\nNo listings found.\n\n" +
        "Top agents: Amsterdam sale listings with a garden\nNo listings found.\n\n";

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
