using System.Security.Cryptography;
using Codelab.Security.Sessions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Starter.Modules.Identity.Application;
using Starter.Modules.Identity.Domain;

namespace Starter.Modules.Identity.Infrastructure.Security;

/// <summary>Session ids and refresh secrets from the operating system's cryptographic random generator.</summary>
public sealed class SecureTokenGenerator : ISecureTokenGenerator
{
    /// <inheritdoc />
    public string NewSessionId() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(IdentityLimits.SessionIdBytes));

    /// <inheritdoc />
    public string NewRefreshSecret() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(IdentityLimits.RefreshSecretBytes));
}

/// <summary>
/// <see cref="IPasswordHasher"/> over the ASP.NET Core Identity hasher (PBKDF2, version 3 format). A check without an
/// account verifies against a decoy hash, so it costs the same as a real one.
/// </summary>
public sealed class IdentityPasswordHasher : Application.IPasswordHasher
{
    private static readonly PasswordOwner Owner = new();
    private readonly PasswordHasher<PasswordOwner> _hasher = new();
    private readonly Lazy<string> _decoyHash;

    /// <summary>Creates the hasher and its decoy (a hash of a random secret nobody knows).</summary>
    public IdentityPasswordHasher()
    {
        _decoyHash = new Lazy<string>(() => _hasher.HashPassword(Owner, Convert.ToHexString(RandomNumberGenerator.GetBytes(IdentityLimits.RefreshSecretBytes))));
    }

    /// <inheritdoc />
    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        return _hasher.HashPassword(Owner, password);
    }

    /// <inheritdoc />
    public PasswordVerification Verify(string? passwordHash, string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (string.IsNullOrEmpty(passwordHash))
        {
            _hasher.VerifyHashedPassword(Owner, _decoyHash.Value, password);
            return PasswordVerification.Failed;
        }

        return _hasher.VerifyHashedPassword(Owner, passwordHash, password) switch
        {
            PasswordVerificationResult.Success => PasswordVerification.Succeeded,
            PasswordVerificationResult.SuccessRehashNeeded => PasswordVerification.SucceededRehashNeeded,
            _ => PasswordVerification.Failed,
        };
    }

    /// <summary>The hasher's user type; the default hasher never reads it.</summary>
    private sealed class PasswordOwner;
}

/// <summary><see cref="ISessionRevocation"/> over the Codelab.Security revocation store (memory or SQL).</summary>
public sealed class SessionRevocationAdapter(ISessionRevocationStore store) : ISessionRevocation
{
    /// <inheritdoc />
    public Task RevokeAsync(string sessionId, DateTimeOffset until, CancellationToken cancellationToken) =>
        store.RevokeAsync(new SessionRevocation(sessionId, until), cancellationToken);
}

/// <summary>
/// <see cref="ISessionVersions"/> over the Codelab.Security version store, keyed by the token subject (the user id
/// claim the session middleware reads), so a bump refuses every older token on the next request.
/// </summary>
public sealed class SessionVersionsAdapter(ISessionVersionStore store) : ISessionVersions
{
    /// <inheritdoc />
    public Task<long> CurrentAsync(Guid subjectId, CancellationToken cancellationToken) =>
        store.GetVersionAsync(subjectId.ToString(), cancellationToken);

    /// <inheritdoc />
    public Task<long> BumpAsync(Guid subjectId, CancellationToken cancellationToken) =>
        store.BumpAsync(subjectId.ToString(), cancellationToken);
}
