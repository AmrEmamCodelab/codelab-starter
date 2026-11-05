using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Codelab.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Starter.Application.Results;
using Starter.Infrastructure.Results;
using Starter.Modules.Identity.Application;
using Starter.Modules.Identity.Application.LoginModes;

namespace Starter.Modules.Identity.Api.LoginModes;

/// <summary>The login-mode routes under <see cref="AuthRoutes.Base"/> and their query parameter names.</summary>
public static class LoginModeRoutes
{
    /// <summary><c>GET api/auth/modes</c>.</summary>
    public const string Modes = "modes";

    /// <summary><c>POST api/auth/ad/login</c>.</summary>
    public const string DirectoryLogin = "ad/login";

    /// <summary>The SSO routes' common segment.</summary>
    public const string SsoSegment = "sso";

    /// <summary><c>GET api/auth/sso/login?returnUrl=&amp;binding=</c>.</summary>
    public const string SsoLogin = SsoSegment + "/login";

    /// <summary><c>GET api/auth/sso/signin-oidc?code&amp;state</c>: the redirect URI registered at the identity provider.</summary>
    public const string SsoCallback = SsoSegment + "/signin-oidc";

    /// <summary><c>POST api/auth/sso/exchange</c>.</summary>
    public const string SsoExchange = SsoSegment + "/exchange";

    /// <summary><c>POST api/auth/sso/logout</c>.</summary>
    public const string SsoLogout = SsoSegment + "/logout";

    /// <summary>The query parameter carrying the SPA's return path.</summary>
    public const string ReturnUrlParameter = "returnUrl";

    /// <summary>The query parameter carrying the optional browser binding hash.</summary>
    public const string BindingParameter = BeginSsoSignIn.BindingField;

    /// <summary>The callback's authorization code parameter (OAuth 2.0).</summary>
    public const string CodeParameter = "code";

    /// <summary>The callback's state parameter (OAuth 2.0).</summary>
    public const string StateParameter = "state";

    /// <summary>The callback's error parameter (OAuth 2.0).</summary>
    public const string ErrorParameter = "error";
}

/// <summary>
/// The browser's SSO sign-in cookie: one per sign-in (named after its state, so two tabs never overwrite each other),
/// sealed, HttpOnly, SameSite=Lax (sent on the provider's top-level redirect back), limited to the SSO routes and to
/// the pending sign-in's lifetime, Secure on https.
/// </summary>
public static class SsoPendingCookie
{
    /// <summary>The name prefix; the suffix is derived from the state.</summary>
    public const string NamePrefix = "sso-pending-";

    /// <summary>How many base64url characters of the state's SHA-256 name the cookie (96 bits).</summary>
    public const int NameSuffixLength = 16;

    /// <summary>The cookie path: the SSO routes only.</summary>
    public const string Path = "/" + AuthRoutes.Base + "/" + LoginModeRoutes.SsoSegment;

    /// <summary>The cookie name of the sign-in with <paramref name="state"/>.</summary>
    public static string NameFor(string state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var hash = Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(state)));
        return NamePrefix + hash[..NameSuffixLength];
    }

    /// <summary>The options the cookie is written (and deleted) with.</summary>
    public static CookieOptions Options(bool isHttps) => new()
    {
        HttpOnly = true,
        Secure = isHttps,
        SameSite = SameSiteMode.Lax,
        Path = Path,
        IsEssential = true,
        MaxAge = SsoLimits.PendingLifetime,
    };
}

/// <summary>
/// Maps a login-mode outcome to its answer: success through the mapping, an unreachable directory or
/// identity provider to 503 problem+json with its own code, every other refusal as <see cref="OutcomeResults"/> does.
/// </summary>
public static class LoginModeResults
{
    private static readonly HashSet<string> UnavailableCodes = new(StringComparer.Ordinal)
    {
        LoginModeErrorCodes.DirectoryUnavailable,
        LoginModeErrorCodes.SsoUnavailable,
    };

    /// <summary>The answer of <paramref name="outcome"/>.</summary>
    public static ActionResult<TResponse> ToActionResult<T, TResponse>(Outcome<T> outcome, Func<T, TResponse> map)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        return outcome is { IsSuccess: false, Refusal.Code: var code } && UnavailableCodes.Contains(code)
            ? new EnvelopeResult(Unavailable(code))
            : outcome.ToActionResult(map);
    }

    /// <summary>The body of <paramref name="result"/>.</summary>
    public static SessionResponse Session(LoginResult result) => SessionResponse.From(result);

    private static ProblemDetails Unavailable(string code)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status503ServiceUnavailable,
            Title = ErrorEnvelope.Title,
            Detail = FrameworkStatusCodes.Describe(StatusCodes.Status503ServiceUnavailable).Detail,
        };
        problem.Extensions[ErrorEnvelope.CodeMember] = code;
        return problem;
    }

    /// <summary>Wraps the envelope so it can stand in an <see cref="ActionResult{TValue}"/>.</summary>
    private sealed class EnvelopeResult(ProblemDetails problem) : ActionResult
    {
        public override Task ExecuteResultAsync(ActionContext context) => new ProblemEnvelopeResult(problem).ExecuteResultAsync(context);
    }
}
