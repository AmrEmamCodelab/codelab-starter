using System.Security.Claims;
using Codelab.OpsConsole.Api.Authentication;
using Codelab.OpsConsole.Api.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Codelab.OpsConsole.Api.Tests.Authentication;

public sealed class OperatorAuthenticationUnitTests
{
    private const string GroupSid = "S-1-5-21-1000-2000-3000-1105";
    private const string GroupName = "Ops Operators";

    [Fact]
    public void A_sid_group_matches_a_group_sid_claim_in_any_case()
    {
        var user = Principal("CORP\\alice", new Claim(ClaimTypes.GroupSid, GroupSid.ToLowerInvariant()));

        Assert.True(OperatorGroupMembership.IsOperator(user, GroupSid));
    }

    [Fact]
    public void A_sid_group_is_not_matched_by_a_role_with_the_same_text()
    {
        var user = Principal("CORP\\alice", new Claim(ClaimTypes.Role, GroupSid));

        Assert.False(OperatorGroupMembership.IsOperator(user, GroupSid));
    }

    [Fact]
    public void A_named_group_goes_through_is_in_role()
    {
        var user = Principal("CORP\\alice", new Claim(ClaimTypes.Role, "CORP\\Ops Operators"));

        Assert.True(OperatorGroupMembership.IsOperator(user, " CORP\\Ops Operators "));
        Assert.False(OperatorGroupMembership.IsOperator(user, "CORP\\Other"));
    }

    [Fact]
    public void Anonymous_nameless_or_unconfigured_is_never_an_operator()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.GroupSid, GroupSid)]));
        var nameless = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.GroupSid, GroupSid)], "Negotiate"));
        var member = Principal("CORP\\alice", new Claim(ClaimTypes.GroupSid, GroupSid));

        Assert.False(OperatorGroupMembership.IsOperator(anonymous, GroupSid));
        Assert.False(OperatorGroupMembership.IsOperator(nameless, GroupSid));
        Assert.False(OperatorGroupMembership.IsOperator(member, operatorGroup: null));
        Assert.False(OperatorGroupMembership.IsOperator(member, " "));
    }

    [Theory]
    [InlineData("S-1-5-21-1-2-3", true)]
    [InlineData(" s-1-5-32-544", true)]
    [InlineData("CORP\\Ops", false)]
    public void Sids_are_told_apart_from_names(string group, bool isSid) =>
        Assert.Equal(isSid, OperatorGroupMembership.IsSid(group));

    [Fact]
    public void The_operator_name_is_the_trimmed_identity_name()
    {
        Assert.Equal("CORP\\alice", OperatorGroupMembership.NameOf(Principal(" CORP\\alice ")).GetValueOrDefault().Value);
        Assert.Null(OperatorGroupMembership.NameOf(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    [Fact]
    public void The_dev_operator_is_used_only_when_enabled_in_development()
    {
        var enabled = new OpsConsoleOptions { DevOperator = { Enabled = true, Name = "DEV\\op" } };

        Assert.True(OperatorAuthentication.UsesDevOperator(enabled, Environment(Environments.Development)));
        Assert.False(OperatorAuthentication.UsesDevOperator(new OpsConsoleOptions(), Environment(Environments.Development)));
        Assert.False(OperatorAuthentication.UsesDevOperator(new OpsConsoleOptions(), Environment(Environments.Production)));
        var refused = Assert.Throws<InvalidOperationException>(() => OperatorAuthentication.UsesDevOperator(enabled, Environment(Environments.Production)));
        Assert.Contains("DevOperator:Enabled", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_dev_operator_identity_has_the_shape_of_a_windows_sign_in()
    {
        var options = new DevOperatorOptions { Enabled = true, Name = " DEV\\op ", Groups = [GroupSid, GroupName, " "] };

        var identity = DevOperatorAuthenticationHandler.CreateIdentity(options, DevOperatorAuthenticationHandler.SchemeName);
        var user = new ClaimsPrincipal(identity);

        Assert.True(identity.IsAuthenticated);
        Assert.Equal("DEV\\op", identity.Name);
        Assert.True(user.HasClaim(ClaimTypes.GroupSid, GroupSid));
        Assert.True(user.IsInRole(GroupName));
        Assert.Equal(2, identity.Claims.Count(claim => claim.Type is ClaimTypes.GroupSid or ClaimTypes.Role));
    }

    private static ClaimsPrincipal Principal(string name, params Claim[] claims) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Name, name), .. claims], "Negotiate", ClaimTypes.Name, ClaimTypes.Role));

    private static FixedEnvironment Environment(string name) => new(name);

    private sealed class FixedEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
