using System.ComponentModel.DataAnnotations;

namespace Funda.AgentRankings.Configuration;

public sealed class FundaOptions
{
    [Required]
    [RegularExpression(@"[^\s\p{C}]+",
        ErrorMessage = "ApiKey must not contain whitespace or control characters.")]
    public string ApiKey { get; set; } = "";

    [Required]
    public Uri BaseUrl { get; set; } = new("https://candidate-excercise.funda.io/feeds/Aanbod.svc/json/");

    [Range(1, 25)]
    public int PageSize { get; set; } = 25;

    [Range(1, 300)]
    public int TimeoutSeconds { get; set; } = 30;

    [Range(650, 60000)]
    public int RequestIntervalMs { get; set; } = 650;

    [Range(0, 5)]
    public int MaxRetries { get; set; } = 3;

    public bool EnableAgentNameFallback { get; set; }

    public TimeSpan RequestTimeout => TimeSpan.FromSeconds(TimeoutSeconds);
    public TimeSpan RequestInterval => TimeSpan.FromMilliseconds(RequestIntervalMs);

    public override string ToString() => "FundaOptions (credentials omitted)";
}
