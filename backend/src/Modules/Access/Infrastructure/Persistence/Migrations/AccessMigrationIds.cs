namespace Starter.Modules.Access.Infrastructure.Persistence.Migrations;

/// <summary>The migration identifiers and constraint names of the Access schema.</summary>
public static class AccessMigrationIds
{
    /// <summary>The id of the first migration.</summary>
    public const string InitialCreate = "20261104203731_InitialCreate";

    /// <summary>The id of the roles, role permissions, user role assignments and grant scope migration.</summary>
    public const string RolesAndGrants = "20261105015102_RolesAndGrants";

    /// <summary>The primary key of <c>access.Permissions</c>.</summary>
    public const string PermissionsPrimaryKey = "PK_Permissions";

    /// <summary>The unique index on the permission key.</summary>
    public const string PermissionsKeyIndex = "IX_Permissions_Key";

    /// <summary>The primary key of <c>access.UserPermissionGrants</c>.</summary>
    public const string UserPermissionGrantsPrimaryKey = "PK_UserPermissionGrants";

    /// <summary>The grant to permission foreign key.</summary>
    public const string UserPermissionGrantsPermissionForeignKey = "FK_UserPermissionGrants_Permissions_PermissionId";

    /// <summary>The index on the granted permission.</summary>
    public const string UserPermissionGrantsPermissionIndex = "IX_UserPermissionGrants_PermissionId";

    /// <summary>The unique (user, permission) index.</summary>
    public const string UserPermissionGrantsUserPermissionIndex = "IX_UserPermissionGrants_UserId_PermissionId";

    /// <summary>The primary key of <c>access.Roles</c>.</summary>
    public const string RolesPrimaryKey = "PK_Roles";

    /// <summary>The unique index on the role code.</summary>
    public const string RolesCodeIndex = "IX_Roles_Code";

    /// <summary>The unique index on the English role name.</summary>
    public const string RolesNameEnIndex = "IX_Roles_NameEn";

    /// <summary>The unique index on the Arabic role name.</summary>
    public const string RolesNameArIndex = "IX_Roles_NameAr";

    /// <summary>The filtered unique index on the virtual kind (one role per kind).</summary>
    public const string RolesVirtualKindIndex = "IX_Roles_VirtualKind";

    /// <summary>The primary key of <c>access.RolePermissions</c>.</summary>
    public const string RolePermissionsPrimaryKey = "PK_RolePermissions";

    /// <summary>The role permission to permission foreign key.</summary>
    public const string RolePermissionsPermissionForeignKey = "FK_RolePermissions_Permissions_PermissionId";

    /// <summary>The role permission to role foreign key (cascade).</summary>
    public const string RolePermissionsRoleForeignKey = "FK_RolePermissions_Roles_RoleId";

    /// <summary>The index on the role permission's permission.</summary>
    public const string RolePermissionsPermissionIndex = "IX_RolePermissions_PermissionId";

    /// <summary>The unique (role, permission) index.</summary>
    public const string RolePermissionsRolePermissionIndex = "IX_RolePermissions_RoleId_PermissionId";

    /// <summary>The primary key of <c>access.UserRoleAssignments</c>.</summary>
    public const string UserRoleAssignmentsPrimaryKey = "PK_UserRoleAssignments";

    /// <summary>The assignment to role foreign key (restrict).</summary>
    public const string UserRoleAssignmentsRoleForeignKey = "FK_UserRoleAssignments_Roles_RoleId";

    /// <summary>The index on the assigned role.</summary>
    public const string UserRoleAssignmentsRoleIndex = "IX_UserRoleAssignments_RoleId";

    /// <summary>The unique (user, role) index.</summary>
    public const string UserRoleAssignmentsUserRoleIndex = "IX_UserRoleAssignments_UserId_RoleId";
}

/// <summary>The SQL Server store types and annotations the Access migrations write.</summary>
public static class SqlTypes
{
    /// <summary>64-bit integer.</summary>
    public const string BigInt = "bigint";

    /// <summary>The 32-bit integer column every enum is stored in.</summary>
    public const string EnumColumn = "int";

    /// <summary>Boolean.</summary>
    public const string Bit = "bit";

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
}
