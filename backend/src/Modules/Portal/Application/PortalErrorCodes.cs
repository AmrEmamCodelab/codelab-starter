namespace Starter.Modules.Portal.Application;

/// <summary>
/// The Portal module's error codes. The client renders <c>errors.&lt;CODE&gt;</c> from its ar and en bundles.
/// </summary>
public static class PortalErrorCodes
{
    /// <summary>The <c>{page}</c> segment names no content page (404).</summary>
    public const string PageNotFound = "PORTAL_PAGE_NOT_FOUND";

    /// <summary>The contact topic is unknown or retired (400, also the <c>topicId</c> field message).</summary>
    public const string TopicNotFound = "PORTAL_TOPIC_NOT_FOUND";

    /// <summary>A section id in a replace is not a section of that page, for example one another editor removed (409).</summary>
    public const string SectionNotOnPage = "PORTAL_SECTION_NOT_ON_PAGE";

    /// <summary>
    /// An administrator switched the public contact form off (runtime setting <c>portal.contact-enabled</c> is false);
    /// nothing is stored or sent (409).
    /// </summary>
    public const string ContactDisabled = "PORTAL_CONTACT_DISABLED";

    // scaffold:error-codes
}

/// <summary>
/// Field message codes the kernel <c>ValidationMessages</c> does not have (the client translates
/// <c>errors.&lt;CODE&gt;</c>; an untranslated one shows the generic invalid-field text).
/// </summary>
public static class PortalValidationMessages
{
    /// <summary>The field is shorter than allowed.</summary>
    public const string TooShort = "TOO_SHORT";

    /// <summary>The value appears more than once where it must be unique.</summary>
    public const string Duplicate = "DUPLICATE";
}
