namespace Funda.AgentRankings.Tests.ObjectMothers;

internal static class FundaEnvironmentMother
{
    public const string TestKey = "test-key-never-a-real-secret";
    public static Func<string, string?> CreateDefault() =>
        name => name == "FUNDA_API_KEY" ? TestKey : null;
    public static Func<string, string?> CreateWithOverride(string setting, string? value) =>
        name => name == setting ? value : CreateDefault()(name);
}
