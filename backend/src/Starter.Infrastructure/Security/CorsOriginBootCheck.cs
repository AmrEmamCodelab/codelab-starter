using Codelab.Security.Cors;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Starter.Infrastructure.Security;

/// <summary>
/// The boot check of <c>Codelab:Security:Cors:AllowedOrigins</c>. It refuses, in one message listing every problem:
/// a wildcard origin (with credentials always, without them outside Development), a non-https origin outside
/// Development, anything that is not a bare <c>scheme://host[:port]</c> (a path, query, trailing slash or the
/// default port spelled out), an origin with user info, and the same origin listed twice (compared as the browser
/// sends it: scheme and host case-insensitively). The first three are Codelab.Security's own rules
/// (<see cref="CorsOriginValidator"/>), kept here so the check stands on its own; user info and duplicates are the
/// starter's additions (a browser never sends user info, and a duplicate is almost always a copy-paste slip that hides
/// the origin that was meant).
/// </summary>
public static class CorsOriginBootCheck
{
    /// <summary>Every problem with <paramref name="options"/> (empty when valid).</summary>
    public static IReadOnlyList<string> Validate(CodelabCorsOptions options, bool isDevelopment)
    {
        ArgumentNullException.ThrowIfNull(options);
        return [.. CorsOriginValidator.Validate(options, isDevelopment), .. WithUserInfo(options.AllowedOrigins), .. Duplicates(options.AllowedOrigins)];
    }

    /// <summary>
    /// Validates the configured CORS options of <paramref name="app"/> and throws <see cref="OptionsValidationException"/>
    /// listing every problem, so a misconfigured host never starts. Resolving the options also runs Codelab.Security's
    /// own validator, which may throw first with the same rules.
    /// </summary>
    public static void EnsureValid(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var options = app.Services.GetRequiredService<IOptions<CodelabCorsOptions>>().Value;
        var problems = Validate(options, app.Environment.IsDevelopment());
        if (problems.Count > 0)
        {
            throw new OptionsValidationException(CodelabCorsOptions.SectionName, typeof(CodelabCorsOptions), problems);
        }
    }

    // A browser never sends user info in Origin, so such an entry would silently match nothing.
    private static IEnumerable<string> WithUserInfo(IEnumerable<string> origins) =>
        origins
            .Where(origin => Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.UserInfo.Length > 0)
            .Select(origin => $"{CodelabCorsOptions.SectionName}:AllowedOrigins '{origin}': an origin carries no user info.");

    private static IEnumerable<string> Duplicates(IEnumerable<string> origins) =>
        origins
            .GroupBy(BrowserForm, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"{CodelabCorsOptions.SectionName}:AllowedOrigins lists '{group.Key}' {group.Count()} times; list each origin once.");

    // The browser's Origin header has a lowercase scheme and host; a value that is not a URL is compared as written.
    private static string BrowserForm(string origin) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri) ? uri.GetLeftPart(UriPartial.Authority) : origin;
}
