namespace Funda.AgentRankings.Listings;

public sealed record ListingSearchProgress(
    ListingSearch Search, double Percentage, ListingResolutionSummary? Summary = null);
