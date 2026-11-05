using Codelab.Security.PublicIds;
using Codelab.Security.Signing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Starter.Infrastructure.Authorization;
using Starter.Infrastructure.Results;
using Starter.Modules.Users.Application;
using Starter.Modules.Users.Application.Import;

namespace Starter.Modules.Users.Api;

/// <summary>
/// The Excel user import: preview one .xlsx (multipart field <c>file</c>), then commit the previewed batch. Thin: each
/// action calls one use case and maps its outcome.
/// </summary>
[ApiController]
[Route(UsersRoutes.Import)]
[Produces(UsersRoutes.JsonContentType)]
public sealed class UserImportController(PreviewUserImport previewImport, CommitUserImport commitImport) : ControllerBase
{
    /// <summary>
    /// Why the preview's body is outside the request signature: the browser builds the multipart envelope (and its
    /// boundary) after the signer runs, so the SPA signs it as UNSIGNED-BODY. The caller, method, path and time stay signed.
    /// </summary>
    public const string MultipartSignatureReason =
        "Multipart upload: the browser writes the envelope after signing; the proof still binds caller, method and path.";

    /// <summary>Reads and validates the sheet and stores the batch; nothing is created yet.</summary>
    [HttpPost(UsersRoutes.Preview)]
    [HasPermission(UsersPermissionKeys.Import)]
    [SignatureExempt(SignatureExemption.BodyOnly, MultipartSignatureReason)]
    [Consumes(UsersRoutes.MultipartContentType)]
    [RequestSizeLimit(UsersRoutes.MaxPreviewRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = UsersRoutes.MaxPreviewRequestBytes)]
    [ProducesResponseType<ImportPreviewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ImportPreviewResponse>> PreviewAsync(
        [FromForm(Name = UsersRoutes.FileField)] IFormFile? file, CancellationToken cancellationToken)
    {
        await using var content = file?.OpenReadStream();
        var upload = new ImportUpload(content, file?.Length ?? 0, file?.FileName);
        return (await previewImport.ExecuteAsync(upload, cancellationToken)).ToActionResult(ImportPreviewResponse.From);
    }

    /// <summary>Creates the accounts of the batch's valid rows (no passwords; activation links for password-bound rows).</summary>
    [HttpPost(UsersRoutes.Commit)]
    [HasPermission(UsersPermissionKeys.Import)]
    [ProducesResponseType<ImportCommitResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ImportCommitResponse>> CommitAsync(PublicId<UserImportIds> batchId, CancellationToken cancellationToken) =>
        (await commitImport.ExecuteAsync(batchId, cancellationToken)).ToActionResult(ImportCommitResponse.From);
}
