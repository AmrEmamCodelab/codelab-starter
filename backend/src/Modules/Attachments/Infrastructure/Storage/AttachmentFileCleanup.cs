using System.Text.Json;
using Microsoft.Extensions.Logging;
using Starter.Application.Messaging;
using Starter.Modules.Attachments.Application;
using Starter.Modules.Attachments.Domain;

namespace Starter.Modules.Attachments.Infrastructure.Storage;

/// <summary>
/// The JSON form of <see cref="DeleteAttachmentFilePayload"/> on the outbox (one owner for writing and reading it).
/// The payload names the file id only: no file name, record or user.
/// </summary>
public static class DeleteAttachmentFileJson
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The payload of <paramref name="fileId"/> as JSON.</summary>
    public static string Write(string fileId) => JsonSerializer.Serialize(new DeleteAttachmentFilePayload(fileId), Json);

    /// <summary>The well-formed file id in <paramref name="json"/>, or null when it is not a delete payload.</summary>
    public static string? TryReadFileId(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<DeleteAttachmentFilePayload>(json, Json);
            return AttachmentsLimits.IsFileId(payload?.FileId) ? payload!.FileId : null;
        }
        catch (JsonException)
        {
            return null; // load-bearing: a malformed payload is reported by the handler, never retried
        }
    }
}

/// <summary>
/// The <see cref="IAttachmentFileCleanup"/> over the kernel outbox: one <see cref="AttachmentsOutboxMessageTypes.DeleteFile"/>
/// message per removed file, delivered by <see cref="DeleteAttachmentFileHandler"/> on the next drain (the jobs module's
/// <c>outbox-drain</c>). The outbox saves on its own, so the caller enqueues after its removal was saved.
/// </summary>
public sealed class OutboxAttachmentFileCleanup(IOutbox outbox) : IAttachmentFileCleanup
{
    /// <inheritdoc />
    /// <exception cref="ArgumentException"><paramref name="fileId"/> is not an attachment-service file id.</exception>
    public Task EnqueueDeleteAsync(string fileId, CancellationToken cancellationToken)
    {
        if (!AttachmentsLimits.IsFileId(fileId))
        {
            throw new ArgumentException("Only an attachment-service file id can be deleted.", nameof(fileId));
        }

        return outbox.EnqueueAsync(
            new OutboxMessage(Type: AttachmentsOutboxMessageTypes.DeleteFile, PayloadJson: DeleteAttachmentFileJson.Write(fileId), NotBefore: null),
            cancellationToken);
    }
}

/// <summary>
/// Delivers <see cref="AttachmentsOutboxMessageTypes.DeleteFile"/>: deletes the file on the attachment service over
/// <see cref="IAttachmentStorageClient.DeleteAsync"/>. Idempotent: a file the service no longer has counts as deleted,
/// so a redelivery is harmless. A service outage throws, and the drain retries with its back-off. A payload that is not
/// a delete payload is logged and dropped (retrying cannot fix it).
/// </summary>
public sealed partial class DeleteAttachmentFileHandler(
    IAttachmentStorageClient storage,
    ILogger<DeleteAttachmentFileHandler> logger) : IOutboxHandler
{
    /// <inheritdoc />
    public string MessageType => AttachmentsOutboxMessageTypes.DeleteFile;

    /// <inheritdoc />
    public async Task HandleAsync(OutboxDelivery delivery, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        var fileId = DeleteAttachmentFileJson.TryReadFileId(delivery.PayloadJson);
        if (fileId is null)
        {
            LogUnreadablePayload(logger, delivery.MessageId);
            return;
        }

        var deleted = await storage.DeleteAsync(fileId, cancellationToken);
        LogDeleted(logger, delivery.MessageId, deleted);
    }

    [LoggerMessage(EventId = 13101, Level = LogLevel.Warning, Message = "Outbox message {MessageId} is not an attachment delete payload; dropped")]
    private static partial void LogUnreadablePayload(ILogger logger, long messageId);

    [LoggerMessage(EventId = 13102, Level = LogLevel.Information, Message = "Outbox message {MessageId} deleted an attachment file (present: {WasPresent})")]
    private static partial void LogDeleted(ILogger logger, long messageId, bool wasPresent);
}
