using System.Security.Claims;
using Codelab.Security.ServiceAuth;
using Codelab.Security.Sessions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Starter.Modules.Ops.Infrastructure.Authorization;
using Starter.Modules.Ops.Infrastructure.Options;

namespace Starter.Modules.Ops.Tests.Infrastructure;

public sealed class OpsOptionsValidatorTests
{
    private readonly OpsOptionsValidator _validator = new();

    [Fact]
    public void The_defaults_are_valid_and_closed()
    {
        var options = new OpsOptions();

        Assert.True(_validator.Validate(name: null, options).Succeeded);
        Assert.False(options.IsOpen);
        Assert.True(options.CaptureReferenceCodes);
        Assert.Equal(TimeSpan.FromMinutes(15), options.ToSettings().SearchMargin);
        Assert.Equal(TimeSpan.FromDays(30), options.ToSettings().SightingRetention);
        Assert.Equal(30, options.ToSettings().Thresholds.WarningDays);
        Assert.Equal(7, options.ToSettings().Thresholds.CriticalDays);
    }

    [Theory]
    [InlineData(7, 7)]
    [InlineData(30, 0)]
    [InlineData(400, 7)]
    public void Inconsistent_thresholds_stop_the_host(int warning, int critical) =>
        AssertFails(new OpsOptions { CertificateWarningDays = warning, CertificateCriticalDays = critical }, nameof(OpsOptions.CertificateCriticalDays));

    [Fact]
    public void Out_of_range_numbers_stop_the_host()
    {
        AssertFails(new OpsOptions { ProbeTimeoutSeconds = 0 }, nameof(OpsOptions.ProbeTimeoutSeconds));
        AssertFails(new OpsOptions { ProbeTimeoutSeconds = 31 }, nameof(OpsOptions.ProbeTimeoutSeconds));
        AssertFails(new OpsOptions { SearchMarginMinutes = 0 }, nameof(OpsOptions.SearchMarginMinutes));
        AssertFails(new OpsOptions { ReferenceCodeRetentionDays = 366 }, nameof(OpsOptions.ReferenceCodeRetentionDays));
        AssertFails(new OpsOptions { IdentityProviderJwksUrl = "not a url" }, nameof(OpsOptions.IdentityProviderJwksUrl));
        AssertFails(new OpsOptions { IdentityProviderJwksUrl = "ftp://idp.example.test/jwks" }, nameof(OpsOptions.IdentityProviderJwksUrl));
    }

    [Fact]
    public void Probes_need_unique_kebab_names_and_absolute_http_urls()
    {
        Assert.True(_validator.Validate(null, WithProbes(("attachment-service", "https://att.example.test/health/ready"))).Succeeded);
        AssertFails(WithProbes(("Attachment Service", "https://att.example.test/health/ready")), nameof(OpsOptions.ReadinessProbes));
        AssertFails(WithProbes(("gateway", "/health/ready")), nameof(OpsOptions.ReadinessProbes));
        AssertFails(WithProbes(("gateway", "https://a.example.test/"), ("gateway", "https://b.example.test/")), nameof(OpsOptions.ReadinessProbes));
    }

    [Fact]
    public void Certificates_need_a_name_and_exactly_one_source()
    {
        Assert.True(_validator.Validate(null, WithCertificate(new WatchedCertificateOptions { Name = "api-tls", Thumbprint = "AB CD" })).Succeeded);
        Assert.True(_validator.Validate(null, WithCertificate(new WatchedCertificateOptions { Name = "signing", Path = "C:\\certs\\signing.cer" })).Succeeded);
        AssertFails(WithCertificate(new WatchedCertificateOptions { Name = "both", Thumbprint = "AB", Path = "x.cer" }), "exactly one");
        AssertFails(WithCertificate(new WatchedCertificateOptions { Name = "neither" }), "exactly one");
        AssertFails(WithCertificate(new WatchedCertificateOptions { Name = "store", Thumbprint = "AB", StoreName = "Nowhere" }), "StoreName");
        AssertFails(WithCertificate(new WatchedCertificateOptions { Name = "-bad", Thumbprint = "AB" }), "kebab");
    }

    [Theory]
    [InlineData("attachment-service", true)]
    [InlineData("a1", true)]
    [InlineData("", false)]
    [InlineData("1abc", false)]
    [InlineData("abc-", false)]
    [InlineData("a--b", false)]
    [InlineData("a_b", false)]
    public void Kebab_names(string name, bool valid) => Assert.Equal(valid, OpsOptionsValidator.IsKebabName(name));

    [Fact]
    public void The_section_binds_every_key()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Codelab:Ops:TrustedCallerKeyId"] = "ops-1",
            ["Codelab:Ops:CertificateWarningDays"] = "45",
            ["Codelab:Ops:CertificateCriticalDays"] = "10",
            ["Codelab:Ops:ReadinessProbes:0:Name"] = "integration-gateway",
            ["Codelab:Ops:ReadinessProbes:0:Url"] = "https://gw.example.test/health/ready",
            ["Codelab:Ops:Certificates:0:Name"] = "api-tls",
            ["Codelab:Ops:Certificates:0:Thumbprint"] = "AB",
        }).Build();

        var options = configuration.GetSection(OpsOptions.SectionName).Get<OpsOptions>()!;

        Assert.True(options.IsOpen);
        Assert.Equal(45, options.CertificateWarningDays);
        Assert.Equal("integration-gateway", Assert.Single(options.ReadinessProbes).Name);
        Assert.Equal("My", Assert.Single(options.Certificates).StoreName);
        Assert.Equal("LocalMachine", options.Certificates[0].StoreLocation);
    }

    private void AssertFails(OpsOptions options, string expectedFragment)
    {
        var result = _validator.Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.Contains(expectedFragment, result.FailureMessage, StringComparison.Ordinal);
    }

    private static OpsOptions WithProbes(params (string Name, string Url)[] probes)
    {
        var options = new OpsOptions();
        foreach (var (name, url) in probes)
        {
            options.ReadinessProbes.Add(new ReadinessProbeOptions { Name = name, Url = url });
        }

        return options;
    }

    private static OpsOptions WithCertificate(WatchedCertificateOptions certificate)
    {
        var options = new OpsOptions();
        options.Certificates.Add(certificate);
        return options;
    }
}

public sealed class OpsTrustedCallerTests
{
    private const string ConsoleService = "starter-ops-console";

    private static readonly ServiceAssertionServerOptions Server = new()
    {
        Audience = "starter-api",
        TrustedServices =
        {
            new TrustedServiceOptions { ServiceName = "starter-gateway", KeyId = "gw-1" },
            new TrustedServiceOptions { ServiceName = ConsoleService, KeyId = "ops-1" },
        },
    };

    [Fact]
    public void The_trusted_service_is_the_one_holding_the_configured_key_id()
    {
        Assert.Equal(ConsoleService, OpsTrustedCallerHandler.TrustedServiceName(new OpsOptions { TrustedCallerKeyId = "ops-1" }, Server));
        Assert.Null(OpsTrustedCallerHandler.TrustedServiceName(new OpsOptions { TrustedCallerKeyId = "nope" }, Server));
        Assert.Null(OpsTrustedCallerHandler.TrustedServiceName(new OpsOptions(), Server));
    }

    [Fact]
    public async Task Only_the_trusted_service_identity_succeeds()
    {
        Assert.True(await AuthorizeAsync(ServicePrincipal(ConsoleService), "ops-1"));
        Assert.False(await AuthorizeAsync(ServicePrincipal("starter-gateway"), "ops-1"));
        Assert.False(await AuthorizeAsync(UserPrincipal(ConsoleService), "ops-1"));
        Assert.False(await AuthorizeAsync(ServicePrincipal(ConsoleService), keyId: null));
        Assert.False(await AuthorizeAsync(new ClaimsPrincipal(new ClaimsIdentity()), "ops-1"));
    }

    [Fact]
    public void A_key_id_without_a_trusted_service_entry_stops_the_host()
    {
        var validator = new OpsTrustedCallerValidator(Microsoft.Extensions.Options.Options.Create(Server));

        Assert.True(validator.Validate(null, new OpsOptions()).Succeeded);
        Assert.True(validator.Validate(null, new OpsOptions { TrustedCallerKeyId = "ops-1" }).Succeeded);
        Assert.True(validator.Validate(null, new OpsOptions { TrustedCallerKeyId = "missing" }).Failed);
    }

    private static async Task<bool> AuthorizeAsync(ClaimsPrincipal user, string? keyId)
    {
        var handler = new OpsTrustedCallerHandler(Monitor(new OpsOptions { TrustedCallerKeyId = keyId }), Monitor(Server));
        var requirement = new OpsTrustedCallerRequirement();
        var context = new AuthorizationHandlerContext([requirement], user, resource: null);
        await handler.HandleAsync(context);
        return context.HasSucceeded;
    }

    private static ClaimsPrincipal ServicePrincipal(string service) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, service), new Claim(SecurityClaimTypes.Service, service)], ServiceAssertionAuthenticationHandler.SchemeName));

    private static ClaimsPrincipal UserPrincipal(string name) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, name)], "Bearer"));

    private static StaticMonitor<T> Monitor<T>(T value)
        where T : class => new StaticMonitor<T>(value);

    private sealed class StaticMonitor<T>(T value) : IOptionsMonitor<T>
        where T : class
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
