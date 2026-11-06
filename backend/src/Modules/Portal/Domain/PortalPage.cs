namespace Starter.Modules.Portal.Domain;

/// <summary>
/// The portal pages whose content the server keeps (stored as int; never reorder or renumber). On the wire a page is
/// its kebab key (<see cref="PortalPages"/>), the <c>{page}</c> route segment.
/// </summary>
public enum PortalPage
{
    /// <summary>The frequently asked questions (one section per question: title = question, body = answer).</summary>
    Faq = 1,

    /// <summary>The service guide.</summary>
    ServiceGuide = 2,

    /// <summary>The privacy policy.</summary>
    Privacy = 3,

    /// <summary>The terms of use.</summary>
    Terms = 4,
}

/// <summary>
/// The wire keys of <see cref="PortalPage"/> (the frontend's <c>PortalContentPages</c>): the one owner of the mapping.
/// </summary>
public static class PortalPages
{
    /// <summary>The key of <see cref="PortalPage.Faq"/>.</summary>
    public const string FaqKey = "faq";

    /// <summary>The key of <see cref="PortalPage.ServiceGuide"/>.</summary>
    public const string ServiceGuideKey = "service-guide";

    /// <summary>The key of <see cref="PortalPage.Privacy"/>.</summary>
    public const string PrivacyKey = "privacy";

    /// <summary>The key of <see cref="PortalPage.Terms"/>.</summary>
    public const string TermsKey = "terms";

    private static readonly IReadOnlyDictionary<PortalPage, string> Keys = new Dictionary<PortalPage, string>
    {
        [PortalPage.Faq] = FaqKey,
        [PortalPage.ServiceGuide] = ServiceGuideKey,
        [PortalPage.Privacy] = PrivacyKey,
        [PortalPage.Terms] = TermsKey,
    };

    /// <summary>Every page, in enum order.</summary>
    public static IReadOnlyList<PortalPage> All { get; } = [.. Keys.Keys.Order()];

    /// <summary>The wire key of <paramref name="page"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The page is not a defined member.</exception>
    public static string KeyOf(PortalPage page) =>
        Keys.TryGetValue(page, out var key) ? key : throw new ArgumentOutOfRangeException(nameof(page), page, "Not a portal page.");

    /// <summary>The page whose key is exactly <paramref name="key"/> (ordinal: keys are lower case), or null.</summary>
    public static PortalPage? Find(string? key)
    {
        foreach (var (page, pageKey) in Keys)
        {
            if (string.Equals(pageKey, key, StringComparison.Ordinal))
            {
                return page;
            }
        }

        return null;
    }
}
