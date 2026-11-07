using Codelab.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Starter.Modules.Ops.Application;
using Starter.Modules.Ops.Infrastructure.Options;
using Starter.Modules.Ops.Infrastructure.ReferenceCodes;
using Starter.Modules.Ops.Tests.Fakes;

namespace Starter.Modules.Ops.Tests.Infrastructure;

public sealed class ReferenceCodeCaptureTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 9, 0, 0, TimeSpan.Zero);
    private readonly ReferenceCodeInbox _inbox = new();

    [Theory]
    [InlineData(500, "application/problem+json", true)]
    [InlineData(409, "application/problem+json; charset=utf-8", true)]
    [InlineData(400, "application/problem+json", true)]
    [InlineData(200, "application/problem+json", false)]
    [InlineData(500, "text/html", false)]
    [InlineData(404, null, false)]
    public void Only_problem_json_error_responses_show_a_code(int status, string? contentType, bool expected)
    {
        var context = new DefaultHttpContext();
        context.Response.StatusCode = status;
        context.Response.ContentType = contentType;

        Assert.Equal(expected, ReferenceCodeCaptureMiddleware.ShowsReferenceCode(context.Response));
    }

    [Fact]
    public async Task An_error_response_is_queued_with_its_code_correlation_id_and_request_line()
    {
        var context = Request("/api/records/x", query: "?secret=1");

        await Middleware(status: 409, captureEnabled: true).InvokeAsync(context);

        Assert.True(_inbox.Reader.TryRead(out var sighting));
        var correlationId = CorrelationId.For(context);
        Assert.Equal(ReferenceCode.From(correlationId), sighting!.ReferenceCode);
        Assert.Equal(correlationId, sighting.CorrelationId);
        Assert.Equal(("POST", "/base/api/records/x", 409, Now), (sighting.Method, sighting.Path, sighting.StatusCode, sighting.OccurredAt));
    }

    [Fact]
    public async Task A_success_or_a_disabled_capture_queues_nothing()
    {
        await Middleware(status: 200, captureEnabled: true).InvokeAsync(Request("/api/ok"));
        await Middleware(status: 500, captureEnabled: false).InvokeAsync(Request("/api/fail"));

        Assert.False(_inbox.Reader.TryRead(out _));
    }

    [Fact]
    public void A_full_inbox_drops_instead_of_blocking()
    {
        for (var index = 0; index < ReferenceCodeInbox.Capacity; index++)
        {
            Assert.True(_inbox.TryEnqueue(Sightings.New("9c1e5b7a20d84f6e8a3b1c2d4e5f6a7b", Now)));
        }

        Assert.False(_inbox.TryEnqueue(Sightings.New("9c1e5b7a20d84f6e8a3b1c2d4e5f6a7b", Now)));
    }

    [Fact]
    public async Task The_writer_stores_queued_sightings_in_batches()
    {
        var store = new FakeSightings();
        for (var index = 0; index < ReferenceCodeIndexWriter.BatchSize + 5; index++)
        {
            _inbox.TryEnqueue(Sightings.New("9c1e5b7a20d84f6e8a3b1c2d4e5f6a7b", Now));
        }

        var writer = Writer(store);

        Assert.Equal(ReferenceCodeIndexWriter.BatchSize, await writer.WriteBatchAsync(CancellationToken.None));
        Assert.Equal(5, await writer.WriteBatchAsync(CancellationToken.None));
        Assert.Equal(0, await writer.WriteBatchAsync(CancellationToken.None));
        Assert.Equal(ReferenceCodeIndexWriter.BatchSize + 5, store.Stored.Count);
    }

    [Fact]
    public async Task A_failed_batch_is_dropped_and_the_writer_goes_on()
    {
        var store = new FakeSightings { FailWith = new InvalidOperationException("database down") };
        _inbox.TryEnqueue(Sightings.New("9c1e5b7a20d84f6e8a3b1c2d4e5f6a7b", Now));
        var writer = Writer(store);

        Assert.Equal(1, await writer.WriteBatchAsync(CancellationToken.None));

        store.FailWith = null;
        _inbox.TryEnqueue(Sightings.New("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", Now));
        Assert.Equal(1, await writer.WriteBatchAsync(CancellationToken.None));
        Assert.Equal("AAAAA-AAAAA", Assert.Single(store.Stored).ReferenceCode);
    }

    private ReferenceCodeCaptureMiddleware Middleware(int status, bool captureEnabled) =>
        new(
            next: context =>
            {
                context.Response.StatusCode = status;
                context.Response.ContentType = ReferenceCodeCaptureMiddleware.ProblemJsonMediaType;
                return Task.CompletedTask;
            },
            _inbox,
            new FakeTimeProvider(Now),
            Options.Create(new OpsOptions { CaptureReferenceCodes = captureEnabled }));

    private static DefaultHttpContext Request(string path, string query = "")
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.PathBase = "/base";
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query.Length == 0 ? null : query);
        return context;
    }

    private ReferenceCodeIndexWriter Writer(FakeSightings store)
    {
        var services = new ServiceCollection().AddScoped<IReferenceCodeSightings>(_ => store).BuildServiceProvider();
        return new ReferenceCodeIndexWriter(_inbox, services.GetRequiredService<IServiceScopeFactory>(), NullLogger<ReferenceCodeIndexWriter>.Instance);
    }
}
