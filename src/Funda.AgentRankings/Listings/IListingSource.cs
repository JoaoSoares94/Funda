namespace Funda.AgentRankings.Listings;

public interface IListingSource
{
    IAsyncEnumerable<Listing> GetListingsAsync(
        ListingSearch search,
        CancellationToken cancellationToken);
}
