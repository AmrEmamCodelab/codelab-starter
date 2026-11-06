using Codelab.Security;
using Codelab.Security.Uploads;
using Microsoft.Extensions.Options;
using Starter.Modules.Attachments.Application;
using Starter.Modules.Attachments.Domain;

namespace Starter.Modules.Attachments.Infrastructure.Security;

/// <summary>
/// The <see cref="IAttachmentReceiptVerifier"/> over <see cref="UploadReceiptSigner"/>: checks an <c>r1.</c> receipt
/// offline under the <c>Codelab:Security:Uploads</c> receipt ring (the ring the attachment service signs with), so a
/// link never calls the service. Receipts are mandatory: while <see cref="UploadSafetyOptions.ReceiptsEnabled"/> is
/// false nothing verifies, so nothing links. The MIME type comes from <see cref="UploadContentTypes"/> by the file's
/// extension; a receipt for an extension without a MIME type does not verify. Singleton (the ring is read once).
/// </summary>
public sealed class ReceiptVerifier : IAttachmentReceiptVerifier
{
    private readonly UploadReceiptSigner? _signer;

    /// <summary>Builds the verifier from the upload options; no signer when receipts are disabled.</summary>
    /// <exception cref="InvalidOperationException">Receipts are enabled but the ring is not usable (the start-up validation refuses it first).</exception>
    public ReceiptVerifier(IOptions<UploadSafetyOptions> uploads, SecurityProtocol protocol)
    {
        ArgumentNullException.ThrowIfNull(uploads);
        ArgumentNullException.ThrowIfNull(protocol);
        _signer = uploads.Value.ReceiptsEnabled ? new UploadReceiptSigner(uploads.Value, protocol) : null;
    }

    /// <summary>True when receipts are enabled, so a receipt can verify at all.</summary>
    public bool IsEnabled => _signer is not null;

    /// <inheritdoc />
    public VerifiedUpload? Verify(string receipt)
    {
        if (_signer is null || !_signer.TryVerify(receipt, out var attested))
        {
            return null;
        }

        return UploadContentTypes.TryGetContentType(attested.FileName, out var contentType)
            ? new VerifiedUpload(
                attested.Scope,
                new StoredFile(
                    FileId: attested.FileId,
                    FileName: attested.FileName,
                    SizeBytes: attested.SizeBytes,
                    Sha256Hex: attested.Sha256Hex,
                    ContentType: contentType))
            : null;
    }
}
