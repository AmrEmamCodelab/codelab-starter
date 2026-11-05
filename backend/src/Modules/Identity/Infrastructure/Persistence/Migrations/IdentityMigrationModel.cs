using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Starter.Modules.Identity.Domain;

namespace Starter.Modules.Identity.Infrastructure.Persistence.Migrations;

/// <summary>
/// The Identity model as of the latest migration: the target model of <see cref="AccountAdministration"/> and the
/// model snapshot that <c>dotnet ef migrations add</c> diffs against. A later migration freezes its own copy (as
/// <see cref="InitialCreateModel"/> was frozen) before this one changes.
/// </summary>
public static class IdentityMigrationModel
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
        modelBuilder.Entity(typeof(AuthEvent).FullName!, BuildAuthEvent);
        modelBuilder.Entity(typeof(UserAccount).FullName!, BuildUserAccount);
        modelBuilder.Entity(typeof(UserSession).FullName!, BuildUserSession);
    }

    private static void BuildAuthEvent(EntityTypeBuilder entity)
    {
        IdentityColumn(entity);
        OptionalText(entity, nameof(AuthEvent.CorrelationId), IdentityLimits.CorrelationIdMaxLength);
        Integer(entity, nameof(AuthEvent.EventType));
        entity.Property<int?>(nameof(AuthEvent.FailureReason)).HasColumnType(IdentitySqlTypes.Int32Column);
        entity.Property<DateTimeOffset>(nameof(AuthEvent.OccurredAt)).HasColumnType(IdentitySqlTypes.DateTimeOffset);
        entity.Property<int?>(nameof(AuthEvent.SignInMode)).HasColumnType(IdentitySqlTypes.Int32Column);
        entity.Property<long?>(nameof(AuthEvent.UserId)).HasColumnType(IdentitySqlTypes.BigInt);
        OptionalAnsiText(entity, nameof(AuthEvent.UserNameHash), IdentityLimits.UserNameHashMaxLength);
        entity.HasKey(IdColumn);
        entity.HasIndex(nameof(AuthEvent.OccurredAt));
        entity.HasIndex(nameof(AuthEvent.UserId), nameof(AuthEvent.OccurredAt));
        entity.HasIndex(nameof(AuthEvent.UserNameHash), nameof(AuthEvent.OccurredAt));
        entity.ToTable(TableNames.AuthEvents, IdentityDbContext.SchemaName);
    }

    private static void BuildUserAccount(EntityTypeBuilder entity)
    {
        IdentityColumn(entity);
        entity.Property<DateTimeOffset?>(nameof(UserAccount.ActivatedAt)).HasColumnType(IdentitySqlTypes.DateTimeOffset);
        OptionalAnsiText(entity, nameof(UserAccount.ActivationTicketId), IdentityLimits.ActivationTicketIdMaxLength);
        Integer(entity, nameof(UserAccount.Binding));
        entity.Property<DateTimeOffset>(nameof(UserAccount.CreatedAt)).HasColumnType(IdentitySqlTypes.DateTimeOffset);
        StringColumn(entity, nameof(UserAccount.DisplayName), IdentityLimits.DisplayNameMaxLength);
        OptionalText(entity, nameof(UserAccount.Email), IdentityLimits.EmailMaxLength);
        Integer(entity, nameof(UserAccount.FailedAccessCount));
        entity.Property<bool>(nameof(UserAccount.IsActive)).HasColumnType(IdentitySqlTypes.Bit);
        entity.Property<bool>(nameof(UserAccount.IsBreakGlass)).HasColumnType(IdentitySqlTypes.Bit);
        Integer(entity, nameof(UserAccount.Kind));
        entity.Property<DateTimeOffset?>(nameof(UserAccount.LockoutEndsAt)).HasColumnType(IdentitySqlTypes.DateTimeOffset);
        OptionalText(entity, nameof(UserAccount.LoginName), IdentityLimits.LoginNameMaxLength);
        OptionalText(entity, nameof(UserAccount.NormalizedEmail), IdentityLimits.EmailMaxLength);
        OptionalText(entity, nameof(UserAccount.NormalizedLoginName), IdentityLimits.LoginNameMaxLength);
        OptionalText(entity, nameof(UserAccount.NormalizedUpn), IdentityLimits.UpnMaxLength);
        StringColumn(entity, nameof(UserAccount.NormalizedUserName), IdentityLimits.UserNameMaxLength);
        entity.Property<long?>(nameof(UserAccount.OrganizationId)).HasColumnType(IdentitySqlTypes.BigInt);
        OptionalText(entity, nameof(UserAccount.PasswordHash), IdentityLimits.PasswordHashMaxLength);
        entity.Property<Guid>(nameof(UserAccount.SubjectId)).HasColumnType(IdentitySqlTypes.UniqueIdentifier);
        OptionalText(entity, nameof(UserAccount.Upn), IdentityLimits.UpnMaxLength);
        StringColumn(entity, nameof(UserAccount.UserName), IdentityLimits.UserNameMaxLength);
        entity.HasKey(IdColumn);
        FilteredUniqueIndex(entity, nameof(UserAccount.NormalizedEmail));
        FilteredUniqueIndex(entity, nameof(UserAccount.NormalizedLoginName));
        FilteredUniqueIndex(entity, nameof(UserAccount.NormalizedUpn));
        entity.HasIndex(nameof(UserAccount.NormalizedUserName)).IsUnique();
        entity.HasIndex(nameof(UserAccount.OrganizationId));
        entity.HasIndex(nameof(UserAccount.SubjectId)).IsUnique();
        entity.ToTable(TableNames.UserAccounts, IdentityDbContext.SchemaName);
    }

    private static void BuildUserSession(EntityTypeBuilder entity)
    {
        IdentityColumn(entity);
        entity.Property<DateTimeOffset>(nameof(UserSession.CreatedAt)).HasColumnType(IdentitySqlTypes.DateTimeOffset);
        entity.Property<DateTimeOffset>(nameof(UserSession.ExpiresAt)).HasColumnType(IdentitySqlTypes.DateTimeOffset);
        OptionalAnsiText(entity, nameof(UserSession.IdTokenHint), IdentityLimits.IdTokenHintMaxLength);
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
        Integer(entity, nameof(UserSession.SignInMode));
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

    private static void Integer(EntityTypeBuilder entity, string name) =>
        entity.Property<int>(name).HasColumnType(IdentitySqlTypes.Int32Column);

    private static void StringColumn(EntityTypeBuilder entity, string name, int maxLength) =>
        entity.Property<string>(name).IsRequired().HasMaxLength(maxLength).HasColumnType(IdentitySqlTypes.NVarChar(maxLength));

    private static void OptionalText(EntityTypeBuilder entity, string name, int maxLength) =>
        entity.Property<string>(name).HasMaxLength(maxLength).HasColumnType(IdentitySqlTypes.NVarChar(maxLength));

    private static void OptionalAnsiText(EntityTypeBuilder entity, string name, int maxLength) =>
        entity.Property<string>(name).HasMaxLength(maxLength).IsUnicode(false).HasColumnType(IdentitySqlTypes.VarChar(maxLength));

    private static void FilteredUniqueIndex(EntityTypeBuilder entity, string column) =>
        entity.HasIndex(column).IsUnique().HasFilter(IdentityIndexFilters.NotNull(column));
}

/// <summary>The model snapshot <c>dotnet ef migrations add</c> diffs the next migration against.</summary>
[DbContext(typeof(IdentityDbContext))]
public sealed class IdentityDbContextModelSnapshot : ModelSnapshot
{
    /// <inheritdoc />
    protected override void BuildModel(ModelBuilder modelBuilder) => IdentityMigrationModel.Build(modelBuilder);
}
