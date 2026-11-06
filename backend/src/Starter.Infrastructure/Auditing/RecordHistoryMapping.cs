using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Starter.Domain.Auditing;

namespace Starter.Infrastructure.Auditing;

/// <summary>
/// Maps a record history table (a <see cref="RecordHistoryEntry"/> subtype) onto its record, and guards the one rule
/// that makes it evidence: the foreign key to the record is ON DELETE RESTRICT, so deleting a record with history is
/// refused by the database instead of cascading the timeline away.
/// </summary>
public static class RecordHistoryMapping
{
    /// <summary>
    /// Maps <typeparamref name="THistory"/>: identity key, column limits, the FK <see cref="RecordHistoryEntry.RecordId"/>
    /// to <typeparamref name="TRecord"/>'s primary key with <see cref="DeleteBehavior.Restrict"/>, and the timeline index
    /// (RecordId, At). Set the table name with <c>ToTable</c> as usual.
    /// </summary>
    public static EntityTypeBuilder<THistory> HasRecordHistory<TRecord, THistory>(this EntityTypeBuilder<THistory> builder)
        where TRecord : class
        where THistory : RecordHistoryEntry
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).UseIdentityColumn();
        builder.Property(entry => entry.Action).HasMaxLength(RecordHistoryLimits.ActionMaxLength).IsRequired();
        builder.Property(entry => entry.Reason).HasMaxLength(RecordHistoryLimits.ReasonMaxLength);
        builder.Property(entry => entry.ActorName).HasMaxLength(RecordHistoryLimits.ActorNameMaxLength).IsRequired();
        builder.HasOne<TRecord>()
            .WithMany()
            .HasForeignKey(entry => entry.RecordId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(entry => new { entry.RecordId, entry.At });
        return builder;
    }

    /// <summary>
    /// Every foreign key of a record history entity in <paramref name="model"/> that would delete or null the history
    /// when its principal is deleted (anything but Restrict, NoAction or ClientNoAction), as readable lines.
    /// </summary>
    public static IReadOnlyList<string> Violations(IReadOnlyModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.GetEntityTypes()
            .Where(entityType => typeof(RecordHistoryEntry).IsAssignableFrom(entityType.ClrType))
            .SelectMany(entityType => entityType.GetForeignKeys())
            .Where(foreignKey => !Keeps(foreignKey.DeleteBehavior))
            .Select(foreignKey => $"{foreignKey.DeclaringEntityType.DisplayName()} -> {foreignKey.PrincipalEntityType.DisplayName()} is {foreignKey.DeleteBehavior}; record history needs Restrict (use HasRecordHistory).")
            .ToList();
    }

    /// <summary>Refuses a model in which a record history could be deleted with its record.</summary>
    /// <exception cref="InvalidOperationException">A record history foreign key cascades or sets null.</exception>
    public static void EnsureRestrict(IReadOnlyModel model)
    {
        var violations = Violations(model);
        if (violations.Count > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, violations));
        }
    }

    private static bool Keeps(DeleteBehavior behavior) =>
        behavior is DeleteBehavior.Restrict or DeleteBehavior.NoAction or DeleteBehavior.ClientNoAction;
}
