using System.Collections.Concurrent;

namespace Codelab.Integration.Tests.Support;

/// <summary>A primary handler that answers from a script and records every attempt it receives.</summary>
internal sealed class StubHandler(Func<int, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    private int _attempts;

    public int Attempts => Volatile.Read(ref _attempts);

    public ConcurrentQueue<HttpRequestMessage> Received { get; } = new();

    public static StubHandler Always(System.Net.HttpStatusCode status) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(status)));

    /// <summary>Answers <paramref name="first"/> on the first attempt and <paramref name="then"/> afterwards.</summary>
    public static StubHandler FirstThen(System.Net.HttpStatusCode first, System.Net.HttpStatusCode then) =>
        new((attempt, _) => Task.FromResult(new HttpResponseMessage(attempt == 1 ? first : then)));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Received.Enqueue(request);
        var attempt = Interlocked.Increment(ref _attempts);
        return respond(attempt, cancellationToken);
    }
}
