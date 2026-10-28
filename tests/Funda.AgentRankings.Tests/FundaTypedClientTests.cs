using Funda.AgentRankings.Api;
using Funda.AgentRankings.Listings;
using Funda.AgentRankings.Tests.ObjectMothers;
using Funda.AgentRankings.Tests.TestUtilities;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Funda.AgentRankings.Tests;

public sealed class FundaTypedClientTests : IDisposable
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send =
        Substitute.For<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>>();
    private readonly IProgress<ListingSearchProgress> _progress = Substitute.For<IProgress<ListingSearchProgress>>();
    private readonly FakeTimeProvider _timeProvider;
    private readonly ServiceProvider _provider;
    private readonly IListingSource _sut;

    public FundaTypedClientTests()
    {
        var configuration = FundaConfigurationMother.CreateDefault();
        _timeProvider = new FakeTimeProvider();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_timeProvider);
        services.AddSingleton(_progress);
        services.AddFundaApi(configuration)
            .ConfigureHttpClient(client => client.BaseAddress = CreateWithCustomBaseAddress())
            .ConfigurePrimaryHttpMessageHandler(() => new TestHttpMessageHandler(_send));
        _provider = services.BuildServiceProvider();
        _sut = _provider.GetRequiredService<IListingSource>();
    }

    [Fact]
    public async Task GetListingsAsync_When_ResolvedAsTypedClient_Then_UsesConfiguredEndpointAndJsonHeader()
    {
        // Arrange
        Uri? requestedUri = null;
        string? acceptHeader = null;
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<HttpRequestMessage>();
                requestedUri = request.RequestUri;
                acceptHeader = request.Headers.Accept.ToString();
                return FundaHttpResponseMother.CreateDefault();
            });

        // Act
        var result = await _sut.GetListingsAsync(ListingSearch.Amsterdam, CancellationToken.None).ToListAsync();

        // Assert
        Assert.Equal("listing-1", Assert.Single(result).Id);
        Assert.Equal(CreateWithExpectedRequestUri(), requestedUri);
        Assert.Equal("application/json", acceptHeader);
    }
    [Fact]
    public async Task GetListingsAsync_When_SeparateTypedClientsRunSearches_Then_SharesRequestPacing()
    {
        // Arrange
        var attemptTimes = CreateDefaultAttemptTimes();
        var secondClient = _provider.GetRequiredService<IListingSource>();
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                attemptTimes.Add(_timeProvider.GetTimestamp());
                return FundaHttpResponseMother.CreateDefault();
            });

        // Act
        await _sut.GetListingsAsync(ListingSearch.Amsterdam, CancellationToken.None).ToListAsync();
        var second = secondClient.GetListingsAsync(ListingSearch.AmsterdamWithGarden, CancellationToken.None).ToListAsync().AsTask();
        _timeProvider.Advance(TimeSpan.FromMilliseconds(650));
        await second.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.Equal(TimeSpan.FromMilliseconds(650), _timeProvider.GetElapsedTime(attemptTimes[0], attemptTimes[1]));
    }

    [Fact]
    public async Task StartAsync_If_ConfigurationIsInvalid_Then_RejectsStartupWithoutSendingRequests()
    {
        // Arrange
        var configuration = FundaConfigurationMother.CreateWithOverride("Funda:PageSize", "0");
        using var host = CreateDefaultHost(configuration, _send, _progress);

        // Act
        var start = () => host.StartAsync();

        // Assert
        await Assert.ThrowsAsync<OptionsValidationException>(start);
        Assert.Empty(_send.ReceivedCalls());
    }

    private static IHost CreateDefaultHost(IConfiguration configuration,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send,
        IProgress<ListingSearchProgress> progress)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddSingleton(progress);
        builder.Services.AddFundaApi(configuration)
            .ConfigurePrimaryHttpMessageHandler(() => new TestHttpMessageHandler(send));
        return builder.Build();
    }

    private static List<long> CreateDefaultAttemptTimes() => [];

    private static Uri CreateWithCustomBaseAddress() => new("https://example.com/custom/feeds/json/");

    private static Uri CreateWithExpectedRequestUri() =>
        new($"https://example.com/custom/feeds/json/{FundaConfigurationMother.TestKey}/?type=koop&zo=%2Famsterdam%2F&page=1&pagesize=25");
    public void Dispose() => _provider.Dispose();

    private sealed class TestHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
