using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Codelab.Security.Headers;

/// <summary>
/// Writes the API security header baseline on every response (including short-circuited and error responses), the
/// strict JSON CSP on JSON responses outside the relaxed prefixes, <c>Cache-Control: no-store</c> on authenticated
/// JSON, and HSTS over HTTPS. Headers are written in <c>OnStarting</c>, so they survive <c>Response.Clear()</c> and
/// overwrite any value set earlier. <c>Server</c> and <c>X-Powered-By</c> are removed; X-XSS-Protection is never sent.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next, IOptionsMonitor<SecurityHeaderOptions> options)
{
    /// <summary>The CSP for JSON responses: nothing may load, frame or submit.</summary>
    public const string JsonContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";

    /// <summary>The no-store directive for authenticated JSON.</summary>
    public const string NoStore = "no-store";

    private const string JsonMediaType = "application/json";
    private const string JsonSuffix = "+json";
    private const string PoweredByHeader = "X-Powered-By";

    /// <summary>The baseline header set, written on every response.</summary>
    public static readonly IReadOnlyDictionary<string, string> Baseline = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [HeaderNames.XContentTypeOptions] = "nosniff",
        [HeaderNames.XFrameOptions] = "DENY",
        ["Referrer-Policy"] = "no-referrer",
        ["Cross-Origin-Resource-Policy"] = "same-origin",
        ["X-Permitted-Cross-Domain-Policies"] = "none",
        ["Permissions-Policy"] =
            "accelerometer=(), autoplay=(), camera=(), display-capture=(), encrypted-media=(), fullscreen=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), midi=(), payment=(), picture-in-picture=(), screen-wake-lock=(), sync-xhr=(), usb=(), xr-spatial-tracking=()",
    };

    /// <summary>Runs the middleware.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var settings = options.CurrentValue;
        if (settings.Enabled)
        {
            context.Response.OnStarting(static state => Write((ValueTuple<HttpContext, SecurityHeaderOptions>)state), (context, settings));
        }

        await next(context);
    }

    private static Task Write(ValueTuple<HttpContext, SecurityHeaderOptions> state)
    {
        var (http, settings) = state;
        var headers = http.Response.Headers;
        foreach (var (name, value) in Baseline)
        {
            headers[name] = value;
        }

        headers.Remove(HeaderNames.Server);
        headers.Remove(PoweredByHeader);
        if (!IsRelaxed(http, settings) && IsJson(http.Response.ContentType))
        {
            headers[HeaderNames.ContentSecurityPolicy] = JsonContentSecurityPolicy;
            if (http.User.Identity?.IsAuthenticated == true)
            {
                headers[HeaderNames.CacheControl] = NoStore;
            }
        }

        if (ShouldSendHsts(http, settings.Hsts))
        {
            headers[HeaderNames.StrictTransportSecurity] = settings.Hsts.ToHeaderValue();
        }

        return Task.CompletedTask;
    }

    private static bool IsRelaxed(HttpContext http, SecurityHeaderOptions settings) =>
        settings.RelaxedPathPrefixes.Any(prefix => http.Request.Path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));

    private static bool IsJson(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType))
        {
            return false;
        }

        var semicolon = contentType.IndexOf(';', StringComparison.Ordinal);
        var media = (semicolon < 0 ? contentType : contentType[..semicolon]).Trim();
        return media.Equals(JsonMediaType, StringComparison.OrdinalIgnoreCase) || media.EndsWith(JsonSuffix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldSendHsts(HttpContext http, HstsOptions hsts) =>
        hsts.Enabled
        && http.Request.IsHttps
        && !hsts.ExcludedHosts.Any(excluded => string.Equals(http.Request.Host.Host, excluded, StringComparison.OrdinalIgnoreCase));
}
