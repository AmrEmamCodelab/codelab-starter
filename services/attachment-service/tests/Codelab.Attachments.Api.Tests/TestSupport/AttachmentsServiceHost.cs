using System.Net.Http.Headers;
using Codelab.Attachments.Api.Authentication;
using Codelab.Attachments.Core.Storage;
using Codelab.Security;
using Codelab.Security.Keys;
using Codelab.Security.ServiceAuth;
using Codelab.Security.Tickets;
using Codelab.Security.Uploads;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Codelab.Attachments.Api.Tests.TestSupport;

/// <summary>
/// The real service (Program) in memory, in the Test environment, on a temp storage folder, with keys generated for
/// this run and a fake clock shared by the host, the ticket sealer and the S2S issuer. Each setting is applied as a
/// host setting (seen while Program registers services) and as the last configuration source.
/// </summary>
internal sealed class AttachmentsServiceHost : WebApplicationFactory<Program>
{
    public const string EnvironmentName = "Test";
    public const string ApiServiceName = "starter-api";
    public const string Audience = "starter-attachments";
    public const string AllowedOrigin = "https://web.example.test";
    public const string Subject = "attachments-demo|42|demo-document|user-7";
    public const long MaxFileBytes = 64 * 1024;
    public const int MaxFileCount = 3;
    public const long MaxTotalBytes = 160 * 1024;

    private const string ServiceKeyId = "s2stest";

    private readonly Dictionary<string, string?> _settings;
    private readonly ServiceAssertionClientOptions _client;
    private readonly Action<IServiceCollection>? _configureServices;

    public AttachmentsServiceHost()
        : this(configure: null)
    {
    }

    public AttachmentsServiceHost(Action<IDictionary<string, string?>>? configure, Action<IServiceCollection>? configureServices = null)
    {
        _configureServices = configureServices;
        StorageRoot = Directory.CreateTempSubdirectory("codelab-attachments-api-tests-").FullName;
        Protocol = SecurityProtocol.Create(SecurityProtocol.TestPrefix);
        TicketRing = TestKeys.NewRing("t1");
        ReceiptRing = TestKeys.NewRing("r1");
        var (privatePem, publicPem) = TestKeys.NewServiceKeyPair();
        _client = new ServiceAssertionClientOptions { ServiceName = ApiServiceName, Audience = Audience, KeyId = ServiceKeyId, PrivateKeyPem = privatePem };
        _settings = BuildSettings(publicPem);
        configure?.Invoke(_settings);
    }

    public FakeTimeProvider Clock { get; } = new();

    public SecurityProtocol Protocol { get; }

    public string StorageRoot { get; }

    public KeyRingOptions TicketRing { get; }

    public KeyRingOptions ReceiptRing { get; }

    public string TicketHeader => UploadTickets.HeaderName(Protocol);

    /// <summary>A ticket sealed with the service's ring, as the API would seal it.</summary>
    public string SealTicket(string subject = Subject, TimeSpan? lifetime = null, string purpose = UploadTickets.Purpose) =>
        Services.GetRequiredService<ISealedTicketService>().Seal(new TicketRequest(purpose, subject, lifetime ?? TimeSpan.FromMinutes(5)));

    /// <summary>A request carrying a fresh service assertion for its method, path and (empty) body.</summary>
    public HttpRequestMessage ServiceRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        AddAssertion(request);
        return request;
    }

    /// <summary>Adds a fresh assertion bound to the request's method and target.</summary>
    public void AddAssertion(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var issuer = new ServiceAssertionIssuer(_client, Clock, Protocol);
        var uri = new Uri(Server.BaseAddress, request.RequestUri!);
        request.Headers.Add(Protocol.ServiceAssertionHeaderName, issuer.Issue(request.Method, uri, ReadOnlySpan<byte>.Empty));
    }

    /// <summary>A multipart upload of <paramref name="files"/> carrying <paramref name="ticket"/> (none when null).</summary>
    public HttpRequestMessage UploadRequest(string? ticket, params (string FileName, byte[] Content)[] files)
    {
        var body = new MultipartFormDataContent();
        foreach (var (fileName, content) in files)
        {
            var part = new ByteArrayContent(content);
            part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            body.Add(part, "file", fileName);
        }

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/uploads") { Content = body };
        if (ticket is not null)
        {
            request.Headers.Add(TicketHeader, ticket);
        }

        return request;
    }

    /// <summary>The verifier the API holds: the same receipt ring and protocol, built independently of the host.</summary>
    public UploadReceiptSigner ApiReceiptVerifier() => new(ReceiptRing, Protocol);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseEnvironment(EnvironmentName);
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(_settings));
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(Clock);
            _configureServices?.Invoke(services);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(StorageRoot))
        {
            Directory.Delete(StorageRoot, recursive: true);
        }
    }

    private Dictionary<string, string?> BuildSettings(string publicPem)
    {
        var settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Codelab:Security:Protocol:Prefix"] = Protocol.Prefix,
            [$"{SealedTicketOptions.SectionName}:Enabled"] = "true",
            [$"{UploadSafetyOptions.SectionName}:ReceiptsEnabled"] = "true",
            [$"{UploadSafetyOptions.SectionName}:MaxFileBytes"] = MaxFileBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [$"{UploadSafetyOptions.SectionName}:MaxFileCount"] = MaxFileCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [$"{UploadSafetyOptions.SectionName}:MaxTotalBytes"] = MaxTotalBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [$"{ServiceAssertionServerOptions.SectionName}:Audience"] = Audience,
            [$"{ServiceAssertionServerOptions.SectionName}:TrustedServices:0:ServiceName"] = ApiServiceName,
            [$"{ServiceAssertionServerOptions.SectionName}:TrustedServices:0:KeyId"] = ServiceKeyId,
            [$"{ServiceAssertionServerOptions.SectionName}:TrustedServices:0:PublicKeyPem"] = publicPem,
            [$"{AttachmentStorageOptions.SectionName}:RootPath"] = StorageRoot,
            ["Attachments:Cors:AllowedOrigins:0"] = AllowedOrigin,
            ["Observability:LogFile:Enabled"] = "false",
        };
        foreach (var (key, value) in TestKeys.AsSettings(TicketRing, SealedTicketOptions.SectionName).Concat(TestKeys.AsSettings(ReceiptRing, UploadSafetyOptions.SectionName)))
        {
            settings[key] = value;
        }

        return settings;
    }
}
