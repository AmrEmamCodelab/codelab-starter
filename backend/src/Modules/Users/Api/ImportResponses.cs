using Codelab.Security.PublicIds;
using Starter.Domain.Users;
using Starter.Modules.Users.Application;
using Starter.Modules.Users.Application.Import;

namespace Starter.Modules.Users.Api;

/// <summary>One row of an import preview.</summary>
/// <param name="RowNumber">The sheet row number.</param>
/// <param name="UserName">The sign-in name, as read.</param>
/// <param name="DisplayName">The display name, as read.</param>
/// <param name="Email">The e-mail address, as read.</param>
/// <param name="Kind">The parsed kind, or null when invalid.</param>
/// <param name="Binding">The parsed binding, or null when invalid.</param>
/// <param name="OrganizationCode">The organization code, as read.</param>
/// <param name="Valid">True when the row will become an account.</param>
/// <param name="Errors">The row's error codes (<c>errors.&lt;CODE&gt;</c> on the client); empty when valid.</param>
public sealed record ImportPreviewRowResponse(
    int RowNumber,
    string? UserName,
    string? DisplayName,
    string? Email,
    UserKind? Kind,
    AccountBinding? Binding,
    string? OrganizationCode,
    bool Valid,
    IReadOnlyList<string> Errors)
{
    /// <summary>The response of <paramref name="row"/>.</summary>
    public static ImportPreviewRowResponse From(CheckedImportRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return new ImportPreviewRowResponse(
            row.Row.RowNumber,
            row.Row.UserName,
            row.Row.DisplayName,
            row.Row.Email,
            row.Kind,
            row.Binding,
            row.Row.OrganizationCode,
            row.IsValid,
            row.Errors);
    }
}

/// <summary>The body of <c>POST /api/admin/user-import/preview</c>.</summary>
/// <param name="BatchId">The batch to commit.</param>
/// <param name="Rows">Every data row with its verdict.</param>
/// <param name="ValidCount">The number of valid rows.</param>
/// <param name="InvalidCount">The number of rows with an error.</param>
public sealed record ImportPreviewResponse(
    [property: WireId] PublicId<UserImportIds> BatchId,
    IReadOnlyList<ImportPreviewRowResponse> Rows,
    int ValidCount,
    int InvalidCount)
{
    /// <summary>The response of <paramref name="preview"/>.</summary>
    public static ImportPreviewResponse From(ImportPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        return new ImportPreviewResponse(
            PublicId<UserImportIds>.From(preview.BatchId),
            [.. preview.Rows.Select(ImportPreviewRowResponse.From)],
            preview.ValidCount,
            preview.InvalidCount);
    }
}

/// <summary>An account the commit created.</summary>
/// <param name="Id">The account's PublicId.</param>
/// <param name="UserName">The sign-in name.</param>
/// <param name="Binding">Password (1), Active Directory (2) or SSO (3).</param>
/// <param name="ActivationLink">The activation link of a password-bound account, or null.</param>
/// <param name="ExpiresAt">When that link stops working, or null.</param>
/// <param name="ActivationErrorCode">Why a password-bound account got no link (rare; issue one from the users list), or null.</param>
public sealed record ImportedUserResponse(
    [property: WireId] PublicId<UsersIds> Id,
    string UserName,
    AccountBinding Binding,
    string? ActivationLink,
    DateTimeOffset? ExpiresAt,
    string? ActivationErrorCode)
{
    /// <summary>The response of <paramref name="user"/>.</summary>
    public static ImportedUserResponse From(ImportedUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return new ImportedUserResponse(
            PublicId<UsersIds>.From(user.UserId),
            user.UserName,
            user.Binding,
            user.Activation?.ActivationLink,
            user.Activation?.ExpiresAt,
            user.ActivationErrorCode);
    }
}

/// <summary>A row the commit left out.</summary>
/// <param name="RowNumber">The sheet row number.</param>
/// <param name="Errors">Why (error codes).</param>
public sealed record RejectedImportRowResponse(int RowNumber, IReadOnlyList<string> Errors);

/// <summary>The body of <c>POST /api/admin/user-import/{batchId}/commit</c>.</summary>
/// <param name="Created">The accounts created.</param>
/// <param name="Rejected">The rows left out.</param>
public sealed record ImportCommitResponse(IReadOnlyList<ImportedUserResponse> Created, IReadOnlyList<RejectedImportRowResponse> Rejected)
{
    /// <summary>The response of <paramref name="result"/>.</summary>
    public static ImportCommitResponse From(ImportCommitResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new ImportCommitResponse(
            [.. result.Created.Select(ImportedUserResponse.From)],
            [.. result.Rejected.Select(row => new RejectedImportRowResponse(row.RowNumber, row.Errors))]);
    }
}
