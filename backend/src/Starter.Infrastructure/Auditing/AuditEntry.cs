using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Starter.Domain.Auditing;

namespace Starter.Infrastructure.Auditing;

/// <summary>
/// One row of <c>audit.AuditEntries</c>: one audited change of one row of a module table, written by
/// <see cref="AuditSaveChangesInterceptor"/> in the same transaction as the change. Append-only: the DBA script denies
/// UPDATE and DELETE on the table to the application login. No foreign key to any user or record: an audit row must
/// outlive what it describes.
/// </summary>
[NotAudited]
public sealed class AuditEntry
{
    private AuditEntry()
    {
    }

    /// <summary>The row id (identity; also the keyset cursor of the viewer).</summary>
    public long Id { get; private set; }

    /// <summary>The audited entity type name (EF's short name, e.g. <c>Organization</c>).</summary>
    public string EntityType { get; private set; } = string.Empty;

    /// <summary>The audited row's key (<see cref="AuditKeys"/>), or null when the entity has none.</summary>
    public string? EntityKey { get; private set; }

    /// <summary>What happened to the row (stored as int).</summary>
    public AuditAction Action { get; private set; }

    /// <summary>The recorded property values as JSON (<see cref="AuditChangesJson"/>), already masked.</summary>
    public string ChangesJson { get; private set; } = string.Empty;

    /// <summary>The acting subject id, or <c>SYSTEM</c>.</summary>
    public string Actor { get; private set; } = string.Empty;

    /// <summary>The request or job correlation id, or null.</summary>
    public string? CorrelationId { get; private set; }

    /// <summary>When the change was saved (from <see cref="TimeProvider"/>).</summary>
    public DateTimeOffset At { get; private set; }

    /// <summary>A new row from <paramref name="facts"/>; over-long texts are cut to their column (never refused: an audit row must not fail the business save).</summary>
    public static AuditEntry From(AuditEntryFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return new AuditEntry
        {
            EntityType = Cut(facts.EntityType, AuditLimits.EntityTypeMaxLength)!,
            EntityKey = Cut(facts.EntityKey, AuditLimits.EntityKeyMaxLength),
            Action = facts.Action,
            ChangesJson = facts.ChangesJson,
            Actor = Cut(facts.Stamp.Actor, AuditLimits.ActorMaxLength)!,
            CorrelationId = Cut(facts.Stamp.CorrelationId, AuditLimits.CorrelationIdMaxLength),
            At = facts.Stamp.At,
        };
    }

    private static string? Cut(string? value, int maxLength) =>
        value is not null && value.Length > maxLength ? value[..maxLength] : value;
}

/// <summary>Who saved a batch of changes, and when: taken once per save.</summary>
/// <param name="Actor">The acting subject id, or <c>SYSTEM</c>.</param>
/// <param name="CorrelationId">The request or job correlation id, or null.</param>
/// <param name="At">When the save started.</param>
public sealed record AuditStamp(string Actor, string? CorrelationId, DateTimeOffset At);

/// <summary>The facts of one audit row.</summary>
/// <param name="EntityType">The audited entity type name.</param>
/// <param name="EntityKey">The row's key, or null.</param>
/// <param name="Action">What happened.</param>
/// <param name="ChangesJson">The recorded values (masked).</param>
/// <param name="Stamp">Who and when.</param>
public sealed record AuditEntryFacts(string EntityType, string? EntityKey, AuditAction Action, string ChangesJson, AuditStamp Stamp);

/// <summary>The column limits of <c>audit.AuditEntries</c>.</summary>
public static class AuditLimits
{
    /// <summary>The longest entity type name.</summary>
    public const int EntityTypeMaxLength = 128;

    /// <summary>The longest entity key.</summary>
    public const int EntityKeyMaxLength = 256;

    /// <summary>The longest actor (<see cref="Starter.Application.Auditing.AuditActors.MaxLength"/>).</summary>
    public const int ActorMaxLength = Starter.Application.Auditing.AuditActors.MaxLength;

    /// <summary>The longest correlation id.</summary>
    public const int CorrelationIdMaxLength = 128;
}

/// <summary>The audit table names.</summary>
public static class AuditTableNames
{
    /// <summary>The data-level audit trail.</summary>
    public const string AuditEntries = "AuditEntries";
}

/// <summary>The <c>audit.AuditEntries</c> table.</summary>
public sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(AuditTableNames.AuditEntries);
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).UseIdentityColumn();
        builder.Property(entry => entry.EntityType).HasMaxLength(AuditLimits.EntityTypeMaxLength).IsRequired();
        builder.Property(entry => entry.EntityKey).HasMaxLength(AuditLimits.EntityKeyMaxLength);
        builder.Property(entry => entry.ChangesJson).IsRequired();
        builder.Property(entry => entry.Actor).HasMaxLength(AuditLimits.ActorMaxLength).IsRequired();
        builder.Property(entry => entry.CorrelationId).HasMaxLength(AuditLimits.CorrelationIdMaxLength);
        builder.HasIndex(entry => new { entry.EntityType, entry.EntityKey, entry.Id });
    }
}
