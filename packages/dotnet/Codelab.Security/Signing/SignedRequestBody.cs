using System.Buffers;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Net.Http.Headers;

namespace Codelab.Security.Signing;

/// <summary>
/// Hashes the request body for a proof, bounded by a byte cap, and rewinds it so model binding still reads it.
/// Returns null when the body is over the cap (or of unknown length outside Enforce, where buffering an unbounded body
/// would cost memory for a verdict nobody acts on).
/// <para>
/// "No body" is decided by the server, never by the <c>Transfer-Encoding</c> header alone: HTTP/2 and HTTP/3 stream a
/// body without <c>Content-Length</c> and have no <c>Transfer-Encoding</c>, so a header-only test would hash such a
/// body as empty and let unsigned bytes through as signed input. <see cref="IHttpRequestBodyDetectionFeature"/> knows.
/// </para>
/// </summary>
internal static class SignedRequestBody
{
    internal static async Task<string?> TryHashAsync(HttpRequest request, int maxBytes, bool enforcing, CancellationToken cancellationToken)
    {
        var declaredLength = request.ContentLength;
        if (!CanHaveBody(request))
        {
            return SignatureCanonicalForm.HashBody([]);
        }

        if (declaredLength > maxBytes || (declaredLength is null && !enforcing))
        {
            return null;
        }

        request.EnableBuffering(bufferThreshold: maxBytes, bufferLimit: maxBytes);
        var capacity = (int)Math.Min(maxBytes, declaredLength ?? maxBytes) + 1;
        var buffer = ArrayPool<byte>.Shared.Rent(capacity);
        try
        {
            var total = await FillAsync(request.Body, buffer.AsMemory(0, capacity), cancellationToken);
            request.Body.Position = 0;
            return total > maxBytes ? null : SignatureCanonicalForm.HashBody(buffer.AsSpan(0, total));
        }
        catch (IOException) when (declaredLength is null)
        {
            // The buffering limit fires as an IOException on a chunked body past the cap: that is "too large".
            return null;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    private static bool CanHaveBody(HttpRequest request)
    {
        if (request.ContentLength is { } length)
        {
            return length > 0;
        }

        var detection = request.HttpContext.Features.Get<IHttpRequestBodyDetectionFeature>();
        return detection?.CanHaveBody ?? request.Headers.ContainsKey(HeaderNames.TransferEncoding);
    }

    private static async Task<int> FillAsync(Stream body, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await body.ReadAsync(buffer[total..], cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }
}
