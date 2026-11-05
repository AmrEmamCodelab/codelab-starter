namespace Starter.Modules.Identity.Domain;

/// <summary>
/// A person who can sign in. <see cref="Id"/> is internal (the wire carries a PublicId); <see cref="SubjectId"/> is the
/// stable token subject (<c>sub</c>), which never changes and never reveals the internal id.
/// </summary>
public sealed class UserAccount
{
    private UserAccount()
    {
        UserName = string.Empty;
        NormalizedUserName = string.Empty;
        DisplayName = string.Empty;
        PasswordHash = string.Empty;
    }

    /// <summary>The internal id.</summary>
    public long Id { get; private set; }

    /// <summary>The token subject.</summary>
    public Guid SubjectId { get; private set; }

    /// <summary>The user name as entered when the account was created.</summary>
    public string UserName { get; private set; }

    /// <summary>The user name in its lookup form (<see cref="UserNames.Normalize"/>), unique.</summary>
    public string NormalizedUserName { get; private set; }

    /// <summary>The name shown in the application.</summary>
    public string DisplayName { get; private set; }

    /// <summary>The password hash (never the password).</summary>
    public string PasswordHash { get; private set; }

    /// <summary>False for a deactivated account: it cannot sign in or refresh, and holds no permission.</summary>
    public bool IsActive { get; private set; }

    /// <summary>True for the break-glass administrator, which holds every catalog permission.</summary>
    public bool IsBreakGlass { get; private set; }

    /// <summary>When the account was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>A new active account.</summary>
    public static UserAccount Create(NewUserAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentException.ThrowIfNullOrWhiteSpace(account.UserName);
        ArgumentException.ThrowIfNullOrWhiteSpace(account.DisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(account.PasswordHash);
        return new UserAccount
        {
            SubjectId = account.SubjectId,
            UserName = account.UserName.Trim(),
            NormalizedUserName = UserNames.Normalize(account.UserName),
            DisplayName = account.DisplayName.Trim(),
            PasswordHash = account.PasswordHash,
            IsActive = true,
            IsBreakGlass = account.IsBreakGlass,
            CreatedAt = account.CreatedAt,
        };
    }

    /// <summary>Replaces the password hash (a rehash after a successful login, or a rotated configured password).</summary>
    public void ReplacePasswordHash(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        PasswordHash = passwordHash;
    }

    /// <summary>Deactivates the account: it can no longer sign in or refresh, and holds no permission.</summary>
    public void Deactivate() => IsActive = false;

    /// <summary>Makes the account a break-glass administrator again and active (configuration is the authority for it).</summary>
    public void RestoreBreakGlass()
    {
        IsBreakGlass = true;
        IsActive = true;
    }
}

/// <summary>What creating an account needs.</summary>
/// <param name="SubjectId">A new random token subject.</param>
/// <param name="UserName">The user name.</param>
/// <param name="DisplayName">The display name.</param>
/// <param name="PasswordHash">The password hash.</param>
/// <param name="IsBreakGlass">True for the break-glass administrator.</param>
/// <param name="CreatedAt">The creation time.</param>
public sealed record NewUserAccount(Guid SubjectId, string UserName, string DisplayName, string PasswordHash, bool IsBreakGlass, DateTimeOffset CreatedAt);
