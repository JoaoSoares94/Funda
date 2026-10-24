using Funda.AgentRankings.Listings;

namespace Funda.AgentRankings.Rankings;

public sealed class AgentRankingService(IListingSource listingSource)
{
    public async Task<IReadOnlyList<AgentRanking>> GetTopAgentsAsync(
        ListingSearch search,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var listingIds = new HashSet<string>(StringComparer.Ordinal);
        var agents = new Dictionary<int, AgentRanking>();

        await foreach (var listing in listingSource.GetListingsAsync(search, cancellationToken)
                           .WithCancellation(cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(listing.Id) || listing.AgentId <= 0 ||
                string.IsNullOrWhiteSpace(listing.AgentName))
            {
                throw new InvalidDataException("A listing is missing a valid listing ID, agent ID, or agent name.");
            }

            if (!listingIds.Add(listing.Id))
            {
                continue;
            }

            agents[listing.AgentId] = agents.TryGetValue(listing.AgentId, out var agent)
                ? agent with { ListingCount = checked(agent.ListingCount + 1) }
                : new AgentRanking(listing.AgentId, listing.AgentName, 1);
        }

        return agents.Values
            .OrderByDescending(agent => agent.ListingCount)
            .ThenBy(agent => agent.AgentName, StringComparer.Ordinal)
            .ThenBy(agent => agent.AgentId)
            .Take(limit)
            .ToArray();
    }
}
