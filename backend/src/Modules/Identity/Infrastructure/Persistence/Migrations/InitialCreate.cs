using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Migrations.Operations.Builders;
using Starter.Modules.Identity.Domain;

namespace Starter.Modules.Identity.Infrastructure.Persistence.Migrations;

/// <summary>
/// The Identity schema: accounts and sessions. Scaffolded with <c>dotnet ef migrations add</c> (dotnet-ef 10) and
/// reshaped to the clean-code rules (no generated marker, named lengths, short methods); see MODULE.md.
/// </summary>
[DbContext(typeof(IdentityDbContext))]
[Migration(IdentityMigrationIds.InitialCreate)]
public sealed class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.EnsureSchema(name: IdentityDbContext.SchemaName);
        CreateUserAccounts(migrationBuilder);
        CreateUserSessions(migrationBuilder);
        CreateIndexes(migrationBuilder);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.DropTable(name: TableNames.UserSessions, schema: IdentityDbContext.SchemaName);
        migrationBuilder.DropTable(name: TableNames.UserAccounts, schema: IdentityDbContext.SchemaName);
    }

    /// <inheritdoc />
    protected override void BuildTargetModel(ModelBuilder modelBuilder) => IdentityMigrationModel.Build(modelBuilder);

    private static void CreateUserAccounts(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: TableNames.UserAccounts,
            schema: IdentityDbContext.SchemaName,
            columns: table => new
            {
                Id = table.Column<long>(type: IdentitySqlTypes.BigInt, nullable: false)
                    .Annotation(IdentitySqlTypes.IdentityAnnotation, IdentitySqlTypes.IdentitySeedIncrement),
                SubjectId = table.Column<Guid>(type: IdentitySqlTypes.UniqueIdentifier, nullable: false),
                UserName = Text(table, IdentityLimits.UserNameMaxLength),
                NormalizedUserName = Text(table, IdentityLimits.UserNameMaxLength),
                DisplayName = Text(table, IdentityLimits.DisplayNameMaxLength),
                PasswordHash = Text(table, IdentityLimits.PasswordHashMaxLength),
                IsActive = table.Column<bool>(type: IdentitySqlTypes.Bit, nullable: false),
                IsBreakGlass = table.Column<bool>(type: IdentitySqlTypes.Bit, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: IdentitySqlTypes.DateTimeOffset, nullable: false),
            },
            constraints: table => table.PrimaryKey(IdentityMigrationIds.UserAccountsPrimaryKey, account => account.Id));

    private static void CreateUserSessions(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: TableNames.UserSessions,
            schema: IdentityDbContext.SchemaName,
            columns: table => new
            {
                Id = table.Column<long>(type: IdentitySqlTypes.BigInt, nullable: false)
                    .Annotation(IdentitySqlTypes.IdentityAnnotation, IdentitySqlTypes.IdentitySeedIncrement),
                SessionId = table.Column<string>(
                    type: IdentitySqlTypes.FixedAnsiText(IdentityLimits.SessionIdLength), unicode: false, fixedLength: true, maxLength: IdentityLimits.SessionIdLength, nullable: false),
                UserId = table.Column<long>(type: IdentitySqlTypes.BigInt, nullable: false),
                RefreshSecretHash = table.Column<byte[]>(
                    type: IdentitySqlTypes.FixedBinary(IdentityLimits.RefreshSecretHashBytes), fixedLength: true, maxLength: IdentityLimits.RefreshSecretHashBytes, nullable: false),
                RefreshGeneration = table.Column<int>(type: IdentitySqlTypes.Int32Column, nullable: false),
                SessionVersion = table.Column<long>(type: IdentitySqlTypes.BigInt, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: IdentitySqlTypes.DateTimeOffset, nullable: false),
                LastActivityAt = table.Column<DateTimeOffset>(type: IdentitySqlTypes.DateTimeOffset, nullable: false),
                ExpiresAt = table.Column<DateTimeOffset>(type: IdentitySqlTypes.DateTimeOffset, nullable: false),
                RevokedAt = table.Column<DateTimeOffset>(type: IdentitySqlTypes.DateTimeOffset, nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey(IdentityMigrationIds.UserSessionsPrimaryKey, session => session.Id);
                table.ForeignKey(
                    name: IdentityMigrationIds.UserSessionsUserForeignKey,
                    column: session => session.UserId,
                    principalSchema: IdentityDbContext.SchemaName,
                    principalTable: TableNames.UserAccounts,
                    principalColumn: nameof(UserAccount.Id),
                    onDelete: ReferentialAction.Cascade);
            });

    private static void CreateIndexes(MigrationBuilder migrationBuilder)
    {
        Index(migrationBuilder, IdentityMigrationIds.UserAccountsUserNameIndex, TableNames.UserAccounts, nameof(UserAccount.NormalizedUserName), unique: true);
        Index(migrationBuilder, IdentityMigrationIds.UserAccountsSubjectIndex, TableNames.UserAccounts, nameof(UserAccount.SubjectId), unique: true);
        Index(migrationBuilder, IdentityMigrationIds.UserSessionsSessionIdIndex, TableNames.UserSessions, nameof(UserSession.SessionId), unique: true);
        Index(migrationBuilder, IdentityMigrationIds.UserSessionsUserIndex, TableNames.UserSessions, nameof(UserSession.UserId), unique: false);
    }

    private static void Index(MigrationBuilder migrationBuilder, string name, string table, string column, bool unique) =>
        migrationBuilder.CreateIndex(name: name, schema: IdentityDbContext.SchemaName, table: table, column: column, unique: unique);

    private static OperationBuilder<AddColumnOperation> Text(ColumnsBuilder table, int maxLength) =>
        table.Column<string>(type: IdentitySqlTypes.NVarChar(maxLength), maxLength: maxLength, nullable: false);
}
