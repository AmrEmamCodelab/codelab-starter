using Codelab.Security.PublicIds;

namespace Starter.Modules.Portal.Application;

/// <summary>The family of content section ids on the wire (<c>PublicId&lt;PortalSectionIds&gt;</c>).</summary>
public sealed class PortalSectionIds : IIdDomain
{
    /// <summary>The domain label bound into every section PublicId (part of the wire format; never rename).</summary>
    public const string DomainLabel = "portal-sections";

    /// <inheritdoc />
    public static string Domain => DomainLabel;
}

/// <summary>The reference number prefixes the Portal module allocates (<c>IRequestNumberAllocator</c>).</summary>
public static class PortalNumberPrefixes
{
    /// <summary>A contact-us message: <c>CU-2026-001</c>.</summary>
    public const string ContactUs = "CU";
}
