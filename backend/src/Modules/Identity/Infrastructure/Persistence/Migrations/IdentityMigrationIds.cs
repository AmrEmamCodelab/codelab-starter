namespace Starter.Modules.Identity.Infrastructure.Persistence.Migrations;

/// <summary>The migration identifiers and constraint names of the Identity schema.</summary>
public static class IdentityMigrationIds
{
    /// <summary>The id of the first migration.</summary>
    public const string InitialCreate = "20261104203909_InitialCreate";

    /// <summary>The id of the phase 3 migration (account administration, lockout, activation, sign-in modes, auth events).</summary>
    public const string AccountAdministration = "20261105120000_AccountAdministration";

    /// <summary>The primary key of <c>identity.UserAccounts</c>.</summary>
    public const string UserAccountsPrimaryKey = "PK_UserAccounts";

    /// <summary>The unique index on the normalized user name.</summary>
    public const string UserAccountsUserNameIndex = "IX_UserAccounts_NormalizedUserName";

    /// <summary>The unique index on the token subject.</summary>
    public const string UserAccountsSubjectIndex = "IX_UserAccounts_SubjectId";

    /// <summary>The filtered unique index on the normalized e-mail address.</summary>
    public const string UserAccountsEmailIndex = "IX_UserAccounts_NormalizedEmail";

    /// <summary>The filtered unique index on the normalized login name.</summary>
    public const string UserAccountsLoginNameIndex = "IX_UserAccounts_NormalizedLoginName";

    /// <summary>The filtered unique index on the normalized UPN.</summary>
    public const string UserAccountsUpnIndex = "IX_UserAccounts_NormalizedUpn";

    /// <summary>The index on the account's organization.</summary>
    public const string UserAccountsOrganizationIndex = "IX_UserAccounts_OrganizationId";

    /// <summary>The primary key of <c>identity.UserSessions</c>.</summary>
    public const string UserSessionsPrimaryKey = "PK_UserSessions";

    /// <summary>The session to account foreign key.</summary>
    public const string UserSessionsUserForeignKey = "FK_UserSessions_UserAccounts_UserId";

    /// <summary>The unique index on the session id.</summary>
    public const string UserSessionsSessionIdIndex = "IX_UserSessions_SessionId";

    /// <summary>The index on the session's user.</summary>
    public const string UserSessionsUserIndex = "IX_UserSessions_UserId";

    /// <summary>The primary key of <c>identity.AuthEvents</c>.</summary>
    public const string AuthEventsPrimaryKey = "PK_AuthEvents";

    /// <summary>The index on the event time.</summary>
    public const string AuthEventsOccurredAtIndex = "IX_AuthEvents_OccurredAt";

    /// <summary>The index on the user, then the event time.</summary>
    public const string AuthEventsUserIndex = "IX_AuthEvents_UserId_OccurredAt";

    /// <summary>The index on the typed identifier's hash, then the event time.</summary>
    public const string AuthEventsUserNameHashIndex = "IX_AuthEvents_UserNameHash_OccurredAt";
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

    /// <summary>A variable-length ANSI string column of <paramref name="length"/> characters.</summary>
    public static string VarChar(int length) => $"varchar({length})";

    /// <summary>A fixed-length ANSI string column of <paramref name="length"/> characters.</summary>
    public static string FixedAnsiText(int length) => $"char({length})";

    /// <summary>A fixed-length binary column of <paramref name="length"/> bytes.</summary>
    public static string FixedBinary(int length) => $"binary({length})";
}
