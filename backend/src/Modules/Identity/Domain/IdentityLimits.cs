namespace Starter.Modules.Identity.Domain;

/// <summary>The size limits of Identity data; the database columns, the validators and the generators use these.</summary>
public static class IdentityLimits
{
    /// <summary>The longest user name, in characters.</summary>
    public const int UserNameMaxLength = 256;

    /// <summary>The longest display name, in characters.</summary>
    public const int DisplayNameMaxLength = 200;

    /// <summary>The longest password a login accepts, in characters (hashing an unbounded input is a denial of service).</summary>
    public const int PasswordMaxLength = 256;

    /// <summary>
    /// The shortest break-glass password, in characters, in every environment. The account holds every permission, so
    /// a hand-set weak password is refused at start (the generators write 24 or more random characters).
    /// </summary>
    public const int BreakGlassPasswordMinLength = 16;

    /// <summary>The fewest distinct characters a break-glass password may have (refuses "aaaaaaaaaaaaaaaa").</summary>
    public const int BreakGlassPasswordMinDistinctCharacters = 8;

    /// <summary>The longest stored password hash, in characters.</summary>
    public const int PasswordHashMaxLength = 512;

    /// <summary>The length of a session id: 16 random bytes as lower-case hex.</summary>
    public const int SessionIdLength = 32;

    /// <summary>The number of random bytes in a session id.</summary>
    public const int SessionIdBytes = 16;

    /// <summary>The number of random bytes in a refresh token secret.</summary>
    public const int RefreshSecretBytes = 32;

    /// <summary>The size of a refresh token secret hash (SHA-256), in bytes.</summary>
    public const int RefreshSecretHashBytes = 32;

    /// <summary>The longest refresh token a refresh request accepts, in characters.</summary>
    public const int RefreshTokenMaxLength = 128;

    /// <summary>The longest e-mail address, in characters.</summary>
    public const int EmailMaxLength = 256;

    /// <summary>The longest directory login name (sAMAccountName style), in characters.</summary>
    public const int LoginNameMaxLength = 256;

    /// <summary>The longest user principal name, in characters.</summary>
    public const int UpnMaxLength = 256;

    /// <summary>The longest identity-provider id_token kept as a logout hint, in characters (ASCII).</summary>
    public const int IdTokenHintMaxLength = 8000;

    /// <summary>The longest activation ticket id, in characters (22 for 16 random bytes in base64url).</summary>
    public const int ActivationTicketIdMaxLength = 64;

    /// <summary>The number of random bytes in an activation ticket id.</summary>
    public const int ActivationTicketIdBytes = 16;

    /// <summary>The longest sealed activation ticket an activation request accepts, in characters.</summary>
    public const int ActivationTicketMaxLength = 2048;

    /// <summary>The shortest password an activation may set, in every configuration (the configured minimum may only be higher).</summary>
    public const int PasswordMinLengthFloor = 8;

    /// <summary>The longest pseudonymous user-name hash an auth event stores, in characters.</summary>
    public const int UserNameHashMaxLength = 64;

    /// <summary>The longest correlation id an auth event stores, in characters.</summary>
    public const int CorrelationIdMaxLength = 128;
}
