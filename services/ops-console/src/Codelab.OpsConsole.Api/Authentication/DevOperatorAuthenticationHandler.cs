using System.Security.Claims;
using System.Text.Encodings.Web;
using Codelab.OpsConsole.Api.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Codelab.OpsConsole.Api.Authentication;

/// <summary>Options of the dev operator scheme (the settings live in <see cref="DevOperatorOptions"/>).</summary>
public sealed class DevOperatorAuthenticationOptions : AuthenticationSchemeOptions;

/// <summary>
/// Development only: signs every request in as <c>Codelab:OpsConsole:DevOperator:Name</c> with its configured groups
/// (a SID becomes a <see cref="ClaimTypes.GroupSid"/> claim, a name a role claim), the shape Windows sign-in produces.
/// <see cref="OperatorAuthentication"/> registers it only in Development, and the options validator refuses it elsewhere.
/// </summary>
public sealed class DevOperatorAuthenticationHandler(
    IOptionsMonitor<DevOperatorAuthenticationOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IOptionsMonitor<OpsConsoleOptions> consoleOptions)
    : AuthenticationHandler<DevOperatorAuthenticationOptions>(options, loggerFactory, encoder)
{
    /// <summary>The scheme name.</summary>
    public const string SchemeName = "OpsDevOperator";

    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var operatorOptions = consoleOptions.CurrentValue.DevOperator;
        if (!operatorOptions.Enabled || string.IsNullOrWhiteSpace(operatorOptions.Name))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var principal = new ClaimsPrincipal(CreateIdentity(operatorOptions, Scheme.Name));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }

    internal static ClaimsIdentity CreateIdentity(DevOperatorOptions operatorOptions, string authenticationType)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, operatorOptions.Name!.Trim()) };
        claims.AddRange(operatorOptions.Groups
            .Where(group => !string.IsNullOrWhiteSpace(group))
            .Select(group => new Claim(OperatorGroupMembership.IsSid(group) ? ClaimTypes.GroupSid : ClaimTypes.Role, group.Trim())));
        return new ClaimsIdentity(claims, authenticationType, ClaimTypes.Name, ClaimTypes.Role);
    }
}
