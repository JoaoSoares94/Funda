namespace Funda.AgentRankings.Listings;

public sealed record ListingResolutionSummary(
    int UniqueProperties, int RankedProperties, int ConflictingProperties, int InferredProperties)
{
    public int UnresolvedProperties => UniqueProperties - RankedProperties;
}
