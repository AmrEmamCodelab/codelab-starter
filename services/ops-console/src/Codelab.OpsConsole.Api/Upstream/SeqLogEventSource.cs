using System.Text.Json;
using Codelab.OpsConsole.Api.Diagnostics;
using Codelab.OpsConsole.Api.Hosting;
using Codelab.OpsConsole.Application.Ports;
using Codelab.OpsConsole.Application.ReferenceCodes;
using Microsoft.Extensions.Options;

namespace Codelab.OpsConsole.Api.Upstream;

/// <summary>
/// <see cref="ILogEventSource"/> over the Seq HTTP API (registered by <c>AddSeqLogEventSource</c>: base address and
/// timeout from <c>Codelab:OpsConsole:Seq</c>, no correlation or trace headers sent to Seq). The read key goes in
/// <see cref="SeqQueries.ApiKeyHeader"/> and never into a log line. Seq not configured, down, slow, refusing the key or
/// answering an unreadable body is <see cref="LogSearchResult.Unavailable"/>, logged as a warning; cancellation propagates.
/// </summary>
internal sealed class SeqLogEventSource(HttpClient http, IOptionsMonitor<OpsConsoleOptions> options, TimeProvider clock, ILogger<SeqLogEventSource> logger)
    : ILogEventSource
{
    /// <summary>The name of the HTTP client and of the dependency in the log.</summary>
    public const string HttpClientName = "seq";

    /// <inheritdoc />
    public async Task<LogSearchResult> FindByReferenceCodeAsync(string referenceCode, CancellationToken cancellationToken)
    {
        var seq = options.CurrentValue.Seq;
        if (!seq.IsConfigured)
        {
            return LogSearchResult.Unavailable(link: null);
        }

        var filter = SeqQueries.ReferenceCodeFilter(referenceCode);
        var link = SeqQueries.UiLink(UpstreamAddress.AsBaseAddress(seq.BaseUrl!), filter);
        var from = clock.GetUtcNow().AddDays(-seq.LookbackDays);
        using var request = new HttpRequestMessage(HttpMethod.Get, SeqQueries.EventsRequest(filter, seq.MaxEvents, from));
        if (!string.IsNullOrWhiteSpace(seq.ApiKey))
        {
            request.Headers.TryAddWithoutValidation(SeqQueries.ApiKeyHeader, seq.ApiKey);
        }

        var events = await ReadEventsAsync(request, cancellationToken);
        return events is null ? LogSearchResult.Unavailable(link) : LogSearchResult.Found(events, link);
    }

    private async Task<IReadOnlyList<LoggedEvent>?> ReadEventsAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                OpsConsoleLog.SeqRefused(logger, (int)response.StatusCode);
                return null;
            }

            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
            return SeqEventReader.Read(document.RootElement);
        }
        catch (HttpRequestException ex)
        {
            OpsConsoleLog.SeqUnreachable(logger, ex.HttpRequestError.ToString());
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            OpsConsoleLog.SeqUnreachable(logger, nameof(TimeoutException));
            return null;
        }
        catch (JsonException)
        {
            OpsConsoleLog.SeqUnreachable(logger, nameof(JsonException));
            return null;
        }
    }
}
