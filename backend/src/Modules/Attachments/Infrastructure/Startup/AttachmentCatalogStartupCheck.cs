using Codelab.Security.Uploads;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Starter.Infrastructure.Startup;
using Starter.Modules.Attachments.Domain;
using Starter.Modules.Attachments.Infrastructure.Persistence;
using Starter.Modules.Attachments.Infrastructure.Security;

namespace Starter.Modules.Attachments.Infrastructure.Startup;

/// <summary>
/// The decision of the start-up catalog check: every attachment type's extensions must be inside the ONE upload
/// allowlist (<c>Codelab:Security:Uploads</c>, <see cref="AttachmentTypeCatalog.FindViolations"/>), else the start
/// fails with every offending (type, extension) pair named.
/// </summary>
public static class AttachmentCatalogCheck
{
    /// <summary>The allowlist <paramref name="uploads"/> configures, as the catalog check reads it.</summary>
    public static UploadAllowlist AllowlistOf(UploadSafetyOptions uploads)
    {
        ArgumentNullException.ThrowIfNull(uploads);
        return new UploadAllowlist(Allowed: [.. uploads.AllowedExtensions], Blocked: [.. uploads.BlockedExtensions]);
    }

    /// <summary>Returns normally when <paramref name="types"/> fit <paramref name="allowlist"/>.</summary>
    /// <exception cref="InvalidOperationException">A type allows an extension the allowlist does not accept.</exception>
    public static void EnsureFits(IEnumerable<ServiceAttachment> types, UploadAllowlist allowlist)
    {
        var violations = AttachmentTypeCatalog.FindViolations(types, allowlist);
        if (violations.Count == 0)
        {
            return;
        }

        var pairs = string.Join(", ", violations.Select(violation => $"{violation.TypeKey} {violation.Extension}"));
        throw new InvalidOperationException(
            $"Attachment types allow extensions outside {UploadSafetyOptions.SectionName}:{nameof(UploadSafetyOptions.AllowedExtensions)} "
            + $"(or inside {nameof(UploadSafetyOptions.BlockedExtensions)}): {pairs}. Add them to the allowlist of the API and the "
            + "attachment service, or retire them from the type in a migration.");
    }
}

/// <summary>
/// Runs <see cref="AttachmentCatalogCheck"/> against the seeded types on every start, after migrations
/// (<see cref="StartupPhase.ReferenceData"/>), and warns when upload receipts are off (then nothing can be linked).
/// </summary>
public sealed partial class AttachmentCatalogStartupCheck(
    AttachmentsDbContext context,
    IOptions<UploadSafetyOptions> uploads,
    ReceiptVerifier receipts,
    ILogger<AttachmentCatalogStartupCheck> logger) : IStartupTask
{
    /// <inheritdoc />
    public StartupPhase Phase => StartupPhase.ReferenceData;

    /// <inheritdoc />
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var types = await context.Types.AsNoTracking().ToListAsync(cancellationToken);
        AttachmentCatalogCheck.EnsureFits(types, AttachmentCatalogCheck.AllowlistOf(uploads.Value));
        LogCatalogChecked(logger, types.Count);
        if (!receipts.IsEnabled)
        {
            LogReceiptsDisabled(logger, UploadSafetyOptions.SectionName);
        }
    }

    [LoggerMessage(EventId = 13001, Level = LogLevel.Information, Message = "Attachment type catalog checked against the upload allowlist: {TypeCount} type(s)")]
    private static partial void LogCatalogChecked(ILogger logger, int typeCount);

    [LoggerMessage(EventId = 13002, Level = LogLevel.Warning, Message = "Upload receipts are disabled ({Section}:ReceiptsEnabled); no attachment can be linked")]
    private static partial void LogReceiptsDisabled(ILogger logger, string section);
}
