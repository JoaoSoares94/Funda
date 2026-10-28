using System.Net;
using Funda.AgentRankings.Api;
using Funda.AgentRankings.Configuration;
using Funda.AgentRankings.Listings;
using Funda.AgentRankings.Tests.ObjectMothers;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Funda.AgentRankings.Tests;

public sealed class FundaClientTimeoutTests : IDisposable
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send =
        Substitute.For<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>>();
    private readonly FakeTimeProvider _timeProvider;
    private readonly HttpClient _httpClient;
    private readonly FundaClient _sut;

    public FundaClientTimeoutTests()
    {
        _timeProvider = new FakeTimeProvider();
        var options = CreateDefaultOptions();
        _httpClient = new HttpClient(new TestHttpMessageHandler(_send)) { BaseAddress = options.BaseUrl, Timeout = Timeout.InfiniteTimeSpan };
        var progress = Substitute.For<IProgress<ListingSearchProgress>>();
        _sut = new FundaClient(_httpClient, options, new FundaRequestPolicy(options, _timeProvider), progress);
    }

    [Fact]
    public async Task GetListingsAsync_If_ResponseBodyStalls_Then_ReturnsSafeTimeoutError()
    {
        // Arrange
        var content = CreateWithStalledContent();
        _send(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => CreateWithContent(content));

        // Act
        var operation = _sut.GetListingsAsync(ListingSearch.Amsterdam, CancellationToken.None).ToListAsync().AsTask();
        await content.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        _timeProvider.Advance(TimeSpan.FromSeconds(1));
        var result = await Record.ExceptionAsync(() => operation.WaitAsync(TimeSpan.FromSeconds(5)));

        // Assert
        Assert.Equal("The API request timed out.", Assert.IsType<FundaApiException>(result).Message);
    }

    public void Dispose() => _httpClient.Dispose();

    private static FundaOptions CreateDefaultOptions()
    {
        var options = FundaConfigurationMother.CreateDefaultOptions(maxRetries: 0);
        options.TimeoutSeconds = 1;
        return options;
    }

    private static StalledContent CreateWithStalledContent() => new();
    private static HttpResponseMessage CreateWithContent(HttpContent content) =>
        new(HttpStatusCode.OK) { Content = content };

    private sealed class TestHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }

    private sealed class StalledContent : HttpContent
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(
            Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
