using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codelab.Errors;

namespace Codelab.Gateway.Api.Tests.TestSupport;

/// <summary>One fake upstream and one gateway shared by the tests of a class (each test resets the upstream's answers).</summary>
public sealed class GatewayFixture : IAsyncLifetime
{
    internal FakeUpstream Upstream { get; private set; } = null!;

    internal GatewayHost Host { get; private set; } = null!;

    internal HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Upstream = await FakeUpstream.StartAsync();
        Host = new GatewayHost(Upstream);
        Client = Host.CreateClient();
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await Host.DisposeAsync();
        await Upstream.DisposeAsync();
    }
}

/// <summary>Assertions on the gateway's problem+json answers.</summary>
internal static class Problems
{
    public static async Task<JsonElement> AssertAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        ArgumentNullException.ThrowIfNull(response);
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(ErrorEnvelope.ProblemJsonMediaType, response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, body.GetProperty(ErrorEnvelope.CodeMember).GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty(ErrorEnvelope.ReferenceCodeMember).GetString()));
        return body;
    }
}
