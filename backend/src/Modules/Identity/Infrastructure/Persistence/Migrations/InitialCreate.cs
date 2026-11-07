using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Migrations.Operations.Builders;
using Starter.Modules.Identity.Domain;

namespace Starter.Modules.Identity.Infrastructure.Persistence.Migrations;

/// <summary>
/// The whole Identity schema in one migration (ADR-010 squash): administered accounts (e-mail, login name and UPN with
/// filtered unique indexes, kind, binding, organization, the nullable password hash with activation, the lockout
/// counter), sessions (with the sign-in mode and the logout hint) and the <c>identity.AuthEvents</c> trail. It keeps
/// the id of the first migration so a database migrated before the squash stays valid. Reshaped to the clean-code
/// rules (no generated marker, named lengths, short methods); a unit test checks it against EF's own model diff.
/// </summary>
[DbContext(typeof(IdentityDbContext))]
[Migration(IdentityMigrationIds.InitialCreate)]
public sealed class InitialCreate : Migration
{
    private const string Schema = IdentityDbContext.SchemaName;

    private static readonly UniqueWhenPresentIndex[] UniqueWhenPresentIndexes =
    [
        new(IdentityMigrationIds.UserAccountsEmailIndex, nameof(UserAccount.NormalizedEmail)),
        new(IdentityMigrationIds.UserAccountsLoginNameIndex, nameof(UserAccount.NormalizedLoginName)),
        new(IdentityMigrationIds.UserAccountsUpnIndex, nameof(UserAccount.NormalizedUpn)),
    ];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.EnsureSchema(name: Schema);
        CreateAuthEvents(migrationBuilder);
        CreateUserAccounts(migrationBuilder);
        CreateUserSessions(migrationBuilder);
        CreateAccountIndexes(migrationBuilder);
        CreateEventAndSessionIndexes(migrationBuilder);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.DropTable(name: TableNames.AuthEvents, schema: Schema);
        migrationBuilder.DropTable(name: TableNames.UserSessions, schema: Schema);
        migrationBuilder.DropTable(name: TableNames.UserAccounts, schema: Schema);
    }

    /// <inheritdoc />
    protected override void BuildTargetModel(ModelBuilder modelBuilder) => IdentityMigrationModel.Build(modelBuilder);

    private static void CreateAuthEvents(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: TableNames.AuthEvents,
            schema: Schema,
            columns: table => new
            {
                Id = IdentityColumn(table),
                EventType = table.Column<int>(type: IdentitySqlTypes.Int32Column, nullable: false),
                FailureReason = table.Column<int>(type: IdentitySqlTypes.Int32Column, nullable: true),
                UserId = table.Column<long>(type: IdentitySqlTypes.BigInt, nullable: true),
                UserNameHash = OptionalAnsiText(table, IdentityLimits.UserNameHashMaxLength),
                SignInMode = table.Column<int>(type: IdentitySqlTypes.Int32Column, nullable: true),
                OccurredAt = table.Column<DateTimeOffset>(type: IdentitySqlTypes.DateTimeOffset, nullable: false),
                CorrelationId = OptionalText(table, IdentityLimits.CorrelationIdMaxLength),
            },
            constraints: table => table.PrimaryKey(IdentityMigrationIds.AuthEventsPrimaryKey, authEvent => authEvent.Id));

    private static void CreateUserAccounts(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: TableNames.UserAccounts,
            schema: Schema,
            columns: table => new
            {
                Id = IdentityColumn(table),
                SubjectId = table.Column<Guid>(type: IdentitySqlTypes.UniqueIdentifier, nullable: false),
                UserName = Text(table, IdentityLimits.UserNameMaxLength),
                NormalizedUserName = Text(table, IdentityLimits.UserNameMaxLength),
                DisplayName = Text(table, IdentityLimits.DisplayNameMaxLength),
                Email = OptionalText(table, IdentityLimits.EmailMaxLength),
                NormalizedEmail = OptionalText(table, IdentityLimits.EmailMaxLength),
                LoginName = OptionalText(table, IdentityLimits.LoginNameMaxLength),
                NormalizedLoginName = OptionalText(table, IdentityLimits.LoginNameMaxLength),
                Upn = OptionalText(table, IdentityLimits.UpnMaxLength),
                NormalizedUpn = OptionalText(table, IdentityLimits.UpnMaxLength),
                Kind = Integer(table),
                Binding = Integer(table),
                OrganizationId = table.Column<long>(type: IdentitySqlTypes.BigInt, nullable: true),
                PasswordHash = OptionalText(table, IdentityLimits.PasswordHashMaxLength),
                ActivatedAt = OptionalTime(table),
                ActivationTicketId = OptionalAnsiText(table, IdentityLimits.ActivationTicketIdMaxLength),
                FailedAccessCount = Integer(table),
                LockoutEndsAt = OptionalTime(table),
                IsActive = table.Column<bool>(type: IdentitySqlTypes.Bit, nullable: false),
                IsBreakGlass = table.Column<bool>(type: IdentitySqlTypes.Bit, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: IdentitySqlTypes.DateTimeOffset, nullable: false),
            },
            constraints: table => table.PrimaryKey(IdentityMigrationIds.UserAccountsPrimaryKey, account => account.Id));

    private static void CreateUserSessions(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: TableNames.UserSessions,
            schema: Schema,
            columns: table => new
            {
                Id = IdentityColumn(table),
                SessionId = table.Column<string>(
                    type: IdentitySqlTypes.FixedAnsiText(IdentityLimits.SessionIdLength), unicode: false, fixedLength: true, maxLength: IdentityLimits.SessionIdLength, nullable: false),
                UserId = table.Column<long>(type: IdentitySqlTypes.BigInt, nullable: false),
                RefreshSecretHash = table.Column<byte[]>(
                    type: IdentitySqlTypes.FixedBinary(IdentityLimits.RefreshSecretHashBytes), fixedLength: true, maxLength: IdentityLimits.RefreshSecretHashBytes, nullable: false),
                RefreshGeneration = Integer(table),
                SessionVersion = table.Column<long>(type: IdentitySqlTypes.BigInt, nullable: false),
                SignInMode = Integer(table),
                IdTokenHint = OptionalAnsiText(table, IdentityLimits.IdTokenHintMaxLength),
                CreatedAt = table.Column<DateTimeOffset>(type: IdentitySqlTypes.DateTimeOffset, nullable: false),
                LastActivityAt = table.Column<DateTimeOffset>(type: IdentitySqlTypes.DateTimeOffset, nullable: false),
                ExpiresAt = table.Column<DateTimeOffset>(type: IdentitySqlTypes.DateTimeOffset, nullable: false),
                RevokedAt = OptionalTime(table),
            },
            constraints: table =>
            {
                table.PrimaryKey(IdentityMigrationIds.UserSessionsPrimaryKey, session => session.Id);
                table.ForeignKey(
                    name: IdentityMigrationIds.UserSessionsUserForeignKey,
                    column: session => session.UserId,
                    principalSchema: Schema,
                    principalTable: TableNames.UserAccounts,
                    principalColumn: nameof(UserAccount.Id),
                    onDelete: ReferentialAction.Cascade);
            });

    private static void CreateAccountIndexes(MigrationBuilder migrationBuilder)
    {
        Index(migrationBuilder, IdentityMigrationIds.UserAccountsUserNameIndex, TableNames.UserAccounts, [nameof(UserAccount.NormalizedUserName)], unique: true);
        Index(migrationBuilder, IdentityMigrationIds.UserAccountsSubjectIndex, TableNames.UserAccounts, [nameof(UserAccount.SubjectId)], unique: true);
        Index(migrationBuilder, IdentityMigrationIds.UserAccountsOrganizationIndex, TableNames.UserAccounts, [nameof(UserAccount.OrganizationId)], unique: false);
        foreach (var index in UniqueWhenPresentIndexes)
        {
            migrationBuilder.CreateIndex(
                name: index.Name,
                schema: Schema,
                table: TableNames.UserAccounts,
                column: index.Column,
                unique: true,
                filter: IdentityIndexFilters.NotNull(index.Column));
        }
    }

    private static void CreateEventAndSessionIndexes(MigrationBuilder migrationBuilder)
    {
        Index(migrationBuilder, IdentityMigrationIds.AuthEventsOccurredAtIndex, TableNames.AuthEvents, [nameof(AuthEvent.OccurredAt)], unique: false);
        Index(migrationBuilder, IdentityMigrationIds.AuthEventsUserIndex, TableNames.AuthEvents, [nameof(AuthEvent.UserId), nameof(AuthEvent.OccurredAt)], unique: false);
        Index(migrationBuilder, IdentityMigrationIds.AuthEventsUserNameHashIndex, TableNames.AuthEvents, [nameof(AuthEvent.UserNameHash), nameof(AuthEvent.OccurredAt)], unique: false);
        Index(migrationBuilder, IdentityMigrationIds.UserSessionsSessionIdIndex, TableNames.UserSessions, [nameof(UserSession.SessionId)], unique: true);
        Index(migrationBuilder, IdentityMigrationIds.UserSessionsUserIndex, TableNames.UserSessions, [nameof(UserSession.UserId)], unique: false);
    }

    private static void Index(MigrationBuilder migrationBuilder, string name, string table, string[] columns, bool unique) =>
        migrationBuilder.CreateIndex(name: name, schema: Schema, table: table, columns: columns, unique: unique);

    private static OperationBuilder<AddColumnOperation> IdentityColumn(ColumnsBuilder table) =>
        table.Column<long>(type: IdentitySqlTypes.BigInt, nullable: false)
            .Annotation(IdentitySqlTypes.IdentityAnnotation, IdentitySqlTypes.IdentitySeedIncrement);

    private static OperationBuilder<AddColumnOperation> Integer(ColumnsBuilder table) =>
        table.Column<int>(type: IdentitySqlTypes.Int32Column, nullable: false);

    private static OperationBuilder<AddColumnOperation> OptionalTime(ColumnsBuilder table) =>
        table.Column<DateTimeOffset>(type: IdentitySqlTypes.DateTimeOffset, nullable: true);

    private static OperationBuilder<AddColumnOperation> Text(ColumnsBuilder table, int maxLength) =>
        table.Column<string>(type: IdentitySqlTypes.NVarChar(maxLength), maxLength: maxLength, nullable: false);

    private static OperationBuilder<AddColumnOperation> OptionalText(ColumnsBuilder table, int maxLength) =>
        table.Column<string>(type: IdentitySqlTypes.NVarChar(maxLength), maxLength: maxLength, nullable: true);

    private static OperationBuilder<AddColumnOperation> OptionalAnsiText(ColumnsBuilder table, int maxLength) =>
        table.Column<string>(type: IdentitySqlTypes.VarChar(maxLength), unicode: false, maxLength: maxLength, nullable: true);

    private sealed record UniqueWhenPresentIndex(string Name, string Column);
}
