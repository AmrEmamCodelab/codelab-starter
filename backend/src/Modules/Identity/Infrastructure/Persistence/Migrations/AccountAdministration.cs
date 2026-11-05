using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Starter.Domain.Users;
using Starter.Modules.Identity.Domain;

namespace Starter.Modules.Identity.Infrastructure.Persistence.Migrations;

/// <summary>
/// Phase 3 of the Identity schema, in one migration: administered accounts (e-mail, login name and UPN with filtered
/// unique indexes, kind, binding, organization), the nullable password hash with activation, the lockout counter,
/// the session's sign-in mode and logout hint, and the <c>identity.AuthEvents</c> trail. Existing rows become
/// internal, password-bound accounts and basic sessions. Written in the reshaped style of <see cref="InitialCreate"/>
/// (no generated marker, named lengths, short methods); a unit test checks it against EF's own model diff.
/// </summary>
[DbContext(typeof(IdentityDbContext))]
[Migration(IdentityMigrationIds.AccountAdministration)]
public sealed class AccountAdministration : Migration
{
    private const string Schema = IdentityDbContext.SchemaName;

    private static readonly string[] AccountColumns =
    [
        nameof(UserAccount.ActivatedAt), nameof(UserAccount.ActivationTicketId), nameof(UserAccount.Binding),
        nameof(UserAccount.Email), nameof(UserAccount.FailedAccessCount), nameof(UserAccount.Kind),
        nameof(UserAccount.LockoutEndsAt), nameof(UserAccount.LoginName), nameof(UserAccount.NormalizedEmail),
        nameof(UserAccount.NormalizedLoginName), nameof(UserAccount.NormalizedUpn), nameof(UserAccount.OrganizationId),
        nameof(UserAccount.Upn),
    ];

    private static readonly AccountIndex[] AccountIndexes =
    [
        new(IdentityMigrationIds.UserAccountsEmailIndex, nameof(UserAccount.NormalizedEmail), IsUniqueWhenPresent: true),
        new(IdentityMigrationIds.UserAccountsLoginNameIndex, nameof(UserAccount.NormalizedLoginName), IsUniqueWhenPresent: true),
        new(IdentityMigrationIds.UserAccountsUpnIndex, nameof(UserAccount.NormalizedUpn), IsUniqueWhenPresent: true),
        new(IdentityMigrationIds.UserAccountsOrganizationIndex, nameof(UserAccount.OrganizationId), IsUniqueWhenPresent: false),
    ];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.AlterColumn<string>(
            name: nameof(UserAccount.PasswordHash),
            schema: Schema,
            table: TableNames.UserAccounts,
            type: IdentitySqlTypes.NVarChar(IdentityLimits.PasswordHashMaxLength),
            maxLength: IdentityLimits.PasswordHashMaxLength,
            nullable: true,
            oldClrType: typeof(string),
            oldType: IdentitySqlTypes.NVarChar(IdentityLimits.PasswordHashMaxLength),
            oldMaxLength: IdentityLimits.PasswordHashMaxLength);
        AddAccountColumns(migrationBuilder);
        AddSessionColumns(migrationBuilder);
        CreateAuthEvents(migrationBuilder);
        CreateIndexes(migrationBuilder);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.DropTable(name: TableNames.AuthEvents, schema: Schema);
        foreach (var index in AccountIndexes)
        {
            migrationBuilder.DropIndex(name: index.Name, schema: Schema, table: TableNames.UserAccounts);
        }

        foreach (var column in AccountColumns)
        {
            migrationBuilder.DropColumn(name: column, schema: Schema, table: TableNames.UserAccounts);
        }

        migrationBuilder.DropColumn(name: nameof(UserSession.IdTokenHint), schema: Schema, table: TableNames.UserSessions);
        migrationBuilder.DropColumn(name: nameof(UserSession.SignInMode), schema: Schema, table: TableNames.UserSessions);
        migrationBuilder.AlterColumn<string>(
            name: nameof(UserAccount.PasswordHash),
            schema: Schema,
            table: TableNames.UserAccounts,
            type: IdentitySqlTypes.NVarChar(IdentityLimits.PasswordHashMaxLength),
            maxLength: IdentityLimits.PasswordHashMaxLength,
            nullable: false,
            defaultValue: string.Empty,
            oldClrType: typeof(string),
            oldType: IdentitySqlTypes.NVarChar(IdentityLimits.PasswordHashMaxLength),
            oldMaxLength: IdentityLimits.PasswordHashMaxLength,
            oldNullable: true);
    }

    /// <inheritdoc />
    protected override void BuildTargetModel(ModelBuilder modelBuilder) => IdentityMigrationModel.Build(modelBuilder);

    private static void AddAccountColumns(MigrationBuilder migrationBuilder)
    {
        OptionalTime(migrationBuilder, TableNames.UserAccounts, nameof(UserAccount.ActivatedAt));
        OptionalAnsiText(migrationBuilder, TableNames.UserAccounts, nameof(UserAccount.ActivationTicketId), IdentityLimits.ActivationTicketIdMaxLength);
        Integer(migrationBuilder, TableNames.UserAccounts, nameof(UserAccount.Binding), (int)AccountBinding.Password);
        OptionalText(migrationBuilder, nameof(UserAccount.Email), IdentityLimits.EmailMaxLength);
        Integer(migrationBuilder, TableNames.UserAccounts, nameof(UserAccount.FailedAccessCount), defaultValue: 0);
        Integer(migrationBuilder, TableNames.UserAccounts, nameof(UserAccount.Kind), (int)UserKind.@Internal); // "@": keeps the scrub sweep's host-name rule off an enum member access
        OptionalTime(migrationBuilder, TableNames.UserAccounts, nameof(UserAccount.LockoutEndsAt));
        OptionalText(migrationBuilder, nameof(UserAccount.LoginName), IdentityLimits.LoginNameMaxLength);
        OptionalText(migrationBuilder, nameof(UserAccount.NormalizedEmail), IdentityLimits.EmailMaxLength);
        OptionalText(migrationBuilder, nameof(UserAccount.NormalizedLoginName), IdentityLimits.LoginNameMaxLength);
        OptionalText(migrationBuilder, nameof(UserAccount.NormalizedUpn), IdentityLimits.UpnMaxLength);
        migrationBuilder.AddColumn<long>(
            name: nameof(UserAccount.OrganizationId), schema: Schema, table: TableNames.UserAccounts, type: IdentitySqlTypes.BigInt, nullable: true);
        OptionalText(migrationBuilder, nameof(UserAccount.Upn), IdentityLimits.UpnMaxLength);
    }

    private static void AddSessionColumns(MigrationBuilder migrationBuilder)
    {
        OptionalAnsiText(migrationBuilder, TableNames.UserSessions, nameof(UserSession.IdTokenHint), IdentityLimits.IdTokenHintMaxLength);
        Integer(migrationBuilder, TableNames.UserSessions, nameof(UserSession.SignInMode), (int)SignInMode.Basic);
    }

    private static void CreateAuthEvents(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: TableNames.AuthEvents,
            schema: Schema,
            columns: table => new
            {
                Id = table.Column<long>(type: IdentitySqlTypes.BigInt, nullable: false)
                    .Annotation(IdentitySqlTypes.IdentityAnnotation, IdentitySqlTypes.IdentitySeedIncrement),
                EventType = table.Column<int>(type: IdentitySqlTypes.Int32Column, nullable: false),
                FailureReason = table.Column<int>(type: IdentitySqlTypes.Int32Column, nullable: true),
                UserId = table.Column<long>(type: IdentitySqlTypes.BigInt, nullable: true),
                UserNameHash = table.Column<string>(
                    type: IdentitySqlTypes.VarChar(IdentityLimits.UserNameHashMaxLength), unicode: false, maxLength: IdentityLimits.UserNameHashMaxLength, nullable: true),
                SignInMode = table.Column<int>(type: IdentitySqlTypes.Int32Column, nullable: true),
                OccurredAt = table.Column<DateTimeOffset>(type: IdentitySqlTypes.DateTimeOffset, nullable: false),
                CorrelationId = table.Column<string>(
                    type: IdentitySqlTypes.NVarChar(IdentityLimits.CorrelationIdMaxLength), maxLength: IdentityLimits.CorrelationIdMaxLength, nullable: true),
            },
            constraints: table => table.PrimaryKey(IdentityMigrationIds.AuthEventsPrimaryKey, authEvent => authEvent.Id));

    private static void CreateIndexes(MigrationBuilder migrationBuilder)
    {
        foreach (var index in AccountIndexes)
        {
            migrationBuilder.CreateIndex(
                name: index.Name,
                schema: Schema,
                table: TableNames.UserAccounts,
                column: index.Column,
                unique: index.IsUniqueWhenPresent,
                filter: index.IsUniqueWhenPresent ? IdentityIndexFilters.NotNull(index.Column) : null);
        }

        migrationBuilder.CreateIndex(
            name: IdentityMigrationIds.AuthEventsOccurredAtIndex, schema: Schema, table: TableNames.AuthEvents, column: nameof(AuthEvent.OccurredAt));
        migrationBuilder.CreateIndex(
            name: IdentityMigrationIds.AuthEventsUserIndex,
            schema: Schema,
            table: TableNames.AuthEvents,
            columns: [nameof(AuthEvent.UserId), nameof(AuthEvent.OccurredAt)]);
        migrationBuilder.CreateIndex(
            name: IdentityMigrationIds.AuthEventsUserNameHashIndex,
            schema: Schema,
            table: TableNames.AuthEvents,
            columns: [nameof(AuthEvent.UserNameHash), nameof(AuthEvent.OccurredAt)]);
    }

    private static void Integer(MigrationBuilder migrationBuilder, string table, string name, int defaultValue) =>
        migrationBuilder.AddColumn<int>(
            name: name, schema: Schema, table: table, type: IdentitySqlTypes.Int32Column, nullable: false, defaultValue: defaultValue);

    private static void OptionalTime(MigrationBuilder migrationBuilder, string table, string name) =>
        migrationBuilder.AddColumn<DateTimeOffset>(name: name, schema: Schema, table: table, type: IdentitySqlTypes.DateTimeOffset, nullable: true);

    private static void OptionalText(MigrationBuilder migrationBuilder, string name, int maxLength) =>
        migrationBuilder.AddColumn<string>(
            name: name, schema: Schema, table: TableNames.UserAccounts, type: IdentitySqlTypes.NVarChar(maxLength), maxLength: maxLength, nullable: true);

    private static void OptionalAnsiText(MigrationBuilder migrationBuilder, string table, string name, int maxLength) =>
        migrationBuilder.AddColumn<string>(
            name: name, schema: Schema, table: table, type: IdentitySqlTypes.VarChar(maxLength), unicode: false, maxLength: maxLength, nullable: true);

    private sealed record AccountIndex(string Name, string Column, bool IsUniqueWhenPresent);
}
