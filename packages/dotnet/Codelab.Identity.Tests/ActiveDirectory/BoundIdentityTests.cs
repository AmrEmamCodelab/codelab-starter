using Codelab.Identity.ActiveDirectory;
using Codelab.Identity.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Codelab.Identity.Tests.ActiveDirectory;

/// <summary>
/// The bound account and the found entry must be the same object. Scenario: carol (child domain, or an OU outside
/// <c>BaseDn</c>) holds the explicit UPN <c>bob@example.org</c>, so the bind for a bare <c>bob</c> checks carol's password,
/// while the search under <c>BaseDn</c> finds the local bob (<c>sAMAccountName=bob</c>).
/// </summary>
public sealed class BoundIdentityTests
{
    private const string CarolSecret = "carol-own-password";
    private const string BobBindName = "bob@example.org";

    private static readonly LdapEntry LocalBob = FakeDirectory.Entry("bob", displayName: "Local Bob");
    private static readonly LdapEntry Carol = new(
        "CN=carol,OU=Staff,DC=example,DC=net",
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ActiveDirectoryAttributes.LoginName] = "carol",
            [ActiveDirectoryAttributes.Upn] = BobBindName,
        });

    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 11, 5, 9, 0, 0, TimeSpan.Zero));
    private readonly FakeDirectory directory = new();

    public BoundIdentityTests()
    {
        // The bind name resolves to carol; the search under BaseDn only sees the local bob.
        directory.Add(BobBindName, CarolSecret, Carol);
        directory.SearchAnswer = [LocalBob];
    }

    private static ActiveDirectoryOptions Settings(string netBiosName = "EXAMPLE") => new()
    {
        Enabled = true,
        Host = "dc1.example.org",
        Domain = "example.org",
        NetBiosName = netBiosName,
        BaseDn = "OU=Staff,DC=example,DC=org",
    };

    private Task<DirectoryAuthResult> SignIn(string userName, ActiveDirectoryOptions? settings = null) =>
        new LdapActiveDirectoryAuthenticator(Options.Create(settings ?? Settings()), directory, clock, NullLogger<LdapActiveDirectoryAuthenticator>.Instance)
            .AuthenticateAsync(userName, CarolSecret, CancellationToken.None);

    [Theory]
    [InlineData("CHILD")]
    [InlineData("EXAMPLE")]
    public async Task A_bind_under_a_UPN_held_by_another_account_does_not_sign_in_as_the_local_namesake(string carolDomain)
    {
        directory.WhoAmIDomain = carolDomain;

        var result = await SignIn("bob");

        Assert.Equal(DirectoryAuthStatus.InvalidCredentials, result.Status);
        Assert.Null(result.Identity);
    }

    [Fact]
    public async Task Without_who_am_i_a_found_entry_whose_UPN_differs_from_the_bind_name_is_refused()
    {
        directory.SearchAnswer = [FakeDirectory.Entry("bob", upn: "bob@corp.example.org")];

        var result = await SignIn("bob");

        Assert.Equal(DirectoryAuthStatus.InvalidCredentials, result.Status);
    }

    [Fact]
    public async Task A_same_logon_name_in_another_domain_is_refused_when_NetBiosName_is_set()
    {
        directory.WhoAmIAnswer = "u:CHILD\\bob";

        var result = await SignIn("bob");

        Assert.Equal(DirectoryAuthStatus.InvalidCredentials, result.Status);
    }

    [Fact]
    public async Task The_bound_account_that_is_the_found_entry_signs_in()
    {
        directory.SearchAnswer = null;
        directory.Add("dave@example.org", CarolSecret, FakeDirectory.Entry("dave", upn: "dave@corp.example.org"));
        directory.WhoAmIDomain = "EXAMPLE";

        var result = await SignIn("dave");

        // The explicit UPN differs from the implicit bind name, which is fine once "Who am I?" names dave.
        Assert.Equal(DirectoryAuthStatus.Succeeded, result.Status);
        Assert.Equal("dave", result.Identity?.LoginName);
    }

    [Fact]
    public async Task An_empty_who_am_i_answer_is_refused()
    {
        directory.WhoAmIAnswer = string.Empty;

        var result = await SignIn("bob");

        Assert.Equal(DirectoryAuthStatus.InvalidCredentials, result.Status);
    }

    [Fact]
    public async Task A_who_am_i_fault_is_unavailable_not_a_sign_in()
    {
        directory.WhoAmIFailure = FakeDirectory.Code(LdapResultCodes.InvalidCredentials);

        var result = await SignIn("bob");

        Assert.Equal(DirectoryAuthStatus.Unavailable, result.Status);
    }

    [Theory]
    [InlineData("u:EXAMPLE\\bob", true)]
    [InlineData("u:example\\BOB", true)]
    [InlineData("u:CHILD\\bob", false)]
    [InlineData("u:EXAMPLE\\carol", false)]
    [InlineData("u:EXAMPLE\\", false)]
    [InlineData("u:bob", true)]
    [InlineData("dn:CN=bob,OU=Staff,DC=example,DC=org", true)]
    [InlineData("dn:cn=BOB,ou=staff,dc=example,dc=org", true)]
    [InlineData("dn:CN=carol,OU=Staff,DC=example,DC=net", false)]
    [InlineData("dn:", false)]
    [InlineData("", false)]
    [InlineData("anonymous", false)]
    public void Verify_compares_the_authorization_identity_with_the_entry(string authorizationId, bool expected)
    {
        var (matches, _) = BoundIdentity.Verify(authorizationId, LocalBob, Name("bob"), Settings());

        Assert.Equal(expected, matches);
    }

    [Theory]
    [InlineData(null, (int)BoundIdentityCheck.UpnFallback)]
    [InlineData("u:EXAMPLE\\bob", (int)BoundIdentityCheck.LogonName)]
    [InlineData("dn:CN=bob,OU=Staff,DC=example,DC=org", (int)BoundIdentityCheck.DistinguishedName)]
    [InlineData("x:bob", (int)BoundIdentityCheck.Unrecognised)]
    public void Verify_reports_which_check_ran(string? authorizationId, int expected)
    {
        var (_, check) = BoundIdentity.Verify(authorizationId, LocalBob, Name("bob"), Settings());

        Assert.Equal((BoundIdentityCheck)expected, check);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("bob@example.org", true)]
    [InlineData("BOB@EXAMPLE.ORG", true)]
    [InlineData("bob@corp.example.org", false)]
    public void The_UPN_fallback_refuses_only_a_present_and_different_UPN(string? upn, bool expected)
    {
        var entry = FakeDirectory.Entry("bob", upn: upn);

        var (matches, _) = BoundIdentity.Verify(authorizationId: null, entry, Name("bob"), Settings());

        Assert.Equal(expected, matches);
    }

    [Fact]
    public void The_UPN_fallback_has_nothing_to_compare_for_a_down_level_bind_name()
    {
        var entry = FakeDirectory.Entry("bob", upn: "bob@corp.example.org");

        var (matches, _) = BoundIdentity.Verify(authorizationId: null, entry, Name("EXAMPLE\\bob"), Settings());

        Assert.True(matches);
    }

    [Theory]
    [InlineData("bob@example.org", true)]
    [InlineData("bob@corp.example.org", false)]
    public void Without_NetBiosName_the_domain_of_a_who_am_i_answer_is_backed_by_the_UPN_check(string upn, bool expected)
    {
        var entry = FakeDirectory.Entry("bob", upn: upn);

        var (matches, _) = BoundIdentity.Verify("u:CHILD\\bob", entry, Name("bob"), Settings(netBiosName: string.Empty));

        Assert.Equal(expected, matches);
    }

    private static DirectoryUserName Name(string submitted) =>
        DirectoryUserName.Parse(submitted, Settings()).Name ?? throw new InvalidOperationException("test name did not parse");
}
