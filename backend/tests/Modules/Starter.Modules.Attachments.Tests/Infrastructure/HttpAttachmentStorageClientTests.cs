using System.Net;
using Starter.Modules.Attachments.Application;
using Starter.Modules.Attachments.Infrastructure.Storage;
using static Starter.Modules.Attachments.Tests.Infrastructure.InfrastructureFixtures;

namespace Starter.Modules.Attachments.Tests.Infrastructure;

public sealed class HttpAttachmentStorageClientTests
{
    private const string Bytes = "%PDF-1.7 test bytes";

    [Fact]
    public async Task OpenRead_streams_the_bytes_of_GET_api_files_fileId()
    {
        var handler = StubHandler.Status(HttpStatusCode.OK, Bytes);

        await using var stream = await Client(handler).OpenReadAsync(FileId, CancellationToken.None);

        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        Assert.Equal(Bytes, await reader.ReadToEndAsync());
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal($"{ServiceRoot}/api/files/{FileId}", request.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task OpenRead_answers_null_when_the_service_has_no_such_file()
    {
        Assert.Null(await Client(StubHandler.Status(HttpStatusCode.NotFound)).OpenReadAsync(FileId, CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task OpenRead_reports_any_other_status_as_storage_unavailable(HttpStatusCode status)
    {
        var refused = await Assert.ThrowsAsync<AttachmentStorageUnavailableException>(
            () => Client(StubHandler.Status(status)).OpenReadAsync(FileId, CancellationToken.None));

        Assert.Equal(status, Assert.IsType<HttpRequestException>(refused.InnerException).StatusCode);
    }

    [Fact]
    public async Task A_connection_failure_is_storage_unavailable()
    {
        var failure = new HttpRequestException("connection refused");

        var refused = await Assert.ThrowsAsync<AttachmentStorageUnavailableException>(
            () => Client(StubHandler.Throws(failure)).OpenReadAsync(FileId, CancellationToken.None));

        Assert.Same(failure, refused.InnerException);
    }

    [Fact]
    public async Task A_timeout_is_storage_unavailable_not_a_cancellation()
    {
        var handler = new StubHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var client = new HttpAttachmentStorageClient(new HttpClient(handler) { BaseAddress = new Uri(ServiceRoot), Timeout = TimeSpan.FromMilliseconds(50) });

        await Assert.ThrowsAsync<AttachmentStorageUnavailableException>(() => client.DeleteAsync(FileId, CancellationToken.None));
    }

    [Fact]
    public async Task A_cancellation_the_caller_asked_for_propagates()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var handler = StubHandler.Throws(new TaskCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client(handler).OpenReadAsync(FileId, cancelled.Token));
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData(HttpStatusCode.OK)]
    public async Task Delete_answers_true_when_the_service_deleted_the_file(HttpStatusCode status)
    {
        var handler = StubHandler.Status(status);

        Assert.True(await Client(handler).DeleteAsync(FileId, CancellationToken.None));
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal($"{ServiceRoot}/api/files/{FileId}", request.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task Delete_answers_false_when_the_file_was_already_gone()
    {
        Assert.False(await Client(StubHandler.Status(HttpStatusCode.NotFound)).DeleteAsync(FileId, CancellationToken.None));
    }

    [Fact]
    public async Task Delete_reports_a_server_error_as_storage_unavailable()
    {
        await Assert.ThrowsAsync<AttachmentStorageUnavailableException>(
            () => Client(StubHandler.Status(HttpStatusCode.BadGateway)).DeleteAsync(FileId, CancellationToken.None));
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("short")]
    [InlineData("has space in it 000000000")]
    public async Task A_malformed_file_id_never_reaches_the_service(string fileId)
    {
        var handler = StubHandler.Status(HttpStatusCode.OK, Bytes);
        var client = Client(handler);

        Assert.Null(await client.OpenReadAsync(fileId, CancellationToken.None));
        Assert.False(await client.DeleteAsync(fileId, CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_root_with_a_path_keeps_it()
    {
        var handler = StubHandler.Status(HttpStatusCode.NoContent);
        var client = new HttpAttachmentStorageClient(new HttpClient(handler) { BaseAddress = new Uri("https://apps.test/attachment-service/") });

        await client.DeleteAsync(FileId, CancellationToken.None);

        Assert.Equal($"https://apps.test/attachment-service/api/files/{FileId}", Assert.Single(handler.Requests).RequestUri!.AbsoluteUri);
    }

    private static HttpAttachmentStorageClient Client(StubHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri(ServiceRoot + "/") });
}
