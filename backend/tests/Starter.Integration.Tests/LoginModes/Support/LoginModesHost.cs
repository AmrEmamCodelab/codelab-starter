using System.Collections.Concurrent;
using Codelab.Identity.ActiveDirectory;
using Codelab.Identity.Oidc;
using Codelab.Security.Keys;
using Codelab.Security.Signing;
using Codelab.Security.Tickets;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Starter.Application.Users;
using Starter.DevIdp;
using Starter.Domain.Users;
using Starter.Infrastructure.Modularity;
using Starter.Integration.Tests.Support;
using Starter.Modules.Identity.Infrastructure.LoginModes;
using Starter.Tests.Hosting;
using DevIdpRoutes = Starter.DevIdp.Protocol.OidcRoutes;

namespace Starter.Integration.Tests.LoginModes.Support;

/// <summary>
/// The real host in Development on its own throwaway database <c>StarterIt_&lt;guid&gt;</c> with all three login modes
/// on: <c>Auth:Modes</c> = basic, ad, sso; Active Directory answered by <see cref="Directory"/> (the in-process LDAP
/// double); SSO against <see cref="Starter.DevIdp"/> hosted in process (the API's named OIDC client is routed to it);
/// sealed tickets on with a fresh ring. Stop: dispose the host and the IdP, then drop ONLY that database.
/// </summary>
public sealed class LoginModesHost : IAsyncLifetime, IDisposable
{
    /// <summary>The SPA origin the SSO callback redirects to (the dev IdP's registered post-logout URI is on it).</summary>
    public const string WebBaseUrl = "http://localhost:4280";

    /// <summary>The API redirect URI registered at the dev IdP.</summary>
    public const string RedirectUri = "http://localhost:5180/api/auth/sso/signin-oidc";

    /// <summary>The dev IdP's registered post-logout redirect URI.</summary>
    public const string PostLogoutRedirectUri = WebBaseUrl + "/login";

    /// <summary>The dev IdP's public client.</summary>
    public const string ClientId = "starter-web";

    /// <summary>The directory domain a bare user name binds in.</summary>
    public const string DirectoryDomain = "example.test";

    private const string OrganizationIdText = "1";
    private const string FirstItem = "0";
    private readonly ConcurrentDictionary<string, long> _ensured = new(StringComparer.OrdinalIgnoreCase);
    private StarterApiFactory? _factory;
    private DevIdpInProcessServer? _idp;

    /// <summary>Creates the fixture.</summary>
    public LoginModesHost() =>
        DatabaseName = $"{ModuleNaming.SolutionTokenOf(typeof(Program).Assembly)}{TestSqlServer.DatabaseMarker}{Guid.NewGuid():N}";

    /// <summary>The throwaway database this fixture owns.</summary>
    public string DatabaseName { get; }

    /// <summary>The accounts seeded at start.</summary>
    public StarterTestAccounts Accounts { get; } = StarterTestAccounts.Generate();

    /// <summary>The in-process Active Directory.</summary>
    public InProcessDirectory Directory { get; } = new();

    /// <summary>The started host.</summary>
    public StarterApiFactory Factory => _factory ?? throw new InvalidOperationException("The host has not started.");

    /// <summary>The started dev IdP.</summary>
    public DevIdpInProcessServer IdentityProvider => _idp ?? throw new InvalidOperationException("The IdP has not started.");

    /// <summary>A client of the host that keeps cookies and never follows a redirect (the test plays the browser).</summary>
    public HttpClient CreateBrowser() => Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    /// <summary>Creates an account bound to <paramref name="binding"/> with <paramref name="loginName"/>, as the Users module would.</summary>
    public async Task<long> CreateAccountAsync(string loginName, AccountBinding binding)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var created = await scope.ServiceProvider.GetRequiredService<IAccountAdministration>().CreateAsync(
            new NewAccount(
                UserName: "lm-" + loginName + "-" + Guid.NewGuid().ToString("N")[..8],
                DisplayName: loginName,
                Email: null,
                LoginName: loginName,
                Upn: null,
                Kind: UserKind.External,
                Binding: binding,
                OrganizationId: long.Parse(OrganizationIdText, System.Globalization.CultureInfo.InvariantCulture)),
            CancellationToken.None);
        Assert.True(created.IsSuccess, created.Refusal?.Code);
        return created.Value.UserId;
    }

    /// <summary>The account with <paramref name="loginName"/>, created on first use (fixture-wide).</summary>
    public async Task<long> EnsureAccountAsync(string loginName, AccountBinding binding)
    {
        if (_ensured.TryGetValue(loginName, out var existing))
        {
            return existing;
        }

        var created = await CreateAccountAsync(loginName, binding);
        _ensured[loginName] = created;
        return created;
    }

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await TestSqlServer.EnsureStartedAsync();
        await TestSqlServer.CreateDatabaseAsync(DatabaseName);
        try
        {
            _idp = await DevIdpInProcessServer.StartAsync();
            _factory = new StarterApiFactory(Environments.Development, Settings(), ConfigureServices);
            _ = _factory.Services;
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        try
        {
            if (_factory is not null)
            {
                await _factory.DisposeAsync();
                _factory = null;
            }

            if (_idp is not null)
            {
                await _idp.DisposeAsync();
                _idp = null;
            }
        }
        finally
        {
            await TestSqlServer.DropDatabaseAsync(DatabaseName);
        }
    }

    /// <summary>Disposes the host if <see cref="DisposeAsync"/> did not already (that path also drops the database).</summary>
    public void Dispose()
    {
        _factory?.Dispose();
        _factory = null;
    }

    private Dictionary<string, string?> Settings()
    {
        var options = new StarterHostOptions(
            ConnectionString: TestSqlServer.ConnectionStringFor(DatabaseName),
            UsesDatabase: true,
            ApplyMigrationsOnStartup: true,
            SigningMode: SecurityEnforcementMode.Off);
        var settings = new Dictionary<string, string?>(StarterHostSettings.For(options, Accounts), StringComparer.OrdinalIgnoreCase);
        string[] modes = [AuthModeNames.Basic, AuthModeNames.ActiveDirectory, AuthModeNames.Sso];
        for (var index = 0; index < modes.Length; index++)
        {
            settings[StarterHostSettings.Key(AuthModesOptions.SectionName, nameof(AuthModesOptions.Modes), index.ToString(System.Globalization.CultureInfo.InvariantCulture))] = modes[index];
        }

        settings[StarterHostSettings.Key(AuthModesOptions.SectionName, nameof(AuthModesOptions.WebBaseUrl))] = WebBaseUrl;
        AddDirectory(settings);
        AddSso(settings, IdentityProvider.Issuer.AbsoluteUri.TrimEnd('/'));
        var keyId = StarterTestAccounts.NewKeyId();
        settings[StarterHostSettings.Key(SealedTicketOptions.SectionName, nameof(SealedTicketOptions.Enabled))] = bool.TrueString;
        settings[StarterHostSettings.Key(SealedTicketOptions.SectionName, nameof(KeyRingOptions.ActiveKeyId))] = keyId;
        settings[StarterHostSettings.Key(SealedTicketOptions.SectionName, nameof(KeyRingOptions.Keys), FirstItem, nameof(KeyEntryOptions.KeyId))] = keyId;
        settings[StarterHostSettings.Key(SealedTicketOptions.SectionName, nameof(KeyRingOptions.Keys), FirstItem, nameof(KeyEntryOptions.MaterialBase64))] = StarterTestAccounts.NewKeyBase64();
        return settings;
    }

    private static void AddDirectory(Dictionary<string, string?> settings)
    {
        settings[StarterHostSettings.Key(ActiveDirectoryOptions.SectionName, nameof(ActiveDirectoryOptions.Host))] = "dc." + DirectoryDomain;
        settings[StarterHostSettings.Key(ActiveDirectoryOptions.SectionName, nameof(ActiveDirectoryOptions.Domain))] = DirectoryDomain;
        settings[StarterHostSettings.Key(ActiveDirectoryOptions.SectionName, nameof(ActiveDirectoryOptions.BaseDn))] = "DC=example,DC=test";
        settings[StarterHostSettings.Key(ActiveDirectoryOptions.SectionName, nameof(ActiveDirectoryOptions.MatchBy))] = nameof(Codelab.Identity.LoginMatchField.LoginName);
    }

    private static void AddSso(Dictionary<string, string?> settings, string issuer)
    {
        settings[Sso(nameof(OidcOptions.Authority))] = issuer;
        settings[Sso(nameof(OidcOptions.ClientId))] = ClientId;
        settings[Sso(nameof(OidcOptions.RedirectUri))] = RedirectUri;
        settings[Sso(nameof(OidcOptions.PostLogoutRedirectUri))] = PostLogoutRedirectUri;
        settings[Sso(nameof(OidcOptions.AuthorizationEndpoint))] = issuer + DevIdpRoutes.Authorize;
        settings[Sso(nameof(OidcOptions.TokenEndpoint))] = issuer + DevIdpRoutes.Token;
        settings[Sso(nameof(OidcOptions.EndSessionEndpoint))] = issuer + DevIdpRoutes.EndSession;
        settings[Sso(nameof(OidcOptions.JwksUri))] = issuer + DevIdpRoutes.Jwks;
        settings[Sso(nameof(OidcOptions.MatchBy))] = nameof(Codelab.Identity.LoginMatchField.LoginName);
    }

    private static string Sso(string key) => StarterHostSettings.Key(OidcOptions.SectionName, key);

    private void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ILdapConnectionFactory>(Directory);
        services.AddHttpClient(OidcHttpClient.Name).ConfigurePrimaryHttpMessageHandler(() => IdentityProvider.CreateHandler());
    }
}

/// <summary>Tests sharing the login-modes host (sequential, one database).</summary>
[CollectionDefinition(Name)]
public sealed class LoginModesHostGroup : ICollectionFixture<LoginModesHost>
{
    /// <summary>The collection name.</summary>
    public const string Name = "login modes host";
}
