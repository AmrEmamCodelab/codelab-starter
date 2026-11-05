using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Starter.Modules.Identity.Domain;

namespace Starter.Modules.Identity.Infrastructure.Persistence.Migrations;

/// <summary>
/// The Identity model as of <see cref="InitialCreate"/>, frozen when <see cref="AccountAdministration"/> changed
/// <see cref="IdentityMigrationModel"/>. A shipped migration's target model never changes.
/// </summary>
public static class InitialCreateModel
{
    private const string IdColumn = nameof(UserAccount.Id);

    /// <summary>Builds the model.</summary>
    public static void Build(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder
            .HasDefaultSchema(IdentityDbContext.SchemaName)
            .HasAnnotation(IdentitySqlTypes.ProductVersionAnnotation, IdentitySqlTypes.ProductVersion)
            .HasAnnotation(RelationalAnnotationNames.MaxIdentifierLength, IdentitySqlTypes.MaxIdentifierLength);
        modelBuilder.UseIdentityColumns();
        modelBuilder.Entity(typeof(UserAccount).FullName!, BuildUserAccount);
        modelBuilder.Entity(typeof(UserSession).FullName!, BuildUserSession);
    }

    private static void BuildUserAccount(EntityTypeBuilder entity)
    {
        IdentityColumn(entity);
        entity.Property<DateTimeOffset>(nameof(UserAccount.CreatedAt)).HasColumnType(IdentitySqlTypes.DateTimeOffset);
        StringColumn(entity, nameof(UserAccount.DisplayName), IdentityLimits.DisplayNameMaxLength);
        entity.Property<bool>(nameof(UserAccount.IsActive)).HasColumnType(IdentitySqlTypes.Bit);
        entity.Property<bool>(nameof(UserAccount.IsBreakGlass)).HasColumnType(IdentitySqlTypes.Bit);
        StringColumn(entity, nameof(UserAccount.NormalizedUserName), IdentityLimits.UserNameMaxLength);
        StringColumn(entity, nameof(UserAccount.PasswordHash), IdentityLimits.PasswordHashMaxLength);
        entity.Property<Guid>(nameof(UserAccount.SubjectId)).HasColumnType(IdentitySqlTypes.UniqueIdentifier);
        StringColumn(entity, nameof(UserAccount.UserName), IdentityLimits.UserNameMaxLength);
        entity.HasKey(IdColumn);
        entity.HasIndex(nameof(UserAccount.NormalizedUserName)).IsUnique();
        entity.HasIndex(nameof(UserAccount.SubjectId)).IsUnique();
        entity.ToTable(TableNames.UserAccounts, IdentityDbContext.SchemaName);
    }

    private static void BuildUserSession(EntityTypeBuilder entity)
    {
        IdentityColumn(entity);
        entity.Property<DateTimeOffset>(nameof(UserSession.CreatedAt)).HasColumnType(IdentitySqlTypes.DateTimeOffset);
        entity.Property<DateTimeOffset>(nameof(UserSession.ExpiresAt)).HasColumnType(IdentitySqlTypes.DateTimeOffset);
        entity.Property<DateTimeOffset>(nameof(UserSession.LastActivityAt)).HasColumnType(IdentitySqlTypes.DateTimeOffset);
        entity.Property<int>(nameof(UserSession.RefreshGeneration)).IsConcurrencyToken().HasColumnType(IdentitySqlTypes.Int32Column);
        entity.Property<byte[]>(nameof(UserSession.RefreshSecretHash))
            .IsRequired()
            .HasMaxLength(IdentityLimits.RefreshSecretHashBytes)
            .HasColumnType(IdentitySqlTypes.FixedBinary(IdentityLimits.RefreshSecretHashBytes))
            .IsFixedLength();
        entity.Property<DateTimeOffset?>(nameof(UserSession.RevokedAt)).HasColumnType(IdentitySqlTypes.DateTimeOffset);
        entity.Property<string>(nameof(UserSession.SessionId))
            .IsRequired()
            .HasMaxLength(IdentityLimits.SessionIdLength)
            .IsUnicode(false)
            .HasColumnType(IdentitySqlTypes.FixedAnsiText(IdentityLimits.SessionIdLength))
            .IsFixedLength();
        entity.Property<long>(nameof(UserSession.SessionVersion)).HasColumnType(IdentitySqlTypes.BigInt);
        entity.Property<long>(nameof(UserSession.UserId)).HasColumnType(IdentitySqlTypes.BigInt);
        entity.HasKey(IdColumn);
        entity.HasIndex(nameof(UserSession.SessionId)).IsUnique();
        entity.HasIndex(nameof(UserSession.UserId));
        entity.ToTable(TableNames.UserSessions, IdentityDbContext.SchemaName);
        entity.HasOne(typeof(UserAccount).FullName!, navigationName: null)
            .WithMany()
            .HasForeignKey(nameof(UserSession.UserId))
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();
    }

    private static void IdentityColumn(EntityTypeBuilder entity) =>
        entity.Property<long>(IdColumn).ValueGeneratedOnAdd().HasColumnType(IdentitySqlTypes.BigInt).UseIdentityColumn();

    private static void StringColumn(EntityTypeBuilder entity, string name, int maxLength) =>
        entity.Property<string>(name).IsRequired().HasMaxLength(maxLength).HasColumnType(IdentitySqlTypes.NVarChar(maxLength));
}
