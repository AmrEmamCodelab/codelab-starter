namespace Starter.Domain.Localization;

/// <summary>
/// The languages the product speaks. Arabic is the default (Arabic-first, right to left). Stored and sent as the
/// integer value; labels come from the client's i18n bundles, never from the member names.
/// </summary>
public enum AppLanguage
{
    /// <summary>Arabic (right to left), the default.</summary>
    Arabic = 1,

    /// <summary>English (left to right).</summary>
    English = 2,
}
