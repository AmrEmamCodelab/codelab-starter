using System.Collections.Concurrent;
using System.Net.Http.Headers; // codelab:module Sms
using System.Text;
using Codelab.Gateway.Api.Sms; // codelab:module Sms
using Codelab.Gateway.Api.Sso;
using Codelab.Integration.Gateway;
using Codelab.Security;
using Codelab.Security.ServiceAuth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Codelab.Gateway.Api.Tests.TestSupport;

/// <summary>
/// The real gateway (Program) in memory, in the Test environment, relaying to <see cref="FakeUpstream"/> over HTTPS
/// with the upstream certificate pinned (real validation). The caller's ES256 key pair is generated for this run; the
/// gateway trusts its public half under <see cref="ApiKeyId"/>. Every setting is applied as a host setting (seen while
/// Program registers services) and as the last configuration source; <c>configure</c> may change or remove any.
/// </summary>
internal sealed class GatewayHost : WebApplicationFactory<Program>
{
    public const string EnvironmentName = "Test";
    public const string ApiServiceName = "starter-api";
    public const string ApiKeyId = "s2sapi";
    public const string Sender = "STARTER"; // codelab:module Sms
    public const string SmsUrlTemplate = FakeUpstream.SmsPath + "?sender={sender}"; // codelab:module Sms
    public const string SmsBodyTemplate = """{"to":"{to}","text":"{text}","lang":"{language}"}"""; // codelab:module Sms

    private readonly Dictionary<string, string?> _settings;
    private readonly ServiceAssertionClientOptions _client;

    public GatewayHost(FakeUpstream upstream, Action<IDictionary<string, string?>>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(upstream);
        Protocol = SecurityProtocol.Create(SecurityProtocol.TestPrefix);
        ProviderCredential = TestKeys.NewSecret(); // codelab:module Sms
        var (privatePem, publicPem) = TestKeys.NewServiceKeyPair();
        _client = new ServiceAssertionClientOptions { ServiceName = ApiServiceName, Audience = GatewayDefaults.Audience, KeyId = ApiKeyId, PrivateKeyPem = privatePem };
        _settings = BuildSettings(upstream, publicPem);
        configure?.Invoke(_settings);
    }

    public SecurityProtocol Protocol { get; }
    // codelab:module-begin Sms

    public string ProviderCredential { get; }
    // codelab:module-end Sms

    public ServiceAssertionClientOptions CallerIdentity => _client;

    public CapturingLoggerProvider Logs { get; } = new();

    /// <summary>A request carrying a fresh service assertion bound to its method, target and <paramref name="content"/>.</summary>
    public HttpRequestMessage SignedRequest(HttpMethod method, string path, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Add(Protocol.ServiceAssertionHeaderName, Assertion(method, path, content));
        return request;
    }

    /// <summary>A fresh assertion for <paramref name="method"/>, <paramref name="path"/> and the bytes of <paramref name="content"/>.</summary>
    public string Assertion(HttpMethod method, string path, HttpContent? content)
    {
        using var issuer = new ServiceAssertionIssuer(_client, TimeProvider.System, Protocol);
        var body = content is null ? [] : content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
        return issuer.Issue(method, new Uri(Server.BaseAddress, path), body);
    }

    /// <summary>A signed token relay of <paramref name="form"/>.</summary>
    public HttpRequestMessage TokenRequest(string form) =>
        SignedRequest(HttpMethod.Post, GatewayRoutes.SsoToken, new StringContent(form, Encoding.ASCII, GatewayMediaTypes.FormUrlEncoded));
    // codelab:module-begin Sms

    /// <summary>A signed SMS relay of <paramref name="json"/>.</summary>
    public HttpRequestMessage SmsRequest(string json)
    {
        var content = new StringContent(json, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue(GatewayMediaTypes.Json);
        return SignedRequest(HttpMethod.Post, GatewayRoutes.Sms, content);
    }
    // codelab:module-end Sms

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseEnvironment(EnvironmentName);
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(_settings));
        builder.ConfigureTestServices(services => services.AddSingleton<ILoggerFactory>(_ => new LoggerFactory([Logs])));
    }

    private Dictionary<string, string?> BuildSettings(FakeUpstream upstream, string publicPem)
    {
        const string server = ServiceAssertionServerOptions.SectionName;
        const string sso = SsoRelayOptions.SectionName;
        const string sms = SmsProviderOptions.SectionName; // codelab:module Sms
        return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Codelab:Security:Protocol:Prefix"] = Protocol.Prefix,
            [$"{server}:Audience"] = GatewayDefaults.Audience,
            [$"{server}:TrustedServices:0:ServiceName"] = ApiServiceName,
            [$"{server}:TrustedServices:0:KeyId"] = ApiKeyId,
            [$"{server}:TrustedServices:0:PublicKeyPem"] = publicPem,
            [$"{GatewayOptions.SectionName}:Profiles:0"] = GatewayProfileKey.Sso,
            [$"{GatewayOptions.SectionName}:Profiles:1"] = GatewayProfileKey.Sms, // codelab:module Sms
            [$"{sso}:TokenEndpoint"] = upstream.Url(FakeUpstream.TokenPath).ToString(),
            [$"{sso}:JwksUri"] = upstream.Url(FakeUpstream.JwksPath).ToString(),
            [$"{sso}:Retry:MaxRetries"] = "0",
            [$"{sso}:Certificate:Mode"] = "3",
            [$"{sso}:Certificate:PinnedThumbprints:0"] = upstream.Thumbprint,
            [$"{sms}:UrlTemplate"] = upstream.BaseAddress + SmsUrlTemplate.TrimStart('/'), // codelab:module Sms
            [$"{sms}:Method"] = "2", // codelab:module Sms
            [$"{sms}:BodyTemplate"] = SmsBodyTemplate, // codelab:module Sms
            [$"{sms}:BodyFormat"] = "1", // codelab:module Sms
            [$"{sms}:AuthHeaderName"] = FakeUpstream.CredentialHeader, // codelab:module Sms
            [$"{sms}:AuthHeaderValue"] = ProviderCredential, // codelab:module Sms
            [$"{sms}:Sender"] = Sender, // codelab:module Sms
            [$"{sms}:AcceptedMember"] = "ok", // codelab:module Sms
            [$"{sms}:MessageIdMember"] = "result.id", // codelab:module Sms
            [$"{sms}:Certificate:Mode"] = "3", // codelab:module Sms
            [$"{sms}:Certificate:PinnedThumbprints:0"] = upstream.Thumbprint, // codelab:module Sms
            ["Observability:LogFile:Enabled"] = "false",
        };
    }
}

/// <summary>
/// Captures the host's log lines (category, event id, rendered message) for assertions. It replaces the host's logger
/// factory, because Codelab.Observability's Serilog factory does not write to other providers.
/// </summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<(string Category, EventId EventId, string Message)> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(CapturingLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            owner.Entries.Enqueue((category, eventId, formatter(state, exception)));
    }
}
