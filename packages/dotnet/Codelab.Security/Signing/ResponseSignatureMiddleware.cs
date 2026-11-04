using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace Codelab.Security.Signing;

/// <summary>
/// Signs responses to verified requests with <c>X-{Prefix}-Response-Signature</c>: HMAC under the request's session
/// key over (request nonce, status, content type, body hash). The body is buffered only for verified requests, and
/// not at all for <see cref="UnsignedResponseAttribute"/> endpoints (their body field is <c>UNSIGNED-BODY</c>).
/// A signing failure sends the response unsigned and logs it; a verifying client refuses such a response.
/// </summary>
public sealed class ResponseSignatureMiddleware(
    RequestDelegate next,
    IOptionsMonitor<ResponseSigningOptions> options,
    ILogger<ResponseSignatureMiddleware> logger)
{
    /// <summary>Runs the middleware.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var settings = options.CurrentValue;
        var endpoint = context.GetEndpoint();
        if (settings.Mode is SecurityEnforcementMode.Off || endpoint is null)
        {
            await next(context);
            return;
        }

        var state = new ResponseSignatureState(context, logger);
        context.Response.OnStarting(static s => ((ResponseSignatureState)s).WriteSignature(), state);
        if (endpoint.Metadata.GetMetadata<UnsignedResponseAttribute>() is not null)
        {
            state.BodyHash = SignatureCanonicalForm.UnsignedBody;
            await next(context);
            return;
        }

        await CaptureAndSignAsync(context, state, settings, endpoint.DisplayName ?? string.Empty);
    }

    private async Task CaptureAndSignAsync(HttpContext context, ResponseSignatureState state, ResponseSigningOptions settings, string endpointName)
    {
        var originalFeature = context.Features.Get<IHttpResponseBodyFeature>()!;
        var originalBody = originalFeature.Stream;
        await using var capture = new CapturingResponseBodyStream(context, originalBody);
        var captureFeature = new StreamResponseBodyFeature(capture, originalFeature);
        context.Features.Set<IHttpResponseBodyFeature>(captureFeature);
        try
        {
            await next(context);

            // Flushes whatever the endpoint wrote through the PipeWriter into the capture.
            await captureFeature.CompleteAsync();
        }
        finally
        {
            context.Features.Set(originalFeature);
        }

        if (!capture.IsCovering)
        {
            return;
        }

        state.BodyHash = capture.HashCapturedBody();
        if (capture.CapturedLength >= settings.LargeBodyWarningBytes)
        {
            SecurityLog.LargeSignedResponse(logger, endpointName, capture.CapturedLength);
        }

        await capture.CopyCapturedToAsync(originalBody, context.RequestAborted);
    }

    private sealed class ResponseSignatureState(HttpContext context, ILogger logger)
    {
        public string? BodyHash { get; set; }

        public Task WriteSignature()
        {
            var binding = SignedRequestBinding.Find(context);
            if (binding is null)
            {
                return Task.CompletedTask;
            }

            if (BodyHash is null)
            {
                SecurityLog.ResponseUnsignable(logger, context.Request.Path);
                return Task.CompletedTask;
            }

            try
            {
                Sign(binding);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                SecurityLog.ResponseSigningFailed(logger, ex, context.Request.Path);
            }

            return Task.CompletedTask;
        }

        private void Sign(SignedRequestBinding binding)
        {
            var protocol = context.RequestServices.GetRequiredService<SecurityProtocol>();
            var forms = new SignatureCanonicalForm(protocol);
            var canonical = forms.ResponseBytes(new SignedResponseFields(
                RequestNonce: binding.RequestNonce,
                Status: context.Response.StatusCode,
                ContentType: context.Response.ContentType ?? string.Empty,
                BodyHash: BodyHash!));
            context.Response.Headers[protocol.ResponseSignatureHeaderName] =
                ResponseSignatureHeader.Format(HmacSignature.Compute(binding.SessionKey, canonical));
        }
    }
}
