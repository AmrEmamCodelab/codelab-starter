using Starter.Application.Users;
using Starter.Modules.Identity.Domain;

namespace Starter.Modules.Identity.Application.Accounts;

/// <summary>The mapping between the Identity account and the kernel's account administration records.</summary>
public static class AccountSummaries
{
    /// <summary>The administration view of <paramref name="account"/> at <paramref name="now"/> (no password, hash or ticket).</summary>
    public static AccountSummary Of(UserAccount account, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(account);
        return new AccountSummary(
            UserId: account.Id,
            SubjectId: account.SubjectId,
            UserName: account.UserName,
            DisplayName: account.DisplayName,
            Email: account.Email,
            LoginName: account.LoginName,
            Upn: account.Upn,
            Kind: account.Kind,
            Binding: account.Binding,
            OrganizationId: account.OrganizationId,
            IsActive: account.IsActive,
            HasPassword: account.HasPassword,
            IsLockedOut: account.IsLockedOut(now),
            IsBreakGlass: account.IsBreakGlass);
    }

    /// <summary>The editable fields of a new account.</summary>
    public static AccountChanges ChangesOf(NewAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        return new AccountChanges(
            DisplayName: account.DisplayName,
            Email: account.Email,
            LoginName: account.LoginName,
            Upn: account.Upn,
            Kind: account.Kind,
            Binding: account.Binding,
            OrganizationId: account.OrganizationId);
    }

    /// <summary>The domain profile of <paramref name="changes"/>.</summary>
    public static AccountProfile ProfileOf(AccountChanges changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        return new AccountProfile(
            DisplayName: changes.DisplayName,
            Email: changes.Email,
            LoginName: changes.LoginName,
            Upn: changes.Upn,
            Kind: changes.Kind,
            Binding: changes.Binding,
            OrganizationId: changes.OrganizationId);
    }
}
