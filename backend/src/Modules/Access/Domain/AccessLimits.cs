namespace Starter.Modules.Access.Domain;

/// <summary>The column sizes of the Access tables (the migration and the entity configurations read these).</summary>
public static class AccessLimits
{
    /// <summary>The longest permission key (equal to the kernel's <c>PermissionKeyFormat.MaxLength</c>).</summary>
    public const int PermissionKeyMaxLength = 128;

    /// <summary>The longest owner segment of a permission key.</summary>
    public const int PermissionOwnerMaxLength = 64;

    /// <summary>The longest English description of a permission.</summary>
    public const int PermissionDescriptionMaxLength = 512;
}
