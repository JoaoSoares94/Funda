using Microsoft.Extensions.Time.Testing;

namespace Funda.AgentRankings.Tests.TestUtilities;

// Non-timing tests simulate one interval of network time, so sequential pages need no timer driving.
internal sealed class FakeTimeHttpMessageHandler(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send,
    FakeTimeProvider timeProvider,
    TimeSpan requestDuration) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await send(request, cancellationToken);
        timeProvider.Advance(requestDuration);
        return response;
    }
}
