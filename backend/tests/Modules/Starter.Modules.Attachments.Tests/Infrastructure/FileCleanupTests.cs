using Microsoft.Extensions.Logging.Abstractions;
using Starter.Application.Messaging;
using Starter.Modules.Attachments.Application;
using Starter.Modules.Attachments.Infrastructure.Storage;
using static Starter.Modules.Attachments.Tests.Infrastructure.InfrastructureFixtures;

namespace Starter.Modules.Attachments.Tests.Infrastructure;

public sealed class FileCleanupTests
{
    [Fact]
    public async Task Enqueue_writes_one_delete_message_that_names_the_file_id_only()
    {
        var outbox = new RecordingOutbox();

        await new OutboxAttachmentFileCleanup(outbox).EnqueueDeleteAsync(FileId, CancellationToken.None);

        var message = Assert.Single(outbox.Messages);
        Assert.Equal(AttachmentsOutboxMessageTypes.DeleteFile, message.Type);
        Assert.Equal($"{{\"fileId\":\"{FileId}\"}}", message.PayloadJson);
        Assert.Null(message.NotBefore);
    }

    [Fact]
    public async Task Enqueue_refuses_a_value_that_is_not_a_file_id()
    {
        var outbox = new RecordingOutbox();

        await Assert.ThrowsAsync<ArgumentException>(() => new OutboxAttachmentFileCleanup(outbox).EnqueueDeleteAsync("../x", CancellationToken.None));
        Assert.Empty(outbox.Messages);
    }

    [Fact]
    public async Task The_handler_deletes_the_named_file_on_the_service()
    {
        var storage = new RecordingStorage(deleteAnswer: true);

        await Handler(storage).HandleAsync(Delivery(DeleteAttachmentFileJson.Write(FileId)), CancellationToken.None);

        Assert.Equal([FileId], storage.Deleted);
    }

    [Fact]
    public async Task A_file_already_gone_counts_as_delivered()
    {
        var storage = new RecordingStorage(deleteAnswer: false);

        await Handler(storage).HandleAsync(Delivery(DeleteAttachmentFileJson.Write(FileId)), CancellationToken.None);

        Assert.Equal([FileId], storage.Deleted);
    }

    [Fact]
    public async Task A_service_outage_propagates_so_the_drain_retries()
    {
        var storage = new RecordingStorage(deleteAnswer: true) { Outage = new AttachmentStorageUnavailableException(innerException: null) };

        await Assert.ThrowsAsync<AttachmentStorageUnavailableException>(
            () => Handler(storage).HandleAsync(Delivery(DeleteAttachmentFileJson.Write(FileId)), CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"fileId\":\"../../escape\"}")]
    [InlineData("{\"fileId\":null}")]
    public async Task An_unreadable_payload_is_dropped_without_a_call(string payload)
    {
        var storage = new RecordingStorage(deleteAnswer: true);

        await Handler(storage).HandleAsync(Delivery(payload), CancellationToken.None);

        Assert.Empty(storage.Deleted);
    }

    [Fact]
    public void The_handler_delivers_the_delete_file_type()
    {
        Assert.Equal(AttachmentsOutboxMessageTypes.DeleteFile, Handler(new RecordingStorage(deleteAnswer: true)).MessageType);
    }

    private static DeleteAttachmentFileHandler Handler(RecordingStorage storage) =>
        new(storage, NullLogger<DeleteAttachmentFileHandler>.Instance);

    private static OutboxDelivery Delivery(string payload) =>
        new(MessageId: 7, Type: AttachmentsOutboxMessageTypes.DeleteFile, PayloadJson: payload, Attempt: 1);

    private sealed class RecordingOutbox : IOutbox
    {
        public List<OutboxMessage> Messages { get; } = [];

        public Task EnqueueAsync(OutboxMessage message, CancellationToken cancellationToken)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingStorage(bool deleteAnswer) : IAttachmentStorageClient
    {
        public List<string> Deleted { get; } = [];

        public Exception? Outage { get; init; }

        public Task<Stream?> OpenReadAsync(string fileId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> DeleteAsync(string fileId, CancellationToken cancellationToken)
        {
            if (Outage is not null)
            {
                return Task.FromException<bool>(Outage);
            }

            Deleted.Add(fileId);
            return Task.FromResult(deleteAnswer);
        }
    }
}
