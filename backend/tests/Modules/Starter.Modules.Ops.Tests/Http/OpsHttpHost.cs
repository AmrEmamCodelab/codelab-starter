using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Codelab.Errors;
using Codelab.Observability;
using Codelab.Security;
using Codelab.Security.ServiceAuth;
using Codelab.Security.Signing;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Starter.Infrastructure.Errors;
using Starter.Infrastructure.Hosting;
using Starter.Infrastructure.Modularity;

namespace Starter.Modules.Ops.Tests.Http;

/// <summary>
/// The kernel host (the same wiring as Starter.Api's Program) with only the Ops module, on a throwaway database, with
/// request and response signing Enforce, fresh keys made at run time, and two extra test credentials that are valid
/// elsewhere in the host (a bearer token as the default scheme, an API-key header scheme), so a test can prove they open
/// nothing under <c>/api/ops</c>.
/// </summary>
public sealed class OpsHttpHost : IAsyncDisposable
{
    public const string Audience = "starter-api";
    public const string ConsoleService = "starter-ops-console";
    public const string GatewayService = "starter-gateway";
    public const string BearerScheme = "TestBearer";
    public const string ApiKeyScheme = "TestApiKey";
    public const string ValidBearerToken = "valid-user-token";
    public const string ApiKeyHeader = "Api-Key";
    public const string ValidApiKey = "valid-api-key";
    public const string BearerProbePath = "/test/bearer";
    public const string ApiKeyProbePath = "/test/api-key";

    private readonly WebApplication _app;

    private OpsHttpHost(WebApplication app, ServiceKey console, ServiceKey gateway)
    {
        _app = app;
        Console = console;
        Gateway = gateway;
        Client = app.GetTestClient();
    }

    /// <summary>The Ops Console API's key (trusted for /api/ops when the host is open).</summary>
    public ServiceKey Console { get; }

    /// <summary>Another trusted service's key (trusted by the host, not for /api/ops).</summary>
    public ServiceKey Gateway { get; }

    public HttpClient Client { get; }

    public IServiceProvider Services => _app.Services;

    public SecurityProtocol Protocol => _app.Services.GetRequiredService<SecurityProtocol>();

    /// <summary>Starts the host on <paramref name="connectionString"/>; <paramref name="open"/> false leaves Codelab:Ops:TrustedCallerKeyId empty.</summary>
    public static async Task<OpsHttpHost> StartAsync(string connectionString, bool open, IDictionary<string, string?>? extra = null)
    {
        var console = ServiceKey.New(ConsoleService);
        var gateway = ServiceKey.New(GatewayService);
        var settings = Settings(connectionString, console, gateway, open);
        foreach (var (key, value) in extra ?? new Dictionary<string, string?>())
        {
            settings[key] = value;
        }

        var app = Build(settings);
        await app.StartAsync();
        return new OpsHttpHost(app, console, gateway);
    }

    /// <summary>A fresh assertion for one request, signed by <paramref name="key"/>.</summary>
    public string Assertion(ServiceKey key, HttpMethod method, string pathAndQuery, byte[]? body = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        using var issuer = new ServiceAssertionIssuer(
            new ServiceAssertionClientOptions { ServiceName = key.ServiceName, Audience = Audience, KeyId = key.KeyId, PrivateKeyPem = key.PrivatePem },
            TimeProvider.System,
            Protocol);
        return issuer.Issue(method, new Uri(Client.BaseAddress!, pathAndQuery), body ?? []);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private static WebApplication Build(Dictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Logging.ClearProviders();
        var catalog = ModuleCatalog.FromModules([new OpsModule()]);
        builder.AddCodelabObservability("starter-ops-tests");
        builder.Services.AddStarterKernel(builder.Configuration);
        builder.Services.AddCodelabErrors(errors => errors.AddStarterFailures());
        builder.AddCodelabSecurity();
        builder.Services.AddAuthentication(options => options.DefaultScheme = BearerScheme)
            .AddScheme<AuthenticationSchemeOptions, TestBearerHandler>(BearerScheme, configureOptions: null)
            .AddScheme<AuthenticationSchemeOptions, TestApiKeyHandler>(ApiKeyScheme, configureOptions: null);
        builder.Services.AddStarterModules(catalog, builder.Configuration);
        var app = builder.Build();
        app.UseCodelabObservability();
        app.UseCodelabErrors();
        app.UseStarterPipeline();
        app.MapStarterEndpoints(catalog);
        MapCredentialProbes(app);
        return app;
    }

    private static void MapCredentialProbes(WebApplication app)
    {
        var exempt = new SignatureExemptAttribute(SignatureExemption.WholeRequest, "Test probe: proves the test credential is valid in this host.");
        app.MapGet(BearerProbePath, () => Results.Ok())
            .RequireAuthorization(policy => policy.AddAuthenticationSchemes(BearerScheme).RequireAuthenticatedUser())
            .WithMetadata(exempt);
        app.MapGet(ApiKeyProbePath, () => Results.Ok())
            .RequireAuthorization(policy => policy.AddAuthenticationSchemes(ApiKeyScheme).RequireAuthenticatedUser())
            .WithMetadata(exempt);
    }

    private static Dictionary<string, string?> Settings(string connectionString, ServiceKey console, ServiceKey gateway, bool open)
    {
        const string server = "Codelab:Security:ServiceAssertions:Server";
        var settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["ConnectionStrings:Starter"] = connectionString,
            ["Database:ApplyMigrationsOnStartup"] = bool.TrueString,
            ["Codelab:Security:Protocol:Prefix"] = "STARTER",
            ["Codelab:Security:SqlStore:EnsureSchemaOnStart"] = bool.FalseString,
            ["Codelab:Security:ReplayProtection:AcknowledgeSingleInstance"] = bool.TrueString,
            ["Codelab:Security:RequestSigning:Mode"] = nameof(SecurityEnforcementMode.Enforce),
            ["Codelab:Security:RequestSigning:Audience"] = Audience,
            ["Codelab:Security:ResponseSigning:Mode"] = nameof(SecurityEnforcementMode.Enforce),
            // The test bearer and API-key principals carry no session id; the probes only prove the credentials are valid here.
            ["Codelab:Security:Sessions:ValidateOnEveryRequest"] = bool.FalseString,
            [UserIdHashSettings.KeyName] = NewKeyBase64(),
            [$"{LogFileSettings.KeyPrefix}:{nameof(LogFileSettings.Enabled)}"] = bool.FalseString,
            [$"{SeqSettings.KeyPrefix}:{nameof(SeqSettings.ServerUrl)}"] = string.Empty,
            ["Jobs:Enabled"] = bool.FalseString,
        };
        AddKeyRing(settings, "Codelab:Security:PublicIds");
        AddKeyRing(settings, "Codelab:Security:RequestSigning");
        if (open)
        {
            settings[$"{server}:Audience"] = Audience;
            AddTrusted(settings, server, index: 0, console);
            AddTrusted(settings, server, index: 1, gateway);
            settings["Codelab:Ops:TrustedCallerKeyId"] = console.KeyId;
        }

        return settings;
    }

    private static void AddTrusted(Dictionary<string, string?> settings, string server, int index, ServiceKey key)
    {
        settings[$"{server}:TrustedServices:{index}:ServiceName"] = key.ServiceName;
        settings[$"{server}:TrustedServices:{index}:KeyId"] = key.KeyId;
        settings[$"{server}:TrustedServices:{index}:PublicKeyPem"] = key.PublicPem;
    }

    private static void AddKeyRing(Dictionary<string, string?> settings, string section)
    {
        var keyId = "t" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(3));
        settings[$"{section}:ActiveKeyId"] = keyId;
        settings[$"{section}:Keys:0:KeyId"] = keyId;
        settings[$"{section}:Keys:0:MaterialBase64"] = NewKeyBase64();
    }

    private static string NewKeyBase64() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    /// <summary>Authenticates <c>Authorization: Bearer valid-user-token</c> as a user: a valid credential of this host.</summary>
    private sealed class TestBearerHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
            Task.FromResult(Request.Headers.Authorization.ToString() == $"Bearer {ValidBearerToken}"
                ? Success(Scheme.Name, "user-1")
                : AuthenticateResult.NoResult());
    }

    /// <summary>Authenticates the API-key header <c>Api-Key: valid-api-key</c>: a valid credential of this host.</summary>
    private sealed class TestApiKeyHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
            Task.FromResult(Request.Headers[ApiKeyHeader].ToString() == ValidApiKey
                ? Success(Scheme.Name, "api-client")
                : AuthenticateResult.NoResult());
    }

    private static AuthenticateResult Success(string scheme, string subject) =>
        AuthenticateResult.Success(new AuthenticationTicket(
            new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, subject)], scheme)), scheme));
}

/// <summary>A service's ES256 key pair, made at run time.</summary>
public sealed record ServiceKey(string ServiceName, string KeyId, string PrivatePem, string PublicPem)
{
    public static ServiceKey New(string serviceName)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return new ServiceKey(serviceName, serviceName + "-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4)), key.ExportPkcs8PrivateKeyPem(), key.ExportSubjectPublicKeyInfoPem());
    }
}

/// <summary>A throwaway database on the test SQL Server, created at start and dropped at the end.</summary>
public sealed class TestDatabase : IAsyncDisposable
{
    public const string DefaultServer = "(localdb)\\P2Proof";
    public const string ServerVariable = "CODELAB_TEST_SQL";
    public const string Marker = "StarterOpsIt_";

    private TestDatabase(string server, string name)
    {
        Server = server;
        Name = name;
    }

    public string Server { get; }

    public string Name { get; }

    public string ConnectionString => Connection(Name);

    public static async Task<TestDatabase> CreateAsync()
    {
        var configured = Environment.GetEnvironmentVariable(ServerVariable);
        var server = string.IsNullOrWhiteSpace(configured) ? DefaultServer : new SqlConnectionStringBuilder(configured.Contains('=', StringComparison.Ordinal) ? configured : $"Server={configured}").DataSource;
        var database = new TestDatabase(server, Marker + Guid.NewGuid().ToString("N"));
        await database.ExecuteOnMasterAsync($"CREATE DATABASE [{database.Name}]");
        return database;
    }

    public async ValueTask DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        await ExecuteOnMasterAsync($"IF DB_ID(N'{Name}') IS NOT NULL BEGIN ALTER DATABASE [{Name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{Name}]; END");
    }

    private string Connection(string database) =>
        new SqlConnectionStringBuilder { DataSource = Server, InitialCatalog = database, IntegratedSecurity = true, TrustServerCertificate = true }.ConnectionString;

    private async Task ExecuteOnMasterAsync(string sql)
    {
        if (!Name.StartsWith(Marker, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Refusing to touch a database without the test marker.");
        }

        await using var connection = new SqlConnection(Connection("master"));
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
