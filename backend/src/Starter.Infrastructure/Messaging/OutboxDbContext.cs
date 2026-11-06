using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Starter.Application.Messaging;
using Starter.Infrastructure.Persistence;

namespace Starter.Infrastructure.Messaging;

/// <summary>The kernel outbox table, in the <c>messaging</c> schema, with its own migrations history.</summary>
public sealed class OutboxDbContext(DbContextOptions<OutboxDbContext> options) : ModuleDbContext(options)
{
    /// <summary>The database schema of the outbox.</summary>
    public const string SchemaName = "messaging";

    /// <summary>The outbox rows.</summary>
    public DbSet<OutboxEntry> Messages => Set<OutboxEntry>();

    /// <inheritdoc />
    protected override string Schema => SchemaName;
}

/// <summary>One row of <c>messaging.OutboxMessages</c>.</summary>
public sealed class OutboxEntry
{
    /// <summary>The row id (identity).</summary>
    public long Id { get; private set; }

    /// <summary>The message type (an <see cref="OutboxMessageTypes"/> constant).</summary>
    public string Type { get; private set; } = string.Empty;

    /// <summary>The payload JSON.</summary>
    public string PayloadJson { get; private set; } = string.Empty;

    /// <summary>The delivery state (stored as int).</summary>
    public OutboxMessageStatus Status { get; private set; }

    /// <summary>When the row was written.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>The earliest delivery time, or null for as soon as possible; a retry moves it out by the back-off.</summary>
    public DateTimeOffset? NotBefore { get; private set; }

    /// <summary>Delivery attempts so far, counted on claim (0 until the first drain claims the row).</summary>
    public int Attempt { get; private set; }

    /// <summary>The lease of the drain that holds a <see cref="OutboxMessageStatus.Processing"/> row, else null.</summary>
    public DateTimeOffset? LockedUntil { get; private set; }

    /// <summary>When the row was delivered, or null.</summary>
    public DateTimeOffset? SentAt { get; private set; }

    /// <summary>The classified code of the last failed attempt (never an exception message or payload), or null.</summary>
    public string? LastError { get; private set; }

    /// <summary>A Pending row for <paramref name="message"/>, written at <paramref name="createdAt"/>.</summary>
    public static OutboxEntry Pending(OutboxMessage message, DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(message);
        return new OutboxEntry
        {
            Type = message.Type,
            PayloadJson = message.PayloadJson,
            Status = OutboxMessageStatus.Pending,
            CreatedAt = createdAt,
            NotBefore = message.NotBefore,
        };
    }
}

/// <summary>The <c>messaging.OutboxMessages</c> table.</summary>
public sealed class OutboxEntryConfiguration : IEntityTypeConfiguration<OutboxEntry>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OutboxEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(OutboxTableNames.OutboxMessages);
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).UseIdentityColumn();
        builder.Property(entry => entry.Type).HasMaxLength(OutboxLimits.TypeMaxLength).IsRequired();
        builder.Property(entry => entry.PayloadJson).IsRequired();
        builder.Property(entry => entry.LastError).HasMaxLength(OutboxColumnLimits.LastErrorMaxLength);
        builder.HasIndex(entry => new { entry.Status, entry.CreatedAt });
    }
}

/// <summary>The column sizes of the outbox table that are not part of the Application contract.</summary>
public static class OutboxColumnLimits
{
    /// <summary>The longest <see cref="OutboxEntry.LastError"/> (a classified error code).</summary>
    public const int LastErrorMaxLength = 128;
}

/// <summary>The outbox table names.</summary>
public static class OutboxTableNames
{
    /// <summary>The outbox table.</summary>
    public const string OutboxMessages = "OutboxMessages";
}
