using System.Net;
using System.Text.Json;
using Codelab.Security.PublicIds;
using Codelab.Security.ServiceAuth;
using Package.Sample.Api.Hosting;
using Package.Sample.Api.Records;
using Package.Sample.Tests.Support;
using Xunit;

namespace Package.Sample.Tests;

/// <summary>Exit criterion: the one service-to-service scheme (ES256 per-request assertions) refuses a replay.</summary>
public sealed class ServiceAssertionTests : IDisposable
{
    private readonly SampleHost _host = new(SampleKeys.Create());

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task An_asserted_call_is_served_once_and_its_replay_is_refused_401()
    {
        using var client = _host.NewClient();
        var path = TouchPath();
        using var issuer = Issuer();
        var assertion = issuer.Issue(HttpMethod.Post, new Uri(SampleHost.BaseAddress, path), ReadOnlySpan<byte>.Empty);

        var first = await SendAsync(client, path, assertion);
        var replay = await SendAsync(client, path, assertion);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using var body = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        Assert.Equal(SampleSettings.CallingService, body.RootElement.GetProperty("calledBy").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
    }

    [Fact]
    public async Task A_call_without_an_assertion_or_bound_to_another_path_is_refused_401()
    {
        using var client = _host.NewClient();
        using var issuer = Issuer();
        var otherPath = SampleRoutes.InternalTouchFor(PublicId<RecordIds>.From(RecordStore.SeedRecordId + 1).ToToken());
        var forOtherPath = issuer.Issue(HttpMethod.Post, new Uri(SampleHost.BaseAddress, otherPath), ReadOnlySpan<byte>.Empty);

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, TouchPath(), assertion: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, TouchPath(), forOtherPath)).StatusCode);
    }

    [Fact]
    public async Task A_user_session_cannot_call_a_service_endpoint()
    {
        var session = _host.NewSession();

        var exchange = await session.SendAsync(HttpMethod.Post, TouchPath());

        Assert.Equal(HttpStatusCode.Unauthorized, exchange.Response.StatusCode);
    }

    private static string TouchPath() => SampleRoutes.InternalTouchFor(PublicId<RecordIds>.From(RecordStore.SeedRecordId).ToToken());

    private ServiceAssertionIssuer Issuer() => new(
        new ServiceAssertionClientOptions
        {
            ServiceName = SampleSettings.CallingService,
            Audience = SampleSettings.Audience,
            KeyId = SampleSettings.CallingServiceKeyId,
            PrivateKeyPem = _host.Keys.ServicePrivateKeyPem,
        },
        TimeProvider.System,
        _host.Protocol);

    private async Task<HttpResponseMessage> SendAsync(HttpClient client, string path, string? assertion)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (assertion is not null)
        {
            request.Headers.Add(_host.Protocol.ServiceAssertionHeaderName, assertion);
        }

        return await client.SendAsync(request);
    }
}
