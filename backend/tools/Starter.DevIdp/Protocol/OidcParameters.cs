using Microsoft.Extensions.Primitives;

namespace Starter.DevIdp.Protocol;

/// <summary>
/// Request parameters from a query string or a form. A parameter counts only when it is present exactly once with a
/// non-empty value: OAuth 2.0 forbids repeating a parameter, so a repeated one reads as missing.
/// </summary>
internal sealed class OidcParameters
{
    private readonly Func<string, StringValues> _lookup;

    private OidcParameters(Func<string, StringValues> lookup) => _lookup = lookup;

    /// <summary>Reads from a query string.</summary>
    public static OidcParameters From(IQueryCollection query) => new(name => query[name]);

    /// <summary>Reads from a form.</summary>
    public static OidcParameters From(IFormCollection form) => new(name => form[name]);

    /// <summary>Reads from plain pairs (tests).</summary>
    public static OidcParameters From(IReadOnlyDictionary<string, string> values) =>
        new(name => values.TryGetValue(name, out var value) ? new StringValues(value) : StringValues.Empty);

    /// <summary>The single non-empty value of <paramref name="name"/>, or <see langword="null"/>.</summary>
    public string? Get(string name)
    {
        var values = _lookup(name);
        return values.Count == 1 && !string.IsNullOrEmpty(values[0]) ? values[0] : null;
    }
}
