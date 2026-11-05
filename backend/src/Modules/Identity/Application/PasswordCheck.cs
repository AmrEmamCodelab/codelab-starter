using Starter.Domain.Users;
using Starter.Modules.Identity.Domain;

namespace Starter.Modules.Identity.Application;

/// <summary>The verdict of a basic sign-in's password check.</summary>
/// <param name="Reason">Why the sign-in is refused, or null when it may start a session.</param>
/// <param name="StartedLockout">True when this attempt locked the account.</param>
public sealed record PasswordCheckResult(AuthFailureReason? Reason, bool StartedLockout)
{
    /// <summary>The sign-in may start a session.</summary>
    public static PasswordCheckResult Passed { get; } = new(Reason: null, StartedLockout: false);

    /// <summary>True when the sign-in may start a session.</summary>
    public bool IsPassed => Reason is null;

    /// <summary>A refusal for <paramref name="reason"/>.</summary>
    public static PasswordCheckResult Refused(AuthFailureReason reason, bool startedLockout = false) => new(reason, startedLockout);
}

/// <summary>
/// The password and lockout rules of basic login. Every path costs one hash check (a decoy when there is no usable
/// hash), so timing does not tell an unknown, locked, unactivated or directory-bound account from a wrong password.
/// Only a wrong password of a password-bound account counts toward the lockout (<see cref="LockoutPolicy"/>); a locked
/// account is refused even with the right password, and a success resets the counter (saved with the session).
/// </summary>
public sealed class PasswordCheck(IPasswordHasher hasher, LockoutPolicy policy, IIdentityUnitOfWork unitOfWork, TimeProvider clock)
{
    /// <summary>Checks <paramref name="password"/> for <paramref name="account"/> (null: no such user).</summary>
    public async Task<PasswordCheckResult> CheckAsync(UserAccount? account, string password, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(password);
        var now = clock.GetUtcNow();
        if (PreCheck(account, now) is { } refused)
        {
            hasher.Verify(passwordHash: null, password);
            return PasswordCheckResult.Refused(refused);
        }

        var verification = hasher.Verify(account!.PasswordHash, password);
        if (verification == PasswordVerification.Failed)
        {
            var startedLockout = account.RecordFailedSignIn(now, policy);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return PasswordCheckResult.Refused(AuthFailureReason.WrongPassword, startedLockout);
        }

        if (!account.IsActive)
        {
            return PasswordCheckResult.Refused(AuthFailureReason.AccountInactive);
        }

        account.RecordSuccessfulSignIn();
        if (verification == PasswordVerification.SucceededRehashNeeded)
        {
            account.ReplacePasswordHash(hasher.Hash(password));
        }

        return PasswordCheckResult.Passed;
    }

    private static AuthFailureReason? PreCheck(UserAccount? account, DateTimeOffset now) => account switch
    {
        null => AuthFailureReason.UnknownAccount,
        { Binding: not AccountBinding.Password } => AuthFailureReason.BindingMismatch,
        _ when account.IsLockedOut(now) => AuthFailureReason.LockedOut,
        { HasPassword: false } => AuthFailureReason.PasswordNotSet,
        _ => null,
    };
}
