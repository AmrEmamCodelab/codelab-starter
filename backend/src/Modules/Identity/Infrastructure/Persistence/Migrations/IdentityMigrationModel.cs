using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Starter.Modules.Identity.Domain;

namespace Starter.Modules.Identity.Infrastructure.Persistence.Migrations;

/// <summary>The migration identifiers and constraint names of the Identity schema.</summary>
public static class IdentityMigrationIds
{
    /// <summary>The id of the first migration.</summary>
    public const string InitialCreate = "20261104203909_InitialCreate";

    /// <summary>The primary key of <c>identity.UserAccounts</c>.</summary>
    public const string UserAccountsPrimaryKey = "PK_UserAccounts";

    /// <summary>The unique index on the normalized user name.</summary>
    public const string UserAccountsUserNameIndex = "IX_UserAccounts_NormalizedUserName";

    /// <summary>The unique index on the token subject.</summary>
    public const string UserAccountsSubjectIndex = "IX_UserAccounts_SubjectId";

    /// <summary>The primary key of <c>identity.UserSessions</c>.</summary>
    public const string UserSessionsPrimaryKey = "PK_UserSessions";

    /// <summary>The session to account foreign key.</summary>
    public const string UserSessionsUserForeignKey = "FK_UserSessions_UserAccounts_UserId";

    /// <summary>The unique index on the session id.</summary>
    public const string UserSessionsSessionIdIndex = "IX_UserSessions_SessionId";

    /// <summary>The index on the session's user.</summary>
    public const string UserSessionsUserIndex = "IX_UserSessions_UserId";
}

/// <summary>The SQL Server store types and annotations the Identity migrations write.</summary>
public static class IdentitySqlTypes
{
    /// <summary>32-bit integer.</summary>
    public const string Int32Column = "int";

    /// <summary>64-bit integer.</summary>
    public const string BigInt = "bigint";

    /// <summary>Boolean.</summary>
    public const string Bit = "bit";

    /// <summary>GUID.</summary>
    public const string UniqueIdentifier = "uniqueidentifier";

    /// <summary>Date, time and offset.</summary>
    public const string DateTimeOffset = "datetimeoffset";

    /// <summary>The identity column annotation.</summary>
    public const string IdentityAnnotation = "SqlServer:Identity";

    /// <summary>Identity seed 1, increment 1.</summary>
    public const string IdentitySeedIncrement = "1, 1";

    /// <summary>The model annotation that records the EF Core version.</summary>
    public const string ProductVersionAnnotation = "ProductVersion";

    /// <summary>The EF Core version that scaffolded the model.</summary>
    public const string ProductVersion = "10.0.14";

    /// <summary>The longest SQL Server identifier.</summary>
    public const int MaxIdentifierLength = 128;

    /// <summary>A Unicode string column of <paramref name="length"/> characters.</summary>
    public static string NVarChar(int length) => $"nvarchar({length})";

    /// <summary>A fixed-length ANSI string column of <paramref name="length"/> characters.</summary>
    public static string FixedAnsiText(int length) => $"char({length})";

    /// <summary>A fixed-length binary column of <paramref name="length"/> bytes.</summary>
    public static string FixedBinary(int length) => $"binary({length})";
}

/// <summary>
/// The Identity model as of the latest migration: the target model of <see cref="InitialCreate"/> and the model
/// snapshot that <c>dotnet ef migrations add</c> diffs against. A later migration freezes its own copy before this one
/// changes.
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

/// <summary>The model snapshot <c>dotnet ef migrations add</c> diffs the next migration against.</summary>
[DbContext(typeof(IdentityDbContext))]
public sealed class IdentityDbContextModelSnapshot : ModelSnapshot
{
    /// <inheritdoc />
    protected override void BuildModel(ModelBuilder modelBuilder) => IdentityMigrationModel.Build(modelBuilder);
}
