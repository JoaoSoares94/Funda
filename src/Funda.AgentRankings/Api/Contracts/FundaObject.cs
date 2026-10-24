using System.Text.Json.Serialization;
using Funda.AgentRankings.Listings;

namespace Funda.AgentRankings.Api.Contracts;

public sealed record FundaObject
{
    public required string Id { get; init; }

    [JsonPropertyName("MakelaarId")]
    public int? AgentId { get; init; }

    [JsonPropertyName("MakelaarNaam")]
    public required string AgentName { get; init; }

    public FundaAgentReference[]? ChildrenObjects { get; init; }

    public Listing ToListing()
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(AgentName))
        {
            throw new InvalidDataException("A listing is missing its listing ID or agent name.");
        }

        if (AgentId is { } agentId)
        {
            if (agentId <= 0)
            {
                throw new InvalidDataException("A listing has an invalid agent ID.");
            }
            return new Listing(Id, agentId, AgentName);
        }

        // Grouped results can omit the parent's agent ID. Use children only as
        // identity evidence; they are not independently counted as search results.
        if (ChildrenObjects is not { Length: > 0 } ||
            ChildrenObjects.Any(child => child is null || child.AgentId is null or <= 0 ||
                !string.Equals(child.AgentName, AgentName, StringComparison.Ordinal)) ||
            ChildrenObjects.Select(child => child.AgentId).Distinct().Count() != 1)
        {
            throw new InvalidDataException("A listing's agent ID cannot be resolved unambiguously.");
        }

        return new Listing(Id, ChildrenObjects[0].AgentId!.Value, AgentName);
    }
}
