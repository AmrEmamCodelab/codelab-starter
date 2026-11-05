using Codelab.Identity.ActiveDirectory;
using Codelab.Identity.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Codelab.Identity.Tests.ActiveDirectory;

public sealed class LdapAuthenticatorTests
{
    private const string Phrase = "correct-horse-battery-staple";
    private const string BindName = "user.one@example.org";

    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 11, 5, 9, 0, 0, TimeSpan.Zero));
    private readonly FakeDirectory directory = new();

    public LdapAuthenticatorTests() =>
        directory.Add(BindName, Phrase, FakeDirectory.Entry("user.one", upn: BindName, mail: "user.one@example.org", displayName: "User One"));

    private static ActiveDirectoryOptions Settings(Action<ActiveDirectoryOptions>? change = null)
    {
        var options = new ActiveDirectoryOptions
        {
            Enabled = true,
            Host = "dc1.example.org",
            Domain = "example.org",
            NetBiosName = "EXAMPLE",
            BaseDn = "DC=example,DC=org",
        };
        change?.Invoke(options);
        return options;
    }

    private LdapActiveDirectoryAuthenticator Authenticator(ActiveDirectoryOptions? options = null) =>
        new LdapActiveDirectoryAuthenticator(Options.Create(options ?? Settings()), directory, clock, NullLogger<LdapActiveDirectoryAuthenticator>.Instance);

    [Fact]
    public async Task A_correct_password_signs_in_and_maps_the_directory_attributes()
    {
        var result = await Authenticator().AuthenticateAsync("user.one", Phrase, CancellationToken.None);

        Assert.Equal(DirectoryAuthStatus.Succeeded, result.Status);
        Assert.Equal(new DirectoryIdentity("user.one", BindName, "user.one@example.org", "User One"), result.Identity);
    }

    [Fact]
    public async Task A_bare_name_binds_as_user_at_domain_and_searches_the_base_dn_with_both_placeholders()
    {
        await Authenticator().AuthenticateAsync("user.one", Phrase, CancellationToken.None);

        Assert.Equal([BindName], directory.BoundNames);
        var search = Assert.Single(directory.Searches);
        Assert.Equal("DC=example,DC=org", search.BaseDn);
        Assert.Equal("(&(objectCategory=person)(objectClass=user)(|(sAMAccountName=user.one)(userPrincipalName=user.one@example.org)))", search.Filter);
        Assert.Equal(ActiveDirectoryAttributes.Requested, search.Attributes);
    }

    [Theory]
    [InlineData("user.one@example.org", "user.one@example.org")]
    [InlineData("EXAMPLE\\user.one", "EXAMPLE\\user.one")]
    [InlineData("example\\user.one", "example\\user.one")]
    [InlineData("user.one@EXAMPLE.ORG", "user.one@EXAMPLE.ORG")]
    [InlineData("  user.one  ", "user.one@example.org")]
    public async Task Qualified_names_are_bound_as_typed(string submitted, string expectedBindName)
    {
        await Authenticator().AuthenticateAsync(submitted, "irrelevant", CancellationToken.None);

        Assert.Equal([expectedBindName], directory.BoundNames);
    }

    [Theory]
    [InlineData("OTHER\\user.one")]
    [InlineData("user.one@other.example")]
    [InlineData("user.one@example.org.other.example")]
    public async Task A_name_qualified_with_a_trusted_domain_is_refused_before_the_bind(string submitted)
    {
        // The trusted domain's user.one has their own password, and the local search would find the local user.one.
        directory.Add(submitted, "trusted-domain-secret", FakeDirectory.Entry("user.one", upn: "user.one@other.example"));
        directory.SearchAnswer = [FakeDirectory.Entry("user.one", upn: BindName)];

        var result = await Authenticator().AuthenticateAsync(submitted, "trusted-domain-secret", CancellationToken.None);

        Assert.Equal(DirectoryAuthResult.InvalidCredentials, result);
        Assert.Empty(directory.Opened);
        Assert.Empty(directory.BoundNames);
    }

    [Fact]
    public async Task The_down_level_form_is_refused_when_no_netbios_name_is_configured()
    {
        var result = await Authenticator(Settings(o => o.NetBiosName = string.Empty)).AuthenticateAsync("EXAMPLE\\user.one", Phrase, CancellationToken.None);

        Assert.Equal(DirectoryAuthResult.InvalidCredentials, result);
        Assert.Empty(directory.Opened);
    }

    [Fact]
    public async Task A_configured_alternative_upn_suffix_is_bound_as_typed()
    {
        directory.Add("user.one@alt.example.test", Phrase, FakeDirectory.Entry("user.one", upn: "user.one@alt.example.test"));

        var result = await Authenticator(Settings(o => o.UpnSuffixes = ["alt.example.test"])).AuthenticateAsync("user.one@alt.example.test", Phrase, CancellationToken.None);

        Assert.Equal(DirectoryAuthStatus.Succeeded, result.Status);
        Assert.Equal(["user.one@alt.example.test"], directory.BoundNames);
    }

    [Fact]
    public async Task The_endpoint_carries_the_configured_host_port_tls_and_timeout()
    {
        await Authenticator(Settings(o => { o.Port = 3269; o.TimeoutSeconds = 7; })).AuthenticateAsync("user.one", Phrase, CancellationToken.None);

        Assert.Equal(new LdapEndpoint("dc1.example.org", 3269, UseLdaps: true, TimeSpan.FromSeconds(7)), Assert.Single(directory.Opened));
    }

    [Fact]
    public async Task A_wrong_password_is_invalid_credentials_and_no_search_runs()
    {
        var result = await Authenticator().AuthenticateAsync("user.one", "wrong", CancellationToken.None);

        Assert.Equal(DirectoryAuthResult.InvalidCredentials, result);
        Assert.Empty(directory.Searches);
    }

    [Theory]
    [InlineData(ActiveDirectoryBindSubCodes.AccountDisabled)]
    [InlineData(ActiveDirectoryBindSubCodes.AccountExpired)]
    [InlineData(ActiveDirectoryBindSubCodes.AccountLockedOut)]
    public async Task A_disabled_expired_or_locked_account_is_account_disabled(string subCode)
    {
        directory.Add("locked@example.org", Phrase, FakeDirectory.Entry("locked"), disabledSubCode: subCode);

        var result = await Authenticator().AuthenticateAsync("locked", Phrase, CancellationToken.None);

        Assert.Equal(DirectoryAuthStatus.AccountDisabled, result.Status);
        Assert.Null(result.Identity);
    }

    [Fact]
    public async Task The_account_disabled_flag_on_the_entry_refuses_even_after_a_successful_bind()
    {
        directory.Add("flagged@example.org", Phrase, FakeDirectory.Entry("flagged", userAccountControl: "514"));

        var result = await Authenticator().AuthenticateAsync("flagged", Phrase, CancellationToken.None);

        Assert.Equal(DirectoryAuthStatus.AccountDisabled, result.Status);
    }

    [Theory]
    [InlineData(LdapResultCodes.ServerDown)]
    [InlineData(LdapResultCodes.ConnectError)]
    [InlineData(LdapResultCodes.Unavailable)]
    [InlineData(LdapResultCodes.StrongerAuthRequired)]
    [InlineData(null)]
    public async Task A_bind_fault_that_is_not_a_credential_verdict_is_unavailable(int? resultCode)
    {
        directory.BindFailure = FakeDirectory.Code(resultCode);

        var result = await Authenticator().AuthenticateAsync("user.one", Phrase, CancellationToken.None);

        Assert.Equal(DirectoryAuthResult.Unavailable, result);
    }

    [Fact]
    public async Task A_server_that_cannot_be_opened_is_unavailable()
    {
        directory.OpenFailure = FakeDirectory.Code(LdapResultCodes.ServerDown);

        Assert.Equal(DirectoryAuthResult.Unavailable, await Authenticator().AuthenticateAsync("user.one", Phrase, CancellationToken.None));
    }

    [Fact]
    public async Task A_search_fault_is_unavailable_never_a_credential_verdict()
    {
        directory.SearchFailure = FakeDirectory.Code(LdapResultCodes.InvalidCredentials);

        Assert.Equal(DirectoryAuthResult.Unavailable, await Authenticator().AuthenticateAsync("user.one", Phrase, CancellationToken.None));
    }

    [Fact]
    public async Task No_entry_after_the_bind_is_invalid_credentials()
    {
        directory.SearchAnswer = [];

        Assert.Equal(DirectoryAuthResult.InvalidCredentials, await Authenticator().AuthenticateAsync("user.one", Phrase, CancellationToken.None));
    }

    [Fact]
    public async Task Two_entries_are_never_resolved_by_taking_the_first()
    {
        directory.SearchAnswer = [FakeDirectory.Entry("user.one"), FakeDirectory.Entry("user.two")];

        Assert.Equal(DirectoryAuthResult.InvalidCredentials, await Authenticator().AuthenticateAsync("user.one", Phrase, CancellationToken.None));
    }

    [Theory]
    [InlineData("user.one", "")]
    [InlineData("", Phrase)]
    [InlineData("   ", Phrase)]
    [InlineData("user\u0000one", Phrase)]
    [InlineData("@example.org", Phrase)]
    [InlineData("EXAMPLE\\", Phrase)]
    public async Task Blank_or_malformed_input_is_refused_before_anything_is_sent(string userName, string secret)
    {
        var result = await Authenticator().AuthenticateAsync(userName, secret, CancellationToken.None);

        Assert.Equal(DirectoryAuthResult.InvalidCredentials, result);
        Assert.Empty(directory.Opened);
    }

    [Fact]
    public async Task An_overlong_user_name_or_password_is_refused_before_anything_is_sent()
    {
        var longName = new string('a', DirectoryUserName.MaxLength + 1);
        var longSecret = new string('a', LdapActiveDirectoryAuthenticator.MaxPasswordLength + 1);

        Assert.Equal(DirectoryAuthResult.InvalidCredentials, await Authenticator().AuthenticateAsync(longName, Phrase, CancellationToken.None));
        Assert.Equal(DirectoryAuthResult.InvalidCredentials, await Authenticator().AuthenticateAsync("user.one", longSecret, CancellationToken.None));
        Assert.Empty(directory.Opened);
    }

    [Fact]
    public async Task Filter_metacharacters_in_the_name_are_escaped()
    {
        await Authenticator().AuthenticateAsync("*)(uid=*", "irrelevant", CancellationToken.None);

        Assert.Empty(directory.Searches);
        Assert.Equal(["*)(uid=*@example.org"], directory.BoundNames);
        Assert.Equal("\\2a\\29\\28uid=\\2a", LdapFilter.Escape("*)(uid=*"));
    }

    [Fact]
    public async Task A_disabled_provider_is_unavailable_and_opens_nothing()
    {
        var result = await Authenticator(Settings(o => o.Enabled = false)).AuthenticateAsync("user.one", Phrase, CancellationToken.None);

        Assert.Equal(DirectoryAuthResult.Unavailable, result);
        Assert.Empty(directory.Opened);
    }

    [Fact]
    public async Task A_directory_that_never_answers_is_unavailable_at_the_timeout()
    {
        using var hang = new ManualResetEventSlim(initialState: false);
        directory.Hang = hang;
        var attempt = Authenticator(Settings(o => o.TimeoutSeconds = 5)).AuthenticateAsync("user.one", Phrase, CancellationToken.None);
        while (directory.BoundNames.Count == 0)
        {
            await Task.Yield();
        }

        clock.Advance(TimeSpan.FromSeconds(5));
        var result = await attempt;
        hang.Set();

        Assert.Equal(DirectoryAuthResult.Unavailable, result);
    }

    [Fact]
    public async Task Cancellation_propagates()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Authenticator().AuthenticateAsync("user.one", Phrase, cancelled.Token));
    }

    [Fact]
    public async Task An_entry_without_attributes_falls_back_to_the_submitted_logon_name()
    {
        directory.SearchAnswer = [new LdapEntry("CN=bare,DC=example,DC=org", new Dictionary<string, string>())];

        var result = await Authenticator().AuthenticateAsync("user.one", Phrase, CancellationToken.None);

        Assert.Equal(new DirectoryIdentity("user.one", Upn: null, Email: null, DisplayName: null), result.Identity);
    }

    [Fact]
    public void The_bind_request_never_prints_its_secret()
    {
        var request = new LdapBindRequest(BindName: BindName, Password: Phrase);

        Assert.DoesNotContain(Phrase, request.ToString(), StringComparison.Ordinal);
        Assert.Contains(BindName, request.ToString(), StringComparison.Ordinal);
    }
}
