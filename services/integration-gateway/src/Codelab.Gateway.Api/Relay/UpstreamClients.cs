using Codelab.Integration.Outbound;
using Microsoft.Extensions.Options;

namespace Codelab.Gateway.Api.Relay;

/// <summary>
/// Registers a profile's upstream client on the Codelab.Integration outbound engine: the base address and policy come
/// from the profile's options section, plain http is allowed only in Development, and
/// redirects are never followed (a 3xx answer is relayed as <c>GATEWAY_UPSTREAM_REJECTED</c>, so a token request is
/// never re-posted to another host).
/// </summary>
internal static class UpstreamClients
{
    public static IHttpClientBuilder Add<TClient, TOptions>(IServiceCollection services, string name, Func<TOptions, Uri?> baseAddress)
        where TClient : class
        where TOptions : UpstreamOptions
    {
        var builder = services.AddCodelabOutboundClient<TClient>(name, static _ => { });
        services.AddOptions<OutboundClientOptions>(name)
            .Configure<IOptions<TOptions>, IHostEnvironment>((outbound, upstream, environment) =>
            {
                outbound.BaseAddress = baseAddress(upstream.Value);
                outbound.AllowPlainHttp = environment.IsDevelopment();
                outbound.Retry = upstream.Value.Retry;
                outbound.Timeout = upstream.Value.Timeout;
                outbound.Certificate = upstream.Value.Certificate;
            });
        builder.ConfigurePrimaryHttpMessageHandler(static (handler, _) => NeverFollowRedirects(handler));
        return builder;
    }

    internal static void NeverFollowRedirects(HttpMessageHandler handler)
    {
        if (handler is SocketsHttpHandler sockets)
        {
            sockets.AllowAutoRedirect = false;
        }
    }
}

/// <summary>
/// A profile's upstream addresses, so readiness can check them without knowing the profile. A null entry is a missing
/// setting.
/// </summary>
internal interface IRelayUpstreams
{
    IEnumerable<Uri?> UpstreamAddresses { get; }
}

/// <summary>The operation names the log lines carry (one per relay route).</summary>
internal static class RelayOperations
{
    public const string Token = "token";
    public const string Jwks = "jwks";
    public const string Send = "send";
}
