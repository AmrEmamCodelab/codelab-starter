using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Starter.Infrastructure.Persistence;
using Starter.Modules.Identity.Application;
using Starter.Modules.Identity.Domain;

namespace Starter.Modules.Identity.Infrastructure.Persistence;

/// <summary>The Identity module's tables, in the <c>identity</c> schema: accounts, sessions and the auth event trail.</summary>
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : ModuleDbContext(options), IIdentityUnitOfWork
{
    /// <summary>The database schema of the module.</summary>
    public const string SchemaName = "identity";

    /// <summary>The accounts.</summary>
    public DbSet<UserAccount> Accounts => Set<UserAccount>();

    /// <summary>The sessions.</summary>
    public DbSet<UserSession> Sessions => Set<UserSession>();

    /// <summary>The authentication trail (append-only).</summary>
    public DbSet<AuthEvent> AuthEvents => Set<AuthEvent>();

    /// <inheritdoc />
    protected override string Schema => SchemaName;
}

/// <summary>The <c>identity.UserAccounts</c> table.</summary>
public sealed class UserAccountConfiguration : IEntityTypeConfiguration<UserAccount>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<UserAccount> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(TableNames.UserAccounts);
        builder.HasKey(account => account.Id);
        builder.Property(account => account.Id).UseIdentityColumn();
        builder.HasIndex(account => account.SubjectId).IsUnique();
        builder.Property(account => account.UserName).HasMaxLength(IdentityLimits.UserNameMaxLength).IsRequired();
        builder.Property(account => account.NormalizedUserName).HasMaxLength(IdentityLimits.UserNameMaxLength).IsRequired();
        builder.HasIndex(account => account.NormalizedUserName).IsUnique();
        builder.Property(account => account.DisplayName).HasMaxLength(IdentityLimits.DisplayNameMaxLength).IsRequired();
        builder.Property(account => account.PasswordHash).HasMaxLength(IdentityLimits.PasswordHashMaxLength);
        OptionalIdentifier(builder, account => account.Email, account => account.NormalizedEmail, IdentityLimits.EmailMaxLength);
        OptionalIdentifier(builder, account => account.LoginName, account => account.NormalizedLoginName, IdentityLimits.LoginNameMaxLength);
        OptionalIdentifier(builder, account => account.Upn, account => account.NormalizedUpn, IdentityLimits.UpnMaxLength);
        builder.Property(account => account.ActivationTicketId).HasMaxLength(IdentityLimits.ActivationTicketIdMaxLength).IsUnicode(false);
        builder.HasIndex(account => account.OrganizationId);
    }

    private static void OptionalIdentifier(
        EntityTypeBuilder<UserAccount> builder,
        Expression<Func<UserAccount, string?>> value,
        Expression<Func<UserAccount, string?>> normalized,
        int maxLength)
    {
        builder.Property(value).HasMaxLength(maxLength);
        builder.Property(normalized).HasMaxLength(maxLength);
        var column = IdentityIndexFilters.ColumnOf(normalized);
        builder.HasIndex(column).IsUnique().HasFilter(IdentityIndexFilters.NotNull(column));
    }
}

/// <summary>The <c>identity.UserSessions</c> table.</summary>
public sealed class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<UserSession> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(TableNames.UserSessions);
        builder.HasKey(session => session.Id);
        builder.Property(session => session.Id).UseIdentityColumn();
        builder.Property(session => session.SessionId).HasMaxLength(IdentityLimits.SessionIdLength).IsFixedLength().IsUnicode(false).IsRequired();
        builder.HasIndex(session => session.SessionId).IsUnique();
        builder.Property(session => session.RefreshSecretHash).HasMaxLength(IdentityLimits.RefreshSecretHashBytes).IsFixedLength().IsRequired();
        builder.Property(session => session.RefreshGeneration).IsConcurrencyToken();
        builder.HasIndex(session => session.UserId);
        builder.Property(session => session.IdTokenHint).HasMaxLength(IdentityLimits.IdTokenHintMaxLength).IsUnicode(false);
        builder.HasOne<UserAccount>().WithMany().HasForeignKey(session => session.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>The <c>identity.AuthEvents</c> table: no foreign key (a row outlives its account), one index per incident question.</summary>
public sealed class AuthEventConfiguration : IEntityTypeConfiguration<AuthEvent>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AuthEvent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(TableNames.AuthEvents);
        builder.HasKey(authEvent => authEvent.Id);
        builder.Property(authEvent => authEvent.Id).UseIdentityColumn();
        builder.Property(authEvent => authEvent.UserNameHash).HasMaxLength(IdentityLimits.UserNameHashMaxLength).IsUnicode(false);
        builder.Property(authEvent => authEvent.CorrelationId).HasMaxLength(IdentityLimits.CorrelationIdMaxLength);
        builder.HasIndex(authEvent => authEvent.OccurredAt);
        builder.HasIndex(authEvent => new { authEvent.UserId, authEvent.OccurredAt });
        builder.HasIndex(authEvent => new { authEvent.UserNameHash, authEvent.OccurredAt });
    }
}

/// <summary>The filters of the Identity filtered unique indexes (an absent identifier is not unique).</summary>
public static class IdentityIndexFilters
{
    /// <summary>The filter <c>[column] IS NOT NULL</c>.</summary>
    public static string NotNull(string column) => $"[{column}] IS NOT NULL";

    /// <summary>The property name an accessor expression names.</summary>
    public static string ColumnOf<TEntity>(Expression<Func<TEntity, string?>> property)
    {
        ArgumentNullException.ThrowIfNull(property);
        return ((MemberExpression)property.Body).Member.Name;
    }
}

/// <summary>The Identity table names.</summary>
public static class TableNames
{
    /// <summary>The accounts table.</summary>
    public const string UserAccounts = "UserAccounts";

    /// <summary>The sessions table.</summary>
    public const string UserSessions = "UserSessions";

    /// <summary>The authentication trail table.</summary>
    public const string AuthEvents = "AuthEvents";
}
