using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Starter.Infrastructure.Persistence;
using Starter.Modules.Identity.Application;
using Starter.Modules.Identity.Domain;

namespace Starter.Modules.Identity.Infrastructure.Persistence;

/// <summary>The Identity module's tables, in the <c>identity</c> schema: accounts and sessions.</summary>
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : ModuleDbContext(options), IIdentityUnitOfWork
{
    /// <summary>The database schema of the module.</summary>
    public const string SchemaName = "identity";

    /// <summary>The accounts.</summary>
    public DbSet<UserAccount> Accounts => Set<UserAccount>();

    /// <summary>The sessions.</summary>
    public DbSet<UserSession> Sessions => Set<UserSession>();

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
        builder.Property(account => account.PasswordHash).HasMaxLength(IdentityLimits.PasswordHashMaxLength).IsRequired();
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
        builder.HasOne<UserAccount>().WithMany().HasForeignKey(session => session.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>The Identity table names.</summary>
public static class TableNames
{
    /// <summary>The accounts table.</summary>
    public const string UserAccounts = "UserAccounts";

    /// <summary>The sessions table.</summary>
    public const string UserSessions = "UserSessions";
}
