using Microsoft.Extensions.Configuration;
using Funda.AgentRankings.Configuration;

namespace Funda.AgentRankings.Tests.ObjectMothers;

internal static class FundaConfigurationMother
{
    public const string TestKey = "test-key-never-a-real-secret";

    public static FundaOptions CreateDefaultOptions(int maxRetries = 3, bool enableAgentNameFallback = false) =>
        new() { ApiKey = TestKey, MaxRetries = maxRetries, EnableAgentNameFallback = enableAgentNameFallback };

    public static IConfigurationRoot CreateDefault() =>
        CreateWithOverride("FUNDA_API_KEY", TestKey);

    public static IConfigurationRoot CreateWithOverride(string setting, string? value) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FUNDA_API_KEY"] = setting == "FUNDA_API_KEY" ? value : TestKey,
            [setting] = value
        }).Build();
}
