using Codelab.Identity.ActiveDirectory;
using Codelab.Identity.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Codelab.Identity.Tests.ActiveDirectory;

public sealed class ActiveDirectoryDeploymentGuardTests
{
    private const string Section = ActiveDirectoryOptions.SectionName;

    private static ActiveDirectoryOptions Valid(Action<ActiveDirectoryOptions>? change = null)
    {
        var options = new ActiveDirectoryOptions { Enabled = true, Host = "dc1.example.org", Domain = "example.org", BaseDn = "DC=example,DC=org" };
        change?.Invoke(options);
        return options;
    }

    [Fact]
    public void A_disabled_section_has_nothing_to_refuse() =>
        Assert.Empty(ActiveDirectoryDeploymentGuard.Check(new ActiveDirectoryOptions(), isDevelopment: false));

    [Fact]
    public void A_complete_ldaps_section_starts_in_production() =>
        Assert.Empty(ActiveDirectoryDeploymentGuard.Check(Valid(), isDevelopment: false));

    [Theory]
    [InlineData(nameof(ActiveDirectoryOptions.Host))]
    [InlineData(nameof(ActiveDirectoryOptions.Domain))]
    [InlineData(nameof(ActiveDirectoryOptions.BaseDn))]
    public void A_missing_required_key_is_refused(string key)
    {
        var options = Valid();
        typeof(ActiveDirectoryOptions).GetProperty(key)!.SetValue(options, " ");

        var problem = Assert.Single(ActiveDirectoryDeploymentGuard.Check(options, isDevelopment: true));
        Assert.Equal($"{Section}:{key} must be set", problem);
    }

    [Theory]
    [InlineData("EXAMPLE\\")]
    [InlineData("EXAMPLE@example.org")]
    public void A_netbios_name_with_a_separator_is_refused(string netBiosName)
    {
        var problem = Assert.Single(ActiveDirectoryDeploymentGuard.Check(Valid(o => o.NetBiosName = netBiosName), isDevelopment: false));
        Assert.StartsWith($"{Section}:NetBiosName must be the bare NetBIOS domain name", problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("@example.gov")]
    public void A_blank_or_qualified_upn_suffix_is_refused(string suffix)
    {
        var problem = Assert.Single(ActiveDirectoryDeploymentGuard.Check(Valid(o => o.UpnSuffixes = ["example.gov", suffix]), isDevelopment: false));
        Assert.StartsWith($"{Section}:UpnSuffixes must list bare DNS suffixes", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_netbios_name_and_alternative_upn_suffixes_start_in_production() =>
        Assert.Empty(ActiveDirectoryDeploymentGuard.Check(Valid(o => { o.NetBiosName = "EXAMPLE"; o.UpnSuffixes = ["example.gov"]; }), isDevelopment: false));

    [Fact]
    public void Cleartext_ldap_is_refused_outside_development()
    {
        var problem = Assert.Single(ActiveDirectoryDeploymentGuard.Check(Valid(o => { o.UseLdaps = false; o.Port = ActiveDirectoryOptions.LdapPort; }), isDevelopment: false));
        Assert.StartsWith($"{Section}:UseLdaps must be true outside Development", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Cleartext_ldap_is_allowed_in_development() =>
        Assert.Empty(ActiveDirectoryDeploymentGuard.Check(Valid(o => { o.UseLdaps = false; o.Port = ActiveDirectoryOptions.LdapPort; }), isDevelopment: true));

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    [InlineData(-1)]
    public void A_port_outside_tcp_is_refused(int port) =>
        Assert.Contains($"{Section}:Port", Assert.Single(ActiveDirectoryDeploymentGuard.Check(Valid(o => o.Port = port), isDevelopment: false)), StringComparison.Ordinal);

    [Theory]
    [InlineData(0)]
    [InlineData(ActiveDirectoryDeploymentGuard.MaxTimeoutSeconds + 1)]
    public void A_timeout_outside_its_range_is_refused(int seconds) =>
        Assert.Contains($"{Section}:TimeoutSeconds", Assert.Single(ActiveDirectoryDeploymentGuard.Check(Valid(o => o.TimeoutSeconds = seconds), isDevelopment: false)), StringComparison.Ordinal);

    [Theory]
    [InlineData("(sAMAccountName=fixed)")]
    [InlineData("")]
    [InlineData("sAMAccountName={0}")]
    [InlineData("(sAMAccountName={0})(x={2})")]
    [InlineData("(sAMAccountName={0}{)")]
    public void A_filter_without_the_placeholder_or_that_does_not_format_is_refused(string filter) =>
        Assert.Contains($"{Section}:SearchFilter", Assert.Single(ActiveDirectoryDeploymentGuard.Check(Valid(o => o.SearchFilter = filter), isDevelopment: false)), StringComparison.Ordinal);

    [Fact]
    public void A_filter_with_only_the_logon_name_placeholder_is_accepted() =>
        Assert.Empty(ActiveDirectoryDeploymentGuard.Check(Valid(o => o.SearchFilter = "(sAMAccountName={0})"), isDevelopment: false));

    [Fact]
    public void An_undefined_match_field_is_refused() =>
        Assert.Contains($"{Section}:MatchBy", Assert.Single(ActiveDirectoryDeploymentGuard.Check(Valid(o => o.MatchBy = (LoginMatchField)9), isDevelopment: false)), StringComparison.Ordinal);

    [Fact]
    public void Every_problem_is_reported_at_once_and_names_keys_not_values()
    {
        var problems = ActiveDirectoryDeploymentGuard.Check(
            new ActiveDirectoryOptions { Enabled = true, UseLdaps = false, BaseDn = "OU=Secret-Value,DC=example,DC=org", TimeoutSeconds = 0 },
            isDevelopment: false);

        Assert.Equal(4, problems.Count);
        Assert.All(problems, problem => Assert.DoesNotContain("Secret-Value", problem, StringComparison.Ordinal));
    }

    [Fact]
    public void Certificate_validation_has_no_off_switch()
    {
        var names = typeof(ActiveDirectoryOptions).GetProperties().Select(property => property.Name).ToArray();

        Assert.DoesNotContain(names, name => name.Contains("Certificate", StringComparison.OrdinalIgnoreCase) || name.Contains("Verify", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_registration_fails_the_host_on_start_outside_development()
    {
        using var provider = Provider(Environments.Production, new Dictionary<string, string?>
        {
            [$"{Section}:Enabled"] = "true",
            [$"{Section}:Host"] = "dc1.example.org",
            [$"{Section}:Domain"] = "example.org",
            [$"{Section}:BaseDn"] = "DC=example,DC=org",
            [$"{Section}:UseLdaps"] = "false",
        });

        var failure = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<ActiveDirectoryOptions>>().Value);
        Assert.Contains($"{Section}:UseLdaps", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_registration_binds_the_section_and_resolves_every_service()
    {
        using var provider = Provider(Environments.Production, new Dictionary<string, string?>
        {
            [$"{Section}:Enabled"] = "true",
            [$"{Section}:Host"] = "dc1.example.org",
            [$"{Section}:Domain"] = "example.org",
            [$"{Section}:BaseDn"] = "DC=example,DC=org",
            [$"{Section}:MatchBy"] = "Upn",
        });

        Assert.Equal(LoginMatchField.Upn, provider.GetRequiredService<IOptions<ActiveDirectoryOptions>>().Value.MatchBy);
        Assert.IsType<LdapActiveDirectoryAuthenticator>(provider.GetRequiredService<IActiveDirectoryAuthenticator>());
        Assert.IsType<SystemLdapConnectionFactory>(provider.GetRequiredService<ILdapConnectionFactory>());
        Assert.IsType<ActiveDirectoryConnectionProbe>(provider.GetRequiredService<IActiveDirectoryConnectionProbe>());
        Assert.NotNull(provider.GetRequiredService<LdapsCertificateReader>());
    }

    [Fact]
    public void A_connection_factory_registered_first_is_kept()
    {
        var fake = new FakeDirectory();
        var services = new ServiceCollection().AddSingleton<ILdapConnectionFactory>(fake);
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(Environments.Development));
        services.AddLogging();
        services.AddCodelabActiveDirectory(new ConfigurationBuilder().Build());

        using var provider = services.BuildServiceProvider();
        Assert.Same(fake, provider.GetRequiredService<ILdapConnectionFactory>());
    }

    private static ServiceProvider Provider(string environment, Dictionary<string, string?> values)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(environment));
        services.AddLogging();
        services.AddCodelabActiveDirectory(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        return services.BuildServiceProvider();
    }
}
