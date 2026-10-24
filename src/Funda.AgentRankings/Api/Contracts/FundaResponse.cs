using System.Text.Json.Serialization;

namespace Funda.AgentRankings.Api.Contracts;

public sealed record FundaResponse
{
    public required int AccountStatus { get; init; }
    public required bool EmailNotConfirmed { get; init; }
    public required bool ValidationFailed { get; init; }
    public required FundaObject[] Objects { get; init; }
    public required FundaPaging Paging { get; init; }

    [JsonPropertyName("TotaalAantalObjecten")]
    public required int TotalObjectCount { get; init; }
}
