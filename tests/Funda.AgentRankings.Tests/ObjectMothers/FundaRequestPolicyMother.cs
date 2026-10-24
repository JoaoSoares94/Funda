using Funda.AgentRankings.Api;
using Funda.AgentRankings.Configuration;
using Funda.AgentRankings.Tests.TestUtilities;

namespace Funda.AgentRankings.Tests.ObjectMothers;

internal static class FundaRequestPolicyMother
{
    public static VirtualRequestTiming CreateDefaultTiming() => new();

    public static FundaRequestPolicy CreateWithRetriesDisabled() =>
        new(FundaOptions.Load(FundaEnvironmentMother.CreateWithOverride("FUNDA_MAX_RETRIES", "0")),
            CreateDefaultTiming(), () => 0);

    public static CancellationTokenSource CreateDefaultCancellationSource() => new();

    public static List<long> CreateDefaultAttemptTimes() => [];

    public static long[] CreateWithExpectedBackoffTimes() =>
        [0, TimeSpan.TicksPerSecond, 3 * TimeSpan.TicksPerSecond, 7 * TimeSpan.TicksPerSecond];
}
