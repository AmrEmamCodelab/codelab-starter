using Codelab.Security.Problems;
using Codelab.Security.Replay;
using Codelab.Security.Sessions;
using Microsoft.Extensions.Options;

namespace Codelab.Security.Signing;

/// <summary>
/// Verifies <c>{Prefix}-SIG-v1</c> request proofs. Order (fixed): OPTIONS and mode Off pass untouched; services are
/// resolved (failure: 503 in Enforce, pass in Report); unrouted, unauthenticated and whole-request-exempt requests
/// pass; the body is hashed within the cap (413 past it); the proof is verified; in Enforce the nonce is consumed
/// in the session's scope (replay 401, store failure 503); the session key is stashed for the response signer.
/// Runs after authentication (UseCodelabSecurity places it).
/// </summary>
public sealed class RequestSignatureMiddleware(
    RequestDelegate next,
    IOptionsMonitor<RequestSigningOptions> signingOptions,
    IOptionsMonitor<ReplayProtectionOptions> replayOptions,
    IOptionsMonitor<SecuritySessionOptions> sessionOptions,
    ILogger<RequestSignatureMiddleware> logger)
{
    private const int StoreUnavailableRetryAfterSeconds = 5;

    /// <summary>Runs the middleware.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var options = signingOptions.CurrentValue;
        if (options.Mode is SecurityEnforcementMode.Off || HttpMethods.IsOptions(context.Request.Method))
        {
            await next(context);
            return;
        }

        var services = SigningServices.TryResolve(context.RequestServices, out var failure);
        if (services is null)
        {
            await HandleUnavailableLayerAsync(context, options, failure);
            return;
        }

        if (!RequiresProof(context, out var exemption))
        {
            await next(context);
            return;
        }

        var refusal = await VerifyAsync(context, services, options, exemption);
        await (refusal is null ? next(context) : RefuseAsync(context, options, refusal));
    }

    private static bool RequiresProof(HttpContext context, out SignatureExemptAttribute? exemption)
    {
        exemption = context.GetEndpoint()?.Metadata.GetMetadata<SignatureExemptAttribute>();
        return context.GetEndpoint() is not null
            && context.User.Identity?.IsAuthenticated == true
            && !SessionClaims.IsService(context.User)
            && exemption?.Scope is not (SignatureExemption.WholeRequest or SignatureExemption.KeyIssuance);
    }

    private async Task<SecurityProblem?> VerifyAsync(
        HttpContext context, SigningServices services, RequestSigningOptions options, SignatureExemptAttribute? exemption)
    {
        var enforcing = options.Mode is SecurityEnforcementMode.Enforce;
        var bodyHash = exemption?.Scope is SignatureExemption.BodyOnly
            ? SignatureCanonicalForm.UnsignedBody
            : await SignedRequestBody.TryHashAsync(context.Request, options.MaxSignedBodyBytes, enforcing, context.RequestAborted);
        if (bodyHash is null)
        {
            SecurityLog.SignedBodyTooLarge(logger, context.Request.Method, context.Request.Path, options.MaxSignedBodyBytes);
            return new SecurityProblem(StatusCodes.Status413PayloadTooLarge, "Request body too large.", SecurityProblemCodes.PayloadTooLarge);
        }

        var input = BuildInput(context, services.Protocol, bodyHash);
        var verdict = services.Verifier.Verify(input);
        if (!verdict.IsValid)
        {
            return Rejection(verdict.Reason ?? ProofRejectionReason.Invalid);
        }

        var replayRefusal = enforcing ? await ConsumeAsync(context, services, options, input, verdict.Verified!) : null;
        if (replayRefusal is null)
        {
            Bind(context, services, input, verdict.Verified!);
        }

        return replayRefusal;
    }

    private ProofVerificationInput BuildInput(HttpContext context, SecurityProtocol protocol, string bodyHash)
    {
        var rawQuery = context.Request.QueryString.Value ?? string.Empty;
        var sessions = sessionOptions.CurrentValue;
        return new ProofVerificationInput(
            Method: context.Request.Method,
            Path: SignedPath(context.Request),
            Query: rawQuery.StartsWith('?') ? rawQuery[1..] : rawQuery,
            BodyHash: bodyHash,
            Proof: context.Request.Headers[protocol.SignatureHeaderName].ToString(),
            SessionKeyId: SessionClaims.Value(context.User, [sessions.SessionKeyIdClaimType]),
            Session: SessionClaims.Identity(context.User, sessions));
    }

    /// <summary>
    /// The path a browser proof covers: <c>Request.Path</c> WITHOUT <c>PathBase</c>, the service-relative path
    /// (<c>/items</c>). @codelab/ng-security signs the URL with its configured API root removed, so an IIS child
    /// application (PathBase <c>/starter-api</c>) or an edge proxy that strips <c>/api</c> verifies the same way. The
    /// service-to-service scheme differs on purpose: a service client knows only the full URL, so it signs PathBase too.
    /// </summary>
    internal static string SignedPath(HttpRequest request) => request.Path.ToUriComponent();

    private async Task<SecurityProblem?> ConsumeAsync(
        HttpContext context, SigningServices services, RequestSigningOptions options, ProofVerificationInput input, VerifiedProof proof)
    {
        var retention = TimeSpan.FromSeconds(options.MaxAgeSeconds + replayOptions.CurrentValue.RetentionMarginSeconds);
        var nonce = new ReplayNonce(input.Session!.Scope, proof.Nonce, proof.IssuedAt + retention);
        var outcome = await services.ReplayGuard.TryConsumeAsync(nonce, context.RequestAborted);
        return outcome switch
        {
            NonceConsumeOutcome.Fresh => null,
            NonceConsumeOutcome.Replayed => Rejection(ProofRejectionReason.Replayed),
            _ => new SecurityProblem(StatusCodes.Status503ServiceUnavailable, "The request could not be checked for replay.", Code: null)
            {
                RetryAfterSeconds = StoreUnavailableRetryAfterSeconds,
            },
        };
    }

    private void Bind(HttpContext context, SigningServices services, ProofVerificationInput input, VerifiedProof proof)
    {
        if (services.Keyring.TryGet(input.SessionKeyId, out var masterKey))
        {
            var sessionKey = services.Derivation.Derive(masterKey, input.Session!);
            SignedRequestBinding.Stash(context, new SignedRequestBinding(sessionKey, proof.Nonce));
            return;
        }

        SecurityLog.SigningKeyRotatedMidRequest(logger, context.Request.Method, context.Request.Path);
    }

    private async Task RefuseAsync(HttpContext context, RequestSigningOptions options, SecurityProblem refusal)
    {
        var code = refusal.Code ?? string.Empty;
        if (options.Mode is not SecurityEnforcementMode.Enforce)
        {
            SecurityLog.SignatureReported(logger, context.Request.Method, context.Request.Path, code);
            await next(context);
            return;
        }

        SecurityLog.SignatureRejected(logger, context.Request.Method, context.Request.Path, code);
        await SecurityProblemWriter.TryWriteAsync(context, refusal);
    }

    private async Task HandleUnavailableLayerAsync(HttpContext context, RequestSigningOptions options, Exception? failure)
    {
        SecurityLog.SigningLayerUnavailable(logger, failure, options.Mode);
        if (options.Mode is SecurityEnforcementMode.Enforce)
        {
            var problem = new SecurityProblem(StatusCodes.Status503ServiceUnavailable, "The request could not be verified.", Code: null)
            {
                RetryAfterSeconds = StoreUnavailableRetryAfterSeconds,
            };
            await SecurityProblemWriter.TryWriteAsync(context, problem);
            return;
        }

        await next(context);
    }

    private static SecurityProblem Rejection(ProofRejectionReason reason) =>
        new(StatusCodes.Status401Unauthorized, "Request signature rejected.", SecurityProblemCodes.ForRejection(reason));
}
