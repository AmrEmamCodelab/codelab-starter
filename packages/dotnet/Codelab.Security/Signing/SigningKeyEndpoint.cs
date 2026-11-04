using Codelab.Security.Problems;
using Codelab.Security.Sessions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Codelab.Security.Signing;

/// <summary>The signing block the client stores per session. Wire names are camelCase and fixed (the client reads them).</summary>
/// <param name="Alg">Always <c>HMAC-SHA256</c>; an algorithm change is a visible wire change.</param>
/// <param name="SessionSigningKey">The 32-byte derived session key, base64. Never the master.</param>
/// <param name="Audience">The audience every proof must carry (a signed field the client cannot guess).</param>
/// <param name="VerifyResponses">True when the server signs responses (response signing is not Off).</param>
/// <param name="ServerTimeUnix">The server clock, so the client can correct its skew.</param>
public sealed record SessionSigningKeyResponse(string Alg, string SessionSigningKey, string Audience, bool VerifyResponses, long ServerTimeUnix);

/// <summary>
/// Issues the per-session signing key to an authenticated caller. Stateless: the key is derived from the master
/// named by the token's <c>skid</c> claim and the session identity, so every replica answers the same.
/// </summary>
public sealed class SigningKeyIssuer(
    IOptionsMonitor<RequestSigningOptions> signingOptions,
    IOptionsMonitor<ResponseSigningOptions> responseOptions,
    IOptionsMonitor<SecuritySessionOptions> sessionOptions,
    TimeProvider clock)
{
    /// <summary>The algorithm name the client checks.</summary>
    public const string Algorithm = "HMAC-SHA256";

    /// <summary>
    /// The result for <paramref name="context"/>: 404 when signing is Off or misconfigured (there is nothing to issue),
    /// 401 <c>signature_key_unbound</c> when the token yields no key, otherwise 200 with the block (no-store).
    /// </summary>
    public IResult Issue(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Response.Headers[HeaderNames.CacheControl] = "no-store";
        var signing = signingOptions.CurrentValue;
        if (signing.Mode is SecurityEnforcementMode.Off || string.IsNullOrWhiteSpace(signing.Audience))
        {
            return Results.NotFound();
        }

        var sessions = sessionOptions.CurrentValue;
        var identity = SessionClaims.Identity(context.User, sessions);
        var keyId = SessionClaims.Value(context.User, [sessions.SessionKeyIdClaimType]);
        var keyring = context.RequestServices.GetRequiredService<ISigningKeyring>();
        if (identity is null || !keyring.TryGet(keyId, out var masterKey))
        {
            return new SecurityProblemResult(new SecurityProblem(StatusCodes.Status401Unauthorized, "No signing key for this session.", SecurityProblemCodes.SignatureKeyUnbound));
        }

        var sessionKey = context.RequestServices.GetRequiredService<SessionKeyDerivation>().Derive(masterKey, identity);
        return Results.Ok(new SessionSigningKeyResponse(
            Alg: Algorithm,
            SessionSigningKey: Convert.ToBase64String(sessionKey),
            Audience: signing.Audience,
            VerifyResponses: responseOptions.CurrentValue.Mode is not SecurityEnforcementMode.Off,
            ServerTimeUnix: clock.GetUtcNow().ToUnixTimeSeconds()));
    }
}

/// <summary>Maps the signing-key issuance endpoint.</summary>
public static class SigningKeyEndpoint
{
    /// <summary>The default route.</summary>
    public const string DefaultPattern = "/api/security/session-signing-key";

    /// <summary>The exemption reason recorded on the endpoint.</summary>
    public const string ExemptionReason = "Issues the session signing key; the caller has no key to sign with yet.";

    /// <summary>
    /// Maps <c>GET {pattern}</c>: authenticated (never anonymous), key-issuance exempt from request signing, responses
    /// unsigned and <c>Cache-Control: no-store</c>. Chain <c>RequireRateLimiting</c> or a narrower authorization policy as needed.
    /// </summary>
    public static RouteHandlerBuilder MapCodelabSigningKeyEndpoint(this IEndpointRouteBuilder endpoints, string pattern = DefaultPattern)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return endpoints
            .MapGet(pattern, static (HttpContext context, SigningKeyIssuer issuer) => issuer.Issue(context))
            .RequireAuthorization()
            .WithSignatureExemption(SignatureExemption.KeyIssuance, ExemptionReason)
            .WithUnsignedResponse(ExemptionReason)
            .Produces<SessionSigningKeyResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);
    }
}
