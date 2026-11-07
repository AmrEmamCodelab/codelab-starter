using System.Security.Cryptography;
using Codelab.OpsConsole.Api.Upstream;
using Codelab.Security;
using Codelab.Security.ServiceAuth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Codelab.OpsConsole.Api.Tests.TestSupport;

/// <summary>
/// The real console (Program) on Kestrel (loopback, a free port), with a fresh ES256 key pair for this run, a fake /api/ops that validates every
/// assertion with the public half, and a fake Seq. By default Windows sign-in is replaced by
/// <see cref="TestOperatorAuthenticationHandler"/> (header-driven); pass <c>replaceSignIn: false</c> to keep the
/// host's own scheme (Negotiate, or the dev operator in Development).
/// </summary>
internal sealed class OpsConsoleHost : WebApplicationFactory<Program>
{
    public const string ServiceName = "starter-ops-console";
    public const string KeyId = "opstest";
    public const string OperatorGroupSid = "S-1-5-21-1000-2000-3000-1105";
    public const string ApiBaseUrl = "https://api.example.test/";
    public const string SeqBaseUrl = "https://seq.example.test/";
    public const string SeqApiKey = "seq-read-key-for-tests";

    // Kestrel on a free loopback port, not the in-memory test server: the Negotiate handler runs on every request and
    // needs a real connection (IConnectionItemsFeature).
    private const int DynamicPort = 0;

    private readonly string _environment;
    private readonly bool _replaceSignIn;
    private readonly Dictionary<string, string?> _settings;

    public OpsConsoleHost(Action<IDictionary<string, string?>>? configure = null, string environment = "Test", bool replaceSignIn = true)
    {
        _environment = environment;
        _replaceSignIn = replaceSignIn;
        Protocol = SecurityProtocol.Create(SecurityProtocol.TestPrefix);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Api = new FakeOpsApi(Protocol, ServiceName, KeyId, key.ExportSubjectPublicKeyInfoPem());
        _settings = BuildSettings(key.ExportPkcs8PrivateKeyPem());
        configure?.Invoke(_settings);
        UseKestrel(DynamicPort);
    }

    public SecurityProtocol Protocol { get; }

    public FakeOpsApi Api { get; }

    public FakeSeq Seq { get; } = new();

    /// <summary>A request signed in as <paramref name="name"/> with <paramref name="groups"/> (no sign-in when null).</summary>
    public static HttpRequestMessage OperatorRequest(string path, string? name = "CORP\\alice", params string[] groups)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (name is not null)
        {
            request.Headers.Add(TestOperatorAuthenticationHandler.NameHeader, name);
            request.Headers.Add(TestOperatorAuthenticationHandler.GroupsHeader, string.Join(',', groups.Length == 0 ? [OperatorGroupSid] : groups));
        }

        return request;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseEnvironment(_environment);
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(_settings));
        builder.ConfigureTestServices(services =>
        {
            services.AddHttpClient(OpsApiClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(Api.CreateHandler);
            services.AddHttpClient(SeqLogEventSource.HttpClientName).ConfigurePrimaryHttpMessageHandler(Seq.CreateHandler);
            if (_replaceSignIn)
            {
                ReplaceSignIn(services);
            }
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            Api.Dispose();
        }
    }

    private static void ReplaceSignIn(IServiceCollection services)
    {
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestOperatorAuthenticationHandler>(TestOperatorAuthenticationHandler.SchemeName, configureOptions: null);
        services.PostConfigure<AuthenticationOptions>(options =>
        {
            options.DefaultScheme = TestOperatorAuthenticationHandler.SchemeName;
            options.DefaultAuthenticateScheme = TestOperatorAuthenticationHandler.SchemeName;
            options.DefaultChallengeScheme = TestOperatorAuthenticationHandler.SchemeName;
            options.DefaultForbidScheme = TestOperatorAuthenticationHandler.SchemeName;
        });
    }

    private Dictionary<string, string?> BuildSettings(string privateKeyPem) => new(StringComparer.OrdinalIgnoreCase)
    {
        ["Codelab:Security:Protocol:Prefix"] = Protocol.Prefix,
        [$"{ServiceAssertionClientOptions.SectionName}:ServiceName"] = ServiceName,
        [$"{ServiceAssertionClientOptions.SectionName}:Audience"] = FakeOpsApi.Audience,
        [$"{ServiceAssertionClientOptions.SectionName}:KeyId"] = KeyId,
        [$"{ServiceAssertionClientOptions.SectionName}:PrivateKeyPem"] = privateKeyPem,
        ["Codelab:OpsConsole:OperatorGroup"] = OperatorGroupSid,
        ["Codelab:OpsConsole:Api:BaseUrl"] = ApiBaseUrl,
        ["Codelab:OpsConsole:Seq:BaseUrl"] = SeqBaseUrl,
        ["Codelab:OpsConsole:Seq:ApiKey"] = SeqApiKey,
        ["Codelab:OpsConsole:DevOperator:Enabled"] = "false",
        ["Observability:LogFile:Enabled"] = "false",
        ["Observability:Seq:ServerUrl"] = string.Empty,
    };
}
