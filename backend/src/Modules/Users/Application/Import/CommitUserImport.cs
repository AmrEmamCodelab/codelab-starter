using Codelab.Security.PublicIds;
using Starter.Application.Results;
using Starter.Application.Users;
using Starter.Domain.Users;
using Starter.Modules.Users.Application.Accounts;

namespace Starter.Modules.Users.Application.Import;

/// <summary>An account the commit created.</summary>
/// <param name="UserId">The new account's id.</param>
/// <param name="UserName">The sign-in name.</param>
/// <param name="Binding">The binding.</param>
/// <param name="Activation">The activation of a password-bound account (a link, or an e-mail on its way), or null (Active Directory or SSO).</param>
/// <param name="ActivationErrorCode">Why a password-bound account got no link (Identity's refusal code), or null.</param>
public sealed record ImportedUser(long UserId, string UserName, AccountBinding Binding, IssuedActivation? Activation, string? ActivationErrorCode);

/// <summary>A row the commit did not turn into an account.</summary>
/// <param name="RowNumber">The sheet row number.</param>
/// <param name="Errors">Why: row error codes, or the Identity module's refusal code.</param>
public sealed record RejectedImportRow(int RowNumber, IReadOnlyList<string> Errors);

/// <summary>What a commit did.</summary>
/// <param name="Created">The accounts created, in sheet order.</param>
/// <param name="Rejected">The rows left out, in sheet order.</param>
public sealed record ImportCommitResult(IReadOnlyList<ImportedUser> Created, IReadOnlyList<RejectedImportRow> Rejected);

/// <summary>
/// <c>POST /api/admin/user-import/{batchId}/commit</c>: re-validates the stored rows, claims the batch once, then creates
/// one account per valid row, none with a password. Every password-bound account gets a sealed one-time activation
/// ticket: an account with an e-mail address gets a Pending account-activation outbox message by ticket id (the link is
/// e-mailed at delivery), one without gets its link in the result; Active Directory and SSO
/// accounts get neither. A row Identity refuses (a duplicate that appeared since the preview) is rejected, the others
/// go on.
/// </summary>
public sealed class CommitUserImport(ImportBatches batches, ImportRowValidator validator, IAccountAdministration accounts, AccountActivations activations)
{
    /// <summary>
    /// The result, or a refusal: the batch refusals of <see cref="ImportBatches.FindCommittableAsync"/>,
    /// <see cref="UsersErrorCodes.ImportBatchCommitted"/> when a concurrent commit won, or
    /// <see cref="UsersErrorCodes.ActivationLinkUnconfigured"/> when password-bound rows need links that cannot be built
    /// (checked before anything is created).
    /// </summary>
    public async Task<Outcome<ImportCommitResult>> ExecuteAsync(PublicId<UserImportIds> batchId, CancellationToken cancellationToken)
    {
        var found = await batches.FindCommittableAsync(batchId, cancellationToken);
        if (!found.IsSuccess)
        {
            return found.Refusal;
        }

        var rows = await validator.ValidateAsync(ImportBatches.RowsOf(found.Value), cancellationToken);
        if (!activations.IsConfigured && rows.Any(row => row.IsValid && row.Binding == AccountBinding.Password))
        {
            return Refusal.Unprocessable(UsersErrorCodes.ActivationLinkUnconfigured);
        }

        if (!await batches.TryClaimAsync(found.Value, cancellationToken))
        {
            return Refusal.Conflict(UsersErrorCodes.ImportBatchCommitted);
        }

        return await CreateAccountsAsync(rows, cancellationToken);
    }

    private async Task<ImportCommitResult> CreateAccountsAsync(IReadOnlyList<CheckedImportRow> rows, CancellationToken cancellationToken)
    {
        var created = new List<ImportedUser>();
        var rejected = new List<RejectedImportRow>();
        foreach (var row in rows)
        {
            if (!row.IsValid)
            {
                rejected.Add(new RejectedImportRow(row.Row.RowNumber, row.Errors));
                continue;
            }

            var account = await accounts.CreateAsync(NewAccountOf(row), cancellationToken);
            if (!account.IsSuccess)
            {
                rejected.Add(new RejectedImportRow(row.Row.RowNumber, [account.Refusal.Code]));
                continue;
            }

            created.Add(await ImportedAsync(account.Value, cancellationToken));
        }

        return new ImportCommitResult(created, rejected);
    }

    private async Task<ImportedUser> ImportedAsync(AccountSummary account, CancellationToken cancellationToken)
    {
        if (account.Binding != AccountBinding.Password)
        {
            return new ImportedUser(account.UserId, account.UserName, account.Binding, Activation: null, ActivationErrorCode: null);
        }

        // The account exists either way; when Identity refuses the ticket the row carries the refusal code instead of a
        // link, and the administrator issues one from the users list.
        var activation = await activations.IssueAsync(account, cancellationToken);
        return activation.IsSuccess
            ? new ImportedUser(account.UserId, account.UserName, account.Binding, activation.Value, ActivationErrorCode: null)
            : new ImportedUser(account.UserId, account.UserName, account.Binding, Activation: null, activation.Refusal.Code);
    }

    private static NewAccount NewAccountOf(CheckedImportRow row) => new(
        UserName: row.Row.UserName!,
        DisplayName: row.Row.DisplayName!,
        Email: row.Row.Email,
        LoginName: row.Row.LoginName,
        Upn: row.Row.Upn,
        Kind: row.Kind!.Value,
        Binding: row.Binding!.Value,
        OrganizationId: row.OrganizationId);
}
