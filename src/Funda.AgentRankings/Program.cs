using Funda.AgentRankings;
using Funda.AgentRankings.Api;
using Funda.AgentRankings.Configuration;
using Funda.AgentRankings.Listings;
using Microsoft.Extensions.DependencyInjection;

FundaOptions options;
try
{
    options = FundaOptions.FromEnvironment();
}
catch (InvalidOperationException exception)
{
    // Configuration validation errors name the setting without echoing its value.
    Console.Error.WriteLine($"Configuration error: {exception.Message}");
    return 2;
}

using var cancellation = new CancellationTokenSource();
ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};
Console.CancelKeyPress += cancelHandler;

try
{
    var services = new ServiceCollection();
    services.AddFundaApi(options);
    using var provider = services.BuildServiceProvider();
    var application = new RankingApplication(
        provider.GetRequiredService<IListingSource>(), Console.Out, Console.Error);
    return await application.RunAsync(cancellation.Token);
}
finally
{
    Console.CancelKeyPress -= cancelHandler;
}
