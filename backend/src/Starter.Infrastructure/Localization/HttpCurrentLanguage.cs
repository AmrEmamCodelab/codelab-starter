using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using Starter.Application.Localization;
using Starter.Domain.Localization;

namespace Starter.Infrastructure.Localization;

/// <summary>
/// <see cref="ICurrentLanguage"/> from the request's <c>Accept-Language</c>: the supported language with the highest
/// quality wins; anything else, or no request, is Arabic (the product is Arabic-first).
/// </summary>
public sealed class HttpCurrentLanguage(IHttpContextAccessor accessor) : ICurrentLanguage
{
    /// <summary>The primary language subtags the product understands, to its languages.</summary>
    public static IReadOnlyDictionary<string, AppLanguage> SupportedTags { get; } =
        new Dictionary<string, AppLanguage>(StringComparer.OrdinalIgnoreCase)
        {
            [LanguageTags.Arabic] = AppLanguage.Arabic,
            [LanguageTags.English] = AppLanguage.English,
        };

    /// <inheritdoc />
    public AppLanguage Language => Resolve(accessor.HttpContext?.Request.Headers.AcceptLanguage.ToString());

    /// <summary>The language an <c>Accept-Language</c> value asks for (Arabic when none is supported).</summary>
    public static AppLanguage Resolve(string? acceptLanguage)
    {
        if (string.IsNullOrWhiteSpace(acceptLanguage)
            || !StringWithQualityHeaderValue.TryParseList([acceptLanguage], out var ranges))
        {
            return AppLanguage.Arabic;
        }

        return ranges
            .Where(range => range.Quality is not 0)
            .OrderByDescending(range => range.Quality ?? 1)
            .Select(range => PrimaryTag(range.Value.Value))
            .Where(tag => tag is not null && SupportedTags.ContainsKey(tag))
            .Select(tag => SupportedTags[tag!])
            .DefaultIfEmpty(AppLanguage.Arabic)
            .First();
    }

    private static string? PrimaryTag(string? range)
    {
        if (string.IsNullOrEmpty(range))
        {
            return null;
        }

        var separator = range.IndexOf(LanguageTags.SubtagSeparator, StringComparison.Ordinal);
        return separator < 0 ? range : range[..separator];
    }
}

/// <summary>The BCP 47 primary language subtags of the product's languages.</summary>
public static class LanguageTags
{
    /// <summary>Arabic.</summary>
    public const string Arabic = "ar";

    /// <summary>English.</summary>
    public const string English = "en";

    /// <summary>The separator between subtags (<c>ar-SA</c>).</summary>
    public const char SubtagSeparator = '-';
}
