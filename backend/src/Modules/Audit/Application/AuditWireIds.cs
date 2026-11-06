using Codelab.Security.PublicIds;

namespace Starter.Modules.Audit.Application;

/// <summary>The family of audit entry ids on the wire (<c>PublicId&lt;AuditEntryIds&gt;</c>), also the page cursor.</summary>
public sealed class AuditEntryIds : IIdDomain
{
    /// <summary>The domain label bound into every audit entry PublicId (part of the wire format; never rename).</summary>
    public const string DomainLabel = "audit-entries";

    /// <inheritdoc />
    public static string Domain => DomainLabel;
}

/// <summary>
/// The family of audited row ids on the wire (<c>PublicId&lt;AuditedEntityIds&gt;</c>): the key of the audited row and
/// every key or foreign key value inside its changes. One family for every entity type, so the viewer never shows a
/// number; the entity type travels beside it.
/// </summary>
public sealed class AuditedEntityIds : IIdDomain
{
    /// <summary>The domain label bound into every audited row PublicId (part of the wire format; never rename).</summary>
    public const string DomainLabel = "audit-entities";

    /// <inheritdoc />
    public static string Domain => DomainLabel;
}

/// <summary>The paging of the audit viewer.</summary>
public static class AuditPaging
{
    /// <summary>The page size when the caller sends none.</summary>
    public const int DefaultTake = 20;

    /// <summary>The largest page.</summary>
    public const int MaxTake = 100;

    /// <summary>The longest entity type filter (the column's length).</summary>
    public const int EntityTypeMaxLength = 128;
}
