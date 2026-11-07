using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Codelab.OpsConsole.Api.Upstream;

namespace Codelab.OpsConsole.Api.Tests.TestSupport;

/// <summary>One request the fake Seq received.</summary>
internal sealed record ReceivedSeqCall(Uri Uri, string? ApiKey);

/// <summary>Seq's <c>api/events</c> as the console sees it: answers a configured status and body, records every request.</summary>
internal sealed class FakeSeq
{
    public ConcurrentQueue<ReceivedSeqCall> Calls { get; } = new();

    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

    public string EventsJson { get; set; } = "[]";

    public bool Unreachable { get; set; }

    public HttpMessageHandler CreateHandler() => new Handler(this);

    /// <summary>A Seq event in the shape <c>api/events?render=true</c> answers.</summary>
    public static string Event(string timestamp, string level, string message, params (string Name, object Value)[] properties)
    {
        var props = string.Join(',', properties.Select(property => $"{{\"Name\":\"{property.Name}\",\"Value\":{Literal(property.Value)}}}"));
        return $"{{\"Id\":\"event-1\",\"Timestamp\":\"{timestamp}\",\"Level\":\"{level}\",\"RenderedMessage\":\"{message}\",\"Properties\":[{props}]}}";
    }

    private static string Literal(object value) => value is int number
        ? number.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : $"\"{value}\"";

    private Task<HttpResponseMessage> Respond(HttpRequestMessage request)
    {
        if (Unreachable)
        {
            throw new HttpRequestException(HttpRequestError.ConnectionError);
        }

        var key = request.Headers.TryGetValues(SeqQueries.ApiKeyHeader, out var values) ? values.Single() : null;
        Calls.Enqueue(new ReceivedSeqCall(request.RequestUri!, key));
        return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(EventsJson, Encoding.UTF8, "application/json") });
    }

    private sealed class Handler(FakeSeq seq) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            seq.Respond(request);
    }
}
