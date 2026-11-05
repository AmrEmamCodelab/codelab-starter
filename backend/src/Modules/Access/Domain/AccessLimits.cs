namespace Starter.Modules.Access.Domain;

/// <summary>The column sizes and write limits of the Access tables (migrations, configurations and validators read these).</summary>
public static class AccessLimits
{
    /// <summary>The longest permission key (equal to the kernel's <c>PermissionKeyFormat.MaxLength</c>).</summary>
    public const int PermissionKeyMaxLength = 128;

    /// <summary>The longest owner segment of a permission key.</summary>
    public const int PermissionOwnerMaxLength = 64;

    /// <summary>The longest English description of a permission.</summary>
    public const int PermissionDescriptionMaxLength = 512;

    /// <summary>The longest role code (lower-case kebab, see <see cref="RoleCodeFormat"/>).</summary>
    public const int RoleCodeMaxLength = 64;

    /// <summary>The longest role name, in either language.</summary>
    public const int RoleNameMaxLength = 128;

    /// <summary>The most permission keys one role, or one user's direct grants, may list in one write.</summary>
    public const int PermissionsPerWriteMax = 512;

    /// <summary>The most roles one user may be assigned in one write.</summary>
    public const int RolesPerUserMax = 64;
}
