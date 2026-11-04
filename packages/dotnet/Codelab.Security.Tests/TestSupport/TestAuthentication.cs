using System.Security.Claims;
using System.Text.Encodings.Web;
using Codelab.Security.Sessions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Codelab.Security.Tests.TestSupport;

/// <summary>A session as the test client presents it (stands in for a real token).</summary>
internal sealed record TestSession(string UserId, string SessionId, string SessionKeyId, long Version = 0)
{
    public void ApplyTo(HttpRequestMessage request)
    {
        request.Headers.Add(TestAuthentication.UserHeader, UserId);
        request.Headers.Add(TestAuthentication.SessionHeader, SessionId);
        request.Headers.Add(TestAuthentication.KeyIdHeader, SessionKeyId);
        request.Headers.Add(TestAuthentication.VersionHeader, Version.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}

/// <summary>Authenticates from test headers into the claims a real session issuer writes.</summary>
internal sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string SecondSchemeName = "TestOther";
    public const string UserHeader = "Test-User";
    public const string SessionHeader = "Test-Session";
    public const string KeyIdHeader = "Test-Key-Id";
    public const string VersionHeader = "Test-Version";

    /// <summary>Adds a <see cref="SecurityClaimTypes.Service"/> claim to the USER identity (a forged or copied claim).</summary>
    public const string ServiceClaimHeader = "Test-Service-Claim";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var user = Request.Headers[UserHeader].ToString();
        if (user.Length == 0)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim> { new(SecurityClaimTypes.Subject, user) };
        AddIfPresent(claims, SecurityClaimTypes.SessionId, SessionHeader);
        AddIfPresent(claims, SecurityClaimTypes.SessionKeyId, KeyIdHeader);
        AddIfPresent(claims, SecurityClaimTypes.SessionVersion, VersionHeader);
        AddIfPresent(claims, SecurityClaimTypes.Service, ServiceClaimHeader);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }

    private void AddIfPresent(List<Claim> claims, string type, string header)
    {
        var value = Request.Headers[header].ToString();
        if (value.Length > 0)
        {
            claims.Add(new Claim(type, value));
        }
    }
}
