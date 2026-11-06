using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using Codelab.Security;
using Codelab.Security.Replay;
using Codelab.Security.Tickets;
using Codelab.Testing.Architecture;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Starter.Domain.Attachments;
using Starter.Integration.Tests.Support;
using Starter.Modules.Attachments.Application;
using Starter.Modules.Attachments.Infrastructure.Options;
using Starter.Tests.Hosting;

namespace Starter.Integration.Tests.Attachments.Http;

/// <summary>
/// Regression for the split ticket rings on a fresh clone: <c>tools/devseed/New-LocalSecrets.ps1</c> generates a new
/// API and attachment-service pair, the API runs with the generated ticket rings, and the ring the service file holds
/// (its only <c>Codelab:Security:Tickets</c>) opens the upload ticket the API issues over HTTP, while it never opens a
/// ticket sealed with the API's own ring (SSO, activation).
/// </summary>
[Collection(AttachmentsHttpHostGroup.Name)]
public sealed class LocalSecretsUploadPairTests(AttachmentsHttpHost host) : IAsyncLifetime
{
    private const string SolutionFile = "codelab-starter.slnx";
    private const string ScriptRelativePath = "tools/devseed/New-LocalSecrets.ps1";
    private const string PowerShell = "powershell.exe";
    private const string ApiFileName = "api.local.json";
    private const string ServiceFolderName = "service";
    private const string ServiceFileName = "appsettings.Development.local.json";
    private const string StorageFolderName = "storage";
    private const string DemoRecordPath = "/api/attachments/demo-record";
    private const string ActivationPurpose = "account-activation";
    private const int ScriptTimeoutMilliseconds = 120_000;
    private const int ReplayEntryLimit = 100;
    private const int ScriptSucceeded = 0;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "starter-local-secrets-" + Guid.NewGuid().ToString("N"));

    /// <inheritdoc />
    public Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(_folder, ServiceFolderName));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DisposeAsync()
    {
        Directory.Delete(_folder, recursive: true);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task A_freshly_generated_pair_uploads_and_the_service_ring_never_opens_an_api_ticket()
    {
        await GenerateAsync();
        var api = Load(ApiFileName);
        var service = Load(Path.Combine(ServiceFolderName, ServiceFileName));
        await using var factory = StartWith(RingsOf(api));
        var serviceTickets = ServiceTickets(service, factory.Services.GetRequiredService<SecurityProtocol>());

        var ticket = await IssueUploadTicketAsync(factory);
        var activation = factory.Services.GetRequiredService<ISealedTicketService>()
            .Seal(new TicketRequest(ActivationPurpose, "user-1", TimeSpan.FromMinutes(5)));

        Assert.Equal(TicketOpenStatus.Valid, (await serviceTickets.OpenAsync(UploadTickets.Purpose, ticket, CancellationToken.None)).Status);
        Assert.Equal(TicketOpenStatus.Invalid, (await serviceTickets.OpenAsync(ActivationPurpose, activation, CancellationToken.None)).Status);
    }

    private async Task GenerateAsync()
    {
        var script = Path.Combine(RepositoryRootPath(), ScriptRelativePath);
        using var process = Process.Start(new ProcessStartInfo(PowerShell,
        [
            "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script,
            "-Path", Path.Combine(_folder, ApiFileName),
            "-ServicePath", Path.Combine(_folder, ServiceFolderName, ServiceFileName),
            "-StorageRoot", Path.Combine(_folder, StorageFolderName),
        ])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException($"Could not run {PowerShell}.");
        using var timeout = new CancellationTokenSource(ScriptTimeoutMilliseconds);
        var errors = process.StandardError.ReadToEndAsync(timeout.Token);
        _ = await process.StandardOutput.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        Assert.True(process.ExitCode == ScriptSucceeded, $"New-LocalSecrets.ps1 exited {process.ExitCode}: {await errors}");
    }

    // The test output folder normally sits in the repository; a build with an artifacts path outside it falls back to this source file.
    private static string RepositoryRootPath([CallerFilePath] string sourceFile = "")
    {
        try
        {
            return RepositoryRoot.Find(SolutionFile);
        }
        catch (DirectoryNotFoundException)
        {
            return RepositoryRoot.Find(SolutionFile, Path.GetDirectoryName(sourceFile));
        }
    }

    private IConfigurationRoot Load(string relativePath) =>
        new ConfigurationBuilder().AddJsonFile(Path.Combine(_folder, relativePath), optional: false, reloadOnChange: false).Build();

    // Only the two ticket rings come from the generated file; the rest stays the shared host's (its accounts and PublicIds).
    private static Dictionary<string, string?> RingsOf(IConfiguration api) =>
        new[] { SealedTicketOptions.SectionName, UploadTicketRingOptions.SectionName }
            .SelectMany(section => api.GetSection(section).AsEnumerable())
            .Where(pair => pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

    private StarterApiFactory StartWith(Dictionary<string, string?> rings)
    {
        var settings = new Dictionary<string, string?>(host.Factory.Settings, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in rings)
        {
            settings[key] = value;
        }

        var factory = new StarterApiFactory(host.EnvironmentName, settings, services =>
        {
            services.RemoveAll<IAttachmentStorageClient>();
            services.AddSingleton<IAttachmentStorageClient>(host.Storage);
        });
        _ = factory.Services;
        return factory;
    }

    // What the attachment service builds from its file: its Codelab:Security:Tickets section is its only ticket ring.
    private static SealedTicketService ServiceTickets(IConfiguration service, SecurityProtocol protocol)
    {
        var ring = service.GetSection(SealedTicketOptions.SectionName).Get<SealedTicketOptions>()
            ?? throw new InvalidOperationException($"The service file has no {SealedTicketOptions.SectionName}.");
        return new SealedTicketService(ring, new MemoryReplayGuard(TimeProvider.System, ReplayEntryLimit), TimeProvider.System, protocol);
    }

    private async Task<string> IssueUploadTicketAsync(StarterApiFactory factory)
    {
        using var client = new StarterClient(factory.CreateClient());
        var token = (await client.LoginAsync(host.Accounts.BreakGlassUserName, host.Accounts.BreakGlassPassword)).AccessToken;
        using var api = new ApiClient(factory.CreateClient());
        using var demo = await api.GetAsync(DemoRecordPath, token);
        var record = await ApiClient.ReadAsync(demo, HttpStatusCode.OK);
        var path = "/api/records/" + AttachableRecordTypes.Demo + "/" + record.GetProperty("recordId").GetString() + "/attachments/upload-ticket";
        using var issued = await api.PostAsync(path, new { typeKey = AttachmentTypeKeys.DemoDocument }, token);
        return (await ApiClient.ReadAsync(issued, HttpStatusCode.OK)).GetProperty("ticket").GetString()!;
    }
}
