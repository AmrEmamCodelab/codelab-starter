using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Codelab.Integration.Gateway;
using Codelab.Security;
using Codelab.Security.ServiceAuth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Codelab.Integration.Tests.Gateway.Support;

/// <summary>A fresh ES256 key pair per test run; nothing here is ever a committed key.</summary>
internal sealed record ServiceKeyPair(string PrivatePem, string PublicPem)
{
    public static ServiceKeyPair Create()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return new ServiceKeyPair(key.ExportPkcs8PrivateKeyPem(), key.ExportSubjectPublicKeyInfoPem());
    }
}

/// <summary>One request as the primary handler saw it: the body is copied before the request is disposed.</summary>
internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Assertion, string? ContentType, byte[] Body)
{
    public string BodyText => Encoding.UTF8.GetString(Body);
}

/// <summary>A primary handler that records every attempt and answers from a script.</summary>
internal sealed class RecordingHandler(Func<int, HttpResponseMessage> respond) : HttpMessageHandler
{
    private int _attempts;

    public ConcurrentQueue<RecordedRequest> Received { get; } = new();

    public static RecordingHandler Json(HttpStatusCode status, string json, string mediaType = GatewayMediaTypes.Json) =>
        new(_ => new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, mediaType) });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        var assertion = request.Headers.TryGetValues(GatewayClientHost.Protocol.ServiceAssertionHeaderName, out var values) ? values.Single() : null;
        Received.Enqueue(new RecordedRequest(request.Method, request.RequestUri!, assertion, request.Content?.Headers.ContentType?.MediaType, body));
        return respond(Interlocked.Increment(ref _attempts));
    }
}

/// <summary>A minimal host environment, so tests can be Development or Production.</summary>
internal sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = environmentName;

    public string ApplicationName { get; set; } = "gateway-tests";

    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

/// <summary>Builds a caller with <c>AddCodelabGatewayClient</c>, its primary handler replaced by a recording stub.</summary>
internal static class GatewayClientHost
{
    public const string CallerName = "starter-api";
    public const string CallerKeyId = "s2s-test";
    public const string OtherAudience = "starter-attachments";
    public static readonly Uri GatewayBase = new("https://gateway.example.test/dmz");
    public static readonly SecurityProtocol Protocol = SecurityProtocol.Create(SecurityProtocol.TestPrefix);

    public static Dictionary<string, string?> Settings(ServiceKeyPair keys, Uri? baseUrl = null) => new()
    {
        [$"{GatewayOptions.SectionName}:BaseUrl"] = (baseUrl ?? GatewayBase).ToString(),
        [$"{GatewayOptions.SectionName}:Retry:MaxRetries"] = "1",
        [$"{GatewayOptions.SectionName}:Retry:BaseDelay"] = "00:00:00.001",
        [$"{GatewayOptions.SectionName}:Retry:MaxDelay"] = "00:00:00.005",
        [$"{GatewayOptions.SectionName}:Retry:UseJitter"] = "false",
        [$"{ServiceAssertionClientOptions.SectionName}:ServiceName"] = CallerName,
        [$"{ServiceAssertionClientOptions.SectionName}:Audience"] = OtherAudience,
        [$"{ServiceAssertionClientOptions.SectionName}:KeyId"] = CallerKeyId,
        [$"{ServiceAssertionClientOptions.SectionName}:PrivateKeyPem"] = keys.PrivatePem,
    };

    public static ServiceProvider Build(IDictionary<string, string?> settings, HttpMessageHandler? primary = null, string environment = "Production")
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddSingleton(Protocol);
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(environment));
        var builder = services.AddCodelabGatewayClient(configuration);
        if (primary is not null)
        {
            builder.ConfigurePrimaryHttpMessageHandler(() => primary);
        }

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    public static ServiceAssertionValidator Validator(ServiceKeyPair keys, string audience = GatewayDefaults.Audience) =>
        new(
            new ServiceAssertionServerOptions
            {
                Audience = audience,
                TrustedServices = { new TrustedServiceOptions { ServiceName = CallerName, KeyId = CallerKeyId, PublicKeyPem = keys.PublicPem } },
            },
            new Codelab.Security.Replay.MemoryReplayGuard(TimeProvider.System, entryLimit: 100),
            TimeProvider.System,
            Protocol);
}
