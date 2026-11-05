using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Starter.Infrastructure.Authorization;

/// <summary>
/// The default scheme of a host whose modules register none: it never authenticates anybody, challenges with 401 and
/// forbids with 403 (both rendered as problem+json by Codelab.Errors). It keeps a host with no Identity module from
/// failing every protected request with "no default challenge scheme".
/// </summary>
public sealed class NoCredentialsAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>The scheme name.</summary>
    public const string SchemeName = "NoCredentials";

    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
}
