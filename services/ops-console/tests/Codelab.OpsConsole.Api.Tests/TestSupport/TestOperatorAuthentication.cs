using System.Security.Claims;
using System.Text.Encodings.Web;
using Codelab.OpsConsole.Api.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Codelab.OpsConsole.Api.Tests.TestSupport;

/// <summary>
/// Stands in for Windows sign-in in the HTTP tests (the in-memory test server cannot run Kerberos/NTLM): the operator
/// name and groups come from two request headers and produce the claims Windows sign-in produces (a SID group becomes
/// a GroupSid claim). No name header: no result, so the policy challenges with 401.
/// </summary>
internal sealed class TestOperatorAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    public const string SchemeName = "TestOperator";
    public const string NameHeader = "X-Test-Operator";
    public const string GroupsHeader = "X-Test-Groups";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var name = Request.Headers[NameHeader].ToString();
        if (name.Length == 0)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim> { new(ClaimTypes.Name, name) };
        foreach (var group in Request.Headers[GroupsHeader].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            claims.Add(new Claim(OperatorGroupMembership.IsSid(group) ? ClaimTypes.GroupSid : ClaimTypes.Role, group));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName, ClaimTypes.Name, ClaimTypes.Role));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
