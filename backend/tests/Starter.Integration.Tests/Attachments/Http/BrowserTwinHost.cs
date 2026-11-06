using System.IO.Pipelines;
using Codelab.Security;
using Codelab.Security.Cors;
using Codelab.Security.Signing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Starter.Integration.Tests.Support;
using Starter.Modules.Attachments.Application;
using Starter.Tests.Hosting;

namespace Starter.Integration.Tests.Attachments.Http;

/// <summary>
/// A second host on the <see cref="AttachmentsHttpHost"/> database, set up as the browser meets the API: real Kestrel
/// on a loopback port (not the in-memory test server), request and response signing Enforce, CORS for the
/// Development SPA origin, and storage streams that cannot seek (as the attachment service's HTTP body). Regression
/// for the phase 6 download that the browser saw without <c>Access-Control-Allow-Origin</c>.
/// </summary>
public sealed class BrowserTwinHost : IAsyncDisposable
{
    /// <summary>The SPA origin the twin allows cross-origin.</summary>
    public const string WebOrigin = "http://localhost:4280";

    private const string FirstItem = "0";
    private const int AnyFreePort = 0;

    private readonly StarterApiFactory _factory;
    private readonly StarterTestAccounts _accounts;

    private BrowserTwinHost(StarterApiFactory factory, StarterTestAccounts accounts)
    {
        _factory = factory;
        _accounts = accounts;
    }

    /// <summary>Starts the twin of <paramref name="host"/> (same settings and storage, the browser-facing overrides).</summary>
    public static BrowserTwinHost Start(AttachmentsHttpHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        var settings = new Dictionary<string, string?>(host.Factory.Settings, StringComparer.OrdinalIgnoreCase)
        {
            [StarterHostSettings.Key(RequestSigningOptions.SectionName, nameof(RequestSigningOptions.Mode))] = nameof(SecurityEnforcementMode.Enforce),
            [StarterHostSettings.Key(ResponseSigningOptions.SectionName, nameof(ResponseSigningOptions.Mode))] = nameof(SecurityEnforcementMode.Enforce),
            [StarterHostSettings.Key(CodelabCorsOptions.SectionName, nameof(CodelabCorsOptions.AllowedOrigins), FirstItem)] = WebOrigin,
        };
        var factory = new StarterApiFactory(host.EnvironmentName, settings, services =>
        {
            services.RemoveAll<IAttachmentStorageClient>();
            services.AddSingleton<IAttachmentStorageClient>(new UnseekableStorage(host.Storage));
        });
        factory.UseKestrel(AnyFreePort);
        factory.StartServer();
        return new BrowserTwinHost(factory, host.Accounts);
    }

    /// <summary>
    /// Signs the break-glass administrator in on the twin and returns the access token (a token the twin issued carries
    /// the signing key id; one from the unsigned host does not).
    /// </summary>
    public async Task<string> AdminTokenAsync()
    {
        using var client = new StarterClient(_factory.CreateClient());
        return (await client.LoginAsync(_accounts.BreakGlassUserName, _accounts.BreakGlassPassword)).AccessToken;
    }

    /// <summary>A signed browser call over a real socket to the twin.</summary>
    public SignedBrowserCall CreateBrowser() =>
        new(_factory.CreateClient(), _factory.Services.GetRequiredService<SecurityProtocol>());

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    /// <summary>The in-memory storage, its bytes served through a stream that cannot seek or report a length.</summary>
    private sealed class UnseekableStorage(InMemoryAttachmentStorage inner) : IAttachmentStorageClient
    {
        public async Task<Stream?> OpenReadAsync(string fileId, CancellationToken cancellationToken) =>
            await inner.OpenReadAsync(fileId, cancellationToken) is { } content ? PipeReader.Create(content).AsStream() : null;

        public Task<bool> DeleteAsync(string fileId, CancellationToken cancellationToken) => inner.DeleteAsync(fileId, cancellationToken);
    }
}
