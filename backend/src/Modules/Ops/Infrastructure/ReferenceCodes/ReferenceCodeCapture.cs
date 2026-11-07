using System.Threading.Channels;
using Codelab.Observability;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Starter.Modules.Ops.Application;
using Starter.Modules.Ops.Domain;
using Starter.Modules.Ops.Infrastructure.Options;

namespace Starter.Modules.Ops.Infrastructure.ReferenceCodes;

/// <summary>
/// The hand-off between requests and the index writer: a bounded in-memory queue. When it is full a sighting is
/// dropped (the error is still in the log under its correlation id); a request never waits for the database.
/// </summary>
public sealed class ReferenceCodeInbox
{
    /// <summary>How many sightings may wait for the writer.</summary>
    public const int Capacity = 1024;

    private readonly Channel<ReferenceCodeSighting> _channel = Channel.CreateBounded<ReferenceCodeSighting>(
        new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });

    /// <summary>The reading side (the writer).</summary>
    public ChannelReader<ReferenceCodeSighting> Reader => _channel.Reader;

    /// <summary>Queues <paramref name="sighting"/>; false when the queue is full and it was dropped.</summary>
    public bool TryEnqueue(ReferenceCodeSighting sighting) => _channel.Writer.TryWrite(sighting);
}

/// <summary>
/// Records every error response that showed the caller a reference code: a status of 400 or more with a problem+json
/// body (the Codelab.Errors envelope always carries <c>referenceCode</c>). It runs outermost, so the status and the
/// correlation id are final when it looks; it never reads or changes the body.
/// </summary>
public sealed class ReferenceCodeCaptureMiddleware(RequestDelegate next, ReferenceCodeInbox queue, TimeProvider clock, IOptions<OpsOptions> options)
{
    /// <summary>The media type of the error envelope.</summary>
    public const string ProblemJsonMediaType = "application/problem+json";

    /// <summary>The lowest status that is an error response.</summary>
    public const int FirstErrorStatus = StatusCodes.Status400BadRequest;

    /// <summary>Runs the middleware.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        await next(context);
        if (options.Value.CaptureReferenceCodes && ShowsReferenceCode(context.Response))
        {
            Capture(context);
        }
    }

    /// <summary>True for an error response with a problem+json body.</summary>
    public static bool ShowsReferenceCode(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return response.StatusCode >= FirstErrorStatus
            && response.ContentType is { } contentType
            && contentType.StartsWith(ProblemJsonMediaType, StringComparison.OrdinalIgnoreCase);
    }

    private void Capture(HttpContext context)
    {
        var correlationId = CorrelationId.For(context);
        var sighting = ReferenceCodeSighting.From(new ReferenceCodeSightingDetails(
            ReferenceCode: ReferenceCode.From(correlationId),
            CorrelationId: correlationId,
            Method: context.Request.Method,
            Path: context.Request.PathBase.Add(context.Request.Path).ToString(),
            StatusCode: context.Response.StatusCode,
            OccurredAt: clock.GetUtcNow()));
        if (sighting is not null)
        {
            queue.TryEnqueue(sighting);
        }
    }
}

/// <summary>Puts <see cref="ReferenceCodeCaptureMiddleware"/> in front of the whole pipeline.</summary>
public sealed class ReferenceCodeCaptureStartupFilter : IStartupFilter
{
    /// <inheritdoc />
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
        app =>
        {
            app.UseMiddleware<ReferenceCodeCaptureMiddleware>();
            next(app);
        };
}

/// <summary>
/// Writes queued sightings to <c>ops.ReferenceCodeSightings</c> in batches of up to <see cref="BatchSize"/>, each batch
/// in its own DI scope. A batch that fails to write is logged (count only) and dropped: the index is a lookup aid next
/// to the log, which still holds every error under its correlation id.
/// </summary>
public sealed partial class ReferenceCodeIndexWriter(
    ReferenceCodeInbox queue,
    IServiceScopeFactory scopes,
    ILogger<ReferenceCodeIndexWriter> logger) : BackgroundService
{
    /// <summary>The most sightings written in one save.</summary>
    public const int BatchSize = 100;

    /// <summary>Writes one batch now (also used by tests); returns how many sightings it took from the queue.</summary>
    public async Task<int> WriteBatchAsync(CancellationToken cancellationToken)
    {
        var batch = new List<ReferenceCodeSighting>(BatchSize);
        while (batch.Count < BatchSize && queue.Reader.TryRead(out var sighting))
        {
            batch.Add(sighting);
        }

        if (batch.Count == 0)
        {
            return 0;
        }

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IReferenceCodeSightings>().AddAsync(batch, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Load-bearing: isolates one failed batch, so the writer keeps serving the next ones.
            BatchNotWritten(logger, ex, batch.Count);
        }

        return batch.Count;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (await queue.Reader.WaitToReadAsync(stoppingToken))
        {
            await WriteBatchAsync(stoppingToken);
        }
    }

    [LoggerMessage(EventName = "OpsReferenceCodesNotIndexed", Level = LogLevel.Error, Message = "Ops reference-code sightings not indexed {Count}")]
    private static partial void BatchNotWritten(ILogger logger, Exception exception, int count);
}
