namespace Starter.Modules.Users.Domain;

/// <summary>
/// The Users module's size limits. The validators, the column sizes and the import reader use the same constants; the
/// account limits mirror the Identity columns the accounts land in (the Identity module refuses anything longer too).
/// </summary>
public static class UsersLimits
{
    /// <summary>The longest organization code (letters, digits, <c>_</c> and <c>-</c>; stored upper-case).</summary>
    public const int OrganizationCodeMaxLength = 50;

    /// <summary>The longest organization name, in either language.</summary>
    public const int OrganizationNameMaxLength = 200;

    /// <summary>The longest sign-in name.</summary>
    public const int UserNameMaxLength = 256;

    /// <summary>The longest display name.</summary>
    public const int DisplayNameMaxLength = 200;

    /// <summary>The longest e-mail address.</summary>
    public const int EmailMaxLength = 256;

    /// <summary>The longest directory login name.</summary>
    public const int LoginNameMaxLength = 256;

    /// <summary>The longest user principal name.</summary>
    public const int UpnMaxLength = 256;

    /// <summary>The longest list search text.</summary>
    public const int SearchMaxLength = 200;

    /// <summary>The page size a list uses when the caller names none.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>The largest page a list returns.</summary>
    public const int MaxPageSize = 100;
}
