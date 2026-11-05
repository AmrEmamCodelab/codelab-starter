using Codelab.Errors;
using Codelab.Security.Sessions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Starter.Modules.Identity.Infrastructure.Options;

namespace Starter.Modules.Identity.Infrastructure.Security;

/// <summary>
/// The bearer scheme that accepts the access tokens of <see cref="JwtAccessTokenIssuer"/>: signature, issuer, audience
/// and lifetime checked, claim names kept as issued (<c>sub</c>, <c>sid</c>), and a revoked session id refused on every
/// request (a token replayed after logout gets 401).
/// </summary>
public sealed class BearerAuthentication(IOptions<JwtOptions> jwt, TimeProvider clock) : IConfigureNamedOptions<JwtBearerOptions>
{
    /// <summary>The tolerated clock difference between the issuing and the validating node.</summary>
    public static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(SkewSeconds);

    /// <summary>The failure message of a revoked or session-less token (logged, never sent).</summary>
    public const string RevokedSessionMessage = "The session of this token was ended.";

    private const int SkewSeconds = 30;

    /// <inheritdoc />
    public void Configure(string? name, JwtBearerOptions options)
    {
        if (name == JwtBearerDefaults.AuthenticationScheme)
        {
            Configure(options);
        }
    }

    /// <inheritdoc />
    public void Configure(JwtBearerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var settings = jwt.Value;
        options.MapInboundClaims = false;
        options.TimeProvider = clock;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = settings.Issuer,
            ValidAudience = settings.Audience,
            IssuerSigningKey = SigningKey.Of(settings),
            ValidAlgorithms = [SigningKey.Algorithm],
            ClockSkew = ClockSkew,
            NameClaimType = SecurityClaimTypes.Subject,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = RejectRevokedSessionAsync,
            OnChallenge = WriteChallengeProblemAsync,
        };
    }

    /// <summary>Fails the authentication of a token without a session id or with a revoked one.</summary>
    public static async Task RejectRevokedSessionAsync(TokenValidatedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var sessionId = context.Principal?.FindFirst(SecurityClaimTypes.SessionId)?.Value;
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            context.Fail(RevokedSessionMessage);
            return;
        }

        var revocations = context.HttpContext.RequestServices.GetRequiredService<ISessionRevocationStore>();
        if (await revocations.IsRevokedAsync(sessionId, context.HttpContext.RequestAborted))
        {
            context.Fail(RevokedSessionMessage);
        }
    }

    /// <summary>
    /// Answers the bearer challenge (no token, a bad token or a revoked session) with a problem+json 401: MVC endpoints
    /// are skipped by the status-code pages, so the challenge writes the Codelab.Errors envelope itself.
    /// </summary>
    public static Task WriteChallengeProblemAsync(JwtBearerChallengeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.HandleResponse();
        var response = context.Response;
        response.StatusCode = StatusCodes.Status401Unauthorized;
        response.Headers.WWWAuthenticate = JwtBearerDefaults.AuthenticationScheme;
        var statusContext = new StatusCodeContext(context.HttpContext, new StatusCodePagesOptions(), static _ => Task.CompletedTask);
        return StatusCodePagesWriter.WriteAsync(statusContext);
    }
}
