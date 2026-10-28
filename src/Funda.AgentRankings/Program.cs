using Funda.AgentRankings;
using Funda.AgentRankings.Api;
using Funda.AgentRankings.Listings;
using Funda.AgentRankings.Output;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

HostApplicationBuilder builder;
try
{
    builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
    {
        DisableDefaults = true,
        ContentRootPath = AppContext.BaseDirectory
    });
    builder.Configuration
        .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: false)
        .AddEnvironmentVariables();
}
catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
{
    Console.Error.WriteLine("Configuration error: appsettings.json could not be loaded. Check that the file exists and contains valid JSON.");
    return 2;
}

var progress = new ConsoleSearchProgress(Console.Error, interactive: !Console.IsErrorRedirected);
builder.Services.AddSingleton<IProgress<ListingSearchProgress>>(progress);
builder.Services.AddFundaApi(builder.Configuration);
using var host = builder.Build();
var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
try
{
    await host.StartAsync();
}
catch (OptionsValidationException exception)
{
    Console.Error.WriteLine($"Configuration error: {exception.Message}");
    return 2;
}
catch (OperationCanceledException) when (lifetime.ApplicationStopping.IsCancellationRequested)
{
    Console.Error.WriteLine("Search canceled. No complete report was produced.");
    return 130;
}

try
{
    var application = new RankingApplication(
        host.Services.GetRequiredService<IListingSource>(), Console.Out, Console.Error, progress);
    return await application.RunAsync(lifetime.ApplicationStopping);
}
finally
{
    await host.StopAsync();
}
