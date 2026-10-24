using Funda.AgentRankings.Api;
using Funda.AgentRankings.Listings;
using Funda.AgentRankings.Output;
using Funda.AgentRankings.Rankings;

namespace Funda.AgentRankings;

public sealed class RankingApplication(IListingSource listingSource, TextWriter output, TextWriter error)
{
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var service = new AgentRankingService(listingSource);
            error.WriteLine("Fetching Amsterdam sale listings...");
            var all = await service.GetTopAgentsAsync(
                ListingSearch.Amsterdam, cancellationToken: cancellationToken);

            error.WriteLine("Fetching Amsterdam sale listings with a garden...");
            var garden = await service.GetTopAgentsAsync(
                ListingSearch.AmsterdamWithGarden, cancellationToken: cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            var report = new ConsoleReportWriter(output);
            report.Write("Top agents: Amsterdam sale listings", all);
            report.Write("Top agents: Amsterdam sale listings with a garden", garden);
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            error.WriteLine("Search canceled. No complete report was produced.");
            return 130;
        }
        catch (FundaApiException exception)
        {
            error.WriteLine($"Search failed: {exception.Message}");
            return 1;
        }
        catch (Exception)
        {
            // Unknown exceptions may contain request URLs or credentials.
            error.WriteLine("The application failed. No complete report was produced.");
            return 1;
        }
    }
}
